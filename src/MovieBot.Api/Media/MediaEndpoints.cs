using Microsoft.Net.Http.Headers;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Media;

public static class MediaEndpoints
{
    /// <summary>
    /// HLS content types. The framework's default provider knows none of these, and a segment
    /// served as application/octet-stream is refused by hls.js rather than played.
    /// </summary>
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".m3u8"] = "application/vnd.apple.mpegurl",
        [".m4s"] = "video/iso.segment",
        [".mp4"] = "video/mp4",
        [".vtt"] = "text/vtt",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png"
    };

    public static void MapMedia(this WebApplication app)
    {
        // HEAD as well as GET: browsers and some players probe a media URL before fetching it,
        // and a 405 there reads to the client as the file being unavailable. A film's bytes open
        // to its ticket or to somebody who may watch.
        app.MapMethods("/media/{id}/{**path}", ["GET", "HEAD"], Serve)
            .RequireMediaAccess();

        // Discord's servers fetch the poster to render an embed and carry nothing of ours. A poster
        // is not the film. It is the title's poster wherever it is kept (TitlePosters), so every
        // surface that names a title's poster by this address shows the same one.
        app.MapMethods("/media/{id}/" + TitlePosters.FileName, ["GET", "HEAD"], (string id, string? v, TitlePosters posters) =>
                TitleLibrary.IsSafeId(id) && posters.FileOf(id) is { } poster
                    ? Results.File(poster, "image/jpeg", enableRangeProcessing: true).WithCacheControl(ArtworkCacheControl(v))
                    : Results.NotFound())
            .AllowAnonymous();
    }

    /// <summary>
    /// A poster and a sheet of scrub previews keep their names while what they name changes: a title
    /// gains the film database's poster once it is described, and a sheet that came out wrong is
    /// rebuilt. Served as immutable at the bare address, whoever already looked at the old one would
    /// hold it for a year, so there it is checked instead, which costs a request answered 304. Asked
    /// for with the version a listing gave it, it is immutable: a change moves the version and so the
    /// address, and a page of posters is drawn from the browser's cache rather than a round trip each.
    /// </summary>
    private static string ArtworkCacheControl(string? v) =>
        string.IsNullOrEmpty(v) ? "public, max-age=0, must-revalidate" : "public, max-age=31536000, immutable";

    /// <param name="v">
    /// The version a listing gave the file (<see cref="TitleSummary.PosterVersion"/>). Its value is
    /// never compared: a rewritten file is listed under another version, so an address carrying one
    /// names a single copy for as long as it is asked for.
    /// </param>
    private static IResult Serve(string id, string path, string? v, TitleLibrary library, Subtitles.SubtitleStore subtitles)
    {
        if (!TitleLibrary.IsSafeId(id)) return Results.NotFound();

        // Subtitles fetched from outside sit under their own root, so that a re-ingest
        // replacing a title's directory cannot take them with it. They are served from the
        // same URL space regardless, because a player has no reason to know the difference.
        if (path.StartsWith(Subtitles.SubtitleStore.UriPrefix, StringComparison.Ordinal))
        {
            var fetched = subtitles.Resolve(id, path[Subtitles.SubtitleStore.UriPrefix.Length..]);

            return fetched is null
                ? Results.NotFound()
                : Results.File(fetched, "text/vtt", enableRangeProcessing: true)
                    .WithCacheControl("public, max-age=60");
        }

        // Asked for per request, because a film moves from the disk it was made on to the
        // disk it is kept on while people are watching it. Both copies are whole for the
        // moment they overlap and the settled one is resolved first, so a request in flight
        // is answered from one or the other and never from neither.
        if (library.DirectoryOf(id) is not { } titleRoot) return Results.NotFound();

        var requested = Path.GetFullPath(Path.Combine(titleRoot, path));

        // Resolve first, then check containment. A path that merely looks safe can still
        // resolve outside the root through an encoded traversal or a symlink.
        if (!requested.StartsWith(titleRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || !File.Exists(requested))
        {
            return Results.NotFound();
        }

        var extension = Path.GetExtension(requested);
        var contentType = ContentTypes.GetValueOrDefault(extension, "application/octet-stream");

        var isPlaylist = extension.Equals(".m3u8", StringComparison.OrdinalIgnoreCase);
        var transcoding = library.Get(id)?.Status == TitleStatus.Transcoding;

        // A growing EVENT playlist must never be cached: a client holding a stale copy sees
        // the film end early and stalls, which looks exactly like a broken transcode.
        // Segments never change once written, so they are immutable.
        //
        // Artwork is neither (ArtworkCacheControl).
        var cacheControl = (isPlaylist, transcoding) switch
        {
            (true, true) => "no-store, no-cache, must-revalidate",
            (true, false) => "public, max-age=300",
            _ when IsArtwork(requested) => ArtworkCacheControl(v),
            _ => "public, max-age=31536000, immutable"
        };

        // ffmpeg replaces a growing playlist by renaming a fresh copy over it. Served by path, its
        // length is read from one copy and its bytes from the next, and the response breaks off
        // mid-playlist; opened once, the length and the bytes are the same copy's.
        if (isPlaylist && transcoding)
        {
            return Results.Stream(File.OpenRead(requested), contentType, enableRangeProcessing: true)
                .WithCacheControl(cacheControl);
        }

        return Results.File(requested, contentType, enableRangeProcessing: true)
            .WithCacheControl(cacheControl);
    }

    /// <summary>What a title's directory holds that is rewritten under the same name.</summary>
    private static bool IsArtwork(string path) =>
        Path.GetFileName(path) is TitlePosters.FileName or "thumbs.jpg";

    private static IResult WithCacheControl(this IResult inner, string value) =>
        new CacheHeaderResult(inner, value);

    private sealed class CacheHeaderResult(IResult inner, string cacheControl) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers[HeaderNames.CacheControl] = cacheControl;
            return inner.ExecuteAsync(httpContext);
        }
    }
}
