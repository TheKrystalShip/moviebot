using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Acquire.Configuration;
using TheKrystalShip.MovieBot.Acquire.Tmdb;
using TheKrystalShip.MovieBot.Api.Data;

namespace TheKrystalShip.MovieBot.Api.Films;

public sealed class FranchiseOptions
{
    public const string Section = "Franchises";

    /// <summary>Whether the film database's series are scanned and shelved; without it the franchises are the curated ones.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Where the film database publishes its daily lists of ids.</summary>
    public string ExportBaseUrl { get; set; } = "https://files.tmdb.org/p/exports/";

    /// <summary>How many days back a scan looks for a list when the day's is not yet published.</summary>
    public int ExportLookbackDays { get; set; } = 3;

    /// <summary>How often a scan looks for series that have come due; zero runs no scan.</summary>
    public int WarmMinutes { get; set; } = 60;

    /// <summary>How long a shelved series' measure is kept before it is asked again.</summary>
    public int RefreshDays { get; set; } = 7;

    /// <summary>How long the measure of a series below the bar is kept before it is asked again.</summary>
    public int OutsiderRefreshDays { get; set; } = 30;

    /// <summary>The most series a scan asks the film database about each second.</summary>
    public double RequestsPerSecond { get; set; } = 4;

    /// <summary>The released films a series needs to be shelved.</summary>
    public int MinFilms { get; set; } = 2;

    /// <summary>The votes a series' released films need between them to be shelved.</summary>
    public int MinVotes { get; set; } = 10000;

    /// <summary>The most series shelved besides the curated ones.</summary>
    public int MostShown { get; set; } = 400;
}

/// <summary>The film database's daily list of every series id, read as it arrives and never held whole.</summary>
public sealed class FranchiseExport(HttpClient http, IOptions<FranchiseOptions> options)
{
    /// <summary>
    /// The series ids in one day's list; null when the day's list is not published, which the store it
    /// is served from answers with 403 or 404. Throws <see cref="HttpRequestException"/> when it cannot
    /// be reached.
    /// </summary>
    public async Task<List<int>?> IdsAsync(DateOnly day, CancellationToken ct)
    {
        var baseUrl = options.Value.ExportBaseUrl;
        var file = $"collection_ids_{day:MM_dd_yyyy}.json.gz";
        using var response = await http.GetAsync(baseUrl.EndsWith('/') ? baseUrl + file : baseUrl + "/" + file, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(ct);
        await using var gzip = new GZipStream(body, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        var ids = new List<int>();
        while (await reader.ReadLineAsync(ct) is { } line)
            if (IdOf(line) is { } id) ids.Add(id);
        return ids;
    }

    /// <summary>The id of one line of the list, <c>{"id":10,"name":"Star Wars Collection"}</c>; null for a line that is not one.</summary>
    internal static int? IdOf(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            using var entry = JsonDocument.Parse(line);
            return entry.RootElement.ValueKind == JsonValueKind.Object
                   && entry.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out var value) && value > 0
                ? value
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// The Discover page's franchises: the curated series, always first, then every series of the film
/// database that has at least <see cref="FranchiseOptions.MinFilms"/> released films and
/// <see cref="FranchiseOptions.MinVotes"/> votes between them, the most voted first.
///
/// <see cref="ScanAsync"/> reads the database's daily list of series ids and asks about each series
/// that is due, slowly, keeping how many released films it has and their votes in <c>franchises</c>.
/// A shelved series is asked again after <see cref="FranchiseOptions.RefreshDays"/> and any other after
/// <see cref="FranchiseOptions.OutsiderRefreshDays"/>, since a forgotten series rarely becomes a known
/// one within a month. The votes only rank; no view shows them. A series' films are asked for as a
/// shelf, never here, so nothing a page reads waits on a scan. What is shelved is held in memory and
/// renewed as a scan goes.
/// </summary>
public sealed class Franchises(
    CinemaDatabase database,
    IServiceScopeFactory scopes,
    IOptions<FranchiseOptions> options,
    IOptions<TmdbOptions> tmdbOptions,
    TimeProvider clock,
    ILogger<Franchises> logger)
{
    public const string Section = "franchises";

    /// <summary>The series always shelved, first and in this order, whatever a scan finds.</summary>
    public static readonly IReadOnlyList<Shelf> Curated =
    [
        .. new[]
        {
            ("star-wars", "Star Wars", 10), ("lord-of-the-rings", "The Lord of the Rings", 119), ("harry-potter", "Harry Potter", 1241),
            ("james-bond", "James Bond", 645), ("mission-impossible", "Mission: Impossible", 87359), ("indiana-jones", "Indiana Jones", 84),
            ("back-to-the-future", "Back to the Future", 264), ("the-godfather", "The Godfather", 230), ("alien", "Alien", 8091),
            ("the-matrix", "The Matrix", 2344), ("dune", "Dune", 726871), ("jurassic-park", "Jurassic Park", 328),
            ("john-wick", "John Wick", 404609), ("toy-story", "Toy Story", 10194), ("pirates-of-the-caribbean", "Pirates of the Caribbean", 295),
            ("mad-max", "Mad Max", 8945), ("the-dark-knight", "The Dark Knight", 263), ("rocky", "Rocky", 1575),
            ("fast-and-furious", "Fast and Furious", 9485), ("oceans", "Ocean's", 304), ("before-trilogy", "The Before trilogy", 123800),
            ("avengers", "The Avengers", 86311),
        }.Select(c => Series(c.Item1, c.Item2, c.Item3)),
    ];

    private static Shelf Series(string id, string title, int collection) =>
        new() { Section = Section, Id = id, Title = title, Icon = "library", Source = ShelfSource.Collection, DatabaseId = collection };

    private sealed record Shelved(IReadOnlyList<Shelf> All, Dictionary<string, Shelf> ById);

    private volatile Shelved? _shelved;

    /// <summary>Whether a scan runs: the setting is on and the film database can be asked.</summary>
    public bool Scanning => options.Value.Enabled && tmdbOptions.Value.IsConfigured;

    /// <summary>Every franchise shelved: the curated ones, then the scanned ones, the most voted first.</summary>
    public IReadOnlyList<Shelf> All => Held().All;

    /// <summary>The franchise shelved under an id; null for one that names none.</summary>
    public Shelf? Find(string id) => Held().ById.GetValueOrDefault(id);

    private Shelved Held() => _shelved ?? Build();

    /// <summary>Reads what is shelved from what the scans measured, and holds it.</summary>
    internal void Rebuild() => Build();

    private Shelved Build()
    {
        var settings = options.Value;
        var all = new List<Shelf>(Curated);
        if (settings.Enabled)
        {
            var curated = Curated.Select(s => s.DatabaseId!.Value).ToHashSet();
            var measured = database.Read(c => c.Query(
                    "SELECT collection_id, name FROM franchises WHERE films >= $films AND votes >= $votes ORDER BY votes DESC, collection_id LIMIT $most",
                    r => (Id: r.GetInt32(0), Name: r.GetString(1)),
                    ("$films", settings.MinFilms), ("$votes", settings.MinVotes), ("$most", Math.Max(0, settings.MostShown) + curated.Count)))
                .Where(m => !curated.Contains(m.Id))
                .Take(Math.Max(0, settings.MostShown))
                .ToList();

            // A slug two series share stays with the older series; the other, and any slug another
            // shelf already has, takes its own id after it.
            var taken = DiscoverShelves.Fixed.Select(s => s.Id).Concat(Curated.Select(s => s.Id)).ToHashSet(StringComparer.Ordinal);
            var first = measured.GroupBy(m => Slug(TitleOf(m.Name))).ToDictionary(g => g.Key, g => g.Min(m => m.Id), StringComparer.Ordinal);
            foreach (var (id, name) in measured)
            {
                var title = TitleOf(name);
                var slug = Slug(title);
                if (slug.Length == 0 || taken.Contains(slug) || first[slug] != id) slug = slug.Length == 0 ? $"series-{id}" : $"{slug}-{id}";
                all.Add(Series(slug, title, id));
            }
        }
        var shelved = new Shelved(all, all.ToDictionary(s => s.Id, StringComparer.Ordinal));
        _shelved = shelved;
        return shelved;
    }

    /// <summary>A series' name as its shelf is titled: the database's without a trailing <c>Collection</c>.</summary>
    internal static string TitleOf(string name)
    {
        var title = name.Trim();
        const string suffix = " Collection";
        return title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && title.Length > suffix.Length ? title[..^suffix.Length].TrimEnd() : title;
    }

    /// <summary>A title as an id: lowercase ASCII letters and digits, every run of anything else one hyphen, none at either end.</summary>
    internal static string Slug(string title)
    {
        var slug = new StringBuilder(title.Length);
        foreach (var c in title.Normalize(NormalizationForm.FormD))
        {
            if (char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark || c is '\'' or '’') continue;
            if (char.IsAsciiLetterOrDigit(c)) slug.Append(char.ToLowerInvariant(c));
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        }
        return slug.ToString().TrimEnd('-');
    }

    private readonly record struct Measure(int Films, int Votes, DateTimeOffset AskedAt);

    private DateOnly? _listedDay;
    private List<int> _listed = [];

    /// <summary>
    /// Asks the film database about every series that is due, one after another and at most
    /// <see cref="FranchiseOptions.RequestsPerSecond"/> a second, keeping each one's measure, and renews
    /// what is shelved as it goes. A series is due when the day's list names it and nothing is measured,
    /// or when its measure is old. The number of series that could not be asked.
    /// </summary>
    public async Task<int> ScanAsync(CancellationToken ct)
    {
        if (!Scanning) return 0;
        var settings = options.Value;
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var listed = await ListedAsync(today, ct);

        var held = database.Read(c => c.Query("SELECT collection_id, films, votes, asked_at FROM franchises",
            r => (Id: r.GetInt32(0), Measure: new Measure(r.GetInt32(1), r.GetInt32(2), r.GetInstant(3)))))
            .ToDictionary(m => m.Id, m => m.Measure);
        var refresh = TimeSpan.FromDays(Math.Max(1, settings.RefreshDays));
        var outsider = TimeSpan.FromDays(Math.Max(1, settings.OutsiderRefreshDays));
        bool Due(int id) => !held.TryGetValue(id, out var m)
                            || now - m.AskedAt >= (m.Films >= settings.MinFilms && m.Votes >= settings.MinVotes ? refresh : outsider);
        var due = listed.Concat(held.Keys).Distinct().Where(Due).Order().ToList();
        if (due.Count == 0) return 0;

        var pause = TimeSpan.FromSeconds(1 / Math.Max(0.1, settings.RequestsPerSecond));
        var unanswered = 0;
        for (var i = 0; i < due.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var started = clock.GetTimestamp();
            try
            {
                using var scope = scopes.CreateScope();
                var series = await scope.ServiceProvider.GetRequiredService<TmdbClient>().CollectionAsync(due[i], ct);
                Keep(due[i], series, today, clock.GetUtcNow());
            }
            catch (HttpRequestException)
            {
                unanswered++;
            }
            if ((i + 1) % 500 == 0) Rebuild();
            var left = pause - clock.GetElapsedTime(started);
            if (left > TimeSpan.Zero) await Task.Delay(left, clock, ct);
        }
        Rebuild();
        if (unanswered > 0) logger.LogWarning("{Unanswered} of {Due} series could not be asked of the film database.", unanswered, due.Count);
        else logger.LogInformation("Measured {Due} series of the film database; {Shelved} franchises are shelved.", due.Count, All.Count);
        return unanswered;
    }

    /// <summary>The ids of the newest list published by <paramref name="today"/>, read once a day; none when no list could be read.</summary>
    private async Task<List<int>> ListedAsync(DateOnly today, CancellationToken ct)
    {
        if (_listedDay == today) return _listed;
        try
        {
            using var scope = scopes.CreateScope();
            var export = scope.ServiceProvider.GetRequiredService<FranchiseExport>();
            for (var back = 0; back <= Math.Max(0, options.Value.ExportLookbackDays); back++)
            {
                if (await export.IdsAsync(today.AddDays(-back), ct) is not { } ids) continue;
                (_listedDay, _listed) = (today, ids);
                return ids;
            }
            logger.LogWarning("The film database has published no list of series since {Day:yyyy-MM-dd}.", today.AddDays(-Math.Max(0, options.Value.ExportLookbackDays)));
        }
        catch (HttpRequestException e)
        {
            logger.LogWarning("The film database's list of series could not be read: {Reason}", e.Message);
        }
        return [];
    }

    /// <summary>
    /// Keeps one series' measure: its films released by today that the database does not mark adult, and
    /// their votes. A series the database does not hold is kept as none, so it waits as any series below
    /// the bar does rather than being asked again on every pass.
    /// </summary>
    private void Keep(int id, TitleCollection? series, DateOnly today, DateTimeOffset at)
    {
        var released = series?.Films.Where(f => f.Released is { } day && day <= today).ToList() ?? [];
        database.Write(c => c.Execute("""
            INSERT INTO franchises (collection_id, name, films, votes, newest, asked_at) VALUES ($id, $name, $films, $votes, $newest, $at)
            ON CONFLICT (collection_id) DO UPDATE SET name = excluded.name, films = excluded.films, votes = excluded.votes,
                newest = excluded.newest, asked_at = excluded.asked_at
            """,
            ("$id", id), ("$name", series?.Name ?? ""), ("$films", released.Count), ("$votes", released.Sum(f => f.Votes)),
            ("$newest", released.Count == 0 ? null : released.Max(f => f.Released)!.Value.ToString("yyyy-MM-dd")), ("$at", at)));
    }
}

/// <summary>Runs a franchise scan every <see cref="FranchiseOptions.WarmMinutes"/>, after the Discover shelves have had their first pass.</summary>
public sealed class FranchiseScanner(Franchises franchises, IOptions<FranchiseOptions> options) : BackgroundService
{
    private static readonly TimeSpan FirstPass = TimeSpan.FromMinutes(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!franchises.Scanning || options.Value.WarmMinutes <= 0) return;
        try
        {
            await Task.Delay(FirstPass, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await franchises.ScanAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromMinutes(options.Value.WarmMinutes), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
