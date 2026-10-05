using System.Text.Json;
using System.Text.Json.Serialization;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Shows;

/// <summary>One person's follow of one show, as it is kept.</summary>
public sealed record StoredFollow
{
    public required string ImdbId { get; init; }

    public required DateTimeOffset Since { get; init; }

    /// <summary>Whether a new episode is downloaded for this person as well as announced.</summary>
    public bool AutoDownload { get; init; }

    /// <summary>
    /// The latest episode that was already due when the follow began; only an episode after it is
    /// news to this person. Absent when the show had none, so every episode counts.
    /// </summary>
    public EpisodeIdentity? Baseline { get; init; }

    /// <summary>Whether an episode is news to this person: it comes after everything that was out when they followed.</summary>
    public bool Covers(int season, int number) =>
        Baseline is not { } known || season > known.Season || (season == known.Season && number > known.Number);
}

/// <summary>Everything the follows keep: each person's, and which episodes have been announced.</summary>
public sealed record FollowData
{
    /// <summary>A person's follows, by account.</summary>
    public Dictionary<string, List<StoredFollow>> People { get; init; } = [];

    /// <summary>The episodes of each show that its followers have been told of, as <c>S02E04</c>.</summary>
    public Dictionary<string, List<string>> Announced { get; init; } = [];
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(FollowData))]
internal partial class FollowJson : JsonSerializerContext;

/// <summary>
/// Which shows each person follows, and which episodes of them have been announced, in one file in
/// the state directory, keyed by account. Written whole through a rename on every change,
/// like every other store here; follows change a few times a day.
///
/// What was announced is kept beside the follows so that a restart never tells anybody twice. It is
/// forgotten once nobody follows the show: a person who follows it later starts from the episodes
/// that are out by then.
/// </summary>
public sealed class FollowStore
{
    private readonly string _path;
    private readonly ILogger<FollowStore> _logger;
    private readonly Lock _gate = new();
    private FollowData _data;

    public FollowStore(string path, ILogger<FollowStore> logger)
    {
        _path = path;
        _logger = logger;
        _data = Read();
    }

    /// <summary>The place an episode takes in the announced list.</summary>
    public static string Label(int season, int number) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"S{season:00}E{number:00}");

    /// <summary>A person's follows, newest first.</summary>
    public List<StoredFollow> For(string userId)
    {
        lock (_gate)
            return [.. (_data.People.GetValueOrDefault(userId) ?? []).OrderByDescending(f => f.Since)];
    }

    public StoredFollow? Get(string userId, string imdbId)
    {
        lock (_gate) return _data.People.GetValueOrDefault(userId)?.FirstOrDefault(f => f.ImdbId == imdbId);
    }

    /// <summary>
    /// Follows a show, or changes whether it is downloaded for somebody who already does: a follow
    /// keeps the moment it began and the baseline it was given then.
    /// </summary>
    public StoredFollow Follow(string userId, string imdbId, bool autoDownload, EpisodeIdentity? baseline, DateTimeOffset now)
    {
        lock (_gate)
        {
            var mine = _data.People.GetValueOrDefault(userId) ?? [];
            var follow = mine.FirstOrDefault(f => f.ImdbId == imdbId) is { } existing
                ? existing with { AutoDownload = autoDownload }
                : new StoredFollow { ImdbId = imdbId, Since = now, AutoDownload = autoDownload, Baseline = baseline };

            var people = new Dictionary<string, List<StoredFollow>>(_data.People)
            {
                [userId] = [.. mine.Where(f => f.ImdbId != imdbId), follow],
            };
            Commit(_data with { People = people });
            return follow;
        }
    }

    /// <summary>Stops following. False when the person did not.</summary>
    public bool Unfollow(string userId, string imdbId)
    {
        lock (_gate)
        {
            if (Get(userId, imdbId) is null) return false;

            var people = new Dictionary<string, List<StoredFollow>>(_data.People);
            var rest = people[userId].Where(f => f.ImdbId != imdbId).ToList();
            if (rest.Count == 0) people.Remove(userId); else people[userId] = rest;
            Commit(_data with { People = people });
            return true;
        }
    }

    /// <summary>Every show anybody follows.</summary>
    public List<string> Shows()
    {
        lock (_gate)
            return [.. _data.People.Values.SelectMany(p => p).Select(f => f.ImdbId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    /// <summary>Everybody who follows a show, the earliest follow first.</summary>
    public List<(string UserId, StoredFollow Follow)> Followers(string imdbId)
    {
        lock (_gate)
            return [.. _data.People
                .SelectMany(p => p.Value.Where(f => f.ImdbId == imdbId).Select(f => (UserId: p.Key, Follow: f)))
                .OrderBy(f => f.Follow.Since).ThenBy(f => f.UserId, StringComparer.Ordinal)];
    }

    public bool IsAnnounced(string imdbId, int season, int number)
    {
        lock (_gate) return _data.Announced.GetValueOrDefault(imdbId)?.Contains(Label(season, number)) ?? false;
    }

    public void Announce(string imdbId, int season, int number)
    {
        lock (_gate)
        {
            var label = Label(season, number);
            var known = _data.Announced.GetValueOrDefault(imdbId) ?? [];
            if (known.Contains(label)) return;

            var announced = new Dictionary<string, List<string>>(_data.Announced) { [imdbId] = [.. known, label] };
            Commit(_data with { Announced = announced });
        }
    }

    private void Commit(FollowData data)
    {
        var followed = data.People.Values.SelectMany(p => p).Select(f => f.ImdbId).ToHashSet(StringComparer.Ordinal);
        _data = data with
        {
            Announced = data.Announced.Where(a => followed.Contains(a.Key)).ToDictionary(a => a.Key, a => a.Value),
        };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_data, FollowJson.Default.FollowData));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Follows at {Path} could not be written", _path);
        }
    }

    private FollowData Read()
    {
        try
        {
            if (!File.Exists(_path)) return new FollowData();
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize(stream, FollowJson.Default.FollowData) ?? new FollowData();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogError(ex, "Follows at {Path} could not be read; nobody follows anything", _path);
            return new FollowData();
        }
    }
}
