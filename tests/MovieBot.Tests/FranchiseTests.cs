using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Api.Films;
using TheKrystalShip.MovieBot.Api.People;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>The film database's daily list of series ids, as the tests answer it: the files it is given, and 403 for any other day.</summary>
public sealed class FranchiseExportStub : HttpMessageHandler
{
    private readonly Dictionary<string, string> _files = [];

    /// <summary>Publishes a day's list, one line each.</summary>
    public void Publish(DateOnly day, params string[] lines) => _files[$"collection_ids_{day:MM_dd_yyyy}.json.gz"] = string.Join("\n", lines);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var file = request.RequestUri!.AbsolutePath[(request.RequestUri.AbsolutePath.LastIndexOf('/') + 1)..];
        if (!_files.TryGetValue(file, out var text)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        var packed = new MemoryStream();
        using (var gzip = new GZipStream(packed, CompressionLevel.Fastest, leaveOpen: true))
            gzip.Write(Encoding.UTF8.GetBytes(text));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(packed.ToArray()) });
    }
}

public sealed class FranchiseFixture : SessionFixture
{
    public FranchiseExportStub Export { get; } = new();

    protected override Dictionary<string, string?> MoreSettings() => new()
    {
        ["Franchises:Enabled"] = "true",
        ["Franchises:WarmMinutes"] = "0",
        ["Franchises:ExportBaseUrl"] = "https://exports.invalid/p/exports/",
        ["Franchises:RequestsPerSecond"] = "1000",
        ["Franchises:MinFilms"] = "2",
        ["Franchises:MinVotes"] = "10000",
    };

    protected override void MoreServices(IServiceCollection services) =>
        services.AddHttpClient<FranchiseExport>().ConfigurePrimaryHttpMessageHandler(() => Export);
}

/// <summary>
/// The franchises: the curated series first, then every series the film database's daily list names
/// that has enough released films and votes, measured by a scan that asks about each series once until
/// it is due, and shown to a person with what they are partway through first.
/// </summary>
public sealed class FranchiseTests : IClassFixture<FranchiseFixture>
{
    private readonly FranchiseFixture _fixture;
    private static readonly SemaphoreSlim Scanned = new(1, 1);
    private static bool _scanned;

    public FranchiseTests(FranchiseFixture fixture)
    {
        _fixture = fixture;
        var future = $"{FilmDatabaseStub.ThisYear + 2}-06-01";
        var db = fixture.FilmDatabase;
        db.Collections[501] = FilmDatabaseStub.Collection(501, "Avatar Collection", (949, "Heat", "1995-12-15", 9000, false), (22, "Pirates of the Caribbean", "2003-07-09", 21000, false));
        db.Collections[502] = FilmDatabaseStub.Collection(502, "Lonely Collection", (7101, "The Cellar Door", "2002-10-31", 40000, false), (7302, "Next Summer", future, 0, false));
        db.Collections[503] = FilmDatabaseStub.Collection(503, "Blue Collection", (7102, "Hollow Hill", "2004-09-10", 6000, false), (7000, "Old Letters", "1999-03-04", 3000, false), (7001, "Hidden", "2001-01-01", 90000, true));
        db.Collections[504] = FilmDatabaseStub.Collection(504, "Star Wars Collection", (7201, "Echo Valley", "2019-05-05", 6000, false), (7202, "Again and Again", "2021-02-02", 6000, false));
        db.Collections[505] = FilmDatabaseStub.Collection(505, "Twin Collection", (7203, "Rerun", "2018-08-08", 6000, false), (5000, "Dead Heat", "1988-05-06", 6000, false));
        db.Collections[506] = FilmDatabaseStub.Collection(506, "Twin Collection", (7004, "Silver Harbour", "2015-01-01", 6000, false), (7600, "Tiny One", "2015-01-01", 6000, false));
        // Only yesterday's list is published, so the scan reads back a day; 508 is a series the database does not hold.
        fixture.Export.Publish(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
            "{\"id\":501,\"name\":\"Avatar Collection\"}", "{\"id\":502,\"name\":\"Lonely Collection\"}", "not a line",
            "{\"id\":503,\"name\":\"Blue Collection\"}", "{\"id\":504,\"name\":\"Star Wars Collection\"}", "{\"id\":505,\"name\":\"Twin Collection\"}",
            "{\"id\":506,\"name\":\"Twin Collection\"}", "{\"id\":508,\"name\":\"Gone Collection\"}", "{\"id\":10,\"name\":\"Star Wars Collection\"}");
    }

    private Franchises Franchises => _fixture.Services.GetRequiredService<Franchises>();

    private async Task ScannedAsync()
    {
        await Scanned.WaitAsync();
        try
        {
            if (_scanned) return;
            Assert.Equal(0, await Franchises.ScanAsync(CancellationToken.None));
            _scanned = true;
        }
        finally
        {
            Scanned.Release();
        }
    }

    private HttpClient Signed(string userId)
    {
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _fixture.TokenFor(userId, "Gil"));
        return client;
    }

    private List<(int Id, int Films, int Votes)> Measured() =>
        _fixture.Services.GetRequiredService<CinemaDatabase>().Read(c => c.Query(
            "SELECT collection_id, films, votes FROM franchises ORDER BY collection_id", r => (r.GetInt32(0), r.GetInt32(1), r.GetInt32(2))));

    [Fact]
    public async Task A_scan_measures_each_listed_series_once_counting_only_released_films_the_database_does_not_mark_adult_and_none_for_a_series_it_does_not_hold()
    {
        await ScannedAsync();

        Assert.Equal([(10, 2, 29740), (501, 2, 30000), (502, 1, 40000), (503, 2, 9000), (504, 2, 12000), (505, 2, 12000), (506, 2, 12000), (508, 0, 0)], Measured());
        var asked = _fixture.FilmDatabase.Requests.Count(r => r.Contains("/collection/"));

        Assert.Equal(0, await Franchises.ScanAsync(CancellationToken.None));
        Assert.Equal(asked, _fixture.FilmDatabase.Requests.Count(r => r.Contains("/collection/")));
    }

    [Fact]
    public async Task The_curated_series_come_first_then_the_measured_ones_by_votes_each_by_its_own_id()
    {
        await ScannedAsync();

        var all = Franchises.All;
        Assert.Equal(Franchises.Curated.Select(s => s.Id), all.Take(Franchises.Curated.Count).Select(s => s.Id));
        var scanned = all.Skip(Franchises.Curated.Count).Select(s => (s.Id, s.Title, s.DatabaseId)).ToList();
        Assert.Equal([("avatar", "Avatar", 501), ("star-wars-504", "Star Wars", 504), ("twin", "Twin", 505), ("twin-506", "Twin", 506)], scanned);
        Assert.Equal(all.Count, all.Select(s => s.Id).Distinct().Count());
    }

    [Fact]
    public async Task The_category_pages_every_series_with_what_a_person_is_partway_through_first()
    {
        await ScannedAsync();
        _fixture.Services.GetRequiredService<WatchRecord>().Record("f1", SessionFixture.Heat, 5900, 6000, DateTimeOffset.UtcNow);

        // A series is ordered by what was last answered for it, which the warmer keeps; here the first page asks.
        await Signed("f1").GetFromJsonAsync<ShelfSectionPageView>("/api/discover/sections/franchises");
        var page = (await Signed("f1").GetFromJsonAsync<ShelfSectionPageView>("/api/discover/sections/franchises"))!;
        Assert.Equal(Franchises.All.Count, page.Count);
        Assert.Null(page.Next);
        Assert.Equal(["star-wars", "avatar"], page.Shelves.Take(2).Select(s => s.Id));
        Assert.Equal((1, "Pirates of the Caribbean"), (page.Shelves[1].Watched, page.Shelves[1].Next));

        var index = (await Signed("f1").GetFromJsonAsync<DiscoverIndexView>("/api/discover"))!;
        var section = index.Sections.Single(s => s.Id == "franchises");
        Assert.Equal(Franchises.All.Count, section.Count);
        Assert.True(section.Shelves.Count <= DiscoverShelves.RowTiles);
        Assert.Equal(["star-wars", "avatar"], section.Shelves.Take(2).Select(s => s.Id));

        var other = (await Signed("f2").GetFromJsonAsync<ShelfSectionPageView>("/api/discover/sections/franchises"))!;
        // The curated series the database holds nothing under are left off; the rest keep their order.
        Assert.Equal(["star-wars", "avatar", "star-wars-504", "twin", "twin-506"], other.Shelves.Select(s => s.Id));
    }

    [Fact]
    public async Task A_measured_series_is_a_shelf_of_its_films()
    {
        await ScannedAsync();

        var shelf = (await Signed("f3").GetFromJsonAsync<ShelfView>("/api/discover/shelves/avatar"))!;
        Assert.Equal(["Heat", "Pirates of the Caribbean"], shelf.Titles.Select(t => t.Title));
    }

    [Fact]
    public async Task Only_a_category_of_series_is_paged_on_its_own()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Signed("f4").GetAsync("/api/discover/sections/moods")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Signed("f4").GetAsync("/api/discover/sections/franchises?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _fixture.CreateClient().GetAsync("/api/discover/sections/franchises")).StatusCode);
    }

    [Theory]
    [InlineData("Star Wars Collection", "Star Wars")]
    [InlineData("Planet of the Apes (Reboot) Collection", "Planet of the Apes (Reboot)")]
    [InlineData("Collection", "Collection")]
    public void A_series_is_titled_without_the_word_collection(string name, string title) => Assert.Equal(title, Franchises.TitleOf(name));

    [Theory]
    [InlineData("Planet of the Apes (Reboot)", "planet-of-the-apes-reboot")]
    [InlineData("Ocean's", "oceans")]
    [InlineData("Amélie: Part 2", "amelie-part-2")]
    [InlineData("!!!", "")]
    public void A_series_id_is_its_title_in_lowercase_letters_digits_and_hyphens(string title, string slug) => Assert.Equal(slug, Franchises.Slug(title));

    [Theory]
    [InlineData("{\"id\":10,\"name\":\"Star Wars Collection\"}", 10)]
    [InlineData("{\"name\":\"No id\"}", null)]
    [InlineData("{\"id\":\"ten\"}", null)]
    [InlineData("not json", null)]
    [InlineData("", null)]
    public void A_line_of_the_list_is_read_for_its_id(string line, int? id) => Assert.Equal(id, FranchiseExport.IdOf(line));

    [Fact]
    public void No_curated_series_shares_an_id_with_another_shelf()
    {
        var ids = DiscoverShelves.Fixed.Select(s => s.Id).Concat(Franchises.Curated.Select(s => s.Id)).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
