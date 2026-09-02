using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Acquire.Imdb;

namespace TheKrystalShip.MovieBot.Bot.Notify;

public enum WishAdded
{
    Subscribed,
    AlreadySubscribed,
}

/// <summary>
/// The films people are waiting on, kept on disk.
///
/// This is the one thing the bot remembers. A download is kept as tags on the torrent and a room
/// as the API's journal, but a film that is not on the tracker yet has no torrent to tag and no
/// manifest to write: a wish for it exists nowhere unless it is written down here. Losing the
/// file on a restart would drop every person waiting, silently, with the sweep carrying on over
/// an empty list.
///
/// Every change is written whole and moved into place. Changes are rare — a person asking, a
/// film arriving — so there is nothing to batch, and a half-written file read at the next start
/// is worse than none.
/// </summary>
public sealed class WishList
{
    private readonly ILogger<WishList> _logger;
    private readonly Lock _gate = new();
    private List<Wish>? _wishes;

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
    public IReadOnlyList<Wish> For(ulong userId)
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
    /// film is what the catalogue said now, which stands in for it until the announcement.
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
    public Wish? Remove(ulong userId, string imdbId)
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

    /// <summary>
    /// Forgets everybody who asked in the given channels, because they have been told. Whoever
    /// asked somewhere the message could not be sent stays, and the next sweep tries them again.
    /// </summary>
    public void Forget(string imdbId, IReadOnlySet<ulong> channelIds)
    {
        lock (_gate)
        {
            var wishes = Loaded();
            var index = wishes.FindIndex(w => Same(w.ImdbId, imdbId));
            if (index < 0) return;

            var wish = wishes[index];
            var remaining = wish.Subscribers.Where(s => !channelIds.Contains(s.ChannelId)).ToList();
            if (remaining.Count == wish.Subscribers.Count) return;

            if (remaining.Count == 0) wishes.RemoveAt(index);
            else wishes[index] = wish with { Subscribers = remaining };

            Save(wishes);
        }
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
            // The list in memory is right and the command that changed it succeeded; what is lost
            // is the next restart. Said as an error because that loss is every wish at once.
            _logger.LogError(ex, "The wish list at {Path} could not be written.", Path);
        }
    }
}
