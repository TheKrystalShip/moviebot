using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Acquire.Configuration;
using TheKrystalShip.MovieBot.Acquire.Tmdb;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>One streaming service, rental shop or store, with its logo served from here when one is kept.</summary>
public sealed record ProviderView(int ProviderId, string Name, string? LogoUrl);

/// <summary>
/// Where a title can be watched in one region, by how. The data is JustWatch's and is attributed to
/// JustWatch wherever it is shown.
/// </summary>
public sealed record WatchView
{
    public required string Region { get; init; }

    /// <summary>The page that lists these on the film database, which is JustWatch's.</summary>
    public string? Link { get; init; }

    public required List<ProviderView> Flatrate { get; init; }
    public required List<ProviderView> Rent { get; init; }
    public required List<ProviderView> Buy { get; init; }
    public required List<ProviderView> Free { get; init; }
    public required List<ProviderView> Ads { get; init; }
}

/// <summary>
/// The streaming services of the configured region: the list the database keeps for it, kept like a
/// vocabulary and read by the shelves and by the questions that say <c>on netflix</c>, and the logos
/// the titles' providers are drawn with, fetched once and served from here like any other artwork.
/// </summary>
public sealed class StreamingServices(string root, AnswerKeeper keeper, KeptImages images, IOptions<TmdbOptions> tmdb)
{
    /// <summary>How long the database's list of services is kept; it changes rarely.</summary>
    internal static readonly TimeSpan ListKept = TimeSpan.FromDays(30);

    private const string LogoSize = "w154";

    /// <summary>What the logos this keeps are named under in <see cref="KeptImages"/>.</summary>
    private const string Images = "catalogue";

    /// <summary>The region titles are asked about, an upper-case ISO 3166-1 code.</summary>
    public string Region => tmdb.Value.StreamingRegion.ToUpperInvariant();

    /// <summary>The names people say for a service besides the one the database lists, with the listed name each means.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["prime"] = "amazon prime video", ["prime video"] = "amazon prime video", ["amazon"] = "amazon prime video",
        ["amazon prime"] = "amazon prime video", ["hbo"] = "hbo max", ["disney"] = "disney plus", ["apple tv plus"] = "apple tv",
        ["appletv"] = "apple tv", ["movistar"] = "movistar plus plus", ["movistar plus"] = "movistar plus plus",
        ["sky showtime"] = "skyshowtime",
    };

    /// <summary>A name in the form services are compared by: lower case, <c>+</c> as <c>plus</c>, letters and digits alone.</summary>
    public static string Normalised(string name)
    {
        var spaced = name.ToLowerInvariant().Replace("+", " plus ");
        var kept = new string([.. spaced.Select(c => char.IsLetterOrDigit(c) ? c : ' ')]);
        return string.Join(' ', kept.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>The service a name means among <paramref name="services"/>: the one listed under it, else the one an alias names.</summary>
    public static WatchProvider? Match(IReadOnlyList<WatchProvider> services, string name)
    {
        var wanted = Normalised(name);
        if (wanted.Length == 0) return null;
        var listed = services.FirstOrDefault(s => Normalised(s.Name) == wanted);
        if (listed is not null) return listed;
        return Aliases.TryGetValue(wanted, out var target) ? services.FirstOrDefault(s => Normalised(s.Name) == target) : null;
    }

    /// <summary>
    /// The database's streaming services for films or shows in the region, asking it when none is kept
    /// or what is kept is old. Null when it cannot be asked and nothing is kept.
    /// </summary>
    public async Task<IReadOnlyList<WatchProvider>?> ListAsync(string kind, CancellationToken ct)
    {
        var kept = await keeper.KeptAsync($"providers/{Region}-{kind}", AnswerJson.Default.ListWatchProvider, ListKept,
            async (client, token) => (await client.WatchProvidersAsync(kind, token)) is { Count: > 0 } list ? list.ToList() : null, null, ct);
        return kept is null ? null : kept.Value ?? [];
    }

    /// <summary>The list as kept, old or not, without asking anything; empty while none is kept.</summary>
    public IReadOnlyList<WatchProvider> Kept(string kind) =>
        keeper.Kept($"providers/{Region}-{kind}", AnswerJson.Default.ListWatchProvider)?.Value ?? [];

    /// <summary>Both kinds' services by name, the film list first, each service once.</summary>
    public async Task<IReadOnlyList<WatchProvider>> AllAsync(CancellationToken ct)
    {
        var films = await ListAsync("film", ct) ?? [];
        var shows = await ListAsync("show", ct) ?? [];
        return [.. films.Concat(shows).DistinctBy(s => s.ProviderId)];
    }

    /// <summary>Keeps the logos of the providers a title is on.</summary>
    internal async Task KeepLogosAsync(TmdbClient client, WatchProviders? providers, CancellationToken ct)
    {
        foreach (var provider in providers?.All.DistinctBy(p => p.ProviderId) ?? [])
            await images.KeepAsync(client, Images, root, LogoName(provider.ProviderId), provider.LogoPath, LogoSize, ct);
    }

    private static string LogoName(int providerId) => $"providers/{providerId}.png";

    /// <summary>Where a provider's logo is served from. Artwork is not the film, so it needs no token.</summary>
    public static string LogoUrl(int providerId) => $"/catalogue/providers/{providerId}.png";

    /// <summary>The kept logo a request names, or null for anything that is not one.</summary>
    public string? LogoFile(string file)
    {
        if (!file.EndsWith(".png", StringComparison.Ordinal) || !int.TryParse(file[..^4], out var id) || id <= 0
            || file[..^4] != id.ToString(System.Globalization.CultureInfo.InvariantCulture)) return null;
        var path = Path.Combine(root, LogoName(id));
        return File.Exists(path) ? path : null;
    }

    /// <summary>A title's providers as a page shows them; absent when the database lists none in the region.</summary>
    public WatchView? View(WatchProviders? providers)
    {
        if (providers is null) return null;
        List<ProviderView> Of(IReadOnlyList<WatchProvider> list) =>
            [.. list.Select(p => new ProviderView(p.ProviderId, p.Name, File.Exists(Path.Combine(root, LogoName(p.ProviderId))) ? LogoUrl(p.ProviderId) : null))];
        return new WatchView
        {
            Region = providers.Region,
            Link = providers.Link,
            Flatrate = Of(providers.Flatrate),
            Rent = Of(providers.Rent),
            Buy = Of(providers.Buy),
            Free = Of(providers.Free),
            Ads = Of(providers.Ads),
        };
    }
}
