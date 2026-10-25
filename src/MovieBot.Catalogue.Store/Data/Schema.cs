using Microsoft.Data.Sqlite;

namespace TheKrystalShip.MovieBot.Catalogue.Data;

/// <summary>
/// The catalogue's tables and the version they are at (the file's <c>user_version</c>, counting the
/// steps it has run). A file at a version this build does not know is refused.
///
/// The catalogue is three kinds of table. **Identity**: <c>titles</c> and <c>people</c>, keyed by the
/// catalogue's own ids and mapped to each source's. **Layers**: what each source last said about a
/// title, as it said it and when (<c>imdb_layer</c>, <c>tmdb_layer</c> and <c>tmdb_seasons</c>,
/// <c>wikidata_layer</c> and <c>wikidata_claims</c>). **The entry**: the columns of <c>titles</c> past
/// its ids and every table a page filters, sorts, groups or searches by, all of it built from the layers
/// by <c>EntryBuilder</c> and nothing else, so it can be dropped and built again from them.
/// </summary>
public static class Schema
{
    private static readonly string[] Steps = [Tables, Fingerprints, Cards, CardGenres, Lookups, PopularOrder, ShelfColumns];

    public static int Version => Steps.Length;

    public static void Apply(SqliteConnection connection)
    {
        var version = connection.Count("PRAGMA user_version;");
        if (version == Version) return;
        if (version < 0 || version > Version)
            throw new InvalidOperationException(
                $"The catalogue {connection.DataSource} is at schema version {version}; this build knows version {Version}.");

        using var transaction = connection.BeginTransaction();
        foreach (var step in Steps[(int)version..]) connection.Execute(step);
        connection.Execute($"PRAGMA user_version = {Version};");
        transaction.Commit();
    }

    private const string ShelfColumns = """
        -- What a list filters by, carried in the indexes it is read down, so a list or a count over every
        -- film never reads a whole row (its overview and the rest) to check a column: the popular order
        -- (its last tie-breaker, the id, named so the order is the index's whole) with every filter
        -- column, and for the counts and the rating order, the filter columns first.
        DROP INDEX titles_popularity;
        CREATE INDEX titles_popularity ON titles
            (listed, popularity DESC, votes DESC, id, kind, category, original_language, year, runtime, rating, genres);
        CREATE INDEX titles_shelf ON titles
            (listed, kind, rating, votes, year, runtime, original_language, category, popularity, genres);
        ANALYZE titles;
        """;

    private const string PopularOrder = """
        -- The most popular first, as a list orders by default (popularity, then votes, then id, which every
        -- index ends with): a list reads its first page down this index and stops, rather than sorting
        -- every title it names.
        DROP INDEX titles_popularity;
        CREATE INDEX titles_popularity ON titles (listed, popularity DESC, votes DESC);
        ANALYZE titles;
        """;

    private const string Lookups = """
        -- A title by its Wikidata item: what a title page's follows and followed-by lists join through.
        CREATE INDEX titles_wikidata ON titles (wikidata_id);

        -- The planner's statistics. Without them it takes listed = 1 for a selective filter, and starts
        -- a lookup by id or a narrow filter from every listed title. CatalogueDatabase keeps them current.
        ANALYZE;
        """;

    private const string CardGenres = """
        -- The genres a card shows, comma-joined: IMDb's, or the film database's for a title IMDb has none
        -- for. Built, so a list and its facets read them from the row.
        ALTER TABLE titles ADD COLUMN genres TEXT NOT NULL DEFAULT '';

        -- Every Wikidata item listed titles name in a fact a filter takes, with how many name it: what a
        -- word is resolved against. Built with the universes (FactItems).
        CREATE TABLE fact_items (
            property TEXT NOT NULL,
            item     TEXT NOT NULL,
            label    TEXT NOT NULL,
            titles   INTEGER NOT NULL,
            PRIMARY KEY (property, item)
        ) WITHOUT ROWID;
        """;

    private const string Cards = """
        -- What a card shows that the entry did not hold. Built: a show's last year, the category (anime is
        -- Animation made in Japanese), a film's budget and revenue, a show's next episode. Kept current by
        -- every rating written (Ratings.Show): the rating a card shows, its votes and whose it is, IMDb's
        -- where IMDb rates the title and the film database's otherwise, with no value under 25 votes.
        ALTER TABLE titles ADD COLUMN end_year INTEGER;
        ALTER TABLE titles ADD COLUMN category TEXT;
        ALTER TABLE titles ADD COLUMN budget INTEGER;
        ALTER TABLE titles ADD COLUMN revenue INTEGER;
        ALTER TABLE titles ADD COLUMN next_air_date TEXT;
        ALTER TABLE titles ADD COLUMN next_season INTEGER;
        ALTER TABLE titles ADD COLUMN next_number INTEGER;
        ALTER TABLE titles ADD COLUMN next_name TEXT;
        ALTER TABLE titles ADD COLUMN rating REAL;
        ALTER TABLE titles ADD COLUMN votes INTEGER;
        ALTER TABLE titles ADD COLUMN rated_by TEXT;
        CREATE INDEX titles_popularity ON titles (listed, popularity DESC);
        CREATE INDEX titles_released ON titles (listed, released);
        CREATE INDEX titles_votes ON titles (listed, votes DESC);
        CREATE INDEX credits_title ON credits (title_id, department, ord);

        -- Search weighs a title's English name over its original and those over the alternatives, each
        -- its own column, so a title known by many names is not found later for having them.
        DROP TABLE titles_fts;
        CREATE VIRTUAL TABLE titles_fts USING fts5 (title, original, alternative, tokenize = 'unicode61 remove_diacritics 2');

        UPDATE titles SET
            rated_by = (SELECT source FROM ratings r WHERE r.title_id = titles.id ORDER BY r.source = 'imdb' DESC LIMIT 1),
            votes = (SELECT votes FROM ratings r WHERE r.title_id = titles.id ORDER BY r.source = 'imdb' DESC LIMIT 1),
            rating = (SELECT CASE WHEN votes >= 25 THEN rating END FROM ratings r WHERE r.title_id = titles.id ORDER BY r.source = 'imdb' DESC LIMIT 1);
        """;

    private const string Fingerprints = """
        -- What each layer holds, as a hash: an answer whose hash is the one held changes nothing but when
        -- the layer was last asked. The film database's hash leaves out its popularity and votes, which
        -- move daily and are kept in their own columns.
        ALTER TABLE tmdb_layer ADD COLUMN hash TEXT NOT NULL DEFAULT '';
        ALTER TABLE wikidata_layer ADD COLUMN hash TEXT NOT NULL DEFAULT '';

        -- built_from is the hash of everything an entry was built from (EntryBuilder.Fingerprint), so a
        -- build from the same inputs is skipped. changed_at is when anything a page shows of the title
        -- last changed: its entry, or a rating.
        ALTER TABLE titles ADD COLUMN built_from TEXT;
        ALTER TABLE titles ADD COLUMN changed_at TEXT;

        -- The film database's recommendations beside a title, in its order, by its own ids: a reader joins
        -- them to the titles the catalogue lists, so a title the catalogue gains later appears without the
        -- recommending title being built again.
        DROP TABLE similar;
        CREATE TABLE similar (
            title_id INTEGER NOT NULL REFERENCES titles (id) ON DELETE CASCADE,
            rank     INTEGER NOT NULL,
            kind     TEXT NOT NULL CHECK (kind IN ('film', 'show')),
            tmdb_id  INTEGER NOT NULL,
            PRIMARY KEY (title_id, rank)
        ) WITHOUT ROWID;
        """;

    private const string Tables = """
        CREATE TABLE meta (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        ) WITHOUT ROWID;

        -- One row per title the catalogue knows: a candidate until its entry is listed. imdb_id is the
        -- title's IMDb id in its canonical form (tt0232500). admitted is how it came in: by its IMDb
        -- votes, or by a ranking holding it while it had too few. listed is whether it is an entry,
        -- which every page shows; a candidate is shown nowhere. Every column after listed is built.
        CREATE TABLE titles (
            id                INTEGER PRIMARY KEY,
            imdb_id           TEXT NOT NULL UNIQUE,
            kind              TEXT NOT NULL CHECK (kind IN ('film', 'show')),
            tmdb_id           INTEGER,
            wikidata_id       TEXT,
            admitted          TEXT NOT NULL CHECK (admitted IN ('votes', 'ranking')),
            listed            INTEGER NOT NULL DEFAULT 0,
            english_title     TEXT,
            original_title    TEXT,
            original_language TEXT,
            year              INTEGER,
            released          TEXT,
            runtime           INTEGER,
            status            TEXT,
            overview          TEXT,
            tagline           TEXT,
            poster            TEXT,
            backdrop          TEXT,
            logo              TEXT,
            seasons           INTEGER,
            episodes          INTEGER,
            popularity        REAL,
            built_at          TEXT
        );
        CREATE UNIQUE INDEX titles_tmdb ON titles (kind, tmdb_id) WHERE tmdb_id IS NOT NULL;
        CREATE INDEX titles_listed ON titles (listed, kind);

        -- IMDb's layer: title.basics as the dataset of the day has it. genres is a comma-joined list.
        CREATE TABLE imdb_layer (
            title_id       INTEGER PRIMARY KEY REFERENCES titles (id) ON DELETE CASCADE,
            primary_title  TEXT NOT NULL,
            original_title TEXT NOT NULL,
            start_year     INTEGER,
            end_year       INTEGER,
            runtime        INTEGER,
            genres         TEXT NOT NULL,
            edition        TEXT NOT NULL
        );

        -- The film database's layer: its description with everything appended, in English, as kept
        -- (TmdbLayer.Prune). due is why it is asked for before its age makes it due: 'changed' by the
        -- changes feed, 'rating' by the rating cadence. A title the database holds nothing for is
        -- missing, and asked after again once that is a sweep old.
        CREATE TABLE tmdb_layer (
            title_id   INTEGER PRIMARY KEY REFERENCES titles (id) ON DELETE CASCADE,
            fetched_at TEXT NOT NULL,
            json       TEXT NOT NULL
        );
        CREATE INDEX tmdb_layer_age ON tmdb_layer (fetched_at);

        CREATE TABLE tmdb_due (
            title_id INTEGER PRIMARY KEY REFERENCES titles (id) ON DELETE CASCADE,
            reason   TEXT NOT NULL,
            since    TEXT NOT NULL
        );

        CREATE TABLE tmdb_missing (
            title_id INTEGER PRIMARY KEY REFERENCES titles (id) ON DELETE CASCADE,
            asked_at TEXT NOT NULL
        );

        -- A show's seasons as the database describes each, with its episodes.
        CREATE TABLE tmdb_seasons (
            title_id   INTEGER NOT NULL REFERENCES titles (id) ON DELETE CASCADE,
            season     INTEGER NOT NULL,
            fetched_at TEXT NOT NULL,
            json       TEXT NOT NULL,
            PRIMARY KEY (title_id, season)
        );

        -- Wikidata's layer: the item a title's IMDb id names (null: none does) and the facts it holds.
        -- A claim is a property, the item it names and that item's English label; ordinal is a
        -- series claim's place in the series. P144/P50 is the author of a work the title is based on.
        CREATE TABLE wikidata_layer (
            title_id   INTEGER PRIMARY KEY REFERENCES titles (id) ON DELETE CASCADE,
            item       TEXT,
            fetched_at TEXT NOT NULL
        );

        CREATE TABLE wikidata_claims (
            title_id INTEGER NOT NULL REFERENCES titles (id) ON DELETE CASCADE,
            property TEXT NOT NULL,
            item     TEXT NOT NULL,
            label    TEXT,
            ordinal  TEXT,
            PRIMARY KEY (title_id, property, item)
        ) WITHOUT ROWID;

        -- Each source's rating of a title, beside each other: neither overrides the other. seen_at is
        -- when the answer it came from was asked.
        CREATE TABLE ratings (
            title_id INTEGER NOT NULL REFERENCES titles (id) ON DELETE CASCADE,
            source   TEXT NOT NULL CHECK (source IN ('imdb', 'tmdb')),
            rating   REAL NOT NULL,
            votes    INTEGER NOT NULL,
            seen_at  TEXT NOT NULL,
            PRIMARY KEY (title_id, source)
        ) WITHOUT ROWID;

        -- What the database ranks: each list's titles in its order, as last asked.
        CREATE TABLE rankings (
            list       TEXT NOT NULL,
            rank       INTEGER NOT NULL,
            title_id   INTEGER NOT NULL REFERENCES titles (id) ON DELETE CASCADE,
            fetched_at TEXT NOT NULL,
            PRIMARY KEY (list, rank)
        ) WITHOUT ROWID;
        CREATE INDEX rankings_title ON rankings (title_id);

        -- The titles whose entry is built again because a layer of theirs changed.
        CREATE TABLE build_due (
            title_id INTEGER PRIMARY KEY REFERENCES titles (id) ON DELETE CASCADE
        );

        -- Everything below is built from the layers.

        -- Every name a title goes by: its English and original titles and the database's alternative
        -- titles, each with the country it is used in where the database says.
        CREATE TABLE title_names (
            title_id INTEGER NOT NULL REFERENCES titles (id) ON DELETE CASCADE,
            name     TEXT NOT NULL,
            kind     TEXT NOT NULL CHECK (kind IN ('english', 'original', 'alternative')),
            country  TEXT NOT NULL DEFAULT '',
            PRIMARY KEY (title_id, kind, name, country)
        ) WITHOUT ROWID;

        -- Genres by source: IMDb's, which every title with an IMDb layer has, and the film database's.
        CREATE TABLE title_genres (
            title_id INTEGER NOT NULL REFERENCES titles (id) ON DELETE CASCADE,
            source   TEXT NOT NULL CHECK (source IN ('imdb', 'tmdb')),
            genre    TEXT NOT NULL,
            PRIMARY KEY (title_id, source, genre)
        ) WITHOUT ROWID;
        CREATE INDEX title_genres_genre ON title_genres (source, genre);

        CREATE TABLE keywords (
            id   INTEGER PRIMARY KEY,
            name TEXT NOT NULL
        );

        CREATE TABLE title_keywords (
            title_id   INTEGER NOT NULL REFERENCES titles (id) ON DELETE CASCADE,
            keyword_id INTEGER NOT NULL REFERENCES keywords (id),
            PRIMARY KEY (title_id, keyword_id)
        ) WITHOUT ROWID;
        CREATE INDEX title_keywords_keyword ON title_keywords (keyword_id);

        CREATE TABLE companies (
            id      INTEGER PRIMARY KEY,
            name    TEXT NOT NULL,
            country TEXT,
            logo    TEXT
        );

        -- A company a title was made by, or for a show a network it aired on.
        CREATE TABLE title_companies (
            title_id   INTEGER NOT NULL REFERENCES titles (id) ON DELETE CASCADE,
            company_id INTEGER NOT NULL REFERENCES companies (id),
            role       TEXT NOT NULL CHECK (role IN ('production', 'network')),
            PRIMARY KEY (title_id, company_id, role)
        ) WITHOUT ROWID;
        CREATE INDEX title_companies_company ON title_companies (company_id);

        -- Countries of origin and production, and the languages spoken, as ISO codes.
        CREATE TABLE title_countries (
            title_id INTEGER NOT NULL REFERENCES titles (id) ON DELETE CASCADE,
            country  TEXT NOT NULL,
            PRIMARY KEY (title_id, country)
        ) WITHOUT ROWID;
        CREATE INDEX title_countries_country ON title_countries (country);

        CREATE TABLE title_languages (
            title_id INTEGER NOT NULL REFERENCES titles (id) ON DELETE CASCADE,
            language TEXT NOT NULL,
            PRIMARY KEY (title_id, language)
        ) WITHOUT ROWID;
        CREATE INDEX title_languages_language ON title_languages (language);

        -- A title's certificate and release date in each country the database lists one for.
        CREATE TABLE title_releases (
            title_id    INTEGER NOT NULL REFERENCES titles (id) ON DELETE CASCADE,
            country     TEXT NOT NULL,
            certificate TEXT,
            released    TEXT,
            PRIMARY KEY (title_id, country)
        ) WITHOUT ROWID;

        -- People, keyed like titles: the catalogue's id mapped to the film database's and IMDb's.
        CREATE TABLE people (
            id         INTEGER PRIMARY KEY,
            tmdb_id    INTEGER NOT NULL UNIQUE,
            imdb_id    TEXT,
            name       TEXT NOT NULL,
            profile    TEXT,
            department TEXT
        );

        -- Who is in a title and who made it. department is 'cast' for the cast; role is the character
        -- played or the job done; ord is billing for the cast and the database's order for the crew.
        CREATE TABLE credits (
            title_id   INTEGER NOT NULL REFERENCES titles (id) ON DELETE CASCADE,
            person_id  INTEGER NOT NULL REFERENCES people (id),
            department TEXT NOT NULL,
            role       TEXT NOT NULL DEFAULT '',
            ord        INTEGER NOT NULL,
            PRIMARY KEY (title_id, person_id, department, role)
        ) WITHOUT ROWID;
        CREATE INDEX credits_person ON credits (person_id);

        -- A franchise is a film database collection, or a Wikidata universe holding several of them
        -- or holding shows a collection cannot. A universe's titles are its own; a collection's are
        -- the titles that name it.
        CREATE TABLE franchises (
            id            INTEGER PRIMARY KEY,
            source        TEXT NOT NULL CHECK (source IN ('tmdb', 'wikidata')),
            source_id     TEXT NOT NULL,
            name          TEXT NOT NULL,
            poster        TEXT,
            backdrop      TEXT,
            UNIQUE (source, source_id)
        );

        CREATE TABLE franchise_titles (
            franchise_id INTEGER NOT NULL REFERENCES franchises (id) ON DELETE CASCADE,
            title_id     INTEGER NOT NULL REFERENCES titles (id) ON DELETE CASCADE,
            ordinal      TEXT,
            PRIMARY KEY (franchise_id, title_id)
        ) WITHOUT ROWID;
        CREATE INDEX franchise_titles_title ON franchise_titles (title_id);

        -- The collections a universe holds.
        CREATE TABLE franchise_parts (
            universe_id   INTEGER NOT NULL REFERENCES franchises (id) ON DELETE CASCADE,
            collection_id INTEGER NOT NULL REFERENCES franchises (id) ON DELETE CASCADE,
            PRIMARY KEY (universe_id, collection_id)
        ) WITHOUT ROWID;

        -- The facts Wikidata holds that a page filters by: where a title is set and filmed, what it is
        -- based on and by whom, its awards and nominations, its subjects and genres, its series and
        -- what it follows or is followed by.
        CREATE TABLE title_facts (
            title_id INTEGER NOT NULL REFERENCES titles (id) ON DELETE CASCADE,
            property TEXT NOT NULL,
            item     TEXT NOT NULL,
            label    TEXT,
            PRIMARY KEY (title_id, property, item)
        ) WITHOUT ROWID;
        CREATE INDEX title_facts_item ON title_facts (property, item);

        -- The titles the database recommends beside a title, in its order, where the catalogue holds them.
        CREATE TABLE similar (
            title_id INTEGER NOT NULL REFERENCES titles (id) ON DELETE CASCADE,
            other_id INTEGER NOT NULL REFERENCES titles (id) ON DELETE CASCADE,
            rank     INTEGER NOT NULL,
            PRIMARY KEY (title_id, other_id)
        ) WITHOUT ROWID;

        CREATE TABLE title_videos (
            title_id INTEGER NOT NULL REFERENCES titles (id) ON DELETE CASCADE,
            ord      INTEGER NOT NULL,
            site     TEXT NOT NULL,
            key      TEXT NOT NULL,
            type     TEXT NOT NULL,
            name     TEXT NOT NULL,
            PRIMARY KEY (title_id, ord)
        ) WITHOUT ROWID;

        CREATE TABLE seasons (
            title_id INTEGER NOT NULL REFERENCES titles (id) ON DELETE CASCADE,
            number   INTEGER NOT NULL,
            name     TEXT,
            overview TEXT,
            air_date TEXT,
            poster   TEXT,
            episodes INTEGER NOT NULL,
            PRIMARY KEY (title_id, number)
        ) WITHOUT ROWID;

        CREATE TABLE episodes (
            title_id INTEGER NOT NULL REFERENCES titles (id) ON DELETE CASCADE,
            season   INTEGER NOT NULL,
            number   INTEGER NOT NULL,
            name     TEXT,
            overview TEXT,
            air_date TEXT,
            runtime  INTEGER,
            still    TEXT,
            PRIMARY KEY (title_id, season, number)
        ) WITHOUT ROWID;

        -- Search: every name a listed title goes by, and every person's name, rowid the catalogue's id.
        CREATE VIRTUAL TABLE titles_fts USING fts5 (names, tokenize = 'unicode61 remove_diacritics 2');
        CREATE VIRTUAL TABLE people_fts USING fts5 (name, tokenize = 'unicode61 remove_diacritics 2');

        -- The image files kept, by the database's path and the size they were fetched at. A file lives
        -- at images/<size><path> under the catalogue's directory.
        CREATE TABLE images (
            path       TEXT NOT NULL,
            size       TEXT NOT NULL,
            fetched_at TEXT NOT NULL,
            bytes      INTEGER NOT NULL,
            PRIMARY KEY (path, size)
        ) WITHOUT ROWID;
        CREATE INDEX images_age ON images (fetched_at);

        -- The shows the library holds, whose episodes' stills are kept.
        CREATE TABLE library_shows (
            title_id INTEGER PRIMARY KEY REFERENCES titles (id) ON DELETE CASCADE
        );
        """;
}
