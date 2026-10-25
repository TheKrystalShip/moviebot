using Microsoft.Data.Sqlite;
using TheKrystalShip.MovieBot.Catalogue.Data;

namespace TheKrystalShip.MovieBot.Catalogue.Entries;

/// <summary>
/// A source's rating of a title, kept beside the other source's. A rating or vote count that moved marks
/// the title changed; one that did not only records when it was seen. Every rating written brings the
/// rating a card shows along (<see cref="Show"/>).
/// </summary>
public static class Ratings
{
    /// <summary>The fewest votes a rating is shown with; under it a card shows the votes and no rating.</summary>
    public const int Shown = 25;

    /// <summary>
    /// Sets the rating a card shows on the titles <paramref name="where"/> selects (a condition on
    /// <c>titles</c>): IMDb's where IMDb rates the title, the film database's otherwise, with no value
    /// under <see cref="Shown"/> votes.
    /// </summary>
    public static int Show(SqliteConnection c, string where, params ReadOnlySpan<(string Name, object? Value)> args) => c.Execute($"""
        UPDATE titles SET
            rated_by = (SELECT source FROM ratings r WHERE r.title_id = titles.id ORDER BY r.source = 'imdb' DESC LIMIT 1),
            votes = (SELECT votes FROM ratings r WHERE r.title_id = titles.id ORDER BY r.source = 'imdb' DESC LIMIT 1),
            rating = (SELECT CASE WHEN votes >= {Shown} THEN rating END FROM ratings r WHERE r.title_id = titles.id ORDER BY r.source = 'imdb' DESC LIMIT 1)
        WHERE {where}
        """, args);

    /// <summary>Records one rating; returns whether it moved.</summary>
    public static bool Observe(SqliteConnection c, long titleId, string source, double rating, int votes, DateTimeOffset at)
    {
        var held = c.First("SELECT rating, votes FROM ratings WHERE title_id = $id AND source = $source",
            r => ((double Rating, int Votes)?)(r.GetDouble(0), r.GetInt32(1)), ("$id", titleId), ("$source", source));
        c.Execute("""
            INSERT INTO ratings (title_id, source, rating, votes, seen_at) VALUES ($id, $source, $rating, $votes, $at)
            ON CONFLICT (title_id, source) DO UPDATE SET rating = excluded.rating, votes = excluded.votes, seen_at = excluded.seen_at
            """, ("$id", titleId), ("$source", source), ("$rating", rating), ("$votes", votes), ("$at", at));
        if (held is { } h && h.Rating.Equals(rating) && h.Votes == votes) return false;
        c.Execute("UPDATE titles SET changed_at = $at WHERE id = $id", ("$at", at), ("$id", titleId));
        Show(c, "id = $id", ("$id", titleId));
        return true;
    }
}
