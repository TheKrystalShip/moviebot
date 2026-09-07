using Microsoft.Net.Http.Headers;
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
        // and a 405 there reads to the client as the file being unavailable.
        app.MapMethods("/media/{id}/{**path}", ["GET", "HEAD"],
            (string id, string path, TitleLibrary library, Subtitles.SubtitleStore subtitles) =>
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
            // Artwork is neither. A poster and a sheet of scrub previews keep their names and are
            // rewritten in place — by a catalogue lookup, or by rebuilding a sheet that came out
            // wrong — so serving them as immutable leaves whoever already looked at the broken one
            // holding it for a year. They are checked instead, which costs a request answered 304
            // and is what makes a correction reach anybody.
            var cacheControl = (isPlaylist, transcoding) switch
            {
                (true, true) => "no-store, no-cache, must-revalidate",
                (true, false) => "public, max-age=300",
                _ when IsArtwork(requested) => "public, max-age=0, must-revalidate",
                _ => "public, max-age=31536000, immutable"
            };

            return Results.File(requested, contentType, enableRangeProcessing: true)
                .WithCacheControl(cacheControl);
        });
    }

    /// <summary>What a title's directory holds that is rewritten under the same name.</summary>
    private static bool IsArtwork(string path) =>
        Path.GetFileName(path) is "poster.jpg" or "thumbs.jpg";

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
