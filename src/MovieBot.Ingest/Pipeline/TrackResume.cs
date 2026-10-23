using System.Globalization;
using TheKrystalShip.MovieBot.Ingest.Ffmpeg;

namespace TheKrystalShip.MovieBot.Ingest.Pipeline;

/// <summary>
/// One track of a transcode that stopped part way (a video rung or an audio rendition), and how it
/// carries on from where its playlist ends rather than from the start.
///
/// The playlist is rewound by at least <see cref="RewindSeconds"/>, whole segments from its end, so a
/// segment the stopped process listed but had not finished putting on disk is written again rather
/// than trusted. The first segment dropped is still read before it goes: its start, on the track's own
/// clock, is where the new process begins, and is the time its first fragment is given. The new
/// process writes its own playlist (<see cref="ResumePlaylist"/>) and its own initialisation file, which
/// nothing reads; each segment it lists is moved onto the stopped transcode's clock and sequence
/// (<see cref="Fmp4.Shift"/>) before <see cref="Publish"/> adds it to the playlist players read, which
/// keeps the first initialisation file it ever named. Every video segment opens on the same frame, at
/// the same time and as the same keyframe as the uninterrupted transcode's, so the rungs stay aligned.
/// </summary>
public sealed class TrackResume
{
    /// <summary>How much of the end of a stopped track is written again, at least.</summary>
    public const double RewindSeconds = 10;

    public const string ResumePlaylist = "resume.m3u8";

    /// <summary>
    /// How far short of the film's running time an ended playlist may fall and still be the whole track:
    /// two video segments, the most the last one and a rounded running time can differ by.
    /// </summary>
    public const double WholeWithinSeconds = 4;

    private readonly string _directory;
    private readonly string _prefix;
    private readonly List<string> _header;
    private readonly List<(string Info, string Uri)> _kept;
    private readonly HashSet<string> _published = new(StringComparer.Ordinal);
    private (long DecodeTime, int Sequence)? _shift;

    private TrackResume(string directory, string prefix, List<string> header, List<(string, string)> kept,
        bool complete, Fmp4.FragmentStart? next, double sourceSeconds)
    {
        _directory = directory;
        _prefix = prefix;
        _header = header;
        _kept = kept;
        Complete = complete;
        Next = next;
        SourceSeconds = sourceSeconds;
    }

    /// <summary>The track's directory under the title, <c>v0</c> or <c>a1</c>.</summary>
    public string Name => Path.GetFileName(_directory);

    /// <summary>Whether the stopped process finished this track: its playlist is ended, and nothing is owed.</summary>
    public bool Complete { get; }

    /// <summary>How many segments are kept, which is the number the resumed process's first one takes.</summary>
    public int Segments => _kept.Count;

    /// <summary>The kept segments' running time, by the playlist.</summary>
    public double KeptSeconds => _kept.Sum(e => Duration(e.Info));

    /// <summary>Where the first segment written again starts on the track's clock; null when nothing is kept.</summary>
    public Fmp4.FragmentStart? Next { get; }

    /// <summary>Where in the source the resumed process starts reading: the first segment written again.</summary>
    public double SourceSeconds { get; }

    /// <summary>The name the resumed process gives its own initialisation file, which nothing reads.</summary>
    public string ResumeInit => $"{_prefix}resume-init.mp4";

    /// <summary>
    /// Reads a stopped track for resuming, or null when it cannot be: no playlist, a segment it lists
    /// missing, or no more in it than the rewind, which is no head start worth the risk.
    ///
    /// A track is finished when its playlist is ended and runs the film's length,
    /// <paramref name="durationSeconds"/>. An ended playlist that falls short is a stopped one: ffmpeg
    /// ends its playlist when it is asked to stop rather than killed, wherever it has got to.
    /// </summary>
    public static TrackResume? Read(string directory, string prefix, double durationSeconds)
    {
        var playlist = Path.Combine(directory, "index.m3u8");
        if (!File.Exists(playlist)) return null;

        var (header, entries, ended) = Parse(File.ReadAllLines(playlist));
        if (entries.Count == 0 || entries.Any(e => !File.Exists(Path.Combine(directory, e.Uri)))) return null;
        if (ended && entries.Sum(e => Duration(e.Info)) >= durationSeconds - WholeWithinSeconds)
            return new TrackResume(directory, prefix, header, entries, complete: true, next: null, sourceSeconds: 0);

        // Whole segments from the end, until at least the rewind is gone.
        var kept = entries.ToList();
        var dropped = 0d;
        while (kept.Count > 0 && dropped < RewindSeconds)
        {
            dropped += Duration(kept[^1].Info);
            kept.RemoveAt(kept.Count - 1);
        }
        if (kept.Count == 0) return null;

        var first = Fmp4.ReadStart(Path.Combine(directory, entries[0].Uri));
        var next = Fmp4.ReadStart(Path.Combine(directory, entries[kept.Count].Uri));
        var (_, timescale) = Fmp4.ReadTrack(Path.Combine(directory, InitOf(header) ?? $"{prefix}init.mp4"));
        var sourceSeconds = (next.DecodeTime - first.DecodeTime) / (double)timescale;
        return new TrackResume(directory, prefix, header, kept, complete: false, next, sourceSeconds);
    }

    /// <summary>
    /// Rewinds the track on disk: the playlist to its kept segments, and every other segment, and
    /// whatever an earlier resume left, removed. Done before the resumed process starts.
    /// </summary>
    public void Apply()
    {
        WritePlaylist(_kept, ended: false);
        var kept = _kept.Select(e => e.Uri).ToHashSet(StringComparer.Ordinal);
        foreach (var segment in Directory.EnumerateFiles(_directory, "*.m4s"))
            if (!kept.Contains(Path.GetFileName(segment))) File.Delete(segment);
        File.Delete(Path.Combine(_directory, ResumePlaylist));
        File.Delete(Path.Combine(_directory, ResumeInit));
    }

    /// <summary>
    /// Adds what the resumed process has finished to the playlist players read: each segment moved
    /// onto the stopped transcode's clock first, once, and then listed. The end is listed once the
    /// resumed process ends its own playlist.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The resumed process describes its samples differently from the stopped one, so the kept
    /// initialisation file cannot decode what it writes.
    /// </exception>
    public void Publish()
    {
        var path = Path.Combine(_directory, ResumePlaylist);
        if (!File.Exists(path) || Next is not { } next) return;

        var (_, written, ended) = Parse(File.ReadAllLines(path));
        if (written.Count == 0) return;

        if (_shift is null)
        {
            var kept = Fmp4.ReadTrack(Path.Combine(_directory, InitOf(_header) ?? $"{_prefix}init.mp4"));
            var resumed = Fmp4.ReadTrack(Path.Combine(_directory, ResumeInit));
            if (kept.Timescale != resumed.Timescale || !kept.Description.AsSpan().SequenceEqual(resumed.Description))
                throw new InvalidDataException($"{Name} is encoded differently from what it continues, so it cannot be continued.");

            var start = Fmp4.ReadStart(Path.Combine(_directory, written[0].Uri));
            _shift = (checked((long)next.DecodeTime - (long)start.DecodeTime), checked((int)next.Sequence - (int)start.Sequence));
        }

        foreach (var (_, uri) in written)
            if (_published.Add(uri)) Fmp4.Shift(Path.Combine(_directory, uri), _shift.Value.DecodeTime, _shift.Value.Sequence);

        WritePlaylist([.. _kept, .. written], ended);
    }

    /// <summary>The last publish, once the resumed process has ended, and its own files gone.</summary>
    public void Finish()
    {
        Publish();
        File.Delete(Path.Combine(_directory, ResumePlaylist));
        File.Delete(Path.Combine(_directory, ResumeInit));
    }

    private void WritePlaylist(IReadOnlyList<(string Info, string Uri)> entries, bool ended)
    {
        var target = Math.Max(
            _header.Where(l => l.StartsWith("#EXT-X-TARGETDURATION:", StringComparison.Ordinal))
                .Select(l => int.Parse(l["#EXT-X-TARGETDURATION:".Length..], CultureInfo.InvariantCulture))
                .DefaultIfEmpty(0).Max(),
            entries.Select(e => (int)Math.Round(Duration(e.Info))).DefaultIfEmpty(0).Max());
        var lines = _header
            .Select(l => l.StartsWith("#EXT-X-TARGETDURATION:", StringComparison.Ordinal) ? $"#EXT-X-TARGETDURATION:{target}" : l)
            .Concat(entries.SelectMany(e => new[] { e.Info, e.Uri }));
        if (ended) lines = lines.Append("#EXT-X-ENDLIST");

        var path = Path.Combine(_directory, "index.m3u8");
        File.WriteAllText(path + ".tmp", string.Join('\n', lines) + "\n");
        File.Move(path + ".tmp", path, overwrite: true);
    }

    private static (List<string> Header, List<(string Info, string Uri)> Entries, bool Ended) Parse(string[] lines)
    {
        var header = new List<string>();
        var entries = new List<(string, string)>();
        var ended = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith("#EXTINF:", StringComparison.Ordinal))
            {
                // A segment ffmpeg has begun to list but not named yet is not a segment.
                if (i + 1 < lines.Length && lines[i + 1].Length > 0 && !lines[i + 1].StartsWith('#'))
                    entries.Add((line, lines[++i]));
            }
            else if (line == "#EXT-X-ENDLIST") ended = true;
            else if (entries.Count == 0 && line.Length > 0) header.Add(line);
        }
        return (header, entries, ended);
    }

    private static string? InitOf(IEnumerable<string> header) =>
        header.Where(l => l.StartsWith("#EXT-X-MAP:URI=\"", StringComparison.Ordinal))
            .Select(l => l["#EXT-X-MAP:URI=\"".Length..].Split('"')[0])
            .FirstOrDefault();

    private static double Duration(string info) =>
        double.TryParse(info["#EXTINF:".Length..].Split(',')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? seconds : 0;
}
