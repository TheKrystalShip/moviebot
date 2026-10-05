using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Acquire.Imdb;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Acquisition;

/// <summary>How films people are waiting on are watched for.</summary>
public sealed class NotifyOptions
{
    public const string Section = "Notify";

    /// <summary>
    /// Where the wish list is written. Empty means the state directory systemd hands the service,
    /// and the working directory when there is none.
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// How often the tracker is asked about every film on the list, in minutes. A film that was
    /// not out this morning is not out this minute, and the tracker caps how often a member may
    /// call it; an hour answers within the day a film appears without spending the cap on it.
    /// </summary>
    public int SweepMinutes { get; set; } = 60;

    /// <summary>
    /// The least a release's source may be for the film to count as available.
    ///
    /// A film in cinemas is on the tracker within days as a camcorder recording, and the ranker
    /// only weighs those down rather than dropping them. What somebody waiting on a film means by
    /// available is its digital release, and a web encode is the first form that takes.
    /// </summary>
    public Source MinimumSource { get; set; } = Source.Web;

    public string ResolvePath()
    {
        if (!string.IsNullOrWhiteSpace(Path)) return System.IO.Path.GetFullPath(Path);

        // The directory systemd makes for this service; the variable may name more than one, and
        // the first is the service's own.
        var state = Environment.GetEnvironmentVariable("STATE_DIRECTORY")?.Split(':').FirstOrDefault();
        return System.IO.Path.GetFullPath(System.IO.Path.Combine(
            string.IsNullOrWhiteSpace(state) ? Directory.GetCurrentDirectory() : state, "wishes.json"));
    }
}

/// <summary>One person waiting on a film, by the account the identity provider names them by.</summary>
public sealed record WishSubscriber
{
    public required string UserId { get; init; }

    public required DateTimeOffset AskedAt { get; init; }
}

/// <summary>
/// A film somebody wants that cannot be downloaded yet, and everybody waiting on it.
///
/// Keyed by the film rather than by the person: two people asking for the same film share one
/// wish and one tracker call per sweep. What is kept about the film is what the catalogue said when
/// it was asked, so a notification can name it and a page show its poster without asking again.
/// </summary>
public sealed record Wish
{
    /// <summary>The canonical <c>tt0458352</c> form. It is the key and what the tracker is asked.</summary>
    public required string ImdbId { get; init; }

    public required string Title { get; init; }

    public int? Year { get; init; }

    public string? Starring { get; init; }

    public string? PosterUrl { get; init; }

    public IReadOnlyList<WishSubscriber> Subscribers { get; init; } = [];

    /// <summary>The one spelling of the name and the year together.</summary>
    [JsonIgnore]
    public string Display => Year is { } year ? $"{Title} ({year})" : Title;

    /// <summary>The wish as it crosses the wire, with the release that answers it once there is one.</summary>
    public WishView View(ReleaseOffer? release = null) => new()
    {
        ImdbId = ImdbId,
        Title = Title,
        Year = Year,
        Display = Display,
        Starring = Starring,
        PosterUrl = PosterUrl,
        Subscribers = Subscribers.Select(s => new WishSubscriberView
        {
            UserId = s.UserId,
            AskedAt = s.AskedAt,
        }).ToList(),
        Release = release,
    };
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(List<Wish>))]
internal partial class WishListJson : JsonSerializerContext;

public enum WishAdded
{
    Subscribed,
    AlreadySubscribed,
}

/// <summary>
/// The films people are waiting on, kept on disk.
///
/// A download is kept as tags on the torrent and a room as the journal, but a film that is not on
/// the tracker yet has no torrent to tag and no manifest to write: a wish for it exists nowhere
/// unless it is written down here. Losing the file on a restart would drop every person waiting,
/// silently, with the sweep carrying on over an empty list.
///
/// Every change is written whole and moved into place. Changes are rare — a person asking, a film
/// arriving — so there is nothing to batch, and a half-written file read at the next start is worse
/// than none.
/// </summary>
public sealed class WishList
{
    private readonly ILogger<WishList> _logger;
    private readonly Lock _gate = new();
    private List<Wish>? _wishes;

    // The release each wish that has come out was found as. Held in memory only: the sweep finds it
    // again on its first pass after a restart.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Release> _out = new(StringComparer.OrdinalIgnoreCase);

    public WishList(IOptions<NotifyOptions> options, ILogger<WishList> logger)
        : this(options.Value.ResolvePath(), logger)
    {
    }

    public WishList(string path, ILogger<WishList> logger)
    {
        Path = path;
        _logger = logger;
    }

    public string Path { get; }

    public IReadOnlyList<Wish> All()
    {
        lock (_gate) return [.. Loaded()];
    }

    /// <summary>The films one person is waiting on.</summary>
    public IReadOnlyList<Wish> For(string userId)
    {
        lock (_gate)
            return Loaded().Where(w => w.Subscribers.Any(s => s.UserId == userId)).ToList();
    }

    public Wish? Find(string imdbId)
    {
        lock (_gate)
            return Loaded().FirstOrDefault(w => Same(w.ImdbId, imdbId));
    }

    /// <summary>
    /// Adds a person to a film's wish, making the wish if it is the first. What is kept about the
    /// film is what the catalogue said now, which stands in for it until the film is out.
    /// </summary>
    public (WishAdded Outcome, Wish Wish) Add(ImdbTitle film, WishSubscriber subscriber)
    {
        lock (_gate)
        {
            var wishes = Loaded();
            var index = wishes.FindIndex(w => Same(w.ImdbId, film.ImdbId));

            if (index >= 0)
            {
                var existing = wishes[index];
                if (existing.Subscribers.Any(s => s.UserId == subscriber.UserId))
                    return (WishAdded.AlreadySubscribed, existing);

                var joined = existing with { Subscribers = [.. existing.Subscribers, subscriber] };
                wishes[index] = joined;
                Save(wishes);
                return (WishAdded.Subscribed, joined);
            }

            var wish = new Wish
            {
                ImdbId = film.ImdbId,
                Title = film.Title,
                Year = film.Year,
                Starring = film.Starring,
                PosterUrl = film.PosterUrl?.ToString(),
                Subscribers = [subscriber],
            };

            wishes.Add(wish);
            Save(wishes);
            return (WishAdded.Subscribed, wish);
        }
    }

    /// <summary>Takes one person off a film's wish. The wish goes with its last subscriber.</summary>
    public Wish? Remove(string userId, string imdbId)
    {
        lock (_gate)
        {
            var wishes = Loaded();
            var index = wishes.FindIndex(w => Same(w.ImdbId, imdbId));
            if (index < 0) return null;

            var wish = wishes[index];
            if (wish.Subscribers.All(s => s.UserId != userId)) return null;

            var remaining = wish.Subscribers.Where(s => s.UserId != userId).ToList();
            if (remaining.Count == 0) wishes.RemoveAt(index);
            else wishes[index] = wish with { Subscribers = remaining };

            Save(wishes);
            return wish;
        }
    }

    /// <summary>Notes that a wished film is out, as this release.</summary>
    public void MarkOut(string imdbId, Release release) => _out[imdbId] = release;

    /// <summary>The release a wished film came out as, or null while it is not out or nobody waits on it.</summary>
    public Release? OutAs(string imdbId)
    {
        if (!_out.TryGetValue(imdbId, out var release)) return null;
        if (Find(imdbId) is not null) return release;

        // A film nobody waits on any longer is nobody's news.
        _out.TryRemove(imdbId, out _);
        return null;
    }

    private static bool Same(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Read once, on first use. Never throws: a file that cannot be read is a list that starts
    /// empty, which is said on the log because it is also every wish being dropped.
    /// </summary>
    private List<Wish> Loaded()
    {
        if (_wishes is not null) return _wishes;

        try
        {
            _wishes = File.Exists(Path)
                ? JsonSerializer.Deserialize(File.ReadAllText(Path), WishListJson.Default.ListWish) ?? []
                : [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The wish list at {Path} could not be read; starting with none.", Path);
            _wishes = [];
        }

        return _wishes;
    }

    private void Save(List<Wish> wishes)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var temporary = Path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(wishes, WishListJson.Default.ListWish));
            File.Move(temporary, Path, overwrite: true);
        }
        catch (Exception ex)
        {
            // The list in memory is right and the request that changed it succeeded; what is lost
            // is the next restart. Said as an error because that loss is every wish at once.
            _logger.LogError(ex, "The wish list at {Path} could not be written.", Path);
        }
    }
}
