using System.Globalization;
using TheKrystalShip.MovieBot.Api.Films;
using TheKrystalShip.MovieBot.Api.Shows;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Library;

/// <summary>
/// The poster a title in the library is shown by, served at <c>/media/{id}/poster.jpg</c>.
///
/// It is the poster the film database's copy keeps for the film, or for the show an episode belongs
/// to: the one a search, the catalogue and the title's page show, pinned to the first the API showed
/// (<see cref="PosterPins"/>), so a title keeps one face from the search that found it to the shelf
/// that holds it. The poster the hand-off put beside the title is served only for a title the
/// database holds no poster for.
/// </summary>
public sealed class TitlePosters(TitleLibrary library, FilmStore films, ShowStore shows)
{
    public const string FileName = "poster.jpg";

    /// <summary>The file a title's poster is served from, or null when it has none.</summary>
    public string? FileOf(string id) => library.Get(id) is { } manifest ? FileOf(manifest) : null;

    public string? FileOf(Manifest manifest) =>
        Kept(manifest.Kind, manifest.Film)
        ?? (manifest.Poster is { } beside && library.DirectoryOf(manifest.Id) is { } directory
            && Path.Combine(directory, beside) is var path && File.Exists(path) ? path : null);

    /// <summary>
    /// A listed title with its poster as served: named whenever there is one, and versioned by the file
    /// it is served from, so an address carrying the version names one image for good.
    /// </summary>
    public TitleSummary Listed(TitleSummary title) =>
        Kept(title.Kind, title.Film) is { } kept ? title with { Poster = FileName, PosterVersion = VersionOf(kept) } : title;

    private string? Kept(TitleKind kind, FilmIdentity? film) => film?.ImdbId is not { } imdbId
        ? null
        : kind == TitleKind.Show ? shows.ImageFile(imdbId, ShowStore.Poster) : films.ImageFile(imdbId, FilmStore.Poster);

    private static string? VersionOf(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists
                ? new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
