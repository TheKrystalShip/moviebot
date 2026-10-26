using TheKrystalShip.MovieBot.Catalogue;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Client;

public sealed partial class MovieBotApiClient
{
    // ── The catalogue ──────────────────────────────────────────────────────────────────────────
    // Every list of titles is one query (TitleFilter); a title is its page; names, genres and
    // languages resolve through the terms and the facets. Each is joined with what the person this
    // client acts as has of it.

    /// <summary>A list of titles: a search, a discovery, a ranking, a filmography or a window of dates.</summary>
    public async Task<CardPage> TitlesAsync(TitleFilter filter, CancellationToken ct) =>
        (await GetAsync("api/catalogue/titles" + filter.QueryString(),
            ManifestJsonContext.Default.CardPage, "The catalogue could not be asked.", ct))!;

    /// <summary>A title's page, film or show.</summary>
    public async Task<TitlePage> TitleAsync(string imdbId, CancellationToken ct) =>
        (await GetAsync($"api/catalogue/titles/{Uri.EscapeDataString(imdbId)}",
            ManifestJsonContext.Default.TitlePage, $"{imdbId} could not be read.", ct))!;

    /// <summary>Words resolved to what the list filters take: genres, people, keywords, companies, Wikidata items, franchises.</summary>
    public async Task<List<Term>> TermsAsync(string words, CancellationToken ct) =>
        await GetAsync($"api/catalogue/terms?q={Uri.EscapeDataString(words)}",
            ManifestJsonContext.Default.ListTerm, "The catalogue could not be asked.", ct) ?? [];

    /// <summary>The catalogue's facets over every title, which name the genres and the original languages it holds.</summary>
    public async Task<Facets> FacetsAsync(CancellationToken ct) =>
        (await GetAsync("api/catalogue/facets", ManifestJsonContext.Default.Facets, "The catalogue could not be asked.", ct))!;
}
