using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TheKrystalShip.MovieBot.Acquire.Tmdb;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>
/// The answers the film database gave to the questions the catalogue asks, kept on disk one file
/// each under one directory: asked when nothing is kept or what is kept is older than the answer's
/// own keep time, two requests for one answer sharing one question, and a database that cannot be
/// reached costing freshness and nothing else.
///
/// An answer that is a question of the visitor's own words (a search, a discovery) is named by a hash
/// of the question and lives under <c>queries/</c>, which is trimmed of what has not been asked for in
/// <see cref="QueryKept"/>.
/// </summary>
public sealed class AnswerKeeper(
    string root,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<AnswerKeeper> logger)
{
    /// <summary>How long a question somebody typed is remembered after it was last answered.</summary>
    internal static readonly TimeSpan QueryKept = TimeSpan.FromDays(30);

    private static readonly TimeSpan TrimEvery = TimeSpan.FromHours(6);

    private readonly ConcurrentDictionary<string, object> _held = new();
    private readonly ConcurrentDictionary<string, Task> _asking = new();
    private DateTimeOffset _trimmed;

    public string Root => root;

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
        string name, JsonTypeInfo<KeptAnswer<T>> type, TimeSpan keep,
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
        string name, JsonTypeInfo<KeptAnswer<T>> type, KeptAnswer<T>? held,
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

    private KeptAnswer<T>? Held<T>(string name, JsonTypeInfo<KeptAnswer<T>> type) where T : class
    {
        if (_held.TryGetValue(name, out var held)) return (KeptAnswer<T>)held;
        var path = Path.Combine(root, name + ".json");
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            if (JsonSerializer.Deserialize(stream, type) is not { } read) return null;
            _held[name] = read;
            return read;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // An answer that cannot be read is one not kept: the next ask writes a good one.
            logger.LogWarning("The kept answer for {Name} could not be read: {Reason}", name, ex.Message);
            return null;
        }
    }

    private void Write<T>(string name, JsonTypeInfo<KeptAnswer<T>> type, KeptAnswer<T> kept) where T : class
    {
        var path = Path.Combine(root, name + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(kept, type));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    /// <summary>Marks a question as asked again, which is what keeps it from being trimmed.</summary>
    private void Touch(string name)
    {
        try
        {
            File.SetLastWriteTimeUtc(Path.Combine(root, name + ".json"), clock.GetUtcNow().UtcDateTime);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A question that cannot be marked is trimmed a little early, and asked again.
        }
    }

    /// <summary>Removes the questions nobody has asked for in <see cref="QueryKept"/>, at most once every few hours.</summary>
    private void Trim()
    {
        var now = clock.GetUtcNow();
        if (now - _trimmed < TrimEvery) return;
        _trimmed = now;

        var directory = Path.Combine(root, "queries");
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
            {
                if (now - File.GetLastWriteTimeUtc(file) < QueryKept) continue;
                File.Delete(file);
                _held.TryRemove("queries/" + Path.GetFileNameWithoutExtension(file), out _);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("The kept questions could not be trimmed: {Reason}", ex.Message);
        }
    }
}
