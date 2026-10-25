using System.Collections.Concurrent;
using AiringEpisode = TheKrystalShip.MovieBot.Acquire.Tmdb.AiringEpisode;
using EpisodeDetails = TheKrystalShip.MovieBot.Acquire.Tmdb.EpisodeDetails;
using SeasonDetails = TheKrystalShip.MovieBot.Acquire.Tmdb.SeasonDetails;
using ShowDetails = TheKrystalShip.MovieBot.Acquire.Tmdb.ShowDetails;
using TheKrystalShip.MovieBot.Api.Shows;
using TheKrystalShip.MovieBot.Catalogue;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Catalogue;

/// <summary>
/// The catalogue as the rest of the API reads it: a title's card by its IMDb id, wherever a title is
/// named (the library, wants, follows, plans, rooms), and a show's seasons with its episodes numbered as
/// its releases number them (<see cref="EpisodeNumbering"/>). Each is held for <see cref="Held"/>, since
/// a listing names hundreds of titles every time it is read, and a title the catalogue does not list is
/// held as none.
/// </summary>
public sealed class CatalogueTitles(CatalogueQueries queries, CatalogueState state, EpisodeNumbering numbering, TimeProvider clock)
{
    private static readonly TimeSpan Held = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, (TitleCard? Card, DateTimeOffset At)> _cards = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (StoredShow? Show, DateTimeOffset At)> _shows = new(StringComparer.Ordinal);

    /// <summary>A title's card; null for one the catalogue does not list, or while it cannot be read.</summary>
    public TitleCard? Card(string? imdbId) =>
        imdbId is null ? null : Cards([imdbId]).GetValueOrDefault(imdbId);

    /// <summary>The cards of the titles the catalogue lists, by IMDb id.</summary>
    public Dictionary<string, TitleCard> Cards(IEnumerable<string?> imdbIds)
    {
        var now = clock.GetUtcNow();
        var found = new Dictionary<string, TitleCard>(StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var id in imdbIds.OfType<string>().Distinct(StringComparer.Ordinal))
        {
            if (_cards.TryGetValue(id, out var held) && now - held.At < Held)
            {
                if (held.Card is not null) found[id] = held.Card;
            }
            else if (TitleQuery.IsImdbId(id)) missing.Add(id);
        }
        if (missing.Count == 0 || state.Problem() is not null) return found;

        var read = queries.CardsOf(missing).ToDictionary(c => c.Id, StringComparer.Ordinal);
        foreach (var id in missing)
        {
            var card = read.GetValueOrDefault(id);
            _cards[id] = (card, now);
            if (card is not null) found[id] = card;
        }
        return found;
    }

    /// <summary>
    /// A show as the episode code reads it: its facts, and its numbered seasons (specials are not) with
    /// their episodes, numbered as its releases number them. Null for a show the catalogue does not list.
    /// </summary>
    public StoredShow? Show(string? imdbId)
    {
        if (imdbId is null || !TitleQuery.IsImdbId(imdbId)) return null;
        var now = clock.GetUtcNow();
        if (!_shows.TryGetValue(imdbId, out var held) || now - held.At >= Held)
        {
            held = (state.Problem() is null ? Read(imdbId, now) : held.Show, now);
            _shows[imdbId] = held;
        }
        return held.Show is { } show ? numbering.Apply(imdbId, show) : null;
    }

    private StoredShow? Read(string imdbId, DateTimeOffset now)
    {
        if (Card(imdbId) is not { Kind: "show" } card) return null;
        var page = queries.Title(imdbId);
        var seasons = queries.Seasons(imdbId).Where(s => s.Number >= 1).Select(s => new SeasonDetails
        {
            Number = s.Number,
            Name = s.Name,
            Overview = s.Overview,
            AirDate = s.AirDate,
            PosterPath = s.Poster,
            Episodes = s.Episodes.Select(e => new EpisodeDetails
            {
                Season = s.Number, Number = e.Number, Name = e.Name, Overview = e.Overview, AirDate = e.AirDate,
                RuntimeMinutes = e.Runtime, StillPath = e.Still,
            }).ToList(),
        }).ToList();
        var today = DateOnly.FromDateTime(now.UtcDateTime).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var last = seasons.SelectMany(s => s.Episodes)
            .Where(e => e.AirDate is { } air && string.CompareOrdinal(air, today) <= 0)
            .MaxBy(e => (e.Season, e.Number));
        return new StoredShow
        {
            Details = new ShowDetails
            {
                TmdbId = 0,
                ImdbId = imdbId,
                Title = card.Title,
                OriginalTitle = card.OriginalTitle,
                FirstYear = card.Year,
                LastYear = card.EndYear,
                Status = page?.Status,
                InProduction = page?.Status is "Returning Series" or "In Production" or "Planned",
                Genres = card.Genres,
                Overview = page?.Overview,
                OriginalLanguage = card.Language,
                Creators = card.MadeBy,
                NextEpisodeToAir = page?.NextEpisode is { } next
                    ? new AiringEpisode { Season = next.Season, Number = next.Number, Name = next.Name, AirDate = next.AirDate }
                    : null,
                LastEpisodeToAir = last is null ? null : new AiringEpisode { Season = last.Season, Number = last.Number, Name = last.Name, AirDate = last.AirDate },
            },
            Seasons = seasons,
            FetchedAt = now,
        };
    }

    /// <summary>An episode's own name, in the releases' numbering, or null.</summary>
    public string? EpisodeName(string? showImdbId, EpisodeIdentity? episode) =>
        showImdbId is null || episode is null ? null
        : Show(showImdbId)?.Seasons.FirstOrDefault(s => s.Number == episode.Season)?.Episodes.FirstOrDefault(e => e.Number == episode.Number)?.Name;

    /// <summary>The episode after one: the next of its season, else the first of the next. Null at the end, and for a show the catalogue does not list.</summary>
    public EpisodeIdentity? EpisodeAfter(string showImdbId, EpisodeIdentity episode) =>
        Show(showImdbId)?.Seasons
            .SelectMany(s => s.Episodes.Select(e => new EpisodeIdentity { Season = s.Number, Number = e.Number }))
            .Where(e => e.Season > episode.Season || (e.Season == episode.Season && e.Number > episode.Number))
            .OrderBy(e => e.Season).ThenBy(e => e.Number)
            .FirstOrDefault();

    /// <summary>The episode before one: the one before it in its season, else the last of the season before.</summary>
    public EpisodeIdentity? EpisodeBefore(string showImdbId, EpisodeIdentity episode) =>
        Show(showImdbId)?.Seasons
            .SelectMany(s => s.Episodes.Select(e => new EpisodeIdentity { Season = s.Number, Number = e.Number }))
            .Where(e => e.Season < episode.Season || (e.Season == episode.Season && e.Number < episode.Number))
            .OrderBy(e => e.Season).ThenBy(e => e.Number)
            .LastOrDefault();
}
