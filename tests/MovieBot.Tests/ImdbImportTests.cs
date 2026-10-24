using System.IO.Compression;
using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Api.Films;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The import of IMDb's datasets: which rows of the two files are kept, that a whole import replaces
/// the table in one swap and a failed one changes nothing, and that an unchanged day costs two HEAD
/// requests. The files are small gzip texts written here; nothing is fetched from IMDb.
/// </summary>
public sealed class ImdbImportTests : IDisposable
{
    private const string Basics = "tconst\ttitleType\tprimaryTitle\toriginalTitle\tisAdult\tstartYear\tendYear\truntimeMinutes\tgenres\n";
    private const string Ratings = "tconst\taverageRating\tnumVotes\n";

    private static readonly TimeProvider Clock = TimeProvider.System;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "moviebot-tests", "imdb-" + Guid.NewGuid().ToString("n"));

    private CinemaDatabase Database => field ??= new CinemaDatabase(Path.Combine(_dir, "moviebot.db"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static byte[] Gz(string text)
    {
        using var memory = new MemoryStream();
        using (var gz = new GZipStream(memory, CompressionLevel.Fastest, leaveOpen: true)) gz.Write(Encoding.UTF8.GetBytes(text));
        return memory.ToArray();
    }

    private int Import(string basics, string ratings, int minVotes = 100, string edition = "20261024") =>
        ImdbImport.Load(Database, () => new MemoryStream(Gz(basics)), () => new MemoryStream(Gz(ratings)), minVotes, edition, "stamp", Clock, default);

    private List<(long Id, string Kind, string Title, int? Year, int? Runtime, string Genres, double Rating, int Votes)> Rows() => Database.Read(c => c.Query(
        "SELECT imdb_id, kind, title, start_year, runtime, genres, rating, votes FROM imdb_titles ORDER BY imdb_id",
        r => (r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetIntOrNull(3), r.GetIntOrNull(4), r.GetString(5), r.GetDouble(6), r.GetInt32(7))));

    private const string Mixed =
        Basics
        + "tt0000001\tmovie\tA Film\tA Film\t0\t2000\t\\N\t95\tHorror,Thriller\n"
        + "tt0000002\ttvSeries\tA Show\tA Show\t0\t2010\t2014\t45\tDrama\n"
        + "tt0000003\ttvMiniSeries\tA Miniseries\tA Miniseries\t0\t2016\t2016\t\\N\tCrime,Drama,Mystery\n"
        + "tt0000004\ttvMovie\tA Television Film\tA Television Film\t0\t1999\t\\N\t88\tComedy\n"
        + "tt0000005\ttvEpisode\tAn Episode\tAn Episode\t0\t2011\t\\N\t45\tDrama\n"
        + "tt0000006\tshort\tA Short\tA Short\t0\t2001\t\\N\t10\tAnimation\n"
        + "tt0000007\tmovie\tAn Adult Film\tAn Adult Film\t1\t2002\t\\N\t80\tAdult\n"
        + "tt0000008\tmovie\tFew Voted\tFew Voted\t0\t2003\t\\N\t90\tDrama\n"
        + "tt0000009\tmovie\tNo Details\tNo Details\t0\t\\N\t\\N\t\\N\t\\N\n"
        + "tt0000010\tmovie\tNo Rating\tNo Rating\t0\t2004\t\\N\t90\tDrama\n";

    private const string Voted =
        Ratings
        + "tt0000001\t7.6\t8000\n"
        + "tt0000002\t8.1\t150\n"
        + "tt0000003\t7.0\t100\n"
        + "tt0000004\t5.5\t3000\n"
        + "tt0000005\t9.0\t50000\n"
        + "tt0000006\t6.0\t5000\n"
        + "tt0000007\t5.0\t5000\n"
        + "tt0000008\t9.9\t99\n"
        + "tt0000009\t6.5\t400\n";

    [Fact]
    public void The_films_and_shows_with_enough_votes_are_kept_and_nothing_else()
    {
        var kept = Import(Mixed, Voted);

        Assert.Equal(5, kept);
        Assert.Equal(
            [
                (1L, "film", "A Film", (int?)2000, (int?)95, ",Horror,Thriller,", 7.6, 8000),
                (2L, "show", "A Show", 2010, 45, ",Drama,", 8.1, 150),
                (3L, "show", "A Miniseries", 2016, null, ",Crime,Drama,Mystery,", 7.0, 100),
                (4L, "film", "A Television Film", 1999, 88, ",Comedy,", 5.5, 3000),
                (9L, "film", "No Details", null, null, ",", 6.5, 400),
            ],
            Rows());
    }

    [Fact]
    public void The_vote_floor_is_the_settings_and_a_title_at_it_stays()
    {
        Import(Mixed, Voted, minVotes: 3000);

        Assert.Equal([1L, 4L], Rows().Select(r => r.Id));
    }

    [Fact]
    public void An_import_records_the_edition_and_replaces_every_row_of_the_one_before()
    {
        Import(Mixed, Voted, edition: "20261023");
        Import(Basics + "tt0000011\tmovie\tNew Film\tNew Film\t0\t2020\t\\N\t100\tDrama\n", Ratings + "tt0000011\t8.0\t1000\n", edition: "20261024");

        Assert.Equal([11L], Rows().Select(r => r.Id));
        Assert.Equal("20261024", Database.Meta(ImdbImport.EditionKey));
        Assert.Equal("stamp", Database.Meta(ImdbImport.StampKey));
        Assert.NotNull(Database.Meta(ImdbImport.ImportedKey));
        Assert.Equal(0, Database.Read(c => c.Count("SELECT COUNT(*) FROM sqlite_master WHERE name = 'imdb_titles_next'")));
    }

    [Fact]
    public void An_import_that_fails_leaves_the_table_as_it_was()
    {
        Import(Mixed, Voted);

        Assert.Throws<InvalidDataException>(() => Import("not\ta\ttitles file\n", Voted));
        Assert.Throws<InvalidDataException>(() => Import(Basics, Voted));
        Assert.Throws<InvalidDataException>(() => Import(Mixed, Ratings + "tt0000001\t7.6\t5\n"));

        Assert.Equal(5, Rows().Count);
        Assert.Equal(0, Database.Read(c => c.Count("SELECT COUNT(*) FROM sqlite_master WHERE name = 'imdb_titles_next'")));
    }

    [Fact]
    public void A_long_file_is_written_in_batches_and_swapped_in_whole()
    {
        var basics = new StringBuilder(Basics);
        var ratings = new StringBuilder(Ratings);
        for (var i = 1; i <= 45_000; i++)
        {
            basics.Append($"tt{i:D7}\tmovie\tFilm {i}\tFilm {i}\t0\t2000\t\\N\t90\tDrama\n");
            ratings.Append($"tt{i:D7}\t7.0\t{200 + i}\n");
        }

        Assert.Equal(45_000, Import(basics.ToString(), ratings.ToString()));
        Assert.Equal(45_000, Database.Read(c => c.Count("SELECT COUNT(*) FROM imdb_titles")));
    }

    [Fact]
    public async Task A_pass_imports_when_the_files_changed_and_costs_two_requests_when_they_did_not()
    {
        var server = new DatasetServer(Gz(Mixed), Gz(Voted));
        var ratings = new ImdbRatings(Database);
        var importer = Importer(server, ratings);

        Assert.True(await importer.RunOnceAsync(default));
        Assert.Equal(["HEAD title.basics.tsv.gz", "HEAD title.ratings.tsv.gz", "GET title.ratings.tsv.gz", "GET title.basics.tsv.gz"], server.Requests);
        Assert.Equal("20261023", ratings.Edition);
        Assert.Equal(7.6, ratings.Of("tt0000001")!.Value.Value);

        server.Requests.Clear();
        Assert.False(await importer.RunOnceAsync(default));
        Assert.Equal(["HEAD title.basics.tsv.gz", "HEAD title.ratings.tsv.gz"], server.Requests);

        server.Version = "\"second\"";
        server.Requests.Clear();
        Assert.True(await importer.RunOnceAsync(default));
        Assert.Equal(4, server.Requests.Count);
    }

    [Fact]
    public async Task A_server_that_cannot_be_reached_leaves_the_table_and_the_pass_fails()
    {
        Import(Mixed, Voted);
        var importer = Importer(new DatasetServer(Gz(Mixed), Gz(Voted)) { Down = true }, new ImdbRatings(Database));

        await Assert.ThrowsAsync<HttpRequestException>(() => importer.RunOnceAsync(default));
        Assert.Equal(5, Rows().Count);
    }

    private ImdbImporter Importer(DatasetServer server, ImdbRatings ratings)
    {
        var options = Options.Create(new ImdbOptions { BaseUrl = "https://datasets.invalid/" });
        return new ImdbImporter(new ImdbDatasets(new HttpClient(server), options), ratings, Database, options, Clock, NullLogger<ImdbImporter>.Instance);
    }

    /// <summary>IMDb's dataset server as the tests answer it: HEAD names a version, GET gives the file.</summary>
    private sealed class DatasetServer(byte[] basics, byte[] ratings) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public string Version { get; set; } = "\"first\"";
        public bool Down { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Down) throw new HttpRequestException("unreachable");
            var file = request.RequestUri!.Segments[^1];
            Requests.Add($"{request.Method} {file}");
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = request.Method == HttpMethod.Head
                    ? new ByteArrayContent([])
                    : new ByteArrayContent(file == ImdbDatasets.BasicsFile ? basics : ratings),
            };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(Version);
            response.Content.Headers.LastModified = DateTimeOffset.Parse("2026-10-23T00:48:50Z");
            return Task.FromResult(response);
        }
    }
}
