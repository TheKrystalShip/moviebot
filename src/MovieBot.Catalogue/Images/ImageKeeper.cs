using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Catalogue.Data;
using TheKrystalShip.MovieBot.Catalogue.Tmdb;

namespace TheKrystalShip.MovieBot.Catalogue.Images;

/// <summary>
/// Where the image files live: <c>images/&lt;size&gt;&lt;path&gt;</c> under the catalogue's directory, by
/// the film database's path, so the API serves a file by the path an entry names.
/// </summary>
public sealed class ImageStore(string root)
{
    public string Root { get; } = Path.GetFullPath(root);

    public string FileOf(string path, string size) =>
        Valid(path) && size.Length > 1 && size[0] == 'w' && size.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0
            ? Path.Combine(Root, size, path[1..])
            : throw new ArgumentException($"Not an image path: {size}{path}");

    /// <summary>Whether a path is one the image store names: a slash, then a file name of letters, digits and one extension.</summary>
    public static bool Valid(string path) =>
        path.Length > 5 && path[0] == '/' && path.IndexOf('/', 1) < 0 && path.IndexOf("..", StringComparison.Ordinal) < 0
        && path.AsSpan(1).IndexOfAnyExcept("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-") < 0;
}

/// <summary>
/// Keeps every image a page shows and nothing else, since what is not kept is not shown: each listed
/// entry's poster (w500), backdrop (w1280) and logo (w500), its top-billed cast's portraits (w185),
/// each franchise's poster and backdrop, and for the shows the library holds their seasons' posters
/// (w500) and episodes' stills (w300). A file is fetched again once it is a sweep old, inside the film
/// database's six months, and a file no entry names any more is deleted once a day. A path the image
/// store has nothing at is recorded with no bytes and asked for again at the sweep.
/// </summary>
public sealed class ImageWorker(
    ImageStore store,
    TmdbSource source,
    CatalogueDatabase database,
    IOptions<CatalogueOptions> options,
    TimeProvider clock,
    ILogger<ImageWorker> logger) : BackgroundService
{
    private const int Batch = 500;
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PruneEvery = TimeSpan.FromHours(24);

    /// <summary>Every image a page shows, as (path, size).</summary>
    private static string Wanted(int portraits) => $"""
        SELECT poster AS path, 'w500' AS size FROM titles WHERE listed = 1 AND poster IS NOT NULL
        UNION SELECT backdrop, 'w1280' FROM titles WHERE listed = 1 AND backdrop IS NOT NULL
        UNION SELECT logo, 'w500' FROM titles WHERE listed = 1 AND logo IS NOT NULL
        UNION SELECT p.profile, 'w185' FROM credits c JOIN people p ON p.id = c.person_id JOIN titles t ON t.id = c.title_id
              WHERE t.listed = 1 AND c.department = 'cast' AND c.ord < {portraits} AND p.profile IS NOT NULL
        UNION SELECT poster, 'w500' FROM franchises WHERE poster IS NOT NULL
        UNION SELECT backdrop, 'w1280' FROM franchises WHERE backdrop IS NOT NULL
        UNION SELECT s.poster, 'w500' FROM seasons s JOIN library_shows l ON l.title_id = s.title_id WHERE s.poster IS NOT NULL
        UNION SELECT e.still, 'w300' FROM episodes e JOIN library_shows l ON l.title_id = e.title_id WHERE e.still IS NOT NULL
        """;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!source.IsConfigured) return;
        var pruned = DateTimeOffset.MinValue;
        var kept = 0L;
        var bytes = 0L;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (clock.GetUtcNow() - pruned >= PruneEvery)
                {
                    pruned = clock.GetUtcNow();
                    Prune();
                }

                var due = Due();
                if (due.Count == 0)
                {
                    if (kept > 0) logger.LogInformation("Images: {Count} kept, {MiB:0} MiB.", kept, bytes / 1048576.0);
                    kept = bytes = 0;
                    await Task.Delay(Idle, clock, stoppingToken);
                    continue;
                }

                var fetched = new System.Collections.Concurrent.ConcurrentBag<(string Path, string Size, long Bytes)>();
                await Parallel.ForEachAsync(due, new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, options.Value.ImagesAtOnce),
                    CancellationToken = stoppingToken,
                }, async (image, ct) =>
                {
                    try
                    {
                        var size = await FetchAsync(image.Path, image.Size, ct);
                        fetched.Add((image.Path, image.Size, size));
                        Interlocked.Increment(ref kept);
                        Interlocked.Add(ref bytes, size);
                    }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException && !ct.IsCancellationRequested)
                    {
                        logger.LogDebug("The image {Size}{Path} could not be kept: {Reason}", image.Size, image.Path, ex.Message);
                    }
                });
                Record(fetched);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private List<(string Path, string Size)> Due()
    {
        var stale = clock.GetUtcNow() - TimeSpan.FromDays(Math.Max(1, options.Value.SweepDays));
        return database.Read(c => c.Query($"""
            SELECT w.path, w.size FROM ({Wanted(options.Value.PortraitsKept)}) w
            LEFT JOIN images i ON i.path = w.path AND i.size = w.size
            WHERE i.path IS NULL OR i.fetched_at < $stale
            LIMIT $n
            """, r => (r.GetString(0), r.GetString(1)), ("$stale", stale), ("$n", Batch)));
    }

    /// <summary>
    /// Fetches one image to its file, written whole before it is renamed into place; returns its size,
    /// zero when the store has none at the path. The caller records it (<see cref="Record"/>).
    /// </summary>
    public async Task<long> FetchAsync(string path, string size, CancellationToken ct)
    {
        var bytes = ImageStore.Valid(path) ? await source.ImageAsync(path, size, ct) : null;
        if (bytes is not null)
        {
            var file = store.FileOf(path, size);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var partial = file + ".part";
            await File.WriteAllBytesAsync(partial, bytes, ct);
            File.Move(partial, file, overwrite: true);
        }
        return bytes?.LongLength ?? 0;
    }

    /// <summary>Records fetched images, in one transaction.</summary>
    public void Record(IEnumerable<(string Path, string Size, long Bytes)> fetched)
    {
        var now = clock.GetUtcNow();
        database.Write(c =>
        {
            foreach (var (path, size, bytes) in fetched)
                c.Execute("""
                    INSERT INTO images (path, size, fetched_at, bytes) VALUES ($path, $size, $at, $bytes)
                    ON CONFLICT (path, size) DO UPDATE SET fetched_at = excluded.fetched_at, bytes = excluded.bytes
                    """, ("$path", path), ("$size", size), ("$at", now), ("$bytes", bytes));
        });
    }

    /// <summary>Deletes the files no page shows any more, and their rows.</summary>
    public int Prune()
    {
        var gone = database.Read(c => c.Query($"""
            SELECT i.path, i.size FROM images i
            WHERE NOT EXISTS (SELECT 1 FROM ({Wanted(options.Value.PortraitsKept)}) w WHERE w.path = i.path AND w.size = i.size)
            """, r => (r.GetString(0), r.GetString(1))));
        foreach (var (path, size) in gone)
        {
            if (ImageStore.Valid(path)) File.Delete(store.FileOf(path, size));
            database.Write(c => c.Execute("DELETE FROM images WHERE path = $path AND size = $size", ("$path", path), ("$size", size)));
        }
        if (gone.Count > 0) logger.LogInformation("Images: {Count} no page shows any more deleted.", gone.Count);
        return gone.Count;
    }
}

/// <summary>
/// Which shows the library holds, read from the manifests under the library's roots every ten minutes:
/// their episodes' stills are kept. A root that is absent (a cold disk not mounted) adds nothing and
/// takes nothing away until it is back.
/// </summary>
public sealed class LibraryShows(IReadOnlyList<string> roots, CatalogueDatabase database, TimeProvider clock, ILogger<LibraryShows> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                Refresh();
                await Task.Delay(Every, clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public int Refresh()
    {
        var present = roots.Where(Directory.Exists).ToList();
        if (present.Count == 0) return 0;
        if (present.Count < roots.Count)
            logger.LogDebug("A library root is absent; the shows already known are kept.");

        var shows = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in present)
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var manifest = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifest)) continue;
            try
            {
                using var stream = File.OpenRead(manifest);
                using var document = JsonDocument.Parse(stream);
                var top = document.RootElement;
                if (top.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() == "show"
                    && top.TryGetProperty("film", out var film) && film.TryGetProperty("imdbId", out var imdb) && imdb.GetString() is { } id)
                    shows.Add(id);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // A manifest being rewritten is read again on the next pass.
            }
        }

        var whole = present.Count == roots.Count;
        return database.Write(c =>
        {
            if (whole) c.Execute("DELETE FROM library_shows");
            foreach (var id in shows)
                c.Execute("INSERT OR IGNORE INTO library_shows (title_id) SELECT id FROM titles WHERE imdb_id = $imdb", ("$imdb", id));
            return (int)c.Count("SELECT count(*) FROM library_shows");
        });
    }
}
