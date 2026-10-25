using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TheKrystalShip.MovieBot.Acquire.Configuration;
using TheKrystalShip.MovieBot.Catalogue;
using TheKrystalShip.MovieBot.Catalogue.Data;
using TheKrystalShip.MovieBot.Catalogue.Entries;
using TheKrystalShip.MovieBot.Catalogue.Tmdb;
using TheKrystalShip.MovieBot.Catalogue.Wikidata;
using Xunit;
using CatalogueImdb = TheKrystalShip.MovieBot.Catalogue.Imdb.ImdbImport;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Reading the catalogue: the one list query with every filter and order, its facets, a title's page and
/// season, people, franchises and words resolved to filters. The catalogue is written here by the same
/// layers and builder the service runs (two Fast &amp; Furious films and Chernobyl), and read through the
/// read-only reader the API holds.
/// </summary>
public sealed class CatalogueQueriesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "moviebot-tests", "queries-" + Guid.NewGuid().ToString("n"));
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
    private readonly CatalogueDatabase _database;
    private readonly CatalogueQueries _queries;
    private readonly long _fast, _two, _chernobyl;

    private const string ShowBasics = "tt7366338\ttvMiniSeries\tChernobyl\tChernobyl\t0\t2019\t2019\t330\tDrama,History,Thriller\n";
    private const string ShowRatings = "tt7366338\t9.3\t900000\n";

    public CatalogueQueriesTests()
    {
        _database = new CatalogueDatabase(Path.Combine(_dir, CatalogueDatabase.FileName));
        var builder = new EntryBuilder(_clock);
        CatalogueImdb.Load(_database,
            () => new MemoryStream(LocalCatalogueTests.Gz(LocalCatalogueTests.FastBasics + ShowBasics)),
            () => new MemoryStream(LocalCatalogueTests.Gz(LocalCatalogueTests.FastRatings + ShowRatings)),
            100, "20261025", "stamp", _clock.GetUtcNow(), default);
        long Id(string imdb) => _database.Read(c => c.First("SELECT id FROM titles WHERE imdb_id = $i", r => r.GetInt64(0), ("$i", imdb)));
        (_fast, _two, _chernobyl) = (Id("tt0232500"), Id("tt0322259"), Id("tt7366338"));

        var layer = new TmdbLayer(_database, null!, builder, _clock, NullLogger<TmdbLayer>.Instance);
        layer.Keep(_fast, new TmdbRef(9799, "film"), LocalCatalogueTests.Fast(), []);
        layer.Keep(_two, new TmdbRef(584, "film"), LocalCatalogueTests.TwoFast(), []);
        layer.Keep(_chernobyl, new TmdbRef(87108, "show"), Chernobyl(), [(1, ChernobylSeason())]);

        var wikidata = new WikidataWorker(null!, _database, builder, Options.Create(new CatalogueOptions()), _clock, NullLogger<WikidataWorker>.Instance);
        wikidata.Keep([(_fast, "tt0232500"), (_two, "tt0322259")], new Dictionary<string, (string, List<WikidataClaim>)>
        {
            ["tt0232500"] = ("Q155476", [new("P840", "Q65", "Los Angeles", null), new("P156", "Q222867", "2 Fast 2 Furious", null),
                new("P179", "Q1576873", "Fast & Furious", "1")]),
            ["tt0322259"] = ("Q222867", [new("P840", "Q8652", "Miami", null), new("P155", "Q155476", "The Fast and the Furious", null),
                new("P179", "Q1576873", "Fast & Furious", "2")]),
        });
        _database.Write(c => FactItems.Build(c));
        _database.Write(c =>
        {
            c.Execute("INSERT INTO rankings (list, rank, title_id, fetched_at) VALUES ('trending-films', 1, $two, $at), ('trending-films', 2, $fast, $at)",
                ("$two", _two), ("$fast", _fast), ("$at", _clock.GetUtcNow()));
        });
        _queries = new CatalogueQueries(new CatalogueReader(_database.Path));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static JsonNode Chernobyl() => JsonNode.Parse("""
        {
          "id": 87108, "name": "Chernobyl", "original_name": "Chernobyl", "original_language": "en", "status": "Ended",
          "first_air_date": "2019-05-06", "last_air_date": "2019-06-03", "poster_path": "/chernobyl.jpg", "backdrop_path": "/cb.jpg",
          "overview": "The true story of one of the worst man-made catastrophes in history. A second sentence.", "popularity": 50.0,
          "vote_average": 8.7, "vote_count": 6000, "number_of_seasons": 1, "number_of_episodes": 5,
          "genres": [{"id": 18, "name": "Drama"}], "networks": [{"id": 49, "name": "HBO", "origin_country": "US"}],
          "created_by": [{"id": 500, "name": "Craig Mazin", "profile_path": "/craig.jpg"}],
          "seasons": [{"season_number": 1, "poster_path": "/s1.jpg"}],
          "aggregate_credits": {"cast": [{"id": 501, "name": "Jared Harris", "roles": [{"character": "Valery Legasov"}], "profile_path": "/jared.jpg"}], "crew": []},
          "content_ratings": {"results": [{"iso_3166_1": "US", "rating": "TV-MA"}]},
          "external_ids": {"imdb_id": "tt7366338"}
        }
        """)!;

    private static JsonNode ChernobylSeason() => TmdbLayer.PruneSeason(JsonNode.Parse("""
        {"season_number": 1, "name": "Season 1", "air_date": "2019-05-06", "poster_path": "/s1.jpg", "episodes": [
          {"episode_number": 1, "name": "1:23:45", "air_date": "2019-05-06", "runtime": 60, "still_path": "/e1.jpg"},
          {"episode_number": 2, "name": "Please Remain Calm", "air_date": "2019-05-13", "runtime": 65, "still_path": "/e2.jpg"}
        ]}
        """)!);

    private static TitleQuery Query(params (string Name, string Value)[] pairs) =>
        TitleQuery.Parse(name => pairs.Where(p => p.Name == name).Select(p => p.Value).ToList()) is ({ } query, null)
            ? query
            : throw new InvalidOperationException("The query was refused.");

    private List<string> Ids(params (string, string)[] pairs) => _queries.Titles(Query(pairs)).Titles.Select(t => t.Id).ToList();

    [Fact]
    public void Every_list_is_the_one_query_and_its_filters_combine()
    {
        Assert.Equal(["tt7366338", "tt0232500", "tt0322259"], Ids()); // The most popular first.
        Assert.Equal(["tt0232500", "tt0322259"], Ids(("kind", "film")));
        Assert.Equal(["tt7366338"], Ids(("genre", "History")));
        Assert.Equal(["tt7366338", "tt0322259"], Ids(("from", "2002")));
        Assert.Equal(["tt0322259"], Ids(("from", "2002"), ("kind", "film")));
        Assert.Equal(["tt0232500"], Ids(("fact", "P840:Q65")));
        Assert.Equal(["tt0232500"], Ids(("keyword", "k830")));
        Assert.Equal(["tt7366338"], Ids(("company", "s49")));
        Assert.Equal(["tt7366338", "tt0232500"], Ids(("minRating", "6.5")));
        Assert.Equal(["tt0232500", "tt0322259"], Ids(("q", "fast furi")));
        Assert.Equal(["tt0232500"], Ids(("q", "todo gas")));
        Assert.Empty(Ids(("q", "fast"), ("kind", "show")));
    }

    [Fact]
    public void A_list_with_an_order_of_its_own_is_in_it_unless_another_is_asked()
    {
        Assert.Equal(["tt0322259", "tt0232500"], Ids(("ranking", "trending-films")));
        Assert.Equal(["tt0232500", "tt0322259"], Ids(("ranking", "trending-films"), ("sort", "oldest")));
        Assert.Equal(["tt0232500", "tt0322259"], Ids(("franchise", "c9485")));
        Assert.Equal(["tt0322259"], Ids(("similarTo", "tt0232500")));
        Assert.Equal(["tt7366338", "tt0232500", "tt0322259"], Ids(("sort", "rating")));
        Assert.Equal(["tt7366338", "tt0322259", "tt0232500"], Ids(("sort", "newest")));
    }

    [Fact]
    public void A_person_s_list_says_their_part_in_each_title()
    {
        var walker = _queries.Terms("paul walker").First(t => t.Filter == "person");
        var page = _queries.Titles(Query(("person", walker.Value)));

        var card = Assert.Single(page.Titles);
        Assert.Equal("tt0232500", card.Id);
        Assert.Equal(new Credit("cast", "Brian O'Conner"), card.Credit);
    }

    [Fact]
    public void A_viewer_s_ids_narrow_a_list()
    {
        Assert.Equal(["tt0232500"], _queries.Titles(Query() with { Only = ["tt0232500", "tt9999999"] }).Titles.Select(t => t.Id));
        Assert.Equal(["tt7366338", "tt0322259"], _queries.Titles(Query() with { Except = ["tt0232500"] }).Titles.Select(t => t.Id));
    }

    [Fact]
    public void Pages_say_how_many_there_are_and_which_is_next()
    {
        var first = _queries.Titles(Query(("size", "2")));
        var second = _queries.Titles(Query(("size", "2"), ("page", "2")));

        Assert.Equal((3, 2), (first.Total, first.Next));
        Assert.Single(second.Titles);
        Assert.Null(second.Next);
    }

    [Theory]
    [InlineData("kind", "anime")]
    [InlineData("length", "endless")]
    [InlineData("fact", "Q65")]
    [InlineData("franchise", "9485")]
    [InlineData("similarTo", "fast")]
    [InlineData("keyword", "830")]
    [InlineData("sort", "order")]
    public void A_value_a_filter_does_not_take_is_refused(string name, string value)
    {
        var (query, refusal) = TitleQuery.Parse(n => n == name ? [value] : []);
        Assert.Null(query);
        Assert.NotNull(refusal);
    }

    [Fact]
    public void A_card_is_the_same_wherever_the_title_is_listed()
    {
        var everywhere = new[]
        {
            Query(), Query(("q", "furious")), Query(("ranking", "trending-films")), Query(("franchise", "c9485")),
            Query(("fact", "P840:Q65")), Query(("genre", "Action")),
        }.Select(q => _queries.Titles(q).Titles.Single(t => t.Id == "tt0232500")).ToList();

        static string Json(TitleCard card) => System.Text.Json.JsonSerializer.Serialize(card);
        Assert.All(everywhere, card => Assert.Equal(Json(everywhere[0]), Json(card)));
        Assert.Equal(Json(everywhere[0]), Json(_queries.CardsOf(["tt0232500"]).Single()));
        var card = everywhere[0];
        Assert.Equal(new Rating(6.8, 468847, "imdb"), card.Rating);
        Assert.Equal("/images/w500/gqY0ITBgT7A82poL9jv851qdnIb.jpg", card.Poster);
        Assert.Equal(["Action", "Crime", "Thriller"], card.Genres);
        Assert.Equal(["Rob Cohen"], card.MadeBy);
        Assert.Equal(["Paul Walker", "Vin Diesel"], card.Starring);
        Assert.Equal("Dominic Toretto is a Los Angeles street racer.", card.Plot);
    }

    [Fact]
    public void A_title_s_page_holds_everything_the_catalogue_does()
    {
        var page = _queries.Title("tt0232500")!;

        Assert.Equal(new SourceRatings(new Rating(6.8, 468847, "imdb"), new Rating(7.0, 11282, "tmdb")), page.Ratings);
        Assert.Equal("PG-13", page.Certificates["US"]);
        Assert.Equal("/images/w500/logo-en.png", page.Logo);
        Assert.Equal("/images/w185/paul.jpg", page.Cast[0].Portrait);
        Assert.Equal([new Named("Q65", "Los Angeles")], page.Facts.SetIn);
        Assert.Equal(["tt0322259"], page.FollowedBy.Select(t => t.Id));
        Assert.Equal([new FranchiseRef("c9485", "The Fast and the Furious Collection", "1")], page.Franchises);
        Assert.Equal(["tt0322259"], page.Similar.Select(t => t.Id));
        Assert.Equal(38_000_000, page.Budget);
        Assert.Null(_queries.Title("tt9999998")); // A candidate is shown nowhere.
    }

    [Fact]
    public void A_show_s_page_has_its_seasons_and_each_season_its_episodes()
    {
        var page = _queries.Title("tt7366338")!;
        Assert.Equal(["Craig Mazin"], page.MadeBy);
        Assert.Equal(2019, page.EndYear);
        Assert.Equal([new SeasonSummary(1, "Season 1", "2019-05-06", "/images/w500/s1.jpg", 2)], page.Seasons);
        Assert.Equal([new Named("s49", "HBO")], page.Networks);

        var season = _queries.Season("tt7366338", 1)!;
        Assert.Equal(["1:23:45", "Please Remain Calm"], season.Episodes.Select(e => e.Name));
        Assert.Equal("/images/w300/e1.jpg", season.Episodes[0].Still);
        Assert.Null(_queries.Season("tt7366338", 2));
    }

    [Fact]
    public void Facets_offer_the_same_options_whatever_is_picked_and_count_with_the_other_picks()
    {
        var facets = _queries.Facets(Query(("kind", "film")));
        var kind = facets.Sections.Single(s => s.Id == "kind");
        var genre = facets.Sections.Single(s => s.Id == "genre");

        Assert.Equal(2, facets.Total);
        Assert.Equal([("film", 2), ("show", 1)], kind.Options.Select(o => (o.Value, o.Count))); // Kind's own pick does not count it.
        Assert.Equal(0, genre.Options.Single(o => o.Value == "History").Count); // Offered, with nothing under it at kind=film.
        Assert.Equal(2, genre.Options.Single(o => o.Value == "Action").Count);
        Assert.Equal("English", facets.Sections.Single(s => s.Id == "language").Options.Single().Label);
    }

    [Fact]
    public void Words_resolve_to_the_filters_that_take_them()
    {
        Assert.Contains(_queries.Terms("los ang"), t => t is { Filter: "fact", Value: "P840:Q65", Detail: "set in" });
        Assert.Contains(_queries.Terms("hbo"), t => t is { Filter: "company", Value: "s49" });
        Assert.Contains(_queries.Terms("revenge"), t => t is { Filter: "keyword", Value: "k9748" });
        Assert.Contains(_queries.Terms("thrill"), t => t is { Filter: "genre", Value: "Thriller" });
        Assert.Contains(_queries.Terms("collection"), t => t is { Filter: "franchise", Value: "c9485" });
    }

    [Fact]
    public void Franchises_are_listed_with_their_titles_counted()
    {
        var page = _queries.Franchises(1, 20);
        var collection = Assert.Single(page.Franchises);
        Assert.Equal(new FranchiseCard("c9485", "collection", "The Fast and the Furious Collection", "/images/w500/coll.jpg",
            "/images/w1280/collb.jpg", 2, 2001, 2003), collection);
        Assert.Equal(collection, _queries.Franchise("c9485")!.Card);
    }

    [Fact]
    public void A_person_is_named_with_how_many_listed_titles_they_are_in()
    {
        var harris = long.Parse(_queries.Terms("jared").First(t => t.Filter == "person").Value);
        Assert.Equal(new Person(harris, "Jared Harris", "/images/w185/jared.jpg", null, 1), _queries.Person(harris));
    }

    [Theory]
    [InlineData("A short plot.", "A short plot.")]
    [InlineData("Dominic Toretto is a Los Angeles street racer. Brian infiltrates his crew.", "Dominic Toretto is a Los Angeles street racer.")]
    public void A_plot_is_an_overview_s_first_sentence(string overview, string plot) =>
        Assert.Equal(plot, CatalogueQueries.Plot(overview));

    [Fact]
    public void A_reader_says_when_the_catalogue_cannot_be_read()
    {
        Assert.Null(new CatalogueReader(_database.Path).Problem());
        Assert.Contains("No catalogue", new CatalogueReader(Path.Combine(_dir, "absent.db")).Problem());
    }
}
