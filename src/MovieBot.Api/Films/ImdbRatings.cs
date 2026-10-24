using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using TheKrystalShip.MovieBot.Acquire;
using TheKrystalShip.MovieBot.Api.Data;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>What the IMDb import keeps for one title, as a ranking reads it.</summary>
public sealed record ImdbCandidate(string ImdbId, string Title, int? Year, double Rating, int Votes);

/// <summary>
/// The titles a question can name in IMDb's own terms: a kind, the genres a title is filed under
/// (each entry is the IMDb names one of the question's genres stands for, a title holding any of them
/// satisfying it), the genres it is filed under none of, and the years it started in.
/// </summary>
public sealed record ImdbSpec(
    string Kind,
    IReadOnlyList<IReadOnlyList<string>> AllOf,
    IReadOnlyList<string> NoneOf,
    int? FromYear,
    int? ToYear);

/// <summary>
/// IMDb's rating and votes for a title, and the titles IMDb itself files under a genre and years, read
/// from the table the import keeps (<see cref="ImdbImport"/>). A host where the import has not run holds
/// nothing: <see cref="Edition"/> is null, every lookup answers none and every candidate list is empty,
/// so what depends on it does what it does without IMDb.
///
/// A listing asks for a rating on every row, so a title looked up once is remembered, absent ones
/// included; nothing is loaded ahead, so only the titles people have seen are held. A new import
/// forgets them all (<see cref="Refresh"/>).
/// </summary>
public sealed class ImdbRatings(CinemaDatabase database)
{
    /// <summary>The most titles remembered before they are forgotten and read again.</summary>
    private const int MostRemembered = 50_000;

    /// <summary>One title's IMDb rating out of ten and the votes behind it.</summary>
    public readonly record struct Rating(double Value, int Votes);

    private readonly ConcurrentDictionary<long, Rating?> _seen = new();
    private readonly Lock _gate = new();
    private string? _edition;
    private bool _loaded;

    /// <summary>
    /// Which day's datasets the table holds, such as <c>20261024</c>; null while nothing has been
    /// imported. It names the answers that were ranked with it.
    /// </summary>
    public string? Edition
    {
        get
        {
            if (_loaded) return _edition;
            lock (_gate)
            {
                if (!_loaded)
                {
                    _edition = database.Meta(ImdbImport.EditionKey);
                    _loaded = true;
                }
                return _edition;
            }
        }
    }

    /// <summary>Forgets what was remembered, once a new import has been swapped in.</summary>
    public void Refresh()
    {
        lock (_gate)
        {
            _seen.Clear();
            _loaded = false;
        }
    }

    /// <summary>The rating IMDb gives a title; null for one the import does not hold.</summary>
    public Rating? Of(string? imdbId)
    {
        if (ImdbId.ToNumber(imdbId) is not { } number || Edition is null) return null;
        if (_seen.TryGetValue(number, out var seen)) return seen;

        var read = database.Read(c => c.First(
            "SELECT rating, votes FROM imdb_titles WHERE imdb_id = $id",
            r => new Rating(r.GetDouble(0), r.GetInt32(1)), ("$id", number)));
        if (_seen.Count >= MostRemembered) _seen.Clear();
        _seen[number] = read;
        return read;
    }

    /// <summary>The votes of the <paramref name="count"/> most voted titles the spec names, the most voted first.</summary>
    public List<int> MostVoted(ImdbSpec spec, int count)
    {
        var (where, args) = Where(spec);
        return database.Read(c => c.Query(
            $"SELECT votes FROM imdb_titles WHERE {where} ORDER BY votes DESC LIMIT $count",
            r => r.GetInt32(0), [.. args, ("$count", count)]));
    }

    /// <summary>
    /// The best rated titles the spec names with at least <paramref name="minVotes"/> votes (and at
    /// least <paramref name="minRating"/> when it is given), the best rated first.
    /// </summary>
    public List<ImdbCandidate> Candidates(ImdbSpec spec, int minVotes, double? minRating, int count)
    {
        var (where, args) = Where(spec);
        return database.Read(c => c.Query(
            $"""
             SELECT imdb_id, title, start_year, rating, votes FROM imdb_titles
             WHERE {where} AND votes >= $minVotes AND rating >= $minRating
             ORDER BY rating DESC, votes DESC LIMIT $count
             """,
            r => new ImdbCandidate(ImdbId.ToTag(r.GetInt64(0))!, r.GetString(1), r.GetIntOrNull(2), r.GetDouble(3), r.GetInt32(4)),
            [.. args, ("$minVotes", minVotes), ("$minRating", minRating ?? 0d), ("$count", count)]));
    }

    private static (string Where, List<(string, object?)> Args) Where(ImdbSpec spec)
    {
        var where = new StringBuilder("kind = $kind");
        var args = new List<(string, object?)> { ("$kind", spec.Kind) };
        var n = 0;
        foreach (var any in spec.AllOf)
        {
            where.Append(" AND (").Append(string.Join(" OR ", any.Select(name => Has(name, args, ref n)))).Append(')');
        }
        foreach (var name in spec.NoneOf) where.Append(" AND NOT ").Append(Has(name, args, ref n));
        if (spec.FromYear is { } from)
        {
            where.Append(" AND start_year >= $from");
            args.Add(("$from", from));
        }
        if (spec.ToYear is { } to)
        {
            where.Append(" AND start_year <= $to");
            args.Add(("$to", to));
        }
        return (where.ToString(), args);
    }

    private static string Has(string genre, List<(string, object?)> args, ref int n)
    {
        var name = "$g" + (++n).ToString(CultureInfo.InvariantCulture);
        args.Add((name, "%," + genre + ",%"));
        return $"genres LIKE {name}";
    }
}
