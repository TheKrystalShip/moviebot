using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TheKrystalShip.MovieBot.Acquire.Configuration;
using TheKrystalShip.MovieBot.Catalogue;
using TheKrystalShip.MovieBot.Catalogue.Data;
using TheKrystalShip.MovieBot.Catalogue.Entries;
using TheKrystalShip.MovieBot.Catalogue.Images;
using TheKrystalShip.MovieBot.Catalogue.Tmdb;
using TheKrystalShip.MovieBot.Catalogue.Wikidata;
using Xunit;
using CatalogueImdb = TheKrystalShip.MovieBot.Catalogue.Imdb.ImdbImport;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The local catalogue: IMDb's layer makes the candidates, the film database's layer and Wikidata's
/// are kept on the same title, and the entry is built from all three by rule, whatever order they
/// arrived in. The Fast and the Furious (tt0232500) is the title throughout. Nothing is fetched from
/// any source: the files and answers are written here.
/// </summary>
public sealed class LocalCatalogueTests : IDisposable
{
    internal const string Basics = "tconst\ttitleType\tprimaryTitle\toriginalTitle\tisAdult\tstartYear\tendYear\truntimeMinutes\tgenres\n";
    internal const string Ratings = "tconst\taverageRating\tnumVotes\n";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "moviebot-tests", "catalogue-" + Guid.NewGuid().ToString("n"));
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));

    private CatalogueDatabase Database => field ??= new CatalogueDatabase(Path.Combine(_dir, CatalogueDatabase.FileName));

    private EntryBuilder Builder => field ??= new EntryBuilder(_clock);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    internal static byte[] Gz(string text)
    {
        using var memory = new MemoryStream();
        using (var gz = new GZipStream(memory, CompressionLevel.Fastest, leaveOpen: true)) gz.Write(Encoding.UTF8.GetBytes(text));
        return memory.ToArray();
    }

    private CatalogueImdb.Outcome Import(string basics, string ratings, string edition = "20261025") =>
        CatalogueImdb.Load(Database, () => new MemoryStream(Gz(basics)), () => new MemoryStream(Gz(ratings)), 100, edition, edition,
            _clock.GetUtcNow(), default);

    internal const string FastBasics =
        Basics
        + "tt0232500\tmovie\tThe Fast and the Furious\tThe Fast and the Furious\t0\t2001\t\\N\t106\tAction,Crime,Thriller\n"
        + "tt0322259\tmovie\t2 Fast 2 Furious\t2 Fast 2 Furious\t0\t2003\t\\N\t107\tAction,Crime,Thriller\n"
        + "tt9999998\tmovie\tFew Voted\tFew Voted\t0\t2003\t\\N\t90\tDrama\n";

    internal const string FastRatings =
        Ratings
        + "tt0232500\t6.8\t468847\n"
        + "tt0322259\t5.9\t300000\n"
        + "tt9999998\t9.9\t99\n";

    private long TitleId(string imdb) => Database.Read(c => c.First("SELECT id FROM titles WHERE imdb_id = $imdb", r => r.GetInt64(0), ("$imdb", imdb)));

    private TmdbLayer Layer(HttpMessageHandler? handler = null) => new(
        Database, Source(handler ?? new Answers()), Builder, _clock, NullLogger<TmdbLayer>.Instance);

    private TmdbSource Source(HttpMessageHandler handler) => new(
        new HttpClient(handler),
        new TmdbGate(Options.Create(new CatalogueOptions { TmdbPerSecond = 1000 }), TimeProvider.System),
        Options.Create(new TmdbOptions { ReadAccessToken = "token" }),
        NullLogger<TmdbSource>.Instance);

    /// <summary>The film database's description of The Fast and the Furious, trimmed to what a description carries that the catalogue reads, and a little it does not.</summary>
    internal static JsonNode Fast(string? poster = "/gqY0ITBgT7A82poL9jv851qdnIb.jpg", string title = "The Fast and the Furious",
        string? released = "2001-06-22", string status = "Released") => JsonNode.Parse($$"""
        {
          "id": 9799, "imdb_id": "tt0232500", "title": "{{title}}", "original_title": "The Fast and the Furious",
          "original_language": "en", "overview": "Dominic Toretto is a Los Angeles street racer.", "tagline": "Live life 1/4 mile at a time.",
          "status": "{{status}}", "release_date": {{(released is null ? "null" : $"\"{released}\"")}}, "runtime": 107,
          "poster_path": {{(poster is null ? "null" : $"\"{poster}\"")}}, "backdrop_path": "/backdrop.jpg", "popularity": 21.5,
          "vote_average": 6.987, "vote_count": 11282,
          "genres": [{"id": 28, "name": "Action"}, {"id": 80, "name": "Crime"}],
          "production_companies": [{"id": 33, "name": "Universal Pictures", "origin_country": "US", "logo_path": "/u.png"}],
          "production_countries": [{"iso_3166_1": "US", "name": "United States of America"}],
          "spoken_languages": [{"iso_639_1": "en", "name": "English"}, {"iso_639_1": "es", "name": "Spanish"}],
          "belongs_to_collection": {"id": 9485, "name": "The Fast and the Furious Collection", "poster_path": "/coll.jpg", "backdrop_path": "/collb.jpg"},
          "budget": 38000000,
          "credits": {
            "cast": [
              {"id": 8167, "name": "Paul Walker", "character": "Brian O'Conner", "profile_path": "/paul.jpg", "known_for_department": "Acting"},
              {"id": 12835, "name": "Vin Diesel", "character": "Dominic Toretto", "profile_path": "/vin.jpg", "known_for_department": "Acting"}
            ],
            "crew": [
              {"id": 20, "name": "Rob Cohen", "job": "Director", "department": "Directing", "profile_path": null},
              {"id": 21, "name": "Someone", "job": "Key Grip", "department": "Crew", "profile_path": null}
            ]
          },
          "keywords": {"keywords": [{"id": 830, "name": "car race"}, {"id": 9748, "name": "revenge"}]},
          "release_dates": {"results": [
            {"iso_3166_1": "US", "release_dates": [{"certification": "PG-13", "release_date": "2001-06-22T00:00:00.000Z", "type": 3}]},
            {"iso_3166_1": "ES", "release_dates": [{"certification": "13", "release_date": "2001-09-21T00:00:00.000Z", "type": 3}]}
          ]},
          "alternative_titles": {"titles": [{"iso_3166_1": "ES", "title": "A todo gas"}, {"iso_3166_1": "IT", "title": "Fast and Furious"}]},
          "recommendations": {"results": [{"id": 584, "media_type": "movie"}, {"id": 1, "media_type": "movie"}]},
          "images": {"logos": [{"file_path": "/logo-fr.png", "iso_639_1": "fr"}, {"file_path": "/logo-en.png", "iso_639_1": "en"}], "posters": [{"file_path": "/other.jpg"}]},
          "videos": {"results": [
            {"key": "clip", "site": "YouTube", "type": "Clip", "name": "A clip"},
            {"key": "trailer", "site": "YouTube", "type": "Trailer", "name": "Official Trailer", "official": true}
          ]},
          "external_ids": {"imdb_id": "tt0232500", "wikidata_id": "Q155476"}
        }
        """)!;

    internal static JsonNode TwoFast() => JsonNode.Parse("""
        {
          "id": 584, "imdb_id": "tt0322259", "title": "2 Fast 2 Furious", "original_title": "2 Fast 2 Furious", "release_date": "2003-06-05",
          "status": "Released", "poster_path": "/two.jpg", "vote_average": 6.4, "vote_count": 8000,
          "belongs_to_collection": {"id": 9485, "name": "The Fast and the Furious Collection", "poster_path": "/coll.jpg"},
          "external_ids": {"imdb_id": "tt0322259"}
        }
        """)!;

    [Fact]
    public void IMDb_makes_the_candidates_and_a_title_under_the_vote_line_is_not_one()
    {
        var outcome = Import(FastBasics, FastRatings);

        Assert.Equal(2, outcome.Added);
        var titles = Database.Read(c => c.Query("SELECT imdb_id, kind, admitted, listed FROM titles ORDER BY imdb_id",
            r => (r.GetString(0), r.GetString(1), r.GetString(2), r.GetBool(3))));
        Assert.Equal([("tt0232500", "film", "votes", false), ("tt0322259", "film", "votes", false)], titles);
        var rating = Database.Read(c => c.First("SELECT rating, votes FROM ratings WHERE title_id = $id AND source = 'imdb'",
            r => (r.GetDouble(0), r.GetInt32(1)), ("$id", TitleId("tt0232500"))));
        Assert.Equal((6.8, 468847), rating);
    }

    [Fact]
    public void An_import_marks_due_only_the_titles_whose_basics_moved()
    {
        Import(FastBasics, FastRatings);
        Database.Write(c => c.Execute("DELETE FROM build_due"));

        var next = Import(FastBasics.Replace("\t106\t", "\t107\t"), FastRatings.Replace("468847", "468900"), "20261026");

        Assert.Equal(0, next.Added);
        Assert.Equal(1, next.Changed);
        Assert.Equal([TitleId("tt0232500")], Database.Read(c => c.Query("SELECT title_id FROM build_due", r => r.GetInt64(0))));
        Assert.Equal(468900, Database.Read(c => c.First("SELECT votes FROM ratings WHERE title_id = $id AND source = 'imdb'",
            r => r.GetInt32(0), ("$id", TitleId("tt0232500")))));
    }

    [Fact]
    public void The_entry_is_built_from_every_layer_by_rule()
    {
        Import(FastBasics, FastRatings);
        var id = TitleId("tt0232500");

        Assert.Equal(TmdbLayer.Outcome.Kept, Layer().Keep(id, new TmdbRef(9799, "film"), Fast(), []));

        var entry = Database.Read(c => c.First("""
            SELECT listed, english_title, original_title, year, runtime, released, poster, logo, tmdb_id, wikidata_id, tagline
            FROM titles WHERE id = $id
            """, r => (Listed: r.GetBool(0), English: r.GetString(1), Original: r.GetString(2), Year: r.GetInt32(3), Runtime: r.GetInt32(4),
                Released: r.GetString(5), Poster: r.GetString(6), Logo: r.GetString(7), Tmdb: r.GetInt32(8), Wikidata: r.GetString(9),
                Tagline: r.GetString(10)), ("$id", id)));
        Assert.True(entry.Listed);
        Assert.Equal("The Fast and the Furious", entry.English);
        Assert.Equal(2001, entry.Year);
        Assert.Equal(106, entry.Runtime); // IMDb's runtime, not the film database's 107.
        Assert.Equal("2001-06-22", entry.Released);
        Assert.Equal("/gqY0ITBgT7A82poL9jv851qdnIb.jpg", entry.Poster);
        Assert.Equal("/logo-en.png", entry.Logo);
        Assert.Equal(9799, entry.Tmdb);
        Assert.Equal("Q155476", entry.Wikidata);

        Assert.Equal(["imdb:Action", "imdb:Crime", "imdb:Thriller", "tmdb:Action", "tmdb:Crime"],
            Database.Read(c => c.Query("SELECT source || ':' || genre FROM title_genres WHERE title_id = $id ORDER BY 1", r => r.GetString(0), ("$id", id))));
        Assert.Equal(["Paul Walker:cast:Brian O'Conner", "Vin Diesel:cast:Dominic Toretto", "Rob Cohen:Directing:Director"],
            Database.Read(c => c.Query("""
                SELECT p.name || ':' || c.department || ':' || c.role FROM credits c JOIN people p ON p.id = c.person_id
                WHERE c.title_id = $id ORDER BY c.department = 'cast' DESC, c.ord
                """, r => r.GetString(0), ("$id", id))));
        Assert.Equal(["ES:13:2001-09-21", "US:PG-13:2001-06-22"],
            Database.Read(c => c.Query("SELECT country || ':' || certificate || ':' || released FROM title_releases WHERE title_id = $id ORDER BY country",
                r => r.GetString(0), ("$id", id))));
        Assert.Equal(["trailer"], Database.Read(c => c.Query("SELECT key FROM title_videos WHERE title_id = $id", r => r.GetString(0), ("$id", id))));
        Assert.Equal((7.0, 11282), Database.Read(c => c.First("SELECT rating, votes FROM ratings WHERE title_id = $id AND source = 'tmdb'",
            r => (r.GetDouble(0), r.GetInt32(1)), ("$id", id))));
    }

    [Fact]
    public void A_title_is_found_by_any_name_it_goes_by_once_it_is_listed()
    {
        Import(FastBasics, FastRatings);
        var id = TitleId("tt0232500");
        Layer().Keep(id, new TmdbRef(9799, "film"), Fast(), []);

        List<long> Find(string words) => Database.Read(c => c.Query("SELECT rowid FROM titles_fts WHERE titles_fts MATCH $q", r => r.GetInt64(0), ("$q", words)));

        Assert.Equal([id], Find("furious"));
        Assert.Equal([id], Find("\"todo gas\""));
        Assert.Empty(Find("\"2 fast\"")); // A candidate with no layer is found by no search.
    }

    [Fact]
    public void Layers_give_the_same_entry_whatever_order_they_arrive_in()
    {
        Import(FastBasics, FastRatings);
        var id = TitleId("tt0232500");
        var wikidata = new WikidataWorker(null!, Database, Builder, Options.Create(new CatalogueOptions()), _clock, NullLogger<WikidataWorker>.Instance);
        var claims = new Dictionary<string, (string, List<WikidataClaim>)>
        {
            ["tt0232500"] = ("Q155476", [new("P179", "Q1576873", "Fast & Furious", "1"), new("P840", "Q65", "Los Angeles", null)]),
        };

        wikidata.Keep([(id, "tt0232500")], claims);
        Layer().Keep(id, new TmdbRef(9799, "film"), Fast(), []);
        var first = Snapshot(id);

        Layer().Keep(id, new TmdbRef(9799, "film"), Fast(), []);
        wikidata.Keep([(id, "tt0232500")], claims);
        Database.Write(c => Builder.Build(c, id));

        Assert.Equal(first, Snapshot(id));
        Assert.Contains("P840:Q65:Los Angeles", first);
        Assert.Equal("1", Database.Read(c => c.First("""
            SELECT ft.ordinal FROM franchise_titles ft JOIN franchises f ON f.id = ft.franchise_id
            WHERE ft.title_id = $id AND f.source = 'tmdb'
            """, r => r.GetStringOrNull(0), ("$id", id))));
    }

    private string Snapshot(long id) => Database.Read(c => string.Join('\n',
        c.Query("SELECT listed, english_title, original_title, year, runtime, poster, overview FROM titles WHERE id = $id",
            r => string.Join('|', Enumerable.Range(0, r.FieldCount).Select(i => r.IsDBNull(i) ? "" : r.GetValue(i).ToString())), ("$id", id))
        .Concat(c.Query("SELECT property || ':' || item || ':' || label FROM title_facts WHERE title_id = $id ORDER BY 1", r => r.GetString(0), ("$id", id)))
        .Concat(c.Query("SELECT name || ':' || kind FROM title_names WHERE title_id = $id ORDER BY 1", r => r.GetString(0), ("$id", id)))
        .Concat(c.Query("SELECT f.name FROM franchise_titles ft JOIN franchises f ON f.id = ft.franchise_id WHERE ft.title_id = $id ORDER BY 1",
            r => r.GetString(0), ("$id", id)))));

    [Fact]
    public void A_title_with_no_poster_is_a_candidate_and_shown_nowhere()
    {
        Import(FastBasics, FastRatings);
        var id = TitleId("tt0232500");

        Layer().Keep(id, new TmdbRef(9799, "film"), Fast(poster: null), []);

        Assert.False(Database.Read(c => c.First("SELECT listed FROM titles WHERE id = $id", r => r.GetBool(0), ("$id", id))));
        Assert.Equal(0, Database.Read(c => c.Count("SELECT count(*) FROM titles_fts")));
    }

    [Theory]
    [InlineData("Untitled Fast & Furious Project", null, "In Production", true)]
    [InlineData("Untitled Fast & Furious Project", "2027-04-02", "Post Production", true)]
    [InlineData("Untitled Horror Movie", "2021-06-01", "Released", false)]
    [InlineData("Untitled", "2017-01-01", "Released", false)]
    [InlineData("Fast X: Part Two", null, "In Production", false)]
    public void Only_an_unreleased_title_named_Untitled_is_a_placeholder(string title, string? released, string status, bool placeholder) =>
        Assert.Equal(placeholder, EntryBuilder.Placeholder(title, released, status, new DateOnly(2026, 10, 25)));

    [Fact]
    public void A_collection_is_a_franchise_and_a_universe_holds_the_collections_its_titles_span()
    {
        Import(FastBasics, FastRatings);
        var fast = TitleId("tt0232500");
        var two = TitleId("tt0322259");
        Layer().Keep(fast, new TmdbRef(9799, "film"), Fast(), []);
        Layer().Keep(two, new TmdbRef(584, "film"), TwoFast(), []);

        var wikidata = new WikidataWorker(null!, Database, Builder, Options.Create(new CatalogueOptions()), _clock, NullLogger<WikidataWorker>.Instance);
        wikidata.Keep([(fast, "tt0232500"), (two, "tt0322259")], new Dictionary<string, (string, List<WikidataClaim>)>
        {
            ["tt0232500"] = ("Q155476", [new("P8345", "Q1", "One collection only", null)]),
            ["tt0322259"] = ("Q222867", [new("P8345", "Q1", "One collection only", null)]),
        });

        Assert.Equal(0, Database.Write(Universes.Build)); // Both titles are in one collection and both are films.
        Assert.Equal([fast, two], Database.Read(c => c.Query("""
            SELECT ft.title_id FROM franchise_titles ft JOIN franchises f ON f.id = ft.franchise_id
            WHERE f.source = 'tmdb' AND f.source_id = '9485' ORDER BY ft.title_id
            """, r => r.GetInt64(0))));

        // A second collection under the same item makes it a universe holding both.
        Layer().Keep(two, new TmdbRef(584, "film"), JsonNode.Parse(TwoFast().ToJsonString()
            .Replace("\"id\":9485", "\"id\":1111").Replace("The Fast and the Furious Collection", "Another Collection"))!, []);

        Assert.Equal(1, Database.Write(Universes.Build));
        Assert.Equal(2, Database.Read(c => c.Count("""
            SELECT count(*) FROM franchise_parts p JOIN franchises u ON u.id = p.universe_id WHERE u.source = 'wikidata' AND u.source_id = 'Q1'
            """)));
    }

    [Fact]
    public void Similar_titles_are_the_recommendations_the_catalogue_lists_whenever_it_gains_them()
    {
        Import(FastBasics, FastRatings);
        var fast = TitleId("tt0232500");
        var two = TitleId("tt0322259");
        List<long> Similar() => Database.Read(c => c.Query("""
            SELECT o.id FROM similar s JOIN titles o ON o.kind = s.kind AND o.tmdb_id = s.tmdb_id AND o.listed = 1
            WHERE s.title_id = $id ORDER BY s.rank
            """, r => r.GetInt64(0), ("$id", fast)));

        Layer().Keep(fast, new TmdbRef(9799, "film"), Fast(), []);
        Assert.Empty(Similar());

        // 2 Fast 2 Furious arrives after the title recommending it was built, which is not built again.
        Layer().Keep(two, new TmdbRef(584, "film"), TwoFast(), []);
        Assert.Equal([two], Similar());
    }

    private (string? FetchedAt, string? BuiltAt, string? ChangedAt) Stamps(long id) => Database.Read(c => c.First("""
        SELECT l.fetched_at, t.built_at, t.changed_at FROM titles t LEFT JOIN tmdb_layer l ON l.title_id = t.id WHERE t.id = $id
        """, r => (r.GetStringOrNull(0), r.GetStringOrNull(1), r.GetStringOrNull(2)), ("$id", id)));

    [Fact]
    public void The_same_answer_again_only_says_when_it_was_asked()
    {
        Import(FastBasics, FastRatings);
        var id = TitleId("tt0232500");
        Layer().Keep(id, new TmdbRef(9799, "film"), Fast(), []);
        var first = Stamps(id);

        _clock.Advance(TimeSpan.FromDays(150));
        Layer().Keep(id, new TmdbRef(9799, "film"), Fast(), []);
        var again = Stamps(id);

        Assert.NotEqual(first.FetchedAt, again.FetchedAt);
        Assert.Equal(first.BuiltAt, again.BuiltAt);
        Assert.Equal(first.ChangedAt, again.ChangedAt);
    }

    [Fact]
    public void Votes_and_popularity_that_move_change_the_title_without_building_it()
    {
        Import(FastBasics, FastRatings);
        var id = TitleId("tt0232500");
        Layer().Keep(id, new TmdbRef(9799, "film"), Fast(), []);
        var first = Stamps(id);

        _clock.Advance(TimeSpan.FromDays(1));
        var moved = Fast();
        moved["vote_count"] = 11300;
        moved["popularity"] = 40.0;
        Layer().Keep(id, new TmdbRef(9799, "film"), moved, []);
        var after = Stamps(id);

        Assert.Equal(first.BuiltAt, after.BuiltAt);
        Assert.NotEqual(first.ChangedAt, after.ChangedAt);
        Assert.Equal((11300, 40.0), Database.Read(c => c.First("""
            SELECT r.votes, t.popularity FROM titles t JOIN ratings r ON r.title_id = t.id AND r.source = 'tmdb' WHERE t.id = $id
            """, r => (r.GetInt32(0), r.GetDouble(1)), ("$id", id))));
    }

    [Fact]
    public void An_answer_that_moved_builds_the_entry_again()
    {
        Import(FastBasics, FastRatings);
        var id = TitleId("tt0232500");
        Layer().Keep(id, new TmdbRef(9799, "film"), Fast(), []);
        var first = Stamps(id);

        _clock.Advance(TimeSpan.FromDays(1));
        var moved = Fast();
        moved["overview"] = "A new overview.";
        Layer().Keep(id, new TmdbRef(9799, "film"), moved, []);

        Assert.NotEqual(first.BuiltAt, Stamps(id).BuiltAt);
        Assert.Equal("A new overview.", Database.Read(c => c.First("SELECT overview FROM titles WHERE id = $id", r => r.GetString(0), ("$id", id))));
    }

    [Fact]
    public void The_same_Wikidata_answer_builds_nothing()
    {
        Import(FastBasics, FastRatings);
        var id = TitleId("tt0232500");
        Layer().Keep(id, new TmdbRef(9799, "film"), Fast(), []);
        var wikidata = new WikidataWorker(null!, Database, Builder, Options.Create(new CatalogueOptions()), _clock, NullLogger<WikidataWorker>.Instance);
        var claims = new Dictionary<string, (string, List<WikidataClaim>)>
        {
            ["tt0232500"] = ("Q155476", [new("P840", "Q65", "Los Angeles", null), new("P179", "Q1576873", "Fast & Furious", "1")]),
        };
        wikidata.Keep([(id, "tt0232500")], claims);
        var first = Stamps(id);

        _clock.Advance(TimeSpan.FromDays(7));
        claims["tt0232500"] = ("Q155476", [new("P179", "Q1576873", "Fast & Furious", "1"), new("P840", "Q65", "Los Angeles", null)]);
        wikidata.Keep([(id, "tt0232500")], claims);

        Assert.Equal(first.BuiltAt, Stamps(id).BuiltAt);
    }

    [Fact]
    public void Entries_built_under_other_rules_are_all_built_again()
    {
        Import(FastBasics, FastRatings);
        var worker = new BuildWorker(Database, Builder, _clock, NullLogger<BuildWorker>.Instance);
        Assert.True(worker.QueueForRules());
        while (worker.DrainOnce() > 0) { }

        Assert.False(worker.QueueForRules());
        Database.SetMeta(BuildWorker.RulesKey, "an older rule");

        Assert.True(worker.QueueForRules());
        Assert.Equal(2, Database.Read(c => c.Count("SELECT count(*) FROM build_due")));
    }

    [Fact]
    public async Task A_title_the_database_files_as_the_other_kind_is_one_it_does_not_hold()
    {
        Import(FastBasics, FastRatings);
        var id = TitleId("tt0232500");
        var answers = new Answers
        {
            ["find/tt0232500"] = """{"movie_results": [], "tv_results": [{"id": 1}]}""",
        };

        Assert.Equal(TmdbLayer.Outcome.Missing, await Layer(answers).FetchAsync(id, default));
        Assert.True(Database.Read(c => c.Exists("SELECT 1 FROM tmdb_missing WHERE title_id = $id", ("$id", id))));
    }

    [Fact]
    public async Task A_title_is_found_described_and_kept_in_one_pass()
    {
        Import(FastBasics, FastRatings);
        var id = TitleId("tt0232500");
        var answers = new Answers
        {
            ["find/tt0232500"] = """{"movie_results": [{"id": 9799}], "tv_results": []}""",
            ["movie/9799"] = Fast().ToJsonString(),
        };

        Assert.Equal(TmdbLayer.Outcome.Kept, await Layer(answers).FetchAsync(id, default));
        Assert.True(Database.Read(c => c.First("SELECT listed FROM titles WHERE id = $id", r => r.GetBool(0), ("$id", id))));
    }

    [Fact]
    public async Task A_change_makes_a_title_due_only_when_its_layer_is_older_than_the_change()
    {
        Import(FastBasics, FastRatings);
        var fast = TitleId("tt0232500");
        var two = TitleId("tt0322259");
        Layer().Keep(fast, new TmdbRef(9799, "film"), Fast(), []);
        Layer().Keep(two, new TmdbRef(584, "film"), TwoFast(), []);
        // The Fast and the Furious was fetched before the day both changed on; 2 Fast 2 Furious after it.
        Database.Write(c => c.Execute("UPDATE tmdb_layer SET fetched_at = $at WHERE title_id = $id",
            ("$at", new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.Zero)), ("$id", fast)));
        Database.SetMeta(ChangesWorker.ThroughKey, "2026-10-24");
        var answers = new Answers
        {
            ["movie/changes"] = """{"results": [{"id": 9799, "adult": false}, {"id": 584, "adult": false}], "total_pages": 1}""",
        };
        var worker = new ChangesWorker(Source(answers), Database, Options.Create(new CatalogueOptions()), _clock, NullLogger<ChangesWorker>.Instance);

        await worker.RunOnceAsync(default);

        Assert.Equal([fast], Database.Read(c => c.Query("SELECT title_id FROM tmdb_due WHERE reason = 'changed'", r => r.GetInt64(0))));
        Assert.Equal("2026-10-25", Database.Meta(ChangesWorker.ThroughKey));
    }

    [Fact]
    public void A_description_is_kept_pruned_to_what_the_catalogue_builds_from()
    {
        var pruned = TmdbLayer.Prune(Fast(), "film");

        Assert.Null(pruned["images"]);
        Assert.Null(pruned["credits"]);
        Assert.Equal(2, pruned["cast"]!.AsArray().Count);
        Assert.Single(pruned["crew"]!.AsArray()); // The key grip is not a job a page names.
        Assert.Equal("/logo-en.png", pruned["logo_path"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("/gqY0ITBgT7A82poL9jv851qdnIb.jpg", true)]
    [InlineData("/../etc/passwd", false)]
    [InlineData("/a/b.jpg", false)]
    [InlineData("no-slash.jpg", false)]
    public void Only_a_path_the_image_store_names_becomes_a_file(string path, bool valid) =>
        Assert.Equal(valid, ImageStore.Valid(path));

    [Fact]
    public async Task Wikidata_claims_are_read_with_series_order_and_authors()
    {
        var handler = new Answers
        {
            ["sparql:?vLabel"] = """
                {"results": {"bindings": [
                  {"imdb": {"value": "tt0232500"}, "item": {"value": "http://www.wikidata.org/entity/Q155476"},
                   "p": {"value": "http://www.wikidata.org/prop/direct/P179"}, "v": {"value": "http://www.wikidata.org/entity/Q1576873"},
                   "vLabel": {"value": "Fast & Furious"}},
                  {"imdb": {"value": "tt0232500"}, "item": {"value": "http://www.wikidata.org/entity/Q999999"},
                   "p": {"value": "http://www.wikidata.org/prop/direct/P840"}, "v": {"value": "http://www.wikidata.org/entity/Q1"}}
                ]}}
                """,
            ["sparql:?authorLabel"] = """
                {"results": {"bindings": [
                  {"imdb": {"value": "tt0232500"}, "item": {"value": "http://www.wikidata.org/entity/Q155476"},
                   "series": {"value": "http://www.wikidata.org/entity/Q1576873"}, "ord": {"value": "1"}}
                ]}}
                """,
        };
        var source = new WikidataSource(new HttpClient(handler), Options.Create(new CatalogueOptions()), _clock, NullLogger<WikidataSource>.Instance);

        var claims = await source.ClaimsAsync(["tt0232500", "not-an-id"], default);

        var (item, held) = claims["tt0232500"];
        Assert.Equal("Q155476", item); // The lowest-numbered item where two name the id.
        Assert.Equal([new WikidataClaim("P179", "Q1576873", "Fast & Furious", "1")], held);
    }

    /// <summary>Answers by the request's path (or, for SPARQL, a word its query holds); anything else is a 404.</summary>
    private sealed class Answers : HttpMessageHandler, IEnumerable<KeyValuePair<string, string>>
    {
        private readonly Dictionary<string, string> _answers = new(StringComparer.Ordinal);

        public string this[string key]
        {
            set => _answers[key] = value;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string? answer = null;
            if (request.Content is { } content)
            {
                var query = Uri.UnescapeDataString((await content.ReadAsStringAsync(ct)).Replace('+', ' '));
                answer = _answers.Where(a => a.Key.StartsWith("sparql:", StringComparison.Ordinal) && query.Contains(a.Key[7..], StringComparison.Ordinal))
                    .Select(a => a.Value).FirstOrDefault();
            }
            else
            {
                var path = request.RequestUri!.AbsolutePath.TrimStart('/');
                if (path.StartsWith("3/", StringComparison.Ordinal)) path = path[2..];
                _answers.TryGetValue(path, out answer);
            }
            return answer is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
        }

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _answers.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
