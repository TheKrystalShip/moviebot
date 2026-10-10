using TheKrystalShip.MovieBot.Acquire.Tmdb;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>
/// A film as a page shows it: what the film database said, with its artwork at this server's own
/// addresses. An image the database had none of is absent rather than a broken link.
/// </summary>
public sealed record FilmView
{
    public required string ImdbId { get; init; }
    public required int TmdbId { get; init; }
    public required string Title { get; init; }
    public string? OriginalTitle { get; init; }
    public int? Year { get; init; }
    public string? ReleaseDate { get; init; }
    public int? RuntimeMinutes { get; init; }
    public string? Certification { get; init; }
    public required List<string> Genres { get; init; }
    public string? Overview { get; init; }
    public string? Tagline { get; init; }

    /// <summary>The database's keywords for the film (<c>time loop</c>, <c>heist</c>); empty while the description is being asked for them.</summary>
    public required List<string> Keywords { get; init; }

    /// <summary>
    /// True when the database tags the film black and white. The database has no tag for colour, so a
    /// film not tagged is not known to be in colour, and this is false for it.
    /// </summary>
    public bool BlackAndWhite { get; init; }

    public double? Rating { get; init; }
    public int Votes { get; init; }
    public required List<string> Directors { get; init; }
    public required List<string> Writers { get; init; }

    /// <summary>Each director's and writer's id in the film database, by the name they are listed under; what their credits are asked for by.</summary>
    public required Dictionary<string, int> PersonIds { get; init; }

    public required List<CastView> Cast { get; init; }
    public string? OriginalLanguage { get; init; }
    public required List<string> Languages { get; init; }
    public required List<string> Countries { get; init; }
    public string? PosterUrl { get; init; }
    public string? BackdropUrl { get; init; }

    /// <summary>The title that plays the film when the library holds it.</summary>
    public string? LibraryId { get; init; }

    /// <summary>Whether the asking person watched the film through; false for the service key.</summary>
    public bool Watched { get; init; }

    /// <summary>The shelf it is on beyond its kind, <c>anime</c> or <c>general</c>; absent while nothing says.</summary>
    public Library.TitleCategory? Category { get; init; }
}

public sealed record CastView
{
    /// <summary>The person's id in the film database, what their credits are asked for by; absent while the description is kept without one.</summary>
    public int? PersonId { get; init; }

    public required string Name { get; init; }
    public string? Character { get; init; }
    public string? PortraitUrl { get; init; }
}

/// <summary>
/// The part of a film's description a listing shows, carried on each title so a shelf, a hero and a
/// filter need no request per film.
/// </summary>
public sealed record FilmFacts
{
    public double? Rating { get; init; }
    public int Votes { get; init; }
    public string? Certification { get; init; }
    public required List<string> Genres { get; init; }
    public int? RuntimeMinutes { get; init; }
    public string? Overview { get; init; }
    public required List<string> Directors { get; init; }
    public string? PosterUrl { get; init; }
    public string? BackdropUrl { get; init; }
}

public static class FilmViews
{
    /// <summary>Where a kept image is served from. Artwork is not the film, so it needs no token.</summary>
    public static string ImageUrl(string imdbId, string file) => $"/films/{imdbId}/{file}";

    public static FilmView? View(this FilmStore store, StoredFilm stored, string? libraryId = null, bool watched = false)
    {
        if (stored.Details is not { } d) return null;
        string? Image(string file) => store.HasImage(d.ImdbId, file) ? ImageUrl(d.ImdbId, file) : null;

        return new FilmView
        {
            ImdbId = d.ImdbId,
            TmdbId = d.TmdbId,
            Title = d.Title,
            OriginalTitle = d.OriginalTitle,
            Year = d.Year,
            ReleaseDate = d.ReleaseDate,
            RuntimeMinutes = d.RuntimeMinutes,
            Certification = d.Certification,
            Genres = [.. d.Genres],
            Overview = d.Overview,
            Tagline = d.Tagline,
            Keywords = [.. d.Keywords ?? []],
            BlackAndWhite = KeywordNames.MarksBlackAndWhite(d.Keywords),
            Rating = d.Rating,
            Votes = d.Votes,
            Directors = [.. d.Directors],
            Writers = [.. d.Writers],
            PersonIds = new Dictionary<string, int>(d.PersonIds),
            Cast = d.Cast.Select((c, i) => new CastView
            {
                PersonId = c.TmdbId,
                Name = c.Name,
                Character = c.Character,
                PortraitUrl = Image(FilmStore.Portrait(i)),
            }).ToList(),
            OriginalLanguage = d.OriginalLanguage,
            Languages = [.. d.Languages],
            Countries = [.. d.Countries],
            PosterUrl = Image(FilmStore.Poster),
            BackdropUrl = Image(FilmStore.Backdrop),
            LibraryId = libraryId,
            Watched = watched,
        };
    }

    public static FilmFacts? Facts(this FilmStore store, string? imdbId)
    {
        if (imdbId is null || store.Held(imdbId)?.Details is not { } d) return null;
        return new FilmFacts
        {
            Rating = d.Rating,
            Votes = d.Votes,
            Certification = d.Certification,
            Genres = [.. d.Genres],
            RuntimeMinutes = d.RuntimeMinutes,
            Overview = d.Overview,
            Directors = [.. d.Directors],
            PosterUrl = store.HasImage(d.ImdbId, FilmStore.Poster) ? ImageUrl(d.ImdbId, FilmStore.Poster) : null,
            BackdropUrl = store.HasImage(d.ImdbId, FilmStore.Backdrop) ? ImageUrl(d.ImdbId, FilmStore.Backdrop) : null,
        };
    }
}
