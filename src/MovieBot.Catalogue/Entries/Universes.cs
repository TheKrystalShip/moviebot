using Microsoft.Data.Sqlite;
using TheKrystalShip.MovieBot.Catalogue.Data;

namespace TheKrystalShip.MovieBot.Catalogue.Entries;

/// <summary>
/// The franchises Wikidata adds above the film database's collections. An item a listed title names as
/// its fictional universe (P1434) or its media franchise (P8345) is a universe when the titles naming it
/// span two collections or more, or hold both films and shows, which no collection can. A series (P179)
/// only numbers a title in one: editors file lists under it too ("list of Pixar films"). A universe holds
/// its titles and the collections they belong to. Built whole after every Wikidata pass, in one
/// transaction.
/// </summary>
/// <remarks>
/// Wikidata often holds one franchise twice, as a media franchise and as its fictional universe, and
/// files part of a franchise as a universe of its own. An item whose titles another item holds all of is
/// not a universe: the one holding more is, and of two holding the same titles, the media franchise
/// (whose name is the one a person knows it by), then the lowest-numbered item.
/// </remarks>
public static class Universes
{
    public static int Build(SqliteConnection c)
    {
        c.Execute("DELETE FROM franchises WHERE source = 'wikidata'");
        c.Execute("""
            CREATE TEMP TABLE IF NOT EXISTS universe_members (item TEXT NOT NULL, label TEXT, franchise INTEGER NOT NULL,
                title_id INTEGER NOT NULL, ordinal TEXT, PRIMARY KEY (item, title_id));
            DELETE FROM universe_members;
            INSERT INTO universe_members (item, label, franchise, title_id, ordinal)
            SELECT w.item, max(w.label), max(w.property = 'P8345'), w.title_id,
                   (SELECT min(s.ordinal) FROM wikidata_claims s WHERE s.title_id = w.title_id AND s.property = 'P179' AND s.ordinal IS NOT NULL)
            FROM wikidata_claims w JOIN titles t ON t.id = w.title_id
            WHERE t.listed = 1 AND w.property IN ('P1434', 'P8345')
            GROUP BY w.item, w.title_id;

            CREATE TEMP TABLE IF NOT EXISTS universe_kept (item TEXT PRIMARY KEY, label TEXT NOT NULL, franchise INTEGER NOT NULL,
                number INTEGER NOT NULL, titles INTEGER NOT NULL);
            DELETE FROM universe_kept;
            INSERT INTO universe_kept (item, label, franchise, number, titles)
            SELECT m.item, coalesce(max(m.label), m.item), max(m.franchise), CAST(substr(m.item, 2) AS INTEGER), count(DISTINCT m.title_id)
            FROM universe_members m JOIN titles t ON t.id = m.title_id
            LEFT JOIN franchise_titles ft ON ft.title_id = m.title_id
                AND ft.franchise_id IN (SELECT id FROM franchises WHERE source = 'tmdb')
            GROUP BY m.item
            HAVING count(DISTINCT ft.franchise_id) >= 2 OR (sum(t.kind = 'show') > 0 AND sum(t.kind = 'film') > 0);

            DELETE FROM universe_kept WHERE item IN (
                SELECT a.item FROM universe_kept a
                JOIN universe_members ma ON ma.item = a.item
                JOIN universe_members mb ON mb.title_id = ma.title_id AND mb.item <> a.item
                JOIN universe_kept b ON b.item = mb.item
                GROUP BY a.item, b.item
                HAVING count(*) = max(a.titles) AND (max(b.titles) > max(a.titles) OR (max(b.titles) = max(a.titles)
                    AND (max(b.franchise) > max(a.franchise) OR (max(b.franchise) = max(a.franchise) AND max(b.number) < max(a.number))))));
            """);
        var kept = c.Query("SELECT item, label FROM universe_kept", r => (Item: r.GetString(0), Label: r.GetString(1)));
        foreach (var (item, label) in kept)
        {
            var id = c.First("INSERT INTO franchises (source, source_id, name) VALUES ('wikidata', $item, $name) RETURNING id",
                r => r.GetInt64(0), ("$item", item), ("$name", label));
            c.Execute("""
                INSERT OR IGNORE INTO franchise_titles (franchise_id, title_id, ordinal)
                SELECT $f, title_id, ordinal FROM universe_members WHERE item = $item
                """, ("$f", id), ("$item", item));
            c.Execute("""
                INSERT OR IGNORE INTO franchise_parts (universe_id, collection_id)
                SELECT DISTINCT $f, ft.franchise_id FROM universe_members m
                JOIN franchise_titles ft ON ft.title_id = m.title_id
                JOIN franchises f ON f.id = ft.franchise_id AND f.source = 'tmdb'
                WHERE m.item = $item
                """, ("$f", id), ("$item", item));
        }
        return kept.Count;
    }
}
