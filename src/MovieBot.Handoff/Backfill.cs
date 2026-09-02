using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Handoff;

/// <summary>
/// Gives films already in the library what a film ingested from now on gets on the way in.
///
/// Run against a media root rather than against a download, because what it is for is the films
/// that arrived before there was anywhere to put a name or a poster. It reads each manifest,
/// asks the catalogue what the film is, writes the artwork beside the film and puts the answer
/// back in the manifest.
///
/// A film that already carries cover art from its own source keeps it: that poster came with the
/// release and is already what somebody chose to ship with the film.
/// </summary>
public sealed class Backfill(FilmMetadata metadata, ILogger<Backfill> logger)
{
    public async Task<int> RunAsync(string mediaRoot, CancellationToken ct)
    {
        if (!Directory.Exists(mediaRoot))
        {
            logger.LogError("No media root at {Root}.", mediaRoot);
            return 1;
        }

        var filled = 0;
        var missed = 0;

        foreach (var directory in Directory.EnumerateDirectories(mediaRoot).OrderBy(d => d))
        {
            ct.ThrowIfCancellationRequested();

            var path = Path.Combine(directory, "manifest.json");
            if (!File.Exists(path)) continue;

            var id = Path.GetFileName(directory);

            Manifest manifest;
            try
            {
                manifest = ManifestJson.Deserialize(await File.ReadAllTextAsync(path, ct))
                           ?? throw new InvalidOperationException("empty manifest");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "{Id}: the manifest could not be read.", id);
                missed++;
                continue;
            }

            // The release name is the better thing to ask about than the title already in the
            // manifest: the title may itself have come from a release name, in which case asking
            // with it repeats whatever it got wrong.
            var release = ReleaseParser.ParseName(manifest.Source?.Release ?? manifest.Title);

            var film = await metadata.ResolveAsync(
                manifest.Film?.ImdbId, release.Title, release.Year, ct);

            if (film is null)
            {
                logger.LogWarning("{Id}: not identified.", id);
                missed++;
                continue;
            }

            var poster = manifest.Poster;
            if (string.IsNullOrEmpty(poster))
            {
                var written = await metadata.WritePosterAsync(film, directory, ct);
                if (written is not null) poster = FilmMetadata.PosterFileName;
            }

            if (film.Title is { Length: > 0 } name) manifest.Title = Display(name, film.Year);
            manifest.Poster = poster;
            manifest.Film = FilmMetadata.Identify(film);

            await File.WriteAllTextAsync(path, ManifestJson.Serialize(manifest), ct);

            logger.LogInformation(
                "{Id}: {Title} ({Imdb}){Poster}", id, manifest.Title, film.ImdbId,
                poster is null ? ", no poster" : $", poster {poster}");
            filled++;
        }

        logger.LogInformation("{Filled} filled in, {Missed} left alone.", filled, missed);
        return missed > 0 ? 1 : 0;
    }

    private static string Display(string name, int? year) => year is { } y ? $"{name} ({y})" : name;
}
