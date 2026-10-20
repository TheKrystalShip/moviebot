using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TheKrystalShip.MovieBot.Acquire.Tmdb;
using TheKrystalShip.MovieBot.Api.Data;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>
/// The answers the film database gave to the questions the catalogue asks, one row each by the name
/// the question is kept under: asked when nothing is kept or what is kept is older than the answer's
/// own keep time, two requests for one answer sharing one question, and a database that cannot be
/// reached costing freshness and nothing else.
///
/// An answer that is a question of the visitor's own words (a search, a discovery) is named by a hash
/// of the question under <c>queries/</c>, and is trimmed once it has not been asked for in
/// <see cref="QueryKept"/>.
///
/// Every title with a rating in an answer is recorded in <see cref="TitleScores"/> as it is served,
/// stamped with when the answer was asked, so an answer kept before a newer one was asked about the
/// same title never decides what a view shows.
/// </summary>
public sealed class AnswerKeeper(
    CinemaDatabase database,
    TitleScores scores,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<AnswerKeeper> logger)
{
    /// <summary>How long a question somebody typed is remembered after it was last answered.</summary>
    internal static readonly TimeSpan QueryKept = TimeSpan.FromDays(30);

    private static readonly TimeSpan TrimEvery = TimeSpan.FromHours(6);

    private readonly ConcurrentDictionary<string, object> _held = new();
    private readonly ConcurrentDictionary<string, Task> _asking = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _observed = new();
    private DateTimeOffset _trimmed;

    /// <summary>The name an answer to a question put in words is kept under: stable for the same words, whatever they contain.</summary>
    public static string QueryName(string family, string question) =>
        $"queries/{family}-{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(question)))[..20]}";

    /// <summary>
    /// The answer kept under <paramref name="name"/>, asking the database when nothing is kept or what
    /// is kept is older than <paramref name="keep"/>. <paramref name="received"/> sees each new value
    /// before it is kept. Null when the database cannot be asked and nothing is kept; an answer with
    /// no value when the database has nothing to say.
    /// </summary>
    public async Task<KeptAnswer<T>?> KeptAsync<T>(
        string name, JsonTypeInfo<T> type, TimeSpan keep,
        Func<TmdbClient, CancellationToken, Task<T?>> ask, Action<T>? received, CancellationToken ct) where T : class
    {
        var kept = await AskedAsync(name, type, keep, ask, received, ct);
        Observe(name, kept);
        return kept;
    }

    /// <summary>Records the ratings of an answer as of when it was asked, once per answer asked.</summary>
    private void Observe<T>(string name, KeptAnswer<T>? kept) where T : class
    {
        if (kept?.Value is not { } value) return;
        if (_observed.TryGetValue(name, out var seen) && seen >= kept.FetchedAt) return;
        scores.Observe(TitleScores.ReadingsOf(value), kept.FetchedAt);
        _observed[name] = kept.FetchedAt;
    }

    private async Task<KeptAnswer<T>?> AskedAsync<T>(
        string name, JsonTypeInfo<T> type, TimeSpan keep,
        Func<TmdbClient, CancellationToken, Task<T?>> ask, Action<T>? received, CancellationToken ct) where T : class
    {
        var held = Held(name, type);
        if (held is not null && clock.GetUtcNow() - held.FetchedAt <= keep)
        {
            if (name.StartsWith("queries/", StringComparison.Ordinal)) Touch(name);
            return held;
        }

        var asking = (Task<KeptAnswer<T>?>)_asking.GetOrAdd(name, _ => FetchAsync(name, type, held, ask, received));
        try
        {
            return await asking.WaitAsync(ct);
        }
        finally
        {
            if (asking.IsCompleted) _asking.TryRemove(new KeyValuePair<string, Task>(name, asking));
        }
    }

    private async Task<KeptAnswer<T>?> FetchAsync<T>(
        string name, JsonTypeInfo<T> type, KeptAnswer<T>? held,
        Func<TmdbClient, CancellationToken, Task<T?>> ask, Action<T>? received) where T : class
    {
        await Task.Yield();
        using var scope = scopes.CreateScope();
        var tmdb = scope.ServiceProvider.GetRequiredService<TmdbClient>();
        if (!tmdb.IsConfigured) return held;

        try
        {
            // Not the caller's token: a page that stops waiting does not cancel the question for the
            // next one, and what comes back is worth keeping either way.
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var value = await ask(tmdb, timeout.Token);
            if (value is not null) received?.Invoke(value);

            var kept = new KeptAnswer<T> { Value = value, FetchedAt = clock.GetUtcNow() };
            Write(name, type, kept);
            _held[name] = kept;
            if (name.StartsWith("queries/", StringComparison.Ordinal)) Trim();
            return kept;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            logger.LogWarning("The film database could not answer for {Name}: {Reason}{Kept}", name, ex.Message,
                held is null ? "" : "; the kept answer is served");
            return held;
        }
    }

    private KeptAnswer<T>? Held<T>(string name, JsonTypeInfo<T> type) where T : class
    {
        if (_held.TryGetValue(name, out var held)) return (KeptAnswer<T>)held;
        try
        {
            if (database.Read(c => c.First("SELECT fetched_at, value FROM answers WHERE name = $name",
                    r => new KeptAnswer<T>
                    {
                        FetchedAt = r.GetInstant(0),
                        Value = r.GetStringOrNull(1) is { } value ? JsonSerializer.Deserialize(value, type) : null,
                    },
                    ("$name", name))) is not { } read)
                return null;
            _held[name] = read;
            return read;
        }
        catch (JsonException ex)
        {
            // An answer that cannot be read is one not kept: the next ask keeps a good one.
            logger.LogWarning("The kept answer for {Name} could not be read: {Reason}", name, ex.Message);
            return null;
        }
    }

    private void Write<T>(string name, JsonTypeInfo<T> type, KeptAnswer<T> kept) where T : class =>
        database.Write(c => c.Execute(
            "INSERT OR REPLACE INTO answers (name, fetched_at, asked_at, value) VALUES ($name, $fetched, $asked, $value)",
            ("$name", name), ("$fetched", kept.FetchedAt), ("$asked", clock.GetUtcNow()),
            ("$value", kept.Value is null ? null : JsonSerializer.Serialize(kept.Value, type))));

    /// <summary>Marks a question as asked again, which is what keeps it from being trimmed.</summary>
    private void Touch(string name) =>
        database.Write(c => c.Execute("UPDATE answers SET asked_at = $now WHERE name = $name", ("$name", name), ("$now", clock.GetUtcNow())));

    /// <summary>Removes the questions nobody has asked for in <see cref="QueryKept"/>, at most once every few hours.</summary>
    private void Trim()
    {
        var now = clock.GetUtcNow();
        if (now - _trimmed < TrimEvery) return;
        _trimmed = now;

        foreach (var name in database.Write(c => c.Query(
                     "DELETE FROM answers WHERE name LIKE 'queries/%' AND asked_at < $cutoff RETURNING name",
                     r => r.GetString(0), ("$cutoff", now - QueryKept))))
        {
            _held.TryRemove(name, out _);
            _observed.TryRemove(name, out _);
        }
    }
}
