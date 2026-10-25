using TheKrystalShip.MovieBot.Acquire.Tmdb;

namespace TheKrystalShip.MovieBot.Api.Shows;

/// <summary>
/// A show as the episode code reads it (<see cref="Catalogue.CatalogueTitles.Show"/>): its facts, and every
/// numbered season with its episodes, in order. Specials (season 0) are not among them.
/// </summary>
public sealed record StoredShow
{
    public ShowDetails? Details { get; init; }

    public List<SeasonDetails> Seasons { get; init; } = [];

    public required DateTimeOffset FetchedAt { get; init; }
}
