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
public static class Universes
{
    public static int Build(SqliteConnection c)
    {
        c.Execute("DELETE FROM franchises WHERE source = 'wikidata'");
        c.Execute("""
            CREATE TEMP TABLE IF NOT EXISTS universe_members (item TEXT NOT NULL, label TEXT, title_id INTEGER NOT NULL, ordinal TEXT,
                PRIMARY KEY (item, title_id));
            DELETE FROM universe_members;
            INSERT OR IGNORE INTO universe_members (item, label, title_id, ordinal)
            SELECT w.item, w.label, w.title_id,
                   (SELECT min(s.ordinal) FROM wikidata_claims s WHERE s.title_id = w.title_id AND s.property = 'P179' AND s.ordinal IS NOT NULL)
            FROM wikidata_claims w JOIN titles t ON t.id = w.title_id
            WHERE t.listed = 1 AND w.property IN ('P1434', 'P8345');
            """);
        var kept = c.Query("""
            SELECT m.item, max(m.label) FROM universe_members m JOIN titles t ON t.id = m.title_id
            LEFT JOIN franchise_titles ft ON ft.title_id = m.title_id
                AND ft.franchise_id IN (SELECT id FROM franchises WHERE source = 'tmdb')
            GROUP BY m.item
            HAVING count(DISTINCT ft.franchise_id) >= 2 OR (sum(t.kind = 'show') > 0 AND sum(t.kind = 'film') > 0)
            """, r => (Item: r.GetString(0), Label: r.IsDBNull(1) ? r.GetString(0) : r.GetString(1)));
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
