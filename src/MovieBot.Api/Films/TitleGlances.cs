using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheKrystalShip.MovieBot.Acquire.Tmdb;
using TheKrystalShip.MovieBot.Api.Shows;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>What is kept for one title at a glance, and when the database was asked.</summary>
public sealed record StoredGlance
{
    /// <summary>Null when the database holds nothing under the id, which is an answer worth keeping.</summary>
    public TitleGlance? Glance { get; init; }

    public required DateTimeOffset FetchedAt { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(StoredGlance))]
internal partial class StoredGlanceJson : JsonSerializerContext;

/// <summary>
/// A search result's rating and overview, from the film database, kept on disk one file per title
/// and asked for once.
///
/// A title already described in full (<see cref="FilmStore"/>, <see cref="ShowStore"/>) answers from
/// that description and asks nothing. Anything else is asked for by its IMDb id the first time a
/// search offers it, every title of one search at once, and the search waits for those answers no
/// longer than <see cref="Wait"/>: a late answer is still kept, for the next search.
///
/// How long a glance is kept follows how much it still moves. A title out for over a year has a
/// settled overview and a rating its votes no longer shift, and is asked about again after
/// <see cref="SettledKept"/>; one released within the year, not out yet, or not in the database at
/// all is still being written and voted on, and is asked about again after <see cref="FreshKept"/>.
/// A kept glance is served as it stands and refreshed in the background, so no search ever waits on
/// a refresh, and a database that cannot be reached costs a result its rating and nothing else.
///
/// A result shows the poster its title's page shows (<see cref="PosterUrl"/>): the described title's
/// own kept poster, or else the one the glance names, fetched once in the background when the glance
/// arrives, kept beside it and served from here at <c>/glances/{imdbId}/poster.jpg</c>.
/// </summary>
public sealed class TitleGlances(
    string root,
    FilmStore films,
    ShowStore shows,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<TitleGlances> logger)
{
    /// <summary>How long a search waits for titles nothing is kept for.</summary>
    internal static readonly TimeSpan Wait = TimeSpan.FromMilliseconds(1500);

    /// <summary>How long a title out for over a year is kept before the database is asked again.</summary>
    internal static readonly TimeSpan SettledKept = TimeSpan.FromDays(365);

    /// <summary>How long anything newer, or unknown to the database, is kept.</summary>
    internal static readonly TimeSpan FreshKept = TimeSpan.FromDays(7);

    private const string PosterSize = "w500";

    private readonly ConcurrentDictionary<string, StoredGlance> _held = new();
    private readonly ConcurrentDictionary<string, Task<StoredGlance?>> _asking = new();
    private readonly ConcurrentDictionary<string, Task<string?>> _posters = new();

    /// <summary>
    /// Where a result's poster is served from, the same image its page shows; null when the database
    /// holds none for it.
    /// </summary>
    public string? PosterUrl(string imdbId, TitleGlance? glance)
    {
        if (films.Held(imdbId)?.Details is not null && films.HasImage(imdbId, FilmStore.Poster))
            return FilmViews.ImageUrl(imdbId, FilmStore.Poster);
        if (shows.Held(imdbId)?.Details is not null && shows.HasImage(imdbId, ShowStore.Poster))
            return ShowViews.ImageUrl(imdbId, ShowStore.Poster);
        return glance?.PosterPath is not null ? $"/glances/{imdbId}/poster.jpg" : null;
    }

    /// <summary>The kept poster of a glance, fetched now if it is not kept yet; null when there is none.</summary>
    public Task<string?> PosterFileAsync(string imdbId)
    {
        if ((Held(imdbId)?.Glance ?? Described(imdbId))?.PosterPath is not { } wanted) return Task.FromResult<string?>(null);
        var target = Path.Combine(root, imdbId + ".poster.jpg");
        var marker = target + ".src";
        if (File.Exists(target) && File.Exists(marker) && File.ReadAllText(marker) == wanted) return Task.FromResult<string?>(target);

        var fetching = _posters.GetOrAdd(imdbId, _ => FetchPosterAsync(wanted, target, marker));
        fetching.ContinueWith(_ => _posters.TryRemove(new KeyValuePair<string, Task<string?>>(imdbId, fetching)),
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return fetching;
    }

    private async Task<string?> FetchPosterAsync(string path, string target, string marker)
    {
        await Task.Yield();
        using var scope = scopes.CreateScope();
        var tmdb = scope.ServiceProvider.GetRequiredService<TmdbClient>();
        try
        {
            if (await tmdb.ImageAsync(path, PosterSize, CancellationToken.None) is not { } bytes) return null;
            Directory.CreateDirectory(root);
            await File.WriteAllBytesAsync(target + ".tmp", bytes);
            File.Move(target + ".tmp", target, overwrite: true);
            await File.WriteAllTextAsync(marker, path);
            return target;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            logger.LogWarning("The poster {Path} could not be fetched: {Reason}", path, ex.Message);
            return File.Exists(target) ? target : null;
        }
    }

    /// <summary>The glance of each title the database knows, by IMDb id; a title it does not, or that is still being asked about, is absent.</summary>
    public async Task<IReadOnlyDictionary<string, TitleGlance>> ForAsync(IReadOnlyList<string> imdbIds, CancellationToken ct)
    {
        var answered = new Dictionary<string, TitleGlance>(StringComparer.Ordinal);
        var waiting = new List<(string Id, Task<StoredGlance?> Asking)>();

        foreach (var id in imdbIds.Distinct(StringComparer.Ordinal))
        {
            if (Described(id) is { } described)
            {
                answered[id] = described;
                continue;
            }

            var held = Held(id);
            if (held is null)
            {
                waiting.Add((id, Ask(id, null)));
                continue;
            }

            if (IsDue(held)) _ = Ask(id, held);
            if (held.Glance is { } glance) answered[id] = glance;
        }

        if (waiting.Count == 0) return answered;
        try
        {
            await Task.WhenAll(waiting.Select(w => w.Asking)).WaitAsync(Wait, clock, ct);
        }
        catch (TimeoutException)
        {
        }

        foreach (var (id, asking) in waiting)
            if (asking.IsCompletedSuccessfully && asking.Result?.Glance is { } glance) answered[id] = glance;
        return answered;
    }

    /// <summary>
    /// Keeps what a browse of the database (<see cref="Catalogue"/>) said of a title as its glance,
    /// unless the title is described or a glance of it is kept and not due, so the title's poster is
    /// served from here like a search result's.
    /// </summary>
    public void Offer(string imdbId, TitleGlance glance)
    {
        if (!FilmStore.IsImdbId(imdbId) || Described(imdbId) is not null) return;
        if (Held(imdbId) is { } held && !IsDue(held)) return;

        var stored = new StoredGlance { Glance = glance, FetchedAt = clock.GetUtcNow() };
        try
        {
            Write(imdbId, stored);
        }
        catch (IOException ex)
        {
            logger.LogWarning("The glance of {Imdb} could not be kept: {Reason}", imdbId, ex.Message);
        }
        _held[imdbId] = stored;
    }

    /// <summary>A title described in full answers from that description.</summary>
    private TitleGlance? Described(string imdbId)
    {
        if (films.Held(imdbId)?.Details is { } film)
            return new TitleGlance { TmdbId = film.TmdbId, Kind = "film", Rating = film.Rating, Votes = film.Votes, Overview = film.Overview, PosterPath = film.PosterPath };
        if (shows.Held(imdbId)?.Details is { } show)
            return new TitleGlance { TmdbId = show.TmdbId, Kind = "show", Rating = show.Rating, Votes = show.Votes, Overview = show.Overview, PosterPath = show.PosterPath };
        return null;
    }

    private StoredGlance? Held(string imdbId)
    {
        if (!FilmStore.IsImdbId(imdbId)) return null;
        if (_held.TryGetValue(imdbId, out var held)) return held;
        var read = Read(imdbId);
        if (read is not null) _held[imdbId] = read;
        return read;
    }

    private bool IsDue(StoredGlance stored)
    {
        var asked = DateOnly.FromDateTime(stored.FetchedAt.UtcDateTime);
        var settled = stored.Glance?.Released is { } released && released.AddYears(1) <= asked;
        return clock.GetUtcNow() - stored.FetchedAt > (settled ? SettledKept : FreshKept);
    }

    /// <summary>One question per title however many searches want it; never cancelled by the search that started it, so its answer is kept.</summary>
    private Task<StoredGlance?> Ask(string imdbId, StoredGlance? held)
    {
        if (!FilmStore.IsImdbId(imdbId)) return Task.FromResult<StoredGlance?>(null);
        var asking = _asking.GetOrAdd(imdbId, id => FetchAsync(id, held));
        asking.ContinueWith(_ => _asking.TryRemove(new KeyValuePair<string, Task<StoredGlance?>>(imdbId, asking)),
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return asking;
    }

    private async Task<StoredGlance?> FetchAsync(string imdbId, StoredGlance? held)
    {
        await Task.Yield();
        using var scope = scopes.CreateScope();
        var tmdb = scope.ServiceProvider.GetRequiredService<TmdbClient>();
        if (!tmdb.IsConfigured) return held;

        try
        {
            var glance = await tmdb.GlanceAsync(imdbId, CancellationToken.None);
            var stored = new StoredGlance { Glance = glance, FetchedAt = clock.GetUtcNow() };
            Write(imdbId, stored);
            _held[imdbId] = stored;
            if (glance?.PosterPath is not null && Described(imdbId) is null) _ = PosterFileAsync(imdbId);
            return stored;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            logger.LogWarning("The film database could not say what {Imdb} is: {Reason}", imdbId, ex.Message);
            return held;
        }
    }

    private StoredGlance? Read(string imdbId)
    {
        var path = Path.Combine(root, imdbId + ".json");
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, StoredGlanceJson.Default.StoredGlance);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            logger.LogWarning("The kept glance of {Imdb} could not be read: {Reason}", imdbId, ex.Message);
            return null;
        }
    }

    private void Write(string imdbId, StoredGlance glance)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, imdbId + ".json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(glance, StoredGlanceJson.Default.StoredGlance));
        File.Move(path + ".tmp", path, overwrite: true);
    }
}
