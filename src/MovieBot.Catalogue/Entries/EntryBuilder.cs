using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TheKrystalShip.MovieBot.Catalogue.Data;

namespace TheKrystalShip.MovieBot.Catalogue.Entries;

/// <summary>
/// Builds a title's entry from its layers, by rule and never by the order the layers arrived in, inside
/// the transaction that changed a layer. Everything it writes it first clears, so building a title twice
/// from the same layers gives the same entry.
///
/// The rules: the English title is the film database's, IMDb's primary title where it has none; the
/// original title, year and runtime are IMDb's, the database's where IMDb has no layer (a title a
/// ranking admitted); the overview, tagline, dates, certificates, credits, keywords, companies,
/// collection, recommendations, images and trailers are the database's; the facts are Wikidata's.
/// Genres are kept by source, both. Ratings are not built: each source writes its own.
///
/// A title is listed, and so an entry, when the database gave it a poster and it is not a placeholder:
/// an unreleased title whose English title begins <c>Untitled</c>.
/// </summary>
public sealed class EntryBuilder(TimeProvider clock)
{
    private static readonly string[] Facts =
        ["P840", "P915", "P144", "P144/P50", "P166", "P1411", "P921", "P136", "P179", "P155", "P156", "P1434", "P8345"];

    public void Build(SqliteConnection c, long titleId)
    {
        var title = c.First("SELECT imdb_id, kind, admitted FROM titles WHERE id = $id",
            r => (Imdb: r.GetString(0), Kind: r.GetString(1)), ("$id", titleId));
        if (title.Imdb is null) return;

        var imdb = c.First("""
            SELECT primary_title, original_title, start_year, runtime, genres FROM imdb_layer WHERE title_id = $id
            """, r => new ImdbRow(r.GetString(0), r.GetString(1), r.GetIntOrNull(2), r.GetIntOrNull(3), r.GetString(4)), ("$id", titleId));
        var tmdb = c.First("SELECT json FROM tmdb_layer WHERE title_id = $id", r => JsonNode.Parse(r.GetString(0)), ("$id", titleId));
        if (tmdb is not null && tmdb["kind"]?.GetValue<string>() != title.Kind) tmdb = null;

        Clear(c, titleId);

        var show = title.Kind == "show";
        var english = Text(tmdb?[show ? "name" : "title"]) ?? imdb?.Primary;
        var original = imdb?.Original ?? Text(tmdb?[show ? "original_name" : "original_title"]) ?? english;
        var released = Text(tmdb?[show ? "first_air_date" : "release_date"]);
        var year = imdb?.Year ?? (released is { Length: >= 4 } && int.TryParse(released[..4], NumberStyles.None, CultureInfo.InvariantCulture, out var y) ? y : null);
        var runtime = imdb?.Runtime ?? (show ? Int(tmdb?["episode_run_time"]?.AsArray().FirstOrDefault()) : Int(tmdb?["runtime"]));
        var poster = Text(tmdb?["poster_path"]);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var listed = english is not null && poster is not null && !Placeholder(english, released, Text(tmdb?["status"]), today);

        c.Execute("""
            UPDATE titles SET
                listed = $listed, english_title = $english, original_title = $original, original_language = $language,
                year = $year, released = $released, runtime = $runtime, status = $status, overview = $overview, tagline = $tagline,
                poster = $poster, backdrop = $backdrop, logo = $logo, seasons = $seasons, episodes = $episodes,
                popularity = $popularity, built_at = $at
            WHERE id = $id
            """,
            ("$id", titleId), ("$listed", listed), ("$english", english), ("$original", original),
            ("$language", Text(tmdb?["original_language"])), ("$year", year), ("$released", released), ("$runtime", runtime),
            ("$status", Text(tmdb?["status"])), ("$overview", Text(tmdb?["overview"])), ("$tagline", Text(tmdb?["tagline"])),
            ("$poster", poster), ("$backdrop", Text(tmdb?["backdrop_path"])), ("$logo", Text(tmdb?["logo_path"])),
            ("$seasons", Int(tmdb?["number_of_seasons"])), ("$episodes", Int(tmdb?["number_of_episodes"])),
            ("$popularity", tmdb?["popularity"]?.GetValue<double>()), ("$at", clock.GetUtcNow()));

        Names(c, titleId, english, original, tmdb);
        Genres(c, titleId, imdb, tmdb);
        if (tmdb is not null)
        {
            Keywords(c, titleId, tmdb);
            Companies(c, titleId, tmdb, show);
            Places(c, titleId, tmdb);
            Releases(c, titleId, tmdb, show);
            Credits(c, titleId, tmdb);
            Collection(c, titleId, tmdb);
            Similar(c, titleId, tmdb);
            Videos(c, titleId, tmdb);
            if (show) Seasons(c, titleId, tmdb);
        }
        c.Execute($"""
            INSERT INTO title_facts (title_id, property, item, label)
            SELECT title_id, property, item, label FROM wikidata_claims
            WHERE title_id = $id AND property IN ({string.Join(", ", Facts.Select(f => $"'{f}'"))})
            """, ("$id", titleId));
        Search(c, titleId, listed);
    }

    /// <summary>Whether a title is a placeholder: not out yet, and named <c>Untitled …</c> for want of a name.</summary>
    public static bool Placeholder(string english, string? released, string? status, DateOnly today)
    {
        if (!english.StartsWith("Untitled", StringComparison.OrdinalIgnoreCase)) return false;
        if (status is "Released" or "Ended" or "Returning Series" or "Canceled") return false;
        return released is null || string.CompareOrdinal(released, today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) > 0;
    }

    private static void Clear(SqliteConnection c, long titleId)
    {
        foreach (var table in new[]
                 {
                     "title_names", "title_genres", "title_keywords", "title_companies", "title_countries", "title_languages",
                     "title_releases", "credits", "similar", "title_videos", "seasons", "episodes", "title_facts",
                 })
            c.Execute($"DELETE FROM {table} WHERE title_id = $id", ("$id", titleId));
        c.Execute("DELETE FROM franchise_titles WHERE title_id = $id AND franchise_id IN (SELECT id FROM franchises WHERE source = 'tmdb')", ("$id", titleId));
        c.Execute("DELETE FROM titles_fts WHERE rowid = $id", ("$id", titleId));
    }

    private static void Names(SqliteConnection c, long titleId, string? english, string? original, JsonNode? tmdb)
    {
        void Name(string? name, string kind, string country)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            c.Execute("INSERT OR IGNORE INTO title_names (title_id, name, kind, country) VALUES ($id, $name, $kind, $country)",
                ("$id", titleId), ("$name", name.Trim()), ("$kind", kind), ("$country", country));
        }

        Name(english, "english", "");
        Name(original, "original", "");
        foreach (var alternative in Items(tmdb?["alternative_titles"]))
            Name(Text(alternative["title"]), "alternative", Text(alternative["iso_3166_1"]) ?? "");
    }

    private static void Genres(SqliteConnection c, long titleId, ImdbRow? imdb, JsonNode? tmdb)
    {
        foreach (var genre in (imdb?.Genres ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            c.Execute("INSERT OR IGNORE INTO title_genres (title_id, source, genre) VALUES ($id, 'imdb', $genre)", ("$id", titleId), ("$genre", genre));
        foreach (var genre in Items(tmdb?["genres"]))
            if (Text(genre["name"]) is { } name)
                c.Execute("INSERT OR IGNORE INTO title_genres (title_id, source, genre) VALUES ($id, 'tmdb', $genre)", ("$id", titleId), ("$genre", name));
    }

    private static void Keywords(SqliteConnection c, long titleId, JsonNode tmdb)
    {
        foreach (var keyword in Items(tmdb["keywords"]))
        {
            if (Int(keyword["id"]) is not { } id || Text(keyword["name"]) is not { } name) continue;
            c.Execute("INSERT INTO keywords (id, name) VALUES ($k, $name) ON CONFLICT (id) DO UPDATE SET name = excluded.name", ("$k", id), ("$name", name));
            c.Execute("INSERT OR IGNORE INTO title_keywords (title_id, keyword_id) VALUES ($id, $k)", ("$id", titleId), ("$k", id));
        }
    }

    private static void Companies(SqliteConnection c, long titleId, JsonNode tmdb, bool show)
    {
        void Company(JsonNode company, string role)
        {
            if (Int(company["id"]) is not { } id || Text(company["name"]) is not { } name) return;
            c.Execute("""
                INSERT INTO companies (id, name, country, logo) VALUES ($c, $name, $country, $logo)
                ON CONFLICT (id) DO UPDATE SET name = excluded.name, country = excluded.country, logo = excluded.logo
                """, ("$c", id), ("$name", name), ("$country", Text(company["origin_country"])), ("$logo", Text(company["logo_path"])));
            c.Execute("INSERT OR IGNORE INTO title_companies (title_id, company_id, role) VALUES ($id, $c, $role)",
                ("$id", titleId), ("$c", id), ("$role", role));
        }

        foreach (var company in Items(tmdb["production_companies"])) Company(company, "production");
        if (show)
            foreach (var network in Items(tmdb["networks"])) Company(network, "network");
    }

    private static void Places(SqliteConnection c, long titleId, JsonNode tmdb)
    {
        var countries = Items(tmdb["production_countries"]).Select(p => Text(p["iso_3166_1"]))
            .Concat((tmdb["origin_country"]?.AsArray() ?? []).Select(Text));
        foreach (var country in countries.OfType<string>().Distinct(StringComparer.Ordinal))
            c.Execute("INSERT OR IGNORE INTO title_countries (title_id, country) VALUES ($id, $country)", ("$id", titleId), ("$country", country));
        foreach (var language in Items(tmdb["spoken_languages"]).Select(l => Text(l["iso_639_1"])).OfType<string>().Distinct(StringComparer.Ordinal))
            c.Execute("INSERT OR IGNORE INTO title_languages (title_id, language) VALUES ($id, $language)", ("$id", titleId), ("$language", language));
    }

    /// <summary>
    /// A certificate and a release date per country. For a film, the certificate is the first non-empty
    /// one of the theatrical releases, else of any; the date is the earliest theatrical, else the earliest.
    /// </summary>
    private static void Releases(SqliteConnection c, long titleId, JsonNode tmdb, bool show)
    {
        if (show)
        {
            foreach (var rating in Items(tmdb["certificates"]))
                if (Text(rating["country"]) is { } country)
                    c.Execute("INSERT OR IGNORE INTO title_releases (title_id, country, certificate) VALUES ($id, $country, $cert)",
                        ("$id", titleId), ("$country", country), ("$cert", Text(rating["certificate"])));
            return;
        }

        foreach (var release in Items(tmdb["releases"]))
        {
            if (Text(release["country"]) is not { } country) continue;
            var dates = Items(release["dates"]).ToList();
            var theatrical = dates.Where(d => Int(d["type"]) is 2 or 3).ToList();
            var certificate = theatrical.Concat(dates).Select(d => Text(d["certificate"])).FirstOrDefault(t => t is not null);
            var date = (theatrical.Count > 0 ? theatrical : dates).Select(d => Text(d["date"])).OfType<string>()
                .Select(d => d.Length >= 10 ? d[..10] : d).Order(StringComparer.Ordinal).FirstOrDefault();
            c.Execute("INSERT OR IGNORE INTO title_releases (title_id, country, certificate, released) VALUES ($id, $country, $cert, $date)",
                ("$id", titleId), ("$country", country), ("$cert", certificate), ("$date", date));
        }
    }

    private static void Credits(SqliteConnection c, long titleId, JsonNode tmdb)
    {
        var ord = 0;
        foreach (var member in Items(tmdb["cast"]))
            Credit(c, titleId, member, "cast", Text(member["character"]) ?? "", ord++);
        ord = 0;
        foreach (var member in Items(tmdb["crew"]))
            Credit(c, titleId, member, Text(member["department"]) ?? "Crew", Text(member["job"]) ?? "", ord++);
    }

    private static void Credit(SqliteConnection c, long titleId, JsonNode member, string department, string role, int ord)
    {
        if (Int(member["id"]) is not { } tmdbId || Text(member["name"]) is not { } name) return;
        var personId = c.First("""
            INSERT INTO people (tmdb_id, name, profile, department) VALUES ($tmdb, $name, $profile, $department)
            ON CONFLICT (tmdb_id) DO UPDATE SET name = excluded.name, profile = coalesce(excluded.profile, people.profile),
                department = coalesce(excluded.department, people.department)
            RETURNING id
            """, r => r.GetInt64(0),
            ("$tmdb", tmdbId), ("$name", name), ("$profile", Text(member["profile_path"])), ("$department", Text(member["department"])));
        c.Execute("DELETE FROM people_fts WHERE rowid = $p", ("$p", personId));
        c.Execute("INSERT INTO people_fts (rowid, name) VALUES ($p, $name)", ("$p", personId), ("$name", name));
        c.Execute("""
            INSERT OR IGNORE INTO credits (title_id, person_id, department, role, ord) VALUES ($id, $p, $department, $role, $ord)
            """, ("$id", titleId), ("$p", personId), ("$department", department), ("$role", role), ("$ord", ord));
    }

    private static void Collection(SqliteConnection c, long titleId, JsonNode tmdb)
    {
        if (tmdb["belongs_to_collection"] is not { } collection || Int(collection["id"]) is not { } id || Text(collection["name"]) is not { } name)
            return;
        var franchiseId = c.First("""
            INSERT INTO franchises (source, source_id, name, poster, backdrop) VALUES ('tmdb', $source, $name, $poster, $backdrop)
            ON CONFLICT (source, source_id) DO UPDATE SET name = excluded.name, poster = excluded.poster, backdrop = excluded.backdrop
            RETURNING id
            """, r => r.GetInt64(0),
            ("$source", id.ToString(CultureInfo.InvariantCulture)), ("$name", name),
            ("$poster", Text(collection["poster_path"])), ("$backdrop", Text(collection["backdrop_path"])));
        // The title's place in its series where Wikidata numbers it; the collection's order is release order otherwise.
        c.Execute("""
            INSERT OR IGNORE INTO franchise_titles (franchise_id, title_id, ordinal)
            VALUES ($f, $id, (SELECT min(ordinal) FROM wikidata_claims WHERE title_id = $id AND property = 'P179' AND ordinal IS NOT NULL))
            """, ("$f", franchiseId), ("$id", titleId));
    }

    /// <summary>The database's recommendations the catalogue holds, by the kind each names.</summary>
    private static void Similar(SqliteConnection c, long titleId, JsonNode tmdb)
    {
        var rank = 0;
        foreach (var other in Items(tmdb["recommendations"]))
        {
            rank++;
            if (Int(other["id"]) is not { } id) continue;
            var kind = Text(other["media_type"]) == "tv" ? "show" : "film";
            c.Execute("""
                INSERT OR IGNORE INTO similar (title_id, other_id, rank)
                SELECT $id, t.id, $rank FROM titles t WHERE t.kind = $kind AND t.tmdb_id = $other AND t.id <> $id
                """, ("$id", titleId), ("$rank", rank), ("$kind", kind), ("$other", id));
        }
    }

    private static void Videos(SqliteConnection c, long titleId, JsonNode tmdb)
    {
        var ord = 0;
        foreach (var video in Items(tmdb["videos"]))
        {
            if (Text(video["key"]) is not { } key) continue;
            c.Execute("INSERT INTO title_videos (title_id, ord, site, key, type, name) VALUES ($id, $ord, $site, $key, $type, $name)",
                ("$id", titleId), ("$ord", ord++), ("$site", Text(video["site"]) ?? "YouTube"), ("$key", key),
                ("$type", Text(video["type"]) ?? "Trailer"), ("$name", Text(video["name"]) ?? ""));
        }
    }

    private static void Seasons(SqliteConnection c, long titleId, JsonNode tmdb)
    {
        var seasons = c.Query("SELECT json FROM tmdb_seasons WHERE title_id = $id ORDER BY season", r => JsonNode.Parse(r.GetString(0)), ("$id", titleId));
        var summaries = Items(tmdb["seasons"]).Where(s => Int(s["season_number"]) is not null).ToDictionary(s => Int(s["season_number"])!.Value);
        foreach (var season in seasons.OfType<JsonNode>())
        {
            if (Int(season["season_number"]) is not { } number) continue;
            var episodes = Items(season["episodes"]).ToList();
            c.Execute("""
                INSERT OR REPLACE INTO seasons (title_id, number, name, overview, air_date, poster, episodes)
                VALUES ($id, $n, $name, $overview, $air, $poster, $count)
                """,
                ("$id", titleId), ("$n", number), ("$name", Text(season["name"])), ("$overview", Text(season["overview"])),
                ("$air", Text(season["air_date"])), ("$poster", Text(season["poster_path"]) ?? (summaries.TryGetValue(number, out var s) ? Text(s["poster_path"]) : null)),
                ("$count", episodes.Count));
            foreach (var episode in episodes)
            {
                if (Int(episode["episode_number"]) is not { } e) continue;
                c.Execute("""
                    INSERT OR REPLACE INTO episodes (title_id, season, number, name, overview, air_date, runtime, still)
                    VALUES ($id, $s, $e, $name, $overview, $air, $runtime, $still)
                    """,
                    ("$id", titleId), ("$s", number), ("$e", e), ("$name", Text(episode["name"])), ("$overview", Text(episode["overview"])),
                    ("$air", Text(episode["air_date"])), ("$runtime", Int(episode["runtime"])), ("$still", Text(episode["still_path"])));
            }
        }
    }

    /// <summary>A listed title's every name, in one row its id keys; a candidate is found by no search.</summary>
    private static void Search(SqliteConnection c, long titleId, bool listed)
    {
        if (!listed) return;
        var names = c.Query("SELECT DISTINCT name FROM title_names WHERE title_id = $id", r => r.GetString(0), ("$id", titleId));
        if (names.Count == 0) return;
        var joined = new StringBuilder();
        foreach (var name in names) joined.Append(name).Append('\n');
        c.Execute("INSERT INTO titles_fts (rowid, names) VALUES ($id, $names)", ("$id", titleId), ("$names", joined.ToString()));
    }

    private static IEnumerable<JsonNode> Items(JsonNode? array) => array is JsonArray items ? items.OfType<JsonNode>() : [];

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    private static int? Int(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;

    private sealed record ImdbRow(string Primary, string Original, int? Year, int? Runtime, string Genres);
}
