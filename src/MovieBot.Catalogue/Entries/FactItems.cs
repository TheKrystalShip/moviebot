using Microsoft.Data.Sqlite;
using TheKrystalShip.MovieBot.Catalogue.Data;

namespace TheKrystalShip.MovieBot.Catalogue.Entries;

/// <summary>
/// The Wikidata items listed titles name in a fact a filter takes, each once with its label and how many
/// titles name it, which words are resolved against. Built whole with the universes, in one transaction.
/// </summary>
public static class FactItems
{
    /// <summary>The properties a word can name an item of: where a title is set and filmed, its source's author, its awards, subjects, genres and series.</summary>
    public static readonly string[] Properties = ["P840", "P915", "P144/P50", "P166", "P921", "P136", "P179"];

    public static int Build(SqliteConnection c)
    {
        c.Execute("DELETE FROM fact_items");
        return c.Execute($"""
            INSERT INTO fact_items (property, item, label, titles)
            SELECT f.property, f.item, max(f.label), count(*) FROM title_facts f JOIN titles t ON t.id = f.title_id AND t.listed = 1
            WHERE f.property IN ({string.Join(", ", Properties.Select(p => $"'{p}'"))}) AND f.label IS NOT NULL
            GROUP BY f.property, f.item
            """);
    }
}
