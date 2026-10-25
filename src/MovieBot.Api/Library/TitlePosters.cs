using System.Globalization;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Api.Catalogue;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Library;

/// <summary>
/// The poster a title in the library is shown by, served at <c>/media/{id}/poster.jpg</c>: the catalogue's
/// poster for the film, or for the show an episode belongs to, the one every list and page shows it by.
/// The poster the hand-off put beside the title is served only for a title the catalogue holds no
/// poster for.
/// </summary>
public sealed class TitlePosters(TitleLibrary library, CatalogueTitles catalogue, IOptions<CatalogueReadOptions> options)
{
    public const string FileName = "poster.jpg";

    /// <summary>The file a title's poster is served from, or null when it has none.</summary>
    public string? FileOf(string id) => library.Get(id) is { } manifest ? FileOf(manifest) : null;

    public string? FileOf(Manifest manifest) =>
        Kept(manifest.Film)
        ?? (manifest.Poster is { } beside && library.DirectoryOf(manifest.Id) is { } directory
            && Path.Combine(directory, beside) is var path && File.Exists(path) ? path : null);

    /// <summary>
    /// A listed title with its poster as served: named whenever there is one, and versioned by the file
    /// it is served from, so an address carrying the version names one image for good.
    /// </summary>
    public TitleSummary Listed(TitleSummary title) =>
        Kept(title.Film) is { } kept ? title with { Poster = FileName, PosterVersion = VersionOf(kept) } : title;

    /// <summary>The catalogue's poster file for a title, where it is kept.</summary>
    private string? Kept(FilmIdentity? film) =>
        catalogue.Card(film?.ImdbId)?.Poster is { } url && url.Split('/') is ["", "images", var size, var file]
            && CatalogueSetup.ImageFile(options.Value, size, file) is { } path && File.Exists(path)
            ? path
            : null;

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
