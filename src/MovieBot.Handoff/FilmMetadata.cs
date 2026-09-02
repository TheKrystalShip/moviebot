using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Acquire.Imdb;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Handoff;

/// <summary>
/// What a film is, and what it looks like, put where the library can use it.
///
/// A release name is what a film arrives as and is not what it is called. It carries the year
/// among the resolution and the codec, keeps whichever edition words the packer felt like, loses
/// every apostrophe, and says nothing about who is in it or what its poster looks like. Every
/// message about a film reads better from the catalogue than from the file name, so the catalogue
/// is consulted once, at the point the film enters the library, and what it says is kept.
///
/// Nothing here throws. A film with no poster is worse than a film with one and far better than
/// an ingest that failed over artwork, so every path returns what it managed and says what it
/// could not.
/// </summary>
public sealed class FilmMetadata(ImdbClient imdb, HttpClient http, ILogger<FilmMetadata> logger)
{
    /// <summary>
    /// Wide enough to be looked at, small enough to travel. The originals are a few thousand
    /// pixels tall, and a message renders the poster at a fraction of that.
    /// </summary>
    private const int PosterWidth = 600;

    public const string PosterFileName = "poster.jpg";

    /// <summary>
    /// Which film this is.
    ///
    /// An id is exact and is preferred whenever the tracker gave one. Without it the parsed name
    /// and year are searched, and a search that cannot be sure returns nothing: a film named
    /// wrongly is worse than one not named at all, because everything downstream believes it.
    /// </summary>
    public async Task<ImdbTitle?> ResolveAsync(
        string? imdbId, string? title, int? year, CancellationToken ct)
    {
        var film = await imdb.LookupAsync(imdbId, ct);
        if (film is not null) return film;

        if (string.IsNullOrWhiteSpace(title)) return null;

        film = await imdb.SearchAsync(title, year, ct);
        if (film is null)
            logger.LogInformation("Nothing in the title index for {Title} ({Year}).", title, year);

        return film;
    }

    /// <summary>What goes in the manifest, from what the index answered.</summary>
    public static FilmIdentity Identify(ImdbTitle film) => new()
    {
        ImdbId = film.ImdbId,
        Name = film.Title,
        Year = film.Year,
        Starring = film.Starring,
        PosterUrl = film.PosterUrl?.ToString()
    };

    /// <summary>
    /// Fetches the poster into a directory, and answers with the path it wrote or null when there
    /// was nothing to write.
    /// </summary>
    public async Task<string?> WritePosterAsync(ImdbTitle film, string directory, CancellationToken ct)
    {
        if (film.PosterAt(PosterWidth) is not { } url) return null;

        try
        {
            var bytes = await http.GetByteArrayAsync(url, ct);

            // A poster is tens of kilobytes. Anything shorter is an error page with a success
            // status, which is what an image host answers a request it does not like.
            if (bytes.Length < 2048)
            {
                logger.LogWarning("The poster for {Imdb} came back as {Bytes} bytes.", film.ImdbId, bytes.Length);
                return null;
            }

            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, PosterFileName);
            await File.WriteAllBytesAsync(path, bytes, ct);
            return path;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not fetch the poster for {Imdb}.", film.ImdbId);
            return null;
        }
    }
}
