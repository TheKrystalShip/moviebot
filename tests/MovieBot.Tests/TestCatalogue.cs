using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Catalogue;
using TheKrystalShip.MovieBot.Catalogue.Data;
using TheKrystalShip.MovieBot.Catalogue.Entries;
using TheKrystalShip.MovieBot.Catalogue.Tmdb;
using TheKrystalShip.MovieBot.Catalogue.Wikidata;
using CatalogueImdb = TheKrystalShip.MovieBot.Catalogue.Imdb.ImdbImport;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A small catalogue, written by the same layers and builder <c>moviebot-catalogue</c> runs: two Fast
/// &amp; Furious films (a collection, a Wikidata series, a place each is set in, a ranking that lists
/// them) and Chernobyl, a show with one season of two episodes. <see cref="WriteImages"/> puts a file
/// under every image the titles name, as the catalogue's image store would.
/// </summary>
internal static class TestCatalogue
{
    public const string Fast = "tt0232500";
    public const string TwoFast = "tt0322259";
    public const string Chernobyl = "tt7366338";

    /// <summary>The poster file of <see cref="Fast"/>, kept at <see cref="ImageUrls.Poster"/>.</summary>
    public const string FastPoster = "gqY0ITBgT7A82poL9jv851qdnIb.jpg";

    private const string ShowBasics = "tt7366338\ttvMiniSeries\tChernobyl\tChernobyl\t0\t2019\t2019\t330\tDrama,History,Thriller\n";
    private const string ShowRatings = "tt7366338\t9.3\t900000\n";

    /// <summary>Writes the catalogue into <paramref name="directory"/> and answers the database, open for writing.</summary>
    public static CatalogueDatabase Write(string directory, TimeProvider clock)
    {
        var database = new CatalogueDatabase(Path.Combine(directory, CatalogueDatabase.FileName));
        var builder = new EntryBuilder(clock);
        CatalogueImdb.Load(database,
            () => new MemoryStream(LocalCatalogueTests.Gz(LocalCatalogueTests.FastBasics + ShowBasics)),
            () => new MemoryStream(LocalCatalogueTests.Gz(LocalCatalogueTests.FastRatings + ShowRatings)),
            100, "20261025", "stamp", clock.GetUtcNow(), default);
        long Id(string imdb) => database.Read(c => c.First("SELECT id FROM titles WHERE imdb_id = $i", r => r.GetInt64(0), ("$i", imdb)));
        var (fast, two, chernobyl) = (Id(Fast), Id(TwoFast), Id(Chernobyl));

        var layer = new TmdbLayer(database, null!, builder, clock, NullLogger<TmdbLayer>.Instance);
        layer.Keep(fast, new TmdbRef(9799, "film"), LocalCatalogueTests.Fast(), []);
        layer.Keep(two, new TmdbRef(584, "film"), LocalCatalogueTests.TwoFast(), []);
        layer.Keep(chernobyl, new TmdbRef(87108, "show"), ChernobylShow(), [(1, ChernobylSeason())]);

        var wikidata = new WikidataWorker(null!, database, builder, Options.Create(new CatalogueOptions()), clock, NullLogger<WikidataWorker>.Instance);
        wikidata.Keep([(fast, Fast), (two, TwoFast)], new Dictionary<string, (string, List<WikidataClaim>)>
        {
            [Fast] = ("Q155476", [new("P840", "Q65", "Los Angeles", null), new("P156", "Q222867", "2 Fast 2 Furious", null),
                new("P179", "Q1576873", "Fast & Furious", "1")]),
            [TwoFast] = ("Q222867", [new("P840", "Q8652", "Miami", null), new("P155", "Q155476", "The Fast and the Furious", null),
                new("P179", "Q1576873", "Fast & Furious", "2")]),
        });
        database.Write(c => FactItems.Build(c));
        database.Write(c =>
        {
            c.Execute("INSERT INTO rankings (list, rank, title_id, fetched_at) VALUES ('trending-films', 1, $two, $at), ('trending-films', 2, $fast, $at)",
                ("$two", two), ("$fast", fast), ("$at", clock.GetUtcNow()));
        });
        KeepImages(database, clock);
        return database;
    }

    /// <summary>
    /// Records every portrait, still and season poster the titles name as kept, as the catalogue's image
    /// store does once it holds the file: a page names an image only once it is kept.
    /// </summary>
    private static void KeepImages(CatalogueDatabase database, TimeProvider clock) => database.Write(c => c.Execute($$"""
        INSERT OR IGNORE INTO images (path, size, fetched_at, bytes)
        SELECT profile, '{{ImageUrls.Portrait}}', $at, 1 FROM people WHERE profile IS NOT NULL
        UNION SELECT still, '{{ImageUrls.Still}}', $at, 1 FROM episodes WHERE still IS NOT NULL
        UNION SELECT poster, '{{ImageUrls.Poster}}', $at, 1 FROM seasons WHERE poster IS NOT NULL
        """, ("$at", clock.GetUtcNow())));

    /// <summary>
    /// Keeps a film as the film database would describe it, with one director and two of its cast, its
    /// title made a candidate first where the catalogue holds none, and builds its entry.
    /// </summary>
    public static void KeepFilm(CatalogueDatabase database, TimeProvider clock, string imdbId, int tmdbId, string title, string released,
        string[] genres, string director, string[] starring, int runtime = 120)
    {
        var id = Candidate(database, imdbId, "film");
        var description = new JsonObject
        {
            ["id"] = tmdbId, ["imdb_id"] = imdbId, ["title"] = title, ["original_title"] = title, ["original_language"] = "en",
            ["status"] = "Released", ["release_date"] = released, ["runtime"] = runtime, ["poster_path"] = $"/film{tmdbId}.jpg",
            ["overview"] = $"{title} is a film. It is a test's.", ["vote_average"] = 7.5, ["vote_count"] = 5000,
            ["genres"] = new JsonArray([.. genres.Select((g, i) => (JsonNode)new JsonObject { ["id"] = i + 1, ["name"] = g })]),
            ["credits"] = new JsonObject
            {
                ["cast"] = new JsonArray([.. starring.Select((name, i) => (JsonNode)new JsonObject
                {
                    ["id"] = tmdbId * 10 + i + 1, ["name"] = name, ["character"] = $"Role {i + 1}", ["known_for_department"] = "Acting",
                })]),
                ["crew"] = new JsonArray(new JsonObject { ["id"] = tmdbId * 10, ["name"] = director, ["job"] = "Director", ["department"] = "Directing" }),
            },
            ["external_ids"] = new JsonObject { ["imdb_id"] = imdbId },
        };
        new TmdbLayer(database, null!, new EntryBuilder(clock), clock, NullLogger<TmdbLayer>.Instance)
            .Keep(id, new TmdbRef(tmdbId, "film"), description, []);
        KeepImages(database, clock);
    }

    private static long Candidate(CatalogueDatabase database, string imdbId, string kind) => database.Write(c =>
    {
        c.Execute("INSERT INTO titles (imdb_id, kind, admitted) VALUES ($imdb, $kind, 'votes') ON CONFLICT (imdb_id) DO NOTHING",
            ("$imdb", imdbId), ("$kind", kind));
        return c.First("SELECT id FROM titles WHERE imdb_id = $imdb", r => r.GetInt64(0), ("$imdb", imdbId));
    });

    /// <summary>
    /// Keeps a show as the film database would describe it, its title made a candidate first where the
    /// catalogue holds none, and builds its entry: the same description again changes nothing, a changed
    /// one replaces the last.
    /// </summary>
    public static void KeepShow(CatalogueDatabase database, TimeProvider clock, string imdbId, int tmdbId, string name, string status,
        IReadOnlyList<ShowEpisode> episodes, ShowEpisode? next = null)
    {
        var id = Candidate(database, imdbId, "show");
        static JsonObject Episode(ShowEpisode e) => new()
        {
            ["season_number"] = e.Season, ["episode_number"] = e.Number, ["name"] = e.Name, ["air_date"] = e.AirDate, ["runtime"] = e.Runtime,
        };
        var seasons = episodes.GroupBy(e => e.Season).OrderBy(g => g.Key).Select(g => (g.Key, TmdbLayer.PruneSeason(new JsonObject
        {
            ["season_number"] = g.Key,
            ["name"] = $"Season {g.Key}",
            ["air_date"] = g.Select(e => e.AirDate).OfType<string>().Order(StringComparer.Ordinal).FirstOrDefault(),
            ["episodes"] = new JsonArray([.. g.OrderBy(e => e.Number).Select(e => (JsonNode)Episode(e))]),
        }))).ToList();
        var description = new JsonObject
        {
            ["id"] = tmdbId, ["name"] = name, ["original_name"] = name, ["original_language"] = "en", ["status"] = status,
            ["first_air_date"] = episodes.Select(e => e.AirDate).OfType<string>().Order(StringComparer.Ordinal).FirstOrDefault(),
            ["poster_path"] = $"/show{tmdbId}.jpg", ["number_of_seasons"] = seasons.Count, ["number_of_episodes"] = episodes.Count,
            ["seasons"] = new JsonArray([.. seasons.Select(s => (JsonNode)new JsonObject { ["season_number"] = s.Key })]),
            ["next_episode_to_air"] = next is null ? null : Episode(next),
            ["external_ids"] = new JsonObject { ["imdb_id"] = imdbId },
        };
        new TmdbLayer(database, null!, new EntryBuilder(clock), clock, NullLogger<TmdbLayer>.Instance)
            .Keep(id, new TmdbRef(tmdbId, "show"), description, seasons);
        KeepImages(database, clock);
    }

    /// <summary>A file, its own name as its bytes, under every image the catalogue's titles name.</summary>
    public static void WriteImages(string directory)
    {
        foreach (var (size, file) in new[]
        {
            (ImageUrls.Poster, FastPoster), (ImageUrls.Poster, "two.jpg"), (ImageUrls.Poster, "chernobyl.jpg"), (ImageUrls.Poster, "s1.jpg"),
            (ImageUrls.Backdrop, "backdrop.jpg"), (ImageUrls.Backdrop, "cb.jpg"), (ImageUrls.Still, "e1.jpg"), (ImageUrls.Still, "e2.jpg"),
        })
        {
            var path = Path.Combine(directory, "images", size, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, file);
        }
    }

    private static JsonNode ChernobylShow() => JsonNode.Parse("""
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
}

/// <summary>An episode of a show the test keeps, as the film database lists it.</summary>
internal sealed record ShowEpisode(int Season, int Number, string? Name, string? AirDate, int? Runtime = null);
