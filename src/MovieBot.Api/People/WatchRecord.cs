using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheKrystalShip.MovieBot.Api.People;

/// <summary>How far one person has got in one film, as of the last time a room playing it was sampled.</summary>
public sealed record WatchEntry
{
    public double PositionSeconds { get; init; }
    public double DurationSeconds { get; init; }
    public DateTimeOffset LastWatchedAt { get; init; }
    public bool Finished { get; init; }
}

/// <summary>The last time a film was played by anybody, and by whom.</summary>
public sealed record TitlePlay
{
    public DateTimeOffset LastPlayedAt { get; init; }
    public string LastPlayedBy { get; init; } = "";
}

/// <summary>Everything the record holds, which is the whole of <c>watch.json</c>.</summary>
public sealed class WatchData
{
    /// <summary>User id, then title id.</summary>
    public Dictionary<string, Dictionary<string, WatchEntry>> People { get; init; } = [];

    public Dictionary<string, TitlePlay> Titles { get; init; } = [];
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(WatchData))]
internal partial class WatchJson : JsonSerializerContext;

/// <summary>
/// What each person has watched, and when each film was last played.
///
/// Two things are kept and they are forgotten differently. A person's entry for a film is theirs: it
/// is what Continue watching and History read, and they can have it removed. The film's own last
/// play is the library's, read by the clock that decides when an unwatched film leaves, so a person
/// forgetting a film does not make it look unplayed.
///
/// Tens of people and hundreds of films fit in one small file. It is written whole through a
/// temporary file and a rename, a few seconds after the first change since the last write: the
/// sampler touches it every pass and a write per pass would be a write per pass of nothing new.
/// </summary>
public sealed class WatchRecord : IPlayHistory, IDisposable
{
    /// <summary>A film is finished once the position passes this share of its length.</summary>
    public const double FinishedFraction = 0.9;

    private readonly string _path;
    private readonly ILogger<WatchRecord> _logger;
    private readonly TimeSpan _writeDelay;
    private readonly Lock _gate = new();
    private readonly WatchData _data;
    private Timer? _timer;
    private bool _pending;

    public WatchRecord(string path, ILogger<WatchRecord> logger, TimeSpan? writeDelay = null)
    {
        _path = path;
        _logger = logger;
        _writeDelay = writeDelay ?? TimeSpan.FromSeconds(3);
        _data = Read();
    }

    /// <summary>
    /// Notes that somebody watched a film up to a position at an instant. The latest sample is the
    /// entry: somebody who starts a film again after finishing it is partway through it again.
    /// </summary>
    public void Record(string userId, string titleId, double positionSeconds, double durationSeconds, DateTimeOffset now)
    {
        if (durationSeconds <= 0) return;
        var position = Math.Clamp(positionSeconds, 0, durationSeconds);

        lock (_gate)
        {
            if (!_data.People.TryGetValue(userId, out var films))
                _data.People[userId] = films = [];

            films[titleId] = new WatchEntry
            {
                PositionSeconds = position,
                DurationSeconds = durationSeconds,
                LastWatchedAt = now,
                Finished = position >= durationSeconds * FinishedFraction
            };
            _data.Titles[titleId] = new TitlePlay { LastPlayedAt = now, LastPlayedBy = userId };
            Schedule();
        }
    }

    /// <summary>One person's films, newest first.</summary>
    public IReadOnlyList<(string TitleId, WatchEntry Entry)> Entries(string userId)
    {
        lock (_gate)
        {
            if (!_data.People.TryGetValue(userId, out var films)) return [];
            return [.. films.OrderByDescending(f => f.Value.LastWatchedAt).Select(f => (f.Key, f.Value))];
        }
    }

    public WatchEntry? EntryOf(string userId, string titleId)
    {
        lock (_gate)
            return _data.People.TryGetValue(userId, out var films) ? films.GetValueOrDefault(titleId) : null;
    }

    /// <summary>
    /// Removes the person's entry for a film and, with it, their progress. The film's last play stays:
    /// it says when the film was played, which is not something one person can take back.
    /// </summary>
    public void Forget(string userId, string titleId)
    {
        lock (_gate)
        {
            if (!_data.People.TryGetValue(userId, out var films) || !films.Remove(titleId)) return;
            if (films.Count == 0) _data.People.Remove(userId);
            Schedule();
        }
    }

    public TitlePlay? LastPlayed(string titleId)
    {
        lock (_gate) return _data.Titles.GetValueOrDefault(titleId);
    }

    public DateTimeOffset? LastPlayedAt(string titleId) => LastPlayed(titleId)?.LastPlayedAt;

    /// <summary>Writes what is held now, without waiting for the delay.</summary>
    public void Flush()
    {
        lock (_gate)
        {
            _pending = false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temporary = _path + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(_data, WatchJson.Default.WatchData));
                File.Move(temporary, _path, overwrite: true);
            }
            catch (IOException ex)
            {
                // What could not be written is still held and goes out with the next change.
                _logger.LogError(ex, "What people have watched could not be written to {Path}", _path);
            }
        }
    }

    public void Dispose()
    {
        bool dirty;
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            dirty = _pending;
        }

        if (dirty) Flush();
    }

    /// <summary>Called with the gate held.</summary>
    private void Schedule()
    {
        if (_pending) return;
        _pending = true;
        _timer?.Dispose();
        _timer = new Timer(_ => Flush(), null, _writeDelay, Timeout.InfiniteTimeSpan);
    }

    private WatchData Read()
    {
        try
        {
            if (!File.Exists(_path)) return new WatchData();
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize(stream, WatchJson.Default.WatchData) ?? new WatchData();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // An unreadable record costs everybody their progress and nothing else. Starting anyway
            // beats refusing to serve films over it; the file is left for somebody to look at.
            _logger.LogError(ex, "What people have watched at {Path} could not be read; the record starts empty", _path);
            return new WatchData();
        }
    }
}
