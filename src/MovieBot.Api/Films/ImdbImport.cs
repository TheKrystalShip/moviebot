using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Api.Data;

namespace TheKrystalShip.MovieBot.Api.Films;

public sealed class ImdbOptions
{
    public const string Section = "Imdb";

    /// <summary>Whether the datasets are imported at all; without them nothing is ranked by IMDb.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Where the datasets are served from.</summary>
    public string BaseUrl { get; set; } = "https://datasets.imdbws.com/";

    /// <summary>How often the datasets are asked after. An unchanged day costs two HEAD requests.</summary>
    public int RefreshHours { get; set; } = 24;

    /// <summary>The votes a title needs to be kept; fewer than this and its rating means nothing.</summary>
    public int MinVotes { get; set; } = 100;
}

/// <summary>
/// Which copy of a dataset the server holds: its entity tag and the day it was last written, which
/// together change whenever the file does.
/// </summary>
public sealed record DatasetVersion(string Tag, DateTimeOffset? Modified);

/// <summary>IMDb's dataset files, asked after with HEAD and read as a stream.</summary>
public sealed class ImdbDatasets(HttpClient http, IOptions<ImdbOptions> options)
{
    public const string BasicsFile = "title.basics.tsv.gz";
    public const string RatingsFile = "title.ratings.tsv.gz";

    /// <summary>The server's version of a file; null when it does not answer with one.</summary>
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

    /// <summary>The file's compressed bytes as they arrive. The caller disposes the stream.</summary>
    public async Task<Stream> OpenAsync(string file, CancellationToken ct)
    {
        var response = await http.GetAsync(UriOf(file), HttpCompletionOption.ResponseHeadersRead, ct);
        try
        {
            response.EnsureSuccessStatusCode();
            return new DisposingStream(await response.Content.ReadAsStreamAsync(ct), response);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private Uri UriOf(string file)
    {
        var baseUrl = options.Value.BaseUrl;
        return new Uri(baseUrl.EndsWith('/') ? baseUrl + file : baseUrl + "/" + file);
    }

    /// <summary>A response body that releases the response with it.</summary>
    private sealed class DisposingStream(Stream inner, IDisposable owner) : Stream
    {
        public override bool CanRead => inner.CanRead;
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
/// Reads IMDb's two datasets into the table <see cref="ImdbRatings"/> reads.
///
/// The files are read as they arrive and never held whole: the ratings of the titles with enough
/// votes are gathered first (the only thing kept in memory), then the titles file is scanned a line
/// at a time and the films and shows among them written to a table of their own in batches. Only
/// when the last batch is in is that table swapped for the live one, in one transaction, so a reader
/// sees the whole of the old import or the whole of the new one, and no batch holds the database's
/// writer for more than a moment. An import that fails leaves the live table as it was.
///
/// What is kept: <c>movie</c> and <c>tvMovie</c> as films (the film database files a made-for-television
/// film with the films), <c>tvSeries</c> and <c>tvMiniSeries</c> as shows; nothing marked adult;
/// nothing with fewer votes than the floor. The data is used to rank and to show a title's IMDb
/// rating and votes, and is never served as a database.
/// </summary>
public static class ImdbImport
{
    /// <summary>The meta key holding which day's datasets the table holds.</summary>
    public const string EditionKey = "imdb_edition";

    /// <summary>The meta key holding the versions of the two files the table was made from.</summary>
    public const string StampKey = "imdb_stamp";

    /// <summary>The meta key holding when the table was swapped in.</summary>
    public const string ImportedKey = "imdb_imported";

    private const string Staging = "imdb_titles_next";
    private const int BatchRows = 20_000;

    private readonly record struct Row(long Id, string Kind, string Title, int? Year, int? Runtime, string Genres, double Rating, int Votes);

    private delegate void LineHandler(ReadOnlySpan<char> line);

    /// <summary>
    /// Replaces the table with the films and shows of the two gzip files that have at least
    /// <paramref name="minVotes"/> votes, each file opened only when it is read and disposed after, and
    /// records <paramref name="edition"/> and
    /// <paramref name="stamp"/> with it. Returns the rows kept. Synchronous: it is the work of one
    /// thread reading a stream, run off the request path by its caller.
    /// </summary>
    public static int Load(
        CinemaDatabase database, Func<Stream> openBasics, Func<Stream> openRatings, int minVotes, string edition, string stamp,
        TimeProvider clock, CancellationToken ct)
    {
        var ratings = ReadRatings(openRatings(), minVotes, ct);
        if (ratings.Count == 0) throw new InvalidDataException("The ratings file holds no title with enough votes.");

        database.Write(c =>
        {
            c.Execute($"DROP TABLE IF EXISTS {Staging}");
            var definition = c.First("SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'imdb_titles'", r => r.GetString(0))
                             ?? throw new InvalidOperationException("The database has no imdb_titles table.");
            c.Execute(definition.Replace("TABLE imdb_titles", "TABLE " + Staging, StringComparison.Ordinal));
        });

        try
        {
            var kept = 0;
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
                    if (batch.Count < BatchRows) return;
                    kept += Flush(database, batch);
                }, ct);
            }
            kept += Flush(database, batch);
            if (kept == 0) throw new InvalidDataException("The titles file holds no film or show with a rating.");

            database.Write(c =>
            {
                c.Execute("DROP TABLE imdb_titles");
                c.Execute($"ALTER TABLE {Staging} RENAME TO imdb_titles");
                Keep(c, EditionKey, edition);
                Keep(c, StampKey, stamp);
                Keep(c, ImportedKey, Sql.Instant(clock.GetUtcNow()));
            });
            return kept;
        }
        catch
        {
            try
            {
                database.Write(c => c.Execute($"DROP TABLE IF EXISTS {Staging}"));
            }
            catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException)
            {
                // The next import drops it first.
            }
            throw;
        }
    }

    private static Dictionary<int, (float Rating, int Votes)> ReadRatings(Stream gz, int minVotes, CancellationToken ct)
    {
        var ratings = new Dictionary<int, (float, int)>();
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
            if (IdOf(id) is not { } key
                || !int.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out var votes) || votes < minVotes
                || !float.TryParse(rating, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return;
            ratings[key] = (value, votes);
        }, ct);
        return ratings;
    }

    private static Row? Parse(ReadOnlySpan<char> line, Dictionary<int, (float Rating, int Votes)> ratings)
    {
        var rest = line;
        if (!Next(ref rest, out var id) || !Next(ref rest, out var type)) return null;
        var kind = type switch
        {
            "movie" or "tvMovie" => "film",
            "tvSeries" or "tvMiniSeries" => "show",
            _ => null,
        };
        if (kind is null || IdOf(id) is not { } key || !ratings.TryGetValue(key, out var rating)) return null;

        if (!Next(ref rest, out var title) || !Next(ref rest, out _) || !Next(ref rest, out var adult)
            || !Next(ref rest, out var start) || !Next(ref rest, out _) || !Next(ref rest, out var runtime))
            return null;
        var genres = rest;
        if (adult is not "0" || title is "\\N" || title.IsEmpty) return null;

        return new Row(key, kind, title.ToString(), NumberOf(start), NumberOf(runtime), GenresOf(genres), Math.Round(rating.Rating, 1), rating.Votes);
    }

    /// <summary>The next tab-separated field of <paramref name="rest"/>, which is left holding what follows it.</summary>
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

    private static int? IdOf(ReadOnlySpan<char> tconst) =>
        tconst.StartsWith("tt") && int.TryParse(tconst[2..], NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;

    private static int? NumberOf(ReadOnlySpan<char> field) =>
        int.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static string GenresOf(ReadOnlySpan<char> field) =>
        field is "\\N" or "" ? "," : "," + field.ToString() + ",";

    private static int Flush(CinemaDatabase database, List<Row> batch)
    {
        if (batch.Count == 0) return 0;
        var count = batch.Count;
        database.Write(c =>
        {
            using var insert = c.Command($"""
                INSERT OR REPLACE INTO {Staging} (imdb_id, kind, title, start_year, runtime, genres, rating, votes)
                VALUES ($id, $kind, $title, $year, $runtime, $genres, $rating, $votes)
                """);
            var id = insert.Parameters.Add("$id", Microsoft.Data.Sqlite.SqliteType.Integer);
            var kind = insert.Parameters.Add("$kind", Microsoft.Data.Sqlite.SqliteType.Text);
            var title = insert.Parameters.Add("$title", Microsoft.Data.Sqlite.SqliteType.Text);
            var year = insert.Parameters.Add("$year", Microsoft.Data.Sqlite.SqliteType.Integer);
            var runtime = insert.Parameters.Add("$runtime", Microsoft.Data.Sqlite.SqliteType.Integer);
            var genres = insert.Parameters.Add("$genres", Microsoft.Data.Sqlite.SqliteType.Text);
            var rating = insert.Parameters.Add("$rating", Microsoft.Data.Sqlite.SqliteType.Real);
            var votes = insert.Parameters.Add("$votes", Microsoft.Data.Sqlite.SqliteType.Integer);
            foreach (var row in batch)
            {
                id.Value = row.Id;
                kind.Value = row.Kind;
                title.Value = row.Title;
                year.Value = row.Year is { } y ? y : DBNull.Value;
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

    private static void Keep(Microsoft.Data.Sqlite.SqliteConnection c, string key, string value) =>
        c.Execute("INSERT INTO meta (key, value) VALUES ($key, $value) ON CONFLICT (key) DO UPDATE SET value = excluded.value",
            ("$key", key), ("$value", value));

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
/// Imports IMDb's datasets once a day. Each pass asks after the two files with HEAD and reads them
/// only when either is not the version the table was made from, so an unchanged day costs two small
/// requests. A pass that fails is asked again within the hour, and the table keeps serving the last
/// whole import meanwhile.
/// </summary>
public sealed class ImdbImporter(
    ImdbDatasets datasets,
    ImdbRatings ratings,
    CinemaDatabase database,
    IOptions<ImdbOptions> options,
    TimeProvider clock,
    ILogger<ImdbImporter> logger) : BackgroundService
{
    /// <summary>After startup has settled and the film database's own first passes have begun.</summary>
    private static readonly TimeSpan FirstPass = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan Retry = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled || settings.RefreshHours <= 0) return;
        var every = TimeSpan.FromHours(settings.RefreshHours);
        try
        {
            await Task.Delay(FirstPass, stoppingToken);
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
                await Task.Delay(worked || every < Retry ? every : Retry, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>One pass: imports when the server's files are not the ones the table holds. Returns whether it imported.</summary>
    public async Task<bool> RunOnceAsync(CancellationToken ct)
    {
        var basics = await datasets.VersionAsync(ImdbDatasets.BasicsFile, ct);
        var rated = await datasets.VersionAsync(ImdbDatasets.RatingsFile, ct);
        if (basics is null || rated is null)
            throw new InvalidDataException("The dataset server does not say which version its files are.");

        var stamp = $"{basics.Tag} {basics.Modified:O}|{rated.Tag} {rated.Modified:O}";
        if (database.Meta(ImdbImport.StampKey) == stamp && ratings.Edition is not null)
        {
            logger.LogDebug("The IMDb datasets are unchanged.");
            return false;
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromMinutes(30));
        ct = limit.Token;
        var edition = (basics.Modified ?? clock.GetUtcNow()).UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var work = System.Diagnostics.Stopwatch.StartNew();
        var rows = await Task.Run(() => ImdbImport.Load(
            database,
            () => datasets.OpenAsync(ImdbDatasets.BasicsFile, ct).GetAwaiter().GetResult(),
            () => datasets.OpenAsync(ImdbDatasets.RatingsFile, ct).GetAwaiter().GetResult(),
            options.Value.MinVotes, edition, stamp, clock, ct), ct);
        ratings.Refresh();
        logger.LogInformation("Imported {Rows} films and shows from the IMDb datasets of {Edition} in {Seconds:0.0}s.",
            rows, edition, work.Elapsed.TotalSeconds);
        return true;
    }
}
