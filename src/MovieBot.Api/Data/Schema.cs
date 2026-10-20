using Microsoft.Data.Sqlite;

namespace TheKrystalShip.MovieBot.Api.Data;

/// <summary>
/// The tables everything this service keeps lives in, and the version they are at.
///
/// The version is the database's <c>user_version</c>, and it counts the <see cref="Steps"/> the file
/// has run: each step takes a file at the version before it to its own, in one transaction, bringing
/// the records it holds across when a table changes shape. A new file runs every step and a file
/// behind runs the ones it lacks, so every file at a version holds the same tables. A file at a version this build does not know is refused rather than guessed at,
/// because a store reading columns that mean something else is a person's records read as somebody
/// else's.
///
/// A person is the account the identity provider names them by and nothing else, so no table is
/// keyed by anything a person typed. What the film database said about a title is kept as that
/// database's own shape, one JSON value per row beside the columns it is looked up and aged by: it is
/// an answer kept for its asker, never queried into, and its shape is the client's to own.
/// </summary>
internal static class Schema
{
    private static readonly string[] Steps = [Tables, Sorts, Wants, Together, Plans, TitleScores];

    public static int Version => Steps.Length;

    public static void Apply(SqliteConnection connection)
    {
        var version = connection.Count("PRAGMA user_version;");
        if (version == Version) return;
        if (version < 0 || version > Version)
            throw new InvalidOperationException(
                $"The database {connection.DataSource} is at schema version {version}; this build knows version {Version}.");

        using var transaction = connection.BeginTransaction();
        foreach (var step in Steps[(int)version..]) connection.Execute(step);
        connection.Execute($"PRAGMA user_version = {Version};");
        transaction.Commit();
    }

    private const string TitleScores = """
        -- The newest rating the film database has given each title, by its id and kind: the one every
        -- view shows. seen_at is when the answer it came from was asked. What the descriptions and the
        -- glances already hold seeds it, the newest of each title winning.
        CREATE TABLE title_scores (
            tmdb_id INTEGER NOT NULL,
            kind    TEXT NOT NULL CHECK (kind IN ('film', 'show')),
            rating  REAL NOT NULL,
            votes   INTEGER NOT NULL,
            seen_at TEXT NOT NULL,
            PRIMARY KEY (tmdb_id, kind)
        ) WITHOUT ROWID;

        INSERT INTO title_scores (tmdb_id, kind, rating, votes, seen_at)
        SELECT json_extract(glance, '$.tmdbId'), json_extract(glance, '$.kind'), json_extract(glance, '$.rating'),
               COALESCE(json_extract(glance, '$.votes'), 0), fetched_at
        FROM glances
        WHERE json_extract(glance, '$.rating') IS NOT NULL AND json_extract(glance, '$.kind') IN ('film', 'show')
        ON CONFLICT (tmdb_id, kind) DO UPDATE SET rating = excluded.rating, votes = excluded.votes, seen_at = excluded.seen_at
        WHERE excluded.seen_at > title_scores.seen_at;

        INSERT INTO title_scores (tmdb_id, kind, rating, votes, seen_at)
        SELECT json_extract(details, '$.tmdbId'), 'film', json_extract(details, '$.rating'),
               COALESCE(json_extract(details, '$.votes'), 0), fetched_at
        FROM film_descriptions
        WHERE json_extract(details, '$.rating') IS NOT NULL
        ON CONFLICT (tmdb_id, kind) DO UPDATE SET rating = excluded.rating, votes = excluded.votes, seen_at = excluded.seen_at
        WHERE excluded.seen_at > title_scores.seen_at;

        INSERT INTO title_scores (tmdb_id, kind, rating, votes, seen_at)
        SELECT json_extract(details, '$.tmdbId'), 'show', json_extract(details, '$.rating'),
               COALESCE(json_extract(details, '$.votes'), 0), fetched_at
        FROM show_descriptions
        WHERE json_extract(details, '$.rating') IS NOT NULL
        ON CONFLICT (tmdb_id, kind) DO UPDATE SET rating = excluded.rating, votes = excluded.votes, seen_at = excluded.seen_at
        WHERE excluded.seen_at > title_scores.seen_at;
        """;

    private const string Plans = """
        -- A plan to watch a title together: a few proposed times, the people invited and their answers.
        -- may_fetch is whether the maker's token held cinema:download, which lets the prefetcher fetch
        -- the title once a time is fixed.
        CREATE TABLE plans (
            id         TEXT PRIMARY KEY,
            imdb_id    TEXT NOT NULL,
            kind       TEXT NOT NULL CHECK (kind IN ('film', 'show')),
            season     INTEGER,
            number     INTEGER,
            made_by    TEXT NOT NULL,
            made_at    TEXT NOT NULL,
            may_fetch  INTEGER NOT NULL,
            state      TEXT NOT NULL CHECK (state IN ('proposed', 'fixed', 'open', 'done', 'cancelled')),
            fixed_time TEXT,
            room_id    TEXT
        ) WITHOUT ROWID;
        CREATE INDEX plans_state ON plans (state);

        CREATE TABLE plan_times (
            plan_id TEXT NOT NULL REFERENCES plans (id) ON DELETE CASCADE,
            at      TEXT NOT NULL,
            PRIMARY KEY (plan_id, at)
        ) WITHOUT ROWID;

        CREATE TABLE plan_people (
            plan_id    TEXT NOT NULL REFERENCES plans (id) ON DELETE CASCADE,
            user_id    TEXT NOT NULL,
            invited_at TEXT NOT NULL,
            PRIMARY KEY (plan_id, user_id)
        );
        CREATE INDEX plan_people_user ON plan_people (user_id);

        CREATE TABLE plan_answers (
            plan_id     TEXT NOT NULL,
            at          TEXT NOT NULL,
            user_id     TEXT NOT NULL,
            answer      TEXT NOT NULL CHECK (answer IN ('in', 'maybe', 'cant')),
            answered_at TEXT NOT NULL,
            PRIMARY KEY (plan_id, at, user_id),
            FOREIGN KEY (plan_id, at) REFERENCES plan_times (plan_id, at) ON DELETE CASCADE
        ) WITHOUT ROWID;
        """;

    private const string Together = """
        -- Whether the together digest is pushed; it is always kept in the bell.
        ALTER TABLE notification_preferences ADD COLUMN together INTEGER NOT NULL DEFAULT 0;
        """;

    private const string Wants = """
        -- What each person wants to watch, by the title's IMDb id. visible is whether the want is named
        -- to everybody signed in; may_fetch and may_wish are whether the token that wrote it held
        -- cinema:download and cinema:wish, which is what the server may spend on it.
        CREATE TABLE title_wants (
            user_id   TEXT NOT NULL,
            imdb_id   TEXT NOT NULL,
            kind      TEXT NOT NULL CHECK (kind IN ('film', 'show')),
            visible   INTEGER NOT NULL,
            may_fetch INTEGER NOT NULL,
            may_wish  INTEGER NOT NULL,
            since     TEXT NOT NULL,
            PRIMARY KEY (user_id, imdb_id)
        ) WITHOUT ROWID;
        CREATE INDEX title_wants_title ON title_wants (imdb_id);

        -- A saved title is a want of the person who saved it. Nobody agreed to be seen when they
        -- saved it, so it comes across as just-me.
        INSERT INTO title_wants (user_id, imdb_id, kind, visible, may_fetch, may_wish, since)
        SELECT added_by, imdb_id, MIN(kind), 0, 0, 0, MIN(added_at) FROM list_entries GROUP BY added_by, imdb_id;

        -- Somebody waiting on a film wants it, and held cinema:wish to wait. A title both saved and
        -- waited on keeps the earlier of the two moments.
        INSERT INTO title_wants (user_id, imdb_id, kind, visible, may_fetch, may_wish, since)
        SELECT user_id, imdb_id, 'film', 0, 0, 1, asked_at FROM wish_subscribers WHERE true
        ON CONFLICT (user_id, imdb_id) DO UPDATE SET
            may_wish = 1,
            since = MIN(title_wants.since, excluded.since);

        DROP TABLE list_entries;
        DROP TABLE list_members;
        DROP TABLE lists;
        DROP TABLE wish_subscribers;

        DELETE FROM notifications WHERE kind = 'shared';
        ALTER TABLE notification_preferences DROP COLUMN shared;
        """;

    private const string Sorts = """
        -- The order each person last picked on each page that sorts, by the site's name for the page.
        CREATE TABLE viewer_sorts (
            user_id   TEXT NOT NULL,
            surface   TEXT NOT NULL,
            key       TEXT NOT NULL,
            direction TEXT NOT NULL CHECK (direction IN ('asc', 'desc')),
            PRIMARY KEY (user_id, surface)
        ) WITHOUT ROWID;
        """;

    private const string Tables = """
        -- Single facts about this installation: the issuer records are kept against, when the watch
        -- record began, this server's push key pair.
        CREATE TABLE meta (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        ) WITHOUT ROWID;

        -- A table whose rows are listed in the order they were made has a rowid, and is read in
        -- its order where two rows were made at the same instant.

        -- What each account is called.
        CREATE TABLE people (
            user_id      TEXT PRIMARY KEY,
            display_name TEXT NOT NULL
        ) WITHOUT ROWID;

        CREATE TABLE viewer_preferences (
            user_id                  TEXT PRIMARY KEY,
            subtitle_language        TEXT,
            audio_language           TEXT,
            quality_height           INTEGER,
            offline_quality_height   INTEGER,
            skip_intros              INTEGER NOT NULL,
            style_size               INTEGER,
            style_font               TEXT,
            style_bold               INTEGER,
            style_text_color         TEXT,
            style_edge               TEXT,
            style_edge_color         TEXT,
            style_background_color   TEXT,
            style_background_opacity REAL,
            style_position           REAL
        ) WITHOUT ROWID;

        -- The subtitle and audio track each person watches each title with, the same on every device
        -- they watch it on. A title with no row, or a row naming no track, starts on the person's
        -- languages. subtitle_off is a chosen "no subtitles", which is not the same as no choice.
        CREATE TABLE viewer_tracks (
            user_id      TEXT NOT NULL,
            title_id     TEXT NOT NULL,
            subtitle_id  TEXT,
            subtitle_off INTEGER NOT NULL,
            audio_id     TEXT,
            PRIMARY KEY (user_id, title_id)
        ) WITHOUT ROWID;

        CREATE TABLE notification_preferences (
            user_id   TEXT PRIMARY KEY,
            available INTEGER NOT NULL,
            ready     INTEGER NOT NULL,
            episode   INTEGER NOT NULL,
            shared    INTEGER NOT NULL
        ) WITHOUT ROWID;

        -- How far each person got in each title.
        CREATE TABLE watch_progress (
            user_id          TEXT NOT NULL,
            title_id         TEXT NOT NULL,
            position_seconds REAL NOT NULL,
            duration_seconds REAL NOT NULL,
            last_watched_at  TEXT NOT NULL,
            finished         INTEGER NOT NULL,
            PRIMARY KEY (user_id, title_id)
        ) WITHOUT ROWID;
        CREATE INDEX watch_progress_title ON watch_progress (title_id);

        -- When each title was last played by anybody, which the library's clock reads.
        CREATE TABLE title_plays (
            title_id       TEXT PRIMARY KEY,
            last_played_at TEXT NOT NULL,
            last_played_by TEXT NOT NULL
        ) WITHOUT ROWID;

        CREATE TABLE lists (
            id          TEXT PRIMARY KEY,
            name        TEXT NOT NULL,
            owner       TEXT NOT NULL,
            watch_later INTEGER NOT NULL,
            created_at  TEXT NOT NULL,
            updated_at  TEXT NOT NULL
        );
        CREATE INDEX lists_owner ON lists (owner);
        CREATE UNIQUE INDEX lists_one_watch_later ON lists (owner) WHERE watch_later = 1;

        CREATE TABLE list_members (
            list_id TEXT NOT NULL REFERENCES lists (id) ON DELETE CASCADE,
            user_id TEXT NOT NULL,
            role    TEXT NOT NULL CHECK (role IN ('editor', 'viewer')),
            since   TEXT NOT NULL,
            PRIMARY KEY (list_id, user_id)
        );
        CREATE INDEX list_members_user ON list_members (user_id);

        CREATE TABLE list_entries (
            list_id  TEXT NOT NULL REFERENCES lists (id) ON DELETE CASCADE,
            imdb_id  TEXT NOT NULL,
            kind     TEXT NOT NULL CHECK (kind IN ('film', 'show')),
            added_at TEXT NOT NULL,
            added_by TEXT NOT NULL,
            note     TEXT,
            PRIMARY KEY (list_id, imdb_id)
        );

        CREATE TABLE follows (
            user_id         TEXT NOT NULL,
            imdb_id         TEXT NOT NULL,
            since           TEXT NOT NULL,
            auto_download   INTEGER NOT NULL,
            baseline_season INTEGER,
            baseline_number INTEGER,
            PRIMARY KEY (user_id, imdb_id)
        );
        CREATE INDEX follows_show ON follows (imdb_id);

        -- The episodes of a followed show its followers have been told of.
        CREATE TABLE announced_episodes (
            imdb_id TEXT NOT NULL,
            season  INTEGER NOT NULL,
            number  INTEGER NOT NULL,
            PRIMARY KEY (imdb_id, season, number)
        ) WITHOUT ROWID;

        CREATE TABLE notifications (
            id         TEXT PRIMARY KEY,
            user_id    TEXT NOT NULL,
            kind       TEXT NOT NULL,
            key        TEXT NOT NULL,
            text       TEXT NOT NULL,
            url        TEXT,
            action     TEXT,
            created_at TEXT NOT NULL,
            read_at    TEXT,
            pushed     INTEGER NOT NULL,
            UNIQUE (user_id, key)
        );
        CREATE INDEX notifications_user ON notifications (user_id, created_at);

        CREATE TABLE push_subscriptions (
            endpoint      TEXT PRIMARY KEY,
            user_id       TEXT NOT NULL,
            p256dh        TEXT NOT NULL,
            auth          TEXT NOT NULL,
            subscribed_at TEXT NOT NULL
        );
        CREATE INDEX push_subscriptions_user ON push_subscriptions (user_id);

        -- A film somebody wants that cannot be downloaded yet, as the catalogue described it.
        CREATE TABLE wishes (
            imdb_id    TEXT PRIMARY KEY,
            title      TEXT NOT NULL,
            year       INTEGER,
            starring   TEXT,
            poster_url TEXT,
            made_at    TEXT NOT NULL
        );

        CREATE TABLE wish_subscribers (
            imdb_id  TEXT NOT NULL REFERENCES wishes (imdb_id) ON DELETE CASCADE,
            user_id  TEXT NOT NULL,
            asked_at TEXT NOT NULL,
            PRIMARY KEY (imdb_id, user_id)
        );
        CREATE INDEX wish_subscribers_user ON wish_subscribers (user_id);

        CREATE TABLE offline_saves (
            id                TEXT PRIMARY KEY,
            user_id           TEXT NOT NULL,
            title_id          TEXT NOT NULL,
            rung              TEXT NOT NULL,
            subtitle_language TEXT,
            subtitle_sought   INTEGER NOT NULL,
            device            TEXT NOT NULL,
            created_at        TEXT NOT NULL,
            completed_at      TEXT,
            UNIQUE (user_id, title_id, device)
        );

        -- The audio and subtitle tracks a save took, in the order it took them.
        CREATE TABLE offline_save_tracks (
            save_id  TEXT NOT NULL REFERENCES offline_saves (id) ON DELETE CASCADE,
            kind     TEXT NOT NULL CHECK (kind IN ('audio', 'subtitle')),
            position INTEGER NOT NULL,
            track    TEXT NOT NULL,
            PRIMARY KEY (save_id, kind, position)
        ) WITHOUT ROWID;

        CREATE TABLE offline_wants (
            id                TEXT PRIMARY KEY,
            user_id           TEXT NOT NULL,
            imdb_id           TEXT NOT NULL,
            season            INTEGER,
            episode           INTEGER,
            device            TEXT NOT NULL,
            height            INTEGER,
            audio_language    TEXT,
            subtitle_language TEXT,
            created_at        TEXT NOT NULL,
            hash              TEXT,
            problem           TEXT,
            refusal           TEXT
        );
        CREATE INDEX offline_wants_user ON offline_wants (user_id, created_at);

        -- A room worth restoring after a restart. Its state is the shared state exactly as it is
        -- pushed to the room, revision and epoch included, so a restored room is the same run of it.
        CREATE TABLE rooms (
            id               TEXT PRIMARY KEY,
            state            TEXT NOT NULL,
            last_activity_at TEXT NOT NULL,
            open             INTEGER NOT NULL,
            guest_pass       TEXT,
            guest_created_by TEXT,
            guest_created_at TEXT,
            guest_expires_at TEXT
        ) WITHOUT ROWID;

        CREATE TABLE room_admitted (
            room_id TEXT NOT NULL REFERENCES rooms (id) ON DELETE CASCADE,
            user_id TEXT NOT NULL,
            PRIMARY KEY (room_id, user_id)
        );

        -- A phone paired with a television screen, each named by its anchor session, with the person
        -- both belong to and when each was last seen.
        CREATE TABLE screen_pairings (
            phone_sid      TEXT NOT NULL,
            screen_sid     TEXT NOT NULL,
            user_id        TEXT NOT NULL,
            phone_name     TEXT NOT NULL,
            screen_name    TEXT,
            paired_at      TEXT NOT NULL,
            phone_seen_at  TEXT NOT NULL,
            screen_seen_at TEXT NOT NULL,
            PRIMARY KEY (phone_sid, screen_sid)
        );
        CREATE INDEX screen_pairings_screen ON screen_pairings (screen_sid);
        CREATE INDEX screen_pairings_user ON screen_pairings (user_id);

        -- A subtitle fetched from outside. Its WebVTT is a file under the subtitle root.
        CREATE TABLE fetched_subtitles (
            title_id              TEXT NOT NULL,
            id                    TEXT NOT NULL,
            language              TEXT NOT NULL,
            label                 TEXT NOT NULL,
            hearing_impaired      INTEGER NOT NULL,
            release               TEXT,
            added_by              TEXT,
            added_at              TEXT NOT NULL,
            applied_shift_seconds REAL,
            aligned_fraction      REAL,
            PRIMARY KEY (title_id, id)
        );

        -- A subtitle track somebody watched a title with and said was right.
        CREATE TABLE subtitle_pins (
            title_id         TEXT NOT NULL,
            track_id         TEXT NOT NULL,
            pinned_by        TEXT NOT NULL,
            pinned_at        TEXT NOT NULL,
            watched_fraction REAL,
            PRIMARY KEY (title_id, track_id)
        ) WITHOUT ROWID;

        -- What the film database said, by what it was asked. A null value is the database having
        -- nothing under the id, which is an answer worth keeping.
        CREATE TABLE film_descriptions (
            imdb_id    TEXT PRIMARY KEY,
            fetched_at TEXT NOT NULL,
            details    TEXT
        ) WITHOUT ROWID;

        CREATE TABLE show_descriptions (
            imdb_id    TEXT PRIMARY KEY,
            fetched_at TEXT NOT NULL,
            details    TEXT,
            seasons    TEXT NOT NULL
        ) WITHOUT ROWID;

        CREATE TABLE glances (
            imdb_id    TEXT PRIMARY KEY,
            fetched_at TEXT NOT NULL,
            glance     TEXT
        ) WITHOUT ROWID;

        -- The catalogue's answers, by the name the question is kept under. asked_at is when it was
        -- last asked for, which is what a question somebody typed is trimmed by.
        CREATE TABLE answers (
            name       TEXT PRIMARY KEY,
            fetched_at TEXT NOT NULL,
            asked_at   TEXT NOT NULL,
            value      TEXT
        ) WITHOUT ROWID;
        CREATE INDEX answers_asked ON answers (asked_at);

        -- This week's trending films and shows, in the database's order.
        CREATE TABLE trending (
            kind  TEXT NOT NULL CHECK (kind IN ('film', 'show')),
            rank  INTEGER NOT NULL,
            title TEXT NOT NULL,
            PRIMARY KEY (kind, rank)
        ) WITHOUT ROWID;

        -- The image path in the database's store each kept image file was fetched from, by the
        -- cache that keeps it and the file's name within it.
        CREATE TABLE images (
            cache  TEXT NOT NULL,
            file   TEXT NOT NULL,
            source TEXT NOT NULL,
            PRIMARY KEY (cache, file)
        ) WITHOUT ROWID;

        -- The poster path each title was first shown with, which every cache fetches its poster by.
        CREATE TABLE poster_pins (
            imdb_id TEXT PRIMARY KEY,
            path    TEXT NOT NULL
        ) WITHOUT ROWID;
        """;
}
