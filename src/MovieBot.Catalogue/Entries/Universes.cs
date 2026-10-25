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
/// files part of a franchise as an item of its own, tagging the titles of each unevenly. An item another
/// holds <see cref="CatalogueQueries.PartOf"/> of the titles of is one universe with it, holding both
/// items' titles. The items are taken the most titles first, each joining the first universe that holds
/// that much of it, so two universes are never joined through a small item both share. A universe is named
/// by the item it began with, or by one of nearly the same titles (holding <see cref="CatalogueQueries.PartOf"/>
/// of that item's too) when it is the better known: a media franchise before a fictional universe, then
/// the lowest-numbered item.
/// </remarks>
public static class Universes
{
    private sealed record Item(string Id, string Label, bool Franchise, long Number, HashSet<long> Titles);

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
            """);
        var candidates = c.Query("""
            SELECT m.item, coalesce(max(m.label), m.item), max(m.franchise), CAST(substr(m.item, 2) AS INTEGER),
                   group_concat(DISTINCT m.title_id)
            FROM universe_members m JOIN titles t ON t.id = m.title_id
            LEFT JOIN franchise_titles ft ON ft.title_id = m.title_id
                AND ft.franchise_id IN (SELECT id FROM franchises WHERE source = 'tmdb')
            GROUP BY m.item
            HAVING count(DISTINCT ft.franchise_id) >= 2 OR (sum(t.kind = 'show') > 0 AND sum(t.kind = 'film') > 0)
            """, r => new Item(r.GetString(0), r.GetString(1), r.GetBool(2), r.GetInt64(3),
                r.GetString(4).Split(',').Select(long.Parse).ToHashSet()));

        static bool Holds(HashSet<long> titles, Item item) => titles.Count(item.Titles.Contains) >= CatalogueQueries.PartOf * item.Titles.Count;
        var universes = new List<(Item First, HashSet<long> Titles, List<Item> Items)>();
        foreach (var item in candidates.OrderByDescending(i => i.Titles.Count).ThenByDescending(i => i.Franchise).ThenBy(i => i.Number))
        {
            var at = universes.FindIndex(u => Holds(u.Titles, item));
            if (at < 0) universes.Add((item, [.. item.Titles], [item]));
            else { universes[at].Titles.UnionWith(item.Titles); universes[at].Items.Add(item); }
        }

        foreach (var (first, _, items) in universes)
        {
            var name = items.Where(i => i == first || Holds(i.Titles, first)).OrderByDescending(i => i.Franchise).ThenBy(i => i.Number).First();
            var id = c.First("INSERT INTO franchises (source, source_id, name) VALUES ('wikidata', $item, $name) RETURNING id",
                r => r.GetInt64(0), ("$item", name.Id), ("$name", name.Label));
            foreach (var item in items)
            {
                c.Execute("""
                    INSERT OR IGNORE INTO franchise_titles (franchise_id, title_id, ordinal)
                    SELECT $f, title_id, ordinal FROM universe_members WHERE item = $item
                    """, ("$f", id), ("$item", item.Id));
                c.Execute("""
                    INSERT OR IGNORE INTO franchise_parts (universe_id, collection_id)
                    SELECT DISTINCT $f, ft.franchise_id FROM universe_members m
                    JOIN franchise_titles ft ON ft.title_id = m.title_id
                    JOIN franchises f ON f.id = ft.franchise_id AND f.source = 'tmdb'
                    WHERE m.item = $item
                    """, ("$f", id), ("$item", item.Id));
            }
        }
        return universes.Count;
    }
}
