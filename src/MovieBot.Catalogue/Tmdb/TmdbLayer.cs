using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Catalogue.Data;
using TheKrystalShip.MovieBot.Catalogue.Entries;

namespace TheKrystalShip.MovieBot.Catalogue.Tmdb;

/// <summary>
/// The film database's layer of a title: found by its IMDb id, described with everything appended in
/// one call (and a show's every season after), pruned to what the catalogue builds from, and kept with
/// the entry built from it in one transaction.
/// </summary>
public sealed class TmdbLayer(CatalogueDatabase database, TmdbSource source, EntryBuilder builder, TimeProvider clock, ILogger<TmdbLayer> logger)
{
    private const int CastKept = 20;
    private const int RecommendationsKept = 20;
    private const int VideosKept = 6;

    private static readonly HashSet<string> CrewJobs = new(StringComparer.Ordinal)
    {
        "Director", "Screenplay", "Writer", "Story", "Novel", "Characters", "Author", "Creator",
        "Producer", "Original Music Composer", "Director of Photography", "Editor",
    };

    /// <summary>What asking about a title came to.</summary>
    public enum Outcome { Kept, Missing, Duplicate }

    /// <summary>
    /// Asks the database about a title and keeps what it says. A title it holds nothing for is
    /// recorded as missing, and its entry is built again without the layer.
    /// </summary>
    public async Task<Outcome> FetchAsync(long titleId, CancellationToken ct)
    {
        var title = database.Read(c => c.First(
            "SELECT imdb_id, kind, tmdb_id FROM titles WHERE id = $id",
            r => (Imdb: r.GetString(0), Kind: r.GetString(1), Tmdb: r.GetIntOrNull(2)), ("$id", titleId)));
        if (title.Imdb is null) return Outcome.Missing;

        var found = title.Tmdb is { } known ? new TmdbRef(known, title.Kind) : await source.FindAsync(title.Imdb, title.Kind, ct);
        var description = found is { } at ? await source.DescribeAsync(at, ct) : null;
        if (found is null || description is null)
        {
            MarkMissing(titleId);
            return Outcome.Missing;
        }

        var seasons = found.Value.Kind == "show" ? await SeasonsAsync(found.Value.Id, description, ct) : [];
        return Keep(titleId, found.Value, description, seasons);
    }

    /// <summary>A show's seasons as the database describes each, by number.</summary>
    public async Task<List<(int Number, JsonNode Season)>> SeasonsAsync(int showId, JsonNode description, CancellationToken ct)
    {
        var numbers = (description["seasons"]?.AsArray() ?? [])
            .Select(s => s?["season_number"]?.GetValue<int>())
            .OfType<int>()
            .Distinct()
            .ToList();
        var seasons = new List<(int, JsonNode)>();
        foreach (var number in numbers)
            if (await source.SeasonAsync(showId, number, ct) is { } season) seasons.Add((number, PruneSeason(season)));
        return seasons;
    }

    /// <summary>Keeps a description as the title's layer and builds its entry, in one transaction.</summary>
    public Outcome Keep(long titleId, TmdbRef found, JsonNode description, List<(int Number, JsonNode Season)> seasons)
    {
        var now = clock.GetUtcNow();
        var pruned = Prune(description, found.Kind);
        var hash = HashOf(pruned, seasons);
        try
        {
            database.Write(c =>
            {
                c.Execute("UPDATE titles SET tmdb_id = $tmdb WHERE id = $id", ("$tmdb", found.Id), ("$id", titleId));
                if (pruned["external_ids"]?["wikidata_id"]?.GetValue<string>() is { Length: > 1 } item && item[0] == 'Q')
                    c.Execute("UPDATE titles SET wikidata_id = $item WHERE id = $id AND wikidata_id IS NULL", ("$item", item), ("$id", titleId));
                // The same answer as held only says when it was asked; a different one replaces the layer.
                if (c.First("SELECT hash FROM tmdb_layer WHERE title_id = $id", r => r.GetString(0), ("$id", titleId)) == hash)
                {
                    c.Execute("UPDATE tmdb_layer SET fetched_at = $at WHERE title_id = $id", ("$id", titleId), ("$at", now));
                    c.Execute("UPDATE tmdb_seasons SET fetched_at = $at WHERE title_id = $id", ("$id", titleId), ("$at", now));
                }
                else
                {
                    c.Execute("""
                        INSERT INTO tmdb_layer (title_id, fetched_at, json, hash) VALUES ($id, $at, $json, $hash)
                        ON CONFLICT (title_id) DO UPDATE SET fetched_at = excluded.fetched_at, json = excluded.json, hash = excluded.hash
                        """, ("$id", titleId), ("$at", now), ("$json", pruned.ToJsonString()), ("$hash", hash));
                    c.Execute("DELETE FROM tmdb_seasons WHERE title_id = $id", ("$id", titleId));
                    foreach (var (number, season) in seasons)
                        c.Execute("INSERT INTO tmdb_seasons (title_id, season, fetched_at, json) VALUES ($id, $n, $at, $json)",
                            ("$id", titleId), ("$n", number), ("$at", now), ("$json", season.ToJsonString()));
                }
                c.Execute("DELETE FROM tmdb_due WHERE title_id = $id", ("$id", titleId));
                c.Execute("DELETE FROM tmdb_missing WHERE title_id = $id", ("$id", titleId));

                // What moves daily is kept beside the layer: the rating with the other source's, the
                // popularity on the title, which only orders and is no change to the entry.
                if (pruned["vote_count"]?.GetValue<int>() is > 0 and var votes && pruned["vote_average"]?.GetValue<double>() is { } rating)
                    Ratings.Observe(c, titleId, "tmdb", Math.Round(rating, 1), votes, now);
                c.Execute("UPDATE titles SET popularity = $popularity WHERE id = $id",
                    ("$popularity", pruned["popularity"]?.GetValue<double>()), ("$id", titleId));
                builder.Build(c, titleId);
            });
            return Outcome.Kept;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            // Two IMDb ids the database files under one title: the first keeps it, and this one is a
            // title the database does not hold.
            logger.LogInformation("Title {Title} names the film database's {Kind} {Id}, which another title holds.", titleId, found.Kind, found.Id);
            MarkMissing(titleId);
            return Outcome.Duplicate;
        }
    }

    private void MarkMissing(long titleId) => database.Write(c =>
    {
        c.Execute("""
            INSERT INTO tmdb_missing (title_id, asked_at) VALUES ($id, $at)
            ON CONFLICT (title_id) DO UPDATE SET asked_at = excluded.asked_at
            """, ("$id", titleId), ("$at", clock.GetUtcNow()));
        c.Execute("DELETE FROM tmdb_due WHERE title_id = $id", ("$id", titleId));
        c.Execute("DELETE FROM tmdb_layer WHERE title_id = $id", ("$id", titleId));
        c.Execute("DELETE FROM tmdb_seasons WHERE title_id = $id", ("$id", titleId));
        c.Execute("UPDATE titles SET tmdb_id = NULL, popularity = NULL WHERE id = $id", ("$id", titleId));
        c.Execute("DELETE FROM ratings WHERE title_id = $id AND source = 'tmdb'", ("$id", titleId));
        builder.Build(c, titleId);
    });

    /// <summary>The fields that move daily, which a layer's hash leaves out.</summary>
    private static readonly string[] Volatile = ["popularity", "vote_average", "vote_count"];

    /// <summary>The hash of a pruned description and its seasons, without what moves daily.</summary>
    public static string HashOf(JsonObject pruned, List<(int Number, JsonNode Season)> seasons)
    {
        var stable = pruned.DeepClone().AsObject();
        foreach (var field in Volatile) stable.Remove(field);
        var text = new System.Text.StringBuilder(stable.ToJsonString());
        foreach (var (number, season) in seasons) text.Append('\n').Append(number).Append(':').Append(season.ToJsonString());
        return Hash.Of(text.ToString());
    }

    private static readonly string[] FilmFields =
    [
        "id", "imdb_id", "title", "original_title", "original_language", "overview", "tagline", "status", "release_date",
        "runtime", "poster_path", "backdrop_path", "popularity", "vote_average", "vote_count", "genres", "production_companies",
        "production_countries", "origin_country", "spoken_languages", "belongs_to_collection", "budget", "revenue", "adult",
    ];

    private static readonly string[] ShowFields =
    [
        "id", "name", "original_name", "original_language", "overview", "tagline", "status", "type", "first_air_date",
        "last_air_date", "in_production", "episode_run_time", "poster_path", "backdrop_path", "popularity", "vote_average",
        "vote_count", "genres", "production_companies", "production_countries", "origin_country", "spoken_languages",
        "networks", "created_by", "number_of_seasons", "number_of_episodes", "seasons", "adult",
    ];

    /// <summary>
    /// The part of a description the catalogue builds from: the title's own facts, its top-billed cast
    /// and the crew whose jobs a page names, its keywords, certificates, alternative titles, the
    /// database's recommendations by id, its English (or textless) logo and its trailers.
    /// </summary>
    public static JsonObject Prune(JsonNode description, string kind)
    {
        var kept = new JsonObject { ["kind"] = kind };
        foreach (var field in kind == "show" ? ShowFields : FilmFields)
            if (description[field] is { } value) kept[field] = value.DeepClone();

        if (kind == "show")
        {
            kept["cast"] = Take(description["aggregate_credits"]?["cast"], CastKept, c => new JsonObject
            {
                ["id"] = Copy(c["id"]),
                ["name"] = Copy(c["name"]),
                ["character"] = Copy(c["roles"]?[0]?["character"]),
                ["profile_path"] = Copy(c["profile_path"]),
                ["department"] = Copy(c["known_for_department"]),
                ["episodes"] = Copy(c["total_episode_count"]),
            });
            kept["crew"] = Crew(description["aggregate_credits"]?["crew"], c => c["jobs"]?[0]?["job"]?.GetValue<string>());
            kept["keywords"] = Copy(description["keywords"]?["results"]);
            kept["certificates"] = Take(description["content_ratings"]?["results"], int.MaxValue, r => new JsonObject
            {
                ["country"] = Copy(r["iso_3166_1"]),
                ["certificate"] = Copy(r["rating"]),
            });
            kept["alternative_titles"] = Copy(description["alternative_titles"]?["results"]);
        }
        else
        {
            kept["cast"] = Take(description["credits"]?["cast"], CastKept, c => new JsonObject
            {
                ["id"] = Copy(c["id"]),
                ["name"] = Copy(c["name"]),
                ["character"] = Copy(c["character"]),
                ["profile_path"] = Copy(c["profile_path"]),
                ["department"] = Copy(c["known_for_department"]),
            });
            kept["crew"] = Crew(description["credits"]?["crew"], c => c["job"]?.GetValue<string>());
            kept["keywords"] = Copy(description["keywords"]?["keywords"]);
            kept["releases"] = Take(description["release_dates"]?["results"], int.MaxValue, r => new JsonObject
            {
                ["country"] = Copy(r["iso_3166_1"]),
                ["dates"] = Take(r["release_dates"], int.MaxValue, d => new JsonObject
                {
                    ["certificate"] = Copy(d["certification"]),
                    ["date"] = Copy(d["release_date"]),
                    ["type"] = Copy(d["type"]),
                }),
            });
            kept["alternative_titles"] = Copy(description["alternative_titles"]?["titles"]);
        }

        kept["recommendations"] = Take(description["recommendations"]?["results"], RecommendationsKept, r => new JsonObject
        {
            ["id"] = Copy(r["id"]),
            ["media_type"] = Copy(r["media_type"]),
        });

        var logos = description["images"]?["logos"]?.AsArray().OfType<JsonNode>().ToList() ?? [];
        var logo = logos.FirstOrDefault(l => l["iso_639_1"]?.GetValue<string>() == "en") ?? logos.FirstOrDefault(l => l["iso_639_1"] is null);
        if (logo?["file_path"] is { } logoPath) kept["logo_path"] = logoPath.DeepClone();

        kept["videos"] = new JsonArray((description["videos"]?["results"]?.AsArray().OfType<JsonNode>() ?? [])
            .Where(v => v["site"]?.GetValue<string>() == "YouTube" && v["type"]?.GetValue<string>() is "Trailer" or "Teaser")
            .OrderByDescending(v => v["type"]?.GetValue<string>() == "Trailer")
            .ThenByDescending(v => v["official"]?.GetValue<bool>() == true)
            .Take(VideosKept)
            .Select(v => (JsonNode)new JsonObject
            {
                ["key"] = Copy(v["key"]),
                ["site"] = Copy(v["site"]),
                ["type"] = Copy(v["type"]),
                ["name"] = Copy(v["name"]),
            })
            .ToArray());

        kept["external_ids"] = new JsonObject
        {
            ["imdb_id"] = Copy(description["external_ids"]?["imdb_id"]),
            ["wikidata_id"] = Copy(description["external_ids"]?["wikidata_id"]),
        };
        return kept;
    }

    /// <summary>A season as the catalogue keeps it: its own facts and each episode's.</summary>
    public static JsonNode PruneSeason(JsonNode season) => new JsonObject
    {
        ["season_number"] = Copy(season["season_number"]),
        ["name"] = Copy(season["name"]),
        ["overview"] = Copy(season["overview"]),
        ["air_date"] = Copy(season["air_date"]),
        ["poster_path"] = Copy(season["poster_path"]),
        ["episodes"] = Take(season["episodes"], int.MaxValue, e => new JsonObject
        {
            ["episode_number"] = Copy(e["episode_number"]),
            ["name"] = Copy(e["name"]),
            ["overview"] = Copy(e["overview"]),
            ["air_date"] = Copy(e["air_date"]),
            ["runtime"] = Copy(e["runtime"]),
            ["still_path"] = Copy(e["still_path"]),
        }),
    };

    private static JsonArray Crew(JsonNode? crew, Func<JsonNode, string?> jobOf) => new((crew?.AsArray().OfType<JsonNode>() ?? [])
        .Select(c => (Crew: c, Job: jobOf(c)))
        .Where(c => c.Job is not null && CrewJobs.Contains(c.Job))
        .Select(c => (JsonNode)new JsonObject
        {
            ["id"] = Copy(c.Crew["id"]),
            ["name"] = Copy(c.Crew["name"]),
            ["job"] = c.Job,
            ["department"] = Copy(c.Crew["department"]),
            ["profile_path"] = Copy(c.Crew["profile_path"]),
        })
        .ToArray());

    private static JsonArray Take(JsonNode? items, int count, Func<JsonNode, JsonNode> map) =>
        new((items?.AsArray().OfType<JsonNode>() ?? []).Take(count).Select(map).ToArray());

    private static JsonNode? Copy(JsonNode? node) => node?.DeepClone();
}
