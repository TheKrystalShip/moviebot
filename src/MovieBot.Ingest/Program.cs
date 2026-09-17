using System.Globalization;
using TheKrystalShip.MovieBot.Ingest.Ffmpeg;
using TheKrystalShip.MovieBot.Ingest.Pipeline;

namespace TheKrystalShip.MovieBot.Ingest;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        IngestOptions options;
        try
        {
            options = ParseArguments(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"moviebot-ingest: {ex.Message}");
            Console.Error.WriteLine("Try 'moviebot-ingest --help'.");
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.Error.WriteLine("\nStopping — the partial output stays on disk and can be re-run.");
            cancellation.Cancel();
        };

        var started = DateTimeOffset.UtcNow;
        var pipeline = new IngestPipeline(options, Console.WriteLine);

        try
        {
            var manifest = await pipeline.RunAsync(cancellation.Token);
            if (options.DryRun) return 0;

            var elapsed = DateTimeOffset.UtcNow - started;
            var ratio = manifest.DurationSeconds / Math.Max(elapsed.TotalSeconds, 0.001);
            Console.WriteLine($"Done in {elapsed:h\\:mm\\:ss} ({ratio:F1}x realtime).");
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
        catch (FfmpegException ex)
        {
            Console.Error.WriteLine($"moviebot-ingest: transcode failed. {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"moviebot-ingest: {ex.Message}");
            return 1;
        }
    }

    private static IngestOptions ParseArguments(string[] args)
    {
        string? source = null;
        var outputRoot = Path.Combine(Directory.GetCurrentDirectory(), "media");
        string? id = null;
        var bitrate = "9M";
        var stepDown = "4M";
        var cq = 19;
        var segment = 2;
        bool? toneMap = null;
        var force = false;
        var dryRun = false;
        var thumbnailsOnly = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            switch (arg)
            {
                case "-o" or "--out":
                    outputRoot = Path.GetFullPath(Next(args, ref i, arg));
                    break;
                case "--id":
                    id = Next(args, ref i, arg);
                    break;
                case "--bitrate":
                    bitrate = Next(args, ref i, arg);
                    break;
                case "--step-down":
                    stepDown = Next(args, ref i, arg);
                    break;
                case "--cq":
                    cq = int.Parse(Next(args, ref i, arg), CultureInfo.InvariantCulture);
                    break;
                case "--segment":
                    segment = int.Parse(Next(args, ref i, arg), CultureInfo.InvariantCulture);
                    break;
                case "--tonemap":
                    toneMap = Next(args, ref i, arg).ToLowerInvariant() switch
                    {
                        "on" or "true" or "yes" => true,
                        "off" or "false" or "no" => false,
                        "auto" => null,
                        var other => throw new ArgumentException($"--tonemap expects auto|on|off, got '{other}'.")
                    };
                    break;
                case "--force":
                    force = true;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--thumbnails":
                    thumbnailsOnly = true;
                    break;
                default:
                    if (arg.StartsWith('-'))
                        throw new ArgumentException($"unknown option '{arg}'.");
                    if (source is not null)
                        throw new ArgumentException("only one source file can be ingested at a time.");
                    source = Path.GetFullPath(arg);
                    break;
            }
        }

        if (source is null)
            throw new ArgumentException("no source file given.");

        return new IngestOptions
        {
            SourcePath = source,
            OutputRoot = outputRoot,
            Id = id,
            VideoBitrate = bitrate,
            StepDownBitrate = stepDown,
            Cq = cq,
            SegmentSeconds = segment,
            ForceToneMap = toneMap,
            Force = force,
            DryRun = dryRun,
            ThumbnailsOnly = thumbnailsOnly
        };
    }

    private static string Next(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"{option} expects a value.");
        return args[++index];
    }

    private static void PrintUsage() => Console.WriteLine(
        """
        moviebot-ingest — prepare a film for shared playback

        Usage:
          moviebot-ingest <source-file> [options]

        Extracts every text subtitle track and the embedded cover art, then transcodes to HLS.
        The playlists are EVENT type, so the film becomes playable within seconds while the
        transcode continues; the manifest carries how far the head has reached.

        Two rungs are written: the source's own size, and a 1280-wide step-down a viewer whose
        connection dips can fall back to. A source no wider than the step-down gets one rung.

        Options:
          -o, --out <dir>        Output root. Default: ./media
              --id <slug>        Output directory and manifest id. Default: derived from the title
              --bitrate <rate>   Top rung bitrate, e.g. 9M or 4500k. Default: 9M
              --step-down <rate> Second rung bitrate, at 1280 wide. 0 writes one rung. Default: 4M
              --cq <n>           NVENC constant quality, lower is better. Default: 19
              --segment <sec>    Segment length; the GOP is pinned to match. Default: 2
              --tonemap <mode>   auto|on|off. Default: auto, from the source transfer function
              --force            Replace an existing output directory
              --dry-run          Probe and print the manifest, transcode nothing
              --thumbnails       Rebuild the scrub previews for a title already ingested
          -h, --help             Show this help

        Requires ffmpeg and ffprobe on PATH, with h264_nvenc for encoding and, for HDR sources,
        an OpenCL device for tone mapping.
        """);
}
