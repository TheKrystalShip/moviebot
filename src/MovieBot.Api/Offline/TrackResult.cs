using System.Diagnostics;
using Microsoft.Net.Http.Headers;

namespace TheKrystalShip.MovieBot.Api.Offline;

/// <summary>
/// Answers a request for a whole track: the files a rendition is made of, joined in the order its
/// playlist lists them, ranged so that a broken transfer resumes where it stopped.
///
/// Nothing is copied or encoded: each file is opened and its bytes are written out as they are on
/// disk. The title's directory is looked up again when a file is not where it was, because a title
/// can move from the disk it was made on to the one it is kept on, and both copies are whole while
/// they overlap.
/// </summary>
public sealed class TrackResult(
    TrackIndex track,
    string titleDirectory,
    Func<string?> resolveTitleDirectory,
    Transfers transfers,
    Transfers.Lease? lease,
    ILogger logger) : IResult
{
    /// <summary>How much is read and written at a time, and so how often the pace is checked.</summary>
    private const int Chunk = 64 * 1024;

    public async Task ExecuteAsync(HttpContext context)
    {
        using var _ = lease;
        var request = context.Request;
        var response = context.Response;

        response.Headers[HeaderNames.AcceptRanges] = "bytes";
        response.Headers[HeaderNames.ETag] = track.ETag;
        // A browser's own cache would keep a second copy of a film the page is already keeping.
        response.Headers[HeaderNames.CacheControl] = "no-store";
        // A proxy buffering ahead of the client would take the track off the disk at its own pace,
        // which is not the pace the transfer is held to.
        response.Headers["X-Accel-Buffering"] = "no";
        response.ContentType = "video/mp4";

        // A range against another version of the track would join two films' bytes; the whole new
        // track is the answer to it. A validator of a stretch the track still begins with is this
        // version, grown.
        var ifRange = request.Headers[HeaderNames.IfRange].ToString();
        var rangeHeader = ifRange.Length > 0 && !track.Continues(ifRange) ? null : request.Headers[HeaderNames.Range].ToString();

        var range = ByteRange.Parse(rangeHeader, track.Length, out var satisfiable);
        if (!satisfiable)
        {
            response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
            response.Headers[HeaderNames.ContentRange] = $"bytes */{track.Length}";
            return;
        }

        var start = range?.Start ?? 0;
        var length = range?.Length ?? track.Length;
        if (range is { } r)
        {
            response.StatusCode = StatusCodes.Status206PartialContent;
            // A growing track's whole length is not known yet.
            response.Headers[HeaderNames.ContentRange] = $"bytes {r.Start}-{r.End}/{(track.Ended ? track.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) : "*")}";
        }
        response.ContentLength = length;

        if (HttpMethods.IsHead(request.Method) || length == 0) return;

        var ct = context.RequestAborted;
        var clock = Stopwatch.StartNew();
        long sent = 0;
        var pace = 0L;
        long pacedFrom = 0;
        var pacedAt = TimeSpan.Zero;
        var buffer = new byte[Chunk];
        var directory = titleDirectory;

        try
        {
            for (var file = track.FileAt(start); file < track.Files.Count && sent < length; file++)
            {
                var within = file == track.FileAt(start) ? start - track.Offsets[file] : 0;
                var take = Math.Min(track.Sizes[file] - within, length - sent);

                await using var stream = Open(ref directory, track.Files[file]);
                if (stream.Length != track.Sizes[file])
                    throw new IOException($"{track.Files[file]} is not the length it was measured at.");
                stream.Seek(within, SeekOrigin.Begin);

                while (take > 0)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(Chunk, take)), ct);
                    if (read == 0) throw new IOException($"{track.Files[file]} ended early.");
                    await response.Body.WriteAsync(buffer.AsMemory(0, read), ct);
                    take -= read;
                    sent += read;

                    // The pace is measured from the moment it last changed, so a room starting or
                    // ending mid-transfer neither stalls it to repay the past nor lets it burst.
                    var now = transfers.BytesPerSecond();
                    if (now != pace)
                    {
                        pace = now;
                        pacedFrom = sent;
                        pacedAt = clock.Elapsed;
                    }

                    var wait = Transfers.Wait(sent - pacedFrom, clock.Elapsed - pacedAt, pace);
                    if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
                }
            }

            logger.LogInformation(
                "Sent {Bytes} bytes of {Rendition} in {Seconds:0.0}s ({Mbps:0.0} Mbit/s)",
                sent, track.Rendition, clock.Elapsed.TotalSeconds,
                sent * 8 / 1e6 / Math.Max(clock.Elapsed.TotalSeconds, 0.001));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The device went away; it resumes from what it has.
            logger.LogInformation("A transfer of {Rendition} stopped after {Bytes} bytes", track.Rendition, sent);
        }
        catch (IOException ex)
        {
            // The headers are gone, so the only way to say so is to end the response short of its
            // length, which the device reads as a transfer to resume.
            logger.LogWarning(ex, "A transfer of {Rendition} broke after {Bytes} bytes", track.Rendition, sent);
            context.Abort();
        }
    }

    /// <summary>A file of the track, from the title's directory as it is now if it has moved since.</summary>
    private FileStream Open(ref string directory, string name)
    {
        var path = Path.Combine(directory, track.Rendition, name);
        if (!File.Exists(path) && resolveTitleDirectory() is { } moved)
        {
            directory = moved;
            path = Path.Combine(directory, track.Rendition, name);
        }

        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, useAsync: true);
    }
}
