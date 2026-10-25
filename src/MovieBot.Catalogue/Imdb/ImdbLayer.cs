using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Catalogue.Data;

namespace TheKrystalShip.MovieBot.Catalogue.Imdb;

/// <summary>Which copy of a dataset the server holds: its entity tag and the day it was last written.</summary>
public sealed record DatasetVersion(string Tag, DateTimeOffset? Modified);

/// <summary>IMDb's dataset files, asked after with HEAD and read as a stream.</summary>
public sealed class ImdbDatasets(HttpClient http, IOptions<CatalogueOptions> options)
{
    public const string BasicsFile = "title.basics.tsv.gz";
    public const string RatingsFile = "title.ratings.tsv.gz";

    public async Task<DatasetVersion?> VersionAsync(string file, CancellationToken ct)
    {
        using var asking = CancellationTokenSource.CreateLinkedTokenSource(ct);
        asking.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Head, UriOf(file));
        using var response = await http.SendAsync(request, asking.Token);
        response.EnsureSuccessStatusCode();
        var etag = response.Headers.ETag?.Tag;
        var modified = response.Content.Headers.LastModified;
        return etag is null && modified is null ? null : new DatasetVersion(etag ?? "", modified);
    }

    /// <summary>The file's compressed bytes as they arrive; disposing the stream releases the response.</summary>
    public async Task<Stream> OpenAsync(string file, CancellationToken ct)
    {
        var response = await http.GetAsync(UriOf(file), HttpCompletionOption.ResponseHeadersRead, ct);
        try
        {
            response.EnsureSuccessStatusCode();
            return new OwningStream(await response.Content.ReadAsStreamAsync(ct), response);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private Uri UriOf(string file)
    {
        var baseUrl = options.Value.ImdbBaseUrl;
        return new Uri(baseUrl.EndsWith('/') ? baseUrl + file : baseUrl + "/" + file);
    }

    private sealed class OwningStream(Stream inner, IDisposable owner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => inner.ReadAsync(buffer, ct);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                owner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

/// <summary>
/// IMDb's layer: <c>title.basics</c> and <c>title.ratings</c>, read into the catalogue.
///
/// The ratings are read first and the titles file scanned a line at a time after, neither held whole.
/// What is kept is every film (<c>movie</c>, <c>tvMovie</c>) and show (<c>tvSeries</c>,
/// <c>tvMiniSeries</c>) not marked adult with at least the vote floor, and every title already in the
/// catalogue whatever its votes, since a ranking admits titles under the floor. The rows go into a
/// staging table in batches; one transaction then adds the new titles as candidates, replaces the
/// layer of each title whose basics changed and marks its entry due, and writes every rating. A title
/// that leaves the dataset or falls under the floor keeps its layer and its entry.
/// </summary>
public static class ImdbImport
{
    public const string StampKey = "imdb_stamp";
    public const string EditionKey = "imdb_edition";
    public const string ImportedKey = "imdb_imported";

    private const int BatchRows = 20_000;

    private readonly record struct Row(
        string ImdbId, string Kind, string Primary, string Original, int? Start, int? End, int? Runtime, string Genres, double Rating, int Votes);

    private delegate void LineHandler(ReadOnlySpan<char> line);

    /// <summary>What one import did.</summary>
    public readonly record struct Outcome(int Read, int Added, int Changed);

    public static Outcome Load(
        CatalogueDatabase database, Func<Stream> openBasics, Func<Stream> openRatings, int minVotes, string edition, string stamp,
        DateTimeOffset now, CancellationToken ct)
    {
        var known = database.Read(c => c.Query("SELECT imdb_id FROM titles", r => r.GetString(0)).ToHashSet(StringComparer.Ordinal));
        var ratings = ReadRatings(openRatings(), minVotes, known, ct);
        if (ratings.Count == 0) throw new InvalidDataException("The ratings file holds no title with enough votes.");

        database.Write(c => c.Execute("""
            DROP TABLE IF EXISTS imdb_next;
            CREATE TABLE imdb_next (
                imdb_id TEXT PRIMARY KEY, kind TEXT NOT NULL, primary_title TEXT NOT NULL, original_title TEXT NOT NULL,
                start_year INTEGER, end_year INTEGER, runtime INTEGER, genres TEXT NOT NULL, rating REAL NOT NULL, votes INTEGER NOT NULL
            ) WITHOUT ROWID;
            """));

        try
        {
            var read = 0;
            var batch = new List<Row>(BatchRows);
            var first = true;
            using (var basics = new GZipStream(openBasics(), CompressionMode.Decompress))
            {
                EachLine(basics, line =>
                {
                    if (first)
                    {
                        first = false;
                        if (!line.StartsWith("tconst\ttitleType\tprimaryTitle")) throw new InvalidDataException("The titles file is not laid out as expected.");
                        return;
                    }
                    if (Parse(line, ratings) is not { } row) return;
                    batch.Add(row);
                    if (batch.Count == BatchRows) read += Flush(database, batch);
                }, ct);
            }
            read += Flush(database, batch);
            if (read == 0) throw new InvalidDataException("The titles file holds no film or show with a rating.");

            return database.Write(c =>
            {
                var at = Sql.Instant(now);
                var added = c.Execute("""
                    INSERT INTO titles (imdb_id, kind, admitted)
                    SELECT n.imdb_id, n.kind, 'votes' FROM imdb_next n
                    WHERE n.votes >= $floor AND NOT EXISTS (SELECT 1 FROM titles t WHERE t.imdb_id = n.imdb_id)
                    """, ("$floor", minVotes));

                // A title whose basics differ from its layer, or that has none, has its entry built again.
                var changed = c.Execute("""
                    INSERT OR IGNORE INTO build_due (title_id)
                    SELECT t.id FROM imdb_next n JOIN titles t ON t.imdb_id = n.imdb_id
                    LEFT JOIN imdb_layer l ON l.title_id = t.id
                    WHERE l.title_id IS NULL
                       OR l.primary_title IS NOT n.primary_title OR l.original_title IS NOT n.original_title
                       OR l.start_year IS NOT n.start_year OR l.end_year IS NOT n.end_year
                       OR l.runtime IS NOT n.runtime OR l.genres IS NOT n.genres OR t.kind IS NOT n.kind
                    """);
                c.Execute("""
                    UPDATE titles SET kind = (SELECT n.kind FROM imdb_next n WHERE n.imdb_id = titles.imdb_id)
                    WHERE id IN (SELECT title_id FROM build_due)
                      AND EXISTS (SELECT 1 FROM imdb_next n WHERE n.imdb_id = titles.imdb_id AND n.kind <> titles.kind)
                    """);
                c.Execute("""
                    INSERT INTO imdb_layer (title_id, primary_title, original_title, start_year, end_year, runtime, genres, edition)
                    SELECT t.id, n.primary_title, n.original_title, n.start_year, n.end_year, n.runtime, n.genres, $edition
                    FROM imdb_next n JOIN titles t ON t.imdb_id = n.imdb_id
                    WHERE true
                    ON CONFLICT (title_id) DO UPDATE SET
                        primary_title = excluded.primary_title, original_title = excluded.original_title,
                        start_year = excluded.start_year, end_year = excluded.end_year, runtime = excluded.runtime,
                        genres = excluded.genres, edition = excluded.edition
                    """, ("$edition", edition));
                // A rating or vote count that moved marks its title changed.
                c.Execute("""
                    UPDATE titles SET changed_at = $at WHERE id IN (
                        SELECT t.id FROM imdb_next n JOIN titles t ON t.imdb_id = n.imdb_id
                        LEFT JOIN ratings r ON r.title_id = t.id AND r.source = 'imdb'
                        WHERE r.title_id IS NULL OR r.rating <> n.rating OR r.votes <> n.votes)
                    """, ("$at", at));
                c.Execute("""
                    INSERT INTO ratings (title_id, source, rating, votes, seen_at)
                    SELECT t.id, 'imdb', n.rating, n.votes, $at FROM imdb_next n JOIN titles t ON t.imdb_id = n.imdb_id
                    WHERE true
                    ON CONFLICT (title_id, source) DO UPDATE SET rating = excluded.rating, votes = excluded.votes, seen_at = excluded.seen_at
                    """, ("$at", at));
                c.Execute("DROP TABLE imdb_next");
                CatalogueDatabase.SetMeta(c, StampKey, stamp);
                CatalogueDatabase.SetMeta(c, EditionKey, edition);
                CatalogueDatabase.SetMeta(c, ImportedKey, at);
                return new Outcome(read, added, changed);
            });
        }
        catch
        {
            try
            {
                database.Write(c => c.Execute("DROP TABLE IF EXISTS imdb_next"));
            }
            catch (SqliteException)
            {
                // The next import drops it first.
            }
            throw;
        }
    }

    /// <summary>The rating of every title with enough votes, and of every title the catalogue already holds.</summary>
    private static Dictionary<string, (float Rating, int Votes)> ReadRatings(Stream gz, int minVotes, HashSet<string> known, CancellationToken ct)
    {
        var ratings = new Dictionary<string, (float, int)>(StringComparer.Ordinal);
        var lookup = known.GetAlternateLookup<ReadOnlySpan<char>>();
        var first = true;
        using var stream = new GZipStream(gz, CompressionMode.Decompress);
        EachLine(stream, line =>
        {
            if (first)
            {
                first = false;
                if (!line.StartsWith("tconst\taverageRating\tnumVotes")) throw new InvalidDataException("The ratings file is not laid out as expected.");
                return;
            }
            var rest = line;
            if (!Next(ref rest, out var id) || !Next(ref rest, out var rating) || rest.IsEmpty) return;
            if (!int.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out var votes)
                || !float.TryParse(rating, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || !id.StartsWith("tt"))
                return;
            if (votes < minVotes && !lookup.Contains(id)) return;
            ratings[id.ToString()] = (value, votes);
        }, ct);
        return ratings;
    }

    private static Row? Parse(ReadOnlySpan<char> line, Dictionary<string, (float Rating, int Votes)> ratings)
    {
        var rest = line;
        if (!Next(ref rest, out var id) || !Next(ref rest, out var type)) return null;
        var kind = type switch
        {
            "movie" or "tvMovie" => "film",
            "tvSeries" or "tvMiniSeries" => "show",
            _ => null,
        };
        if (kind is null || !ratings.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(id, out var rating)) return null;

        if (!Next(ref rest, out var primary) || !Next(ref rest, out var original) || !Next(ref rest, out var adult)
            || !Next(ref rest, out var start) || !Next(ref rest, out var end) || !Next(ref rest, out var runtime))
            return null;
        if (adult is not "0" || primary is "\\N" || primary.IsEmpty) return null;

        return new Row(id.ToString(), kind, primary.ToString(), original is "\\N" || original.IsEmpty ? primary.ToString() : original.ToString(),
            NumberOf(start), NumberOf(end), NumberOf(runtime), rest is "\\N" ? "" : rest.ToString(), Math.Round(rating.Rating, 1), rating.Votes);
    }

    private static bool Next(ref ReadOnlySpan<char> rest, out ReadOnlySpan<char> field)
    {
        var tab = rest.IndexOf('\t');
        if (tab < 0)
        {
            field = default;
            return false;
        }
        field = rest[..tab];
        rest = rest[(tab + 1)..];
        return true;
    }

    private static int? NumberOf(ReadOnlySpan<char> field) =>
        int.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static int Flush(CatalogueDatabase database, List<Row> batch)
    {
        if (batch.Count == 0) return 0;
        var count = batch.Count;
        database.Write(c =>
        {
            using var insert = c.Command("""
                INSERT OR REPLACE INTO imdb_next (imdb_id, kind, primary_title, original_title, start_year, end_year, runtime, genres, rating, votes)
                VALUES ($id, $kind, $primary, $original, $start, $end, $runtime, $genres, $rating, $votes)
                """);
            var id = insert.Parameters.Add("$id", SqliteType.Text);
            var kind = insert.Parameters.Add("$kind", SqliteType.Text);
            var primary = insert.Parameters.Add("$primary", SqliteType.Text);
            var original = insert.Parameters.Add("$original", SqliteType.Text);
            var start = insert.Parameters.Add("$start", SqliteType.Integer);
            var end = insert.Parameters.Add("$end", SqliteType.Integer);
            var runtime = insert.Parameters.Add("$runtime", SqliteType.Integer);
            var genres = insert.Parameters.Add("$genres", SqliteType.Text);
            var rating = insert.Parameters.Add("$rating", SqliteType.Real);
            var votes = insert.Parameters.Add("$votes", SqliteType.Integer);
            foreach (var row in batch)
            {
                id.Value = row.ImdbId;
                kind.Value = row.Kind;
                primary.Value = row.Primary;
                original.Value = row.Original;
                start.Value = row.Start is { } s ? s : DBNull.Value;
                end.Value = row.End is { } e ? e : DBNull.Value;
                runtime.Value = row.Runtime is { } r ? r : DBNull.Value;
                genres.Value = row.Genres;
                rating.Value = row.Rating;
                votes.Value = row.Votes;
                insert.ExecuteNonQuery();
            }
        });
        batch.Clear();
        return count;
    }

    /// <summary>Calls <paramref name="handle"/> with each line of the text, without making a string of any.</summary>
    private static void EachLine(Stream stream, LineHandler handle, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1 << 16);
        var buffer = new char[1 << 16];
        var held = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (held == buffer.Length) Array.Resize(ref buffer, buffer.Length * 2);
            var read = reader.Read(buffer, held, buffer.Length - held);
            if (read == 0) break;

            var filled = held + read;
            var start = 0;
            while (buffer.AsSpan(start, filled - start).IndexOf('\n') is var end and >= 0)
            {
                handle(buffer.AsSpan(start, end).TrimEnd('\r'));
                start += end + 1;
            }
            held = filled - start;
            if (start > 0 && held > 0) Array.Copy(buffer, start, buffer, 0, held);
        }
        if (held > 0) handle(buffer.AsSpan(0, held).TrimEnd('\r'));
    }
}

/// <summary>
/// Imports IMDb's datasets every <see cref="CatalogueOptions.ImdbHours"/>, reading them only when HEAD
/// says either file is not the version the layer was made from. A failed pass is tried again within
/// the hour, and the layer keeps the last whole import meanwhile.
/// </summary>
public sealed class ImdbWorker(
    ImdbDatasets datasets,
    CatalogueDatabase database,
    IOptions<CatalogueOptions> options,
    TimeProvider clock,
    ILogger<ImdbWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Retry = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var every = TimeSpan.FromHours(Math.Max(1, options.Value.ImdbHours));
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var worked = true;
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    worked = false;
                    logger.LogWarning("The IMDb datasets could not be imported: {Reason}", ex.Message);
                }
                await Task.Delay(worked ? every : Retry, clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>One pass; returns whether it imported.</summary>
    public async Task<bool> RunOnceAsync(CancellationToken ct)
    {
        var basics = await datasets.VersionAsync(ImdbDatasets.BasicsFile, ct);
        var rated = await datasets.VersionAsync(ImdbDatasets.RatingsFile, ct);
        if (basics is null || rated is null)
            throw new InvalidDataException("The dataset server does not say which version its files are.");

        var stamp = $"{basics.Tag} {basics.Modified:O}|{rated.Tag} {rated.Modified:O}";
        if (database.Meta(ImdbImport.StampKey) == stamp)
        {
            logger.LogDebug("The IMDb datasets are unchanged.");
            return false;
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromMinutes(30));
        var token = limit.Token;
        var edition = (basics.Modified ?? clock.GetUtcNow()).UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var work = System.Diagnostics.Stopwatch.StartNew();
        var outcome = await Task.Run(() => ImdbImport.Load(
            database,
            () => datasets.OpenAsync(ImdbDatasets.BasicsFile, token).GetAwaiter().GetResult(),
            () => datasets.OpenAsync(ImdbDatasets.RatingsFile, token).GetAwaiter().GetResult(),
            options.Value.MinVotes, edition, stamp, clock.GetUtcNow(), token), token);
        logger.LogInformation(
            "Imported the IMDb datasets of {Edition}: {Read} titles read, {Added} new candidates, {Changed} entries due, in {Seconds:0.0}s.",
            edition, outcome.Read, outcome.Added, outcome.Changed, work.Elapsed.TotalSeconds);
        return true;
    }
}
