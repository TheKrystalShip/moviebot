using System.Diagnostics;
using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.MovieBot.Ingest.Ffmpeg;
using TheKrystalShip.MovieBot.Ingest.Probe;

namespace TheKrystalShip.MovieBot.Ingest.Pipeline;

/// <summary>A re-encode that did not produce rungs whole enough to replace the ones a title has.</summary>
public sealed class ReencodeException(string message) : Exception(message);

/// <summary>
/// Encodes a finished title's video again at the library's quality, from the title's own top rung,
/// into a directory of its own. What it writes replaces nothing; whoever called it swaps the rungs
/// in.
///
/// The top rung is the best copy of the film a title still has once its download is gone. Every
/// rung is made from it, the step-down included, so a title that has only the top rung gains the
/// step-down on the way. Audio, subtitles and artwork are left as they are.
/// </summary>
public sealed class Reencoder(Action<string> log)
{
    /// <summary>
    /// The constant quality a title is encoded at again from its own top rung.
    ///
    /// Two lossy passes cost more than one: scored with VMAF against the original on two-minute
    /// clips of a grainy Blu-ray and a clean web release, 25 from the top rung lands 1.3 to 1.6 points
    /// under 25 from the original, and 23 from the top rung lands within 0.2 of it, at about 42% less
    /// than the rung it replaces. So a title encoded again matches one encoded from its source.
    /// </summary>
    public const int LibraryCq = 23;

    /// <summary>The prefix this pass's segments are named with, so their URLs differ from the ones replaced.</summary>
    public static readonly string SegmentPrefix = $"q{LibraryCq}-";

    private static readonly TimeSpan ProgressEvery = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Whether a title is owed a pass: it is ready, and a rung of it records no quality or the
    /// ladder for its size has a rung it lacks.
    /// </summary>
    public static bool IsOwed(Manifest manifest)
    {
        if (manifest.Status != TitleStatus.Ready || manifest.Video.Renditions.Count == 0) return false;

        var ladder = Ladder.For(
            manifest.Video.Width, manifest.Video.Height,
            Ladder.TopKbps, Ladder.StepDownWidth, Ladder.StepDownKbps);

        return manifest.Video.Renditions.Any(r => r.Cq == 0)
               || manifest.Video.Renditions.Count < ladder.Count;
    }

    /// <summary>
    /// Writes every rung of the ladder for the title's size under <paramref name="workDirectory"/>
    /// and returns what the manifest should list for them.
    /// </summary>
    /// <exception cref="ReencodeException">A rung did not come out whole.</exception>
    /// <exception cref="FfmpegException">An encode failed.</exception>
    public async Task<IReadOnlyList<Rendition>> EncodeAsync(
        string titleDirectory, Manifest manifest, string workDirectory, CancellationToken ct)
    {
        var source = Path.Combine(titleDirectory, manifest.Video.Renditions[0].Uri);
        var options = new IngestOptions
        {
            SourcePath = source,
            OutputRoot = workDirectory,
            Cq = LibraryCq,
            SegmentPrefix = SegmentPrefix
        };

        var probe = await FfProbe.RunAsync(source, ct, waitForHeader: false);
        var video = probe.Streams.FirstOrDefault(s => s.IsVideo && !s.IsAttachedPicture)
                    ?? throw new ReencodeException($"{source} holds no video.");

        var ladder = VideoLadder.For(manifest.Video.Width, manifest.Video.Height, options);
        var arguments = new MainPassArguments(options);
        var decode = MainPassArguments.DecodeFor(video);
        var expected = HlsPlaylist.PlayableSeconds(source);

        var units = new List<TranscodeUnit>();
        foreach (var rung in ladder)
        {
            var directory = Path.Combine(workDirectory, rung.Directory);
            Directory.CreateDirectory(directory);

            var attempts = new List<IReadOnlyList<string>>
            {
                arguments.Video(video, rung, workDirectory, toneMap: false, decode)
            };
            if (decode == VideoDecode.Cuvid)
                attempts.Add(arguments.Video(video, rung, workDirectory, toneMap: false, VideoDecode.Hwaccel));

            units.Add(new TranscodeUnit(rung.Directory, rung.Directory, attempts,
                () => Directory.EnumerateFiles(directory, "*.m4s").Any(), guard: null, log));
        }

        var playlists = ladder.Select(r => Path.Combine(workDirectory, r.Directory, "index.m3u8")).ToList();
        var clock = Stopwatch.StartNew();

        using var progress = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var reporter = Task.Run(async () =>
        {
            while (!progress.Token.IsCancellationRequested)
            {
                try { await Task.Delay(ProgressEvery, progress.Token); }
                catch (OperationCanceledException) { break; }

                var head = HlsPlaylist.PlayableSeconds(playlists);
                log($"  {head / Math.Max(expected, 1) * 100:F0}% at {head / Math.Max(clock.Elapsed.TotalSeconds, 1):F1}x");
            }
        }, CancellationToken.None);

        try
        {
            await TranscodeUnit.RunAllAsync(units, ct);
        }
        finally
        {
            await progress.CancelAsync();
            await reporter;
        }

        // A rung replaces one a room may open, so it has to cover the film as the old one did, to
        // within a segment.
        foreach (var playlist in playlists)
        {
            if (!HlsPlaylist.IsComplete(playlist))
                throw new ReencodeException($"{playlist} was not closed.");

            var seconds = HlsPlaylist.PlayableSeconds(playlist);
            if (Math.Abs(seconds - expected) > Ladder.SegmentSeconds)
                throw new ReencodeException($"{playlist} holds {seconds:F1} s against {expected:F1} s in {source}.");
        }

        return ladder
            .Select(r => new Rendition
            {
                Name = r.Name,
                BitrateKbps = r.BitrateKbps,
                Width = r.Width,
                Height = r.Height,
                Uri = r.Uri,
                Cq = LibraryCq
            })
            .ToList();
    }
}
