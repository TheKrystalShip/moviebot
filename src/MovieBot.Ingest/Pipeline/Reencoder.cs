using System.Diagnostics;
using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.MovieBot.Ingest.Ffmpeg;
using TheKrystalShip.MovieBot.Ingest.Probe;

namespace TheKrystalShip.MovieBot.Ingest.Pipeline;

/// <summary>A re-encode that did not produce rungs whole enough to replace the ones a title has.</summary>
public sealed class ReencodeException(string message) : Exception(message);

/// <summary>
/// Encodes a finished title's video again at the library's quality, from the title's own top rung,
/// into a directory of its own. What it writes replaces nothing; whoever called it moves the rungs
/// in.
///
/// The top rung is the best copy of the film a title still has once its download is gone, and
/// every rung a pass writes is made from it. A title with a rung that records no quality has its
/// whole ladder made again, gaining any rung it lacks on the way; a title whose rungs all record one
/// has only the rungs it lacks made, and keeps the ones it has. Audio, subtitles and artwork are
/// left as they are.
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
    public static bool IsOwed(Manifest manifest) => Owed(manifest).Count > 0;

    /// <summary>
    /// Whether the pass a title is owed makes again rungs it already has, which a player may be on,
    /// rather than only adding rungs beside them.
    /// </summary>
    public static bool Replaces(Manifest manifest) => manifest.Video.Renditions.Any(r => r.Cq == 0);

    /// <summary>
    /// The rungs a pass over a title writes: the whole ladder for its size when it
    /// <see cref="Replaces"/>, otherwise the rungs of the ladder it lacks. Empty when it is owed
    /// nothing.
    /// </summary>
    public static IReadOnlyList<LadderRung> Owed(Manifest manifest)
    {
        if (manifest.Status != TitleStatus.Ready || manifest.Video.Renditions.Count == 0) return [];

        var ladder = Ladder.For(manifest.Video.Width, manifest.Video.Height);
        if (Replaces(manifest)) return ladder;

        var have = manifest.Video.Renditions.Select(DirectoryOf).ToHashSet(StringComparer.Ordinal);
        return ladder.Where(r => !have.Contains(r.Directory)).ToList();
    }

    /// <summary>The directory a rendition is written in, which is the first part of its URI.</summary>
    public static string DirectoryOf(Rendition rendition) => rendition.Uri.Split('/')[0];

    /// <summary>
    /// Writes the rungs the title is owed (<see cref="Owed"/>) under
    /// <paramref name="workDirectory"/> and returns what the manifest should list for them.
    /// </summary>
    /// <exception cref="ReencodeException">A rung did not come out whole.</exception>
    /// <exception cref="FfmpegException">An encode failed.</exception>
    public async Task<IReadOnlyList<Rendition>> EncodeAsync(
        string titleDirectory, Manifest manifest, string workDirectory, CancellationToken ct)
    {
        var top = manifest.Video.Renditions.FirstOrDefault(r => DirectoryOf(r) == Ladder.TopDirectory)
                  ?? manifest.Video.Renditions[0];
        var source = Path.Combine(titleDirectory, top.Uri);
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

        var owed = Owed(manifest).Select(r => r.Directory).ToHashSet(StringComparer.Ordinal);
        var ladder = VideoLadder.For(manifest.Video.Width, manifest.Video.Height, options)
            .Where(r => owed.Contains(r.Directory))
            .ToList();
        if (ladder.Count == 0) throw new ReencodeException($"{titleDirectory} is owed no rung.");

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
