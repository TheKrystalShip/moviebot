using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TheKrystalShip.MovieBot.Api.Offline;

/// <summary>
/// One rendition as a single file: its init segment and then every segment its playlist lists, in
/// order, each file's bytes as they are on disk.
/// </summary>
/// <param name="Files">The init segment first, then the segments, by name within the rendition's directory.</param>
/// <param name="Sizes">Each of <paramref name="Files"/>' length.</param>
/// <param name="Offsets">Where each of <paramref name="Files"/> begins in the track.</param>
/// <param name="Seconds">Each segment's duration; one fewer than the files, the init segment having none.</param>
/// <param name="Ended">
/// Whether the playlist ends. A rendition still being encoded is a track that grows: its files are the
/// ones listed so far, and the encoder only ever adds to them.
/// </param>
public sealed record TrackIndex(
    string Rendition, IReadOnlyList<string> Files, long[] Sizes, long[] Offsets, double[] Seconds, long Length, string ETag,
    bool Ended = true)
{
    public long InitBytes => Sizes[0];

    /// <summary>The segments' lengths, in order.</summary>
    public long[] SegmentBytes => Sizes[1..];

    /// <summary>The file holding byte <paramref name="position"/> of the track.</summary>
    public int FileAt(long position)
    {
        var at = Array.BinarySearch(Offsets, position);
        return at >= 0 ? at : ~at - 1;
    }

    /// <summary>
    /// Whether <paramref name="validator"/>, an <see cref="ETag"/> this track or an earlier stretch of it
    /// answered, names bytes this track still begins with: its first files under the same names and
    /// lengths. A segment is never rewritten under its own name, so those are the same bytes, and a
    /// device holding a stretch of a growing track resumes it as the track grows.
    /// </summary>
    public bool Continues(string? validator)
    {
        if (string.IsNullOrEmpty(validator)) return false;
        if (validator == ETag) return true;

        var body = validator.Trim('"');
        var dash = body.LastIndexOf('-');
        if (dash < 0)
            // A validator naming no count names every file, as a finished track's always did.
            return validator == $"\"{Hash(Files, Sizes, Files.Count)}\"";
        return int.TryParse(body[(dash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            && count > 0 && count <= Files.Count
            && body[..dash] == Hash(Files, Sizes, count);
    }

    /// <summary>
    /// Reads a rendition's playlist and measures its files. Null when the playlist is missing, names a
    /// file outside its directory or one that is not there, or does not end and <paramref name="growing"/>
    /// is false: a finished title's track is only ever a whole rendition. A growing track needs its init
    /// segment and nothing more.
    /// </summary>
    public static TrackIndex? Read(string renditionDirectory, string rendition, string playlist, bool growing = false)
    {
        string? init = null;
        var names = new List<string>();
        var seconds = new List<double>();
        double? pending = null;
        var ended = false;

        foreach (var raw in playlist.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("#EXT-X-MAP:", StringComparison.Ordinal))
            {
                init = QuotedUri(line);
                if (init is null) return null;
            }
            else if (line.StartsWith("#EXTINF:", StringComparison.Ordinal))
            {
                var value = line["#EXTINF:".Length..];
                var comma = value.IndexOf(',');
                if (!double.TryParse(comma < 0 ? value : value[..comma], NumberStyles.Float, CultureInfo.InvariantCulture,
                        out var duration))
                    return null;
                pending = duration;
            }
            else if (line == "#EXT-X-ENDLIST")
            {
                ended = true;
            }
            else if (line[0] != '#')
            {
                if (pending is not { } duration) return null;
                names.Add(line);
                seconds.Add(duration);
                pending = null;
            }
        }

        if (init is null) return null;
        if (ended ? names.Count == 0 : !growing) return null;

        var files = new List<string>(names.Count + 1) { init };
        files.AddRange(names);
        if (files.Any(f => !IsPlainName(f))) return null;

        var sizes = new long[files.Count];
        var offsets = new long[files.Count];
        long length = 0;
        for (var i = 0; i < files.Count; i++)
        {
            var info = new FileInfo(Path.Combine(renditionDirectory, files[i]));
            if (!info.Exists) return null;
            sizes[i] = info.Length;
            offsets[i] = length;
            length += info.Length;
        }

        return new TrackIndex(rendition, files, sizes, offsets, [.. seconds], length,
            $"\"{Hash(files, sizes, files.Count)}-{files.Count.ToString(CultureInfo.InvariantCulture)}\"", ended);
    }

    /// <summary>
    /// What a strong validator is made of: the name and length of each of the first
    /// <paramref name="count"/> files, which the validator also carries. A segment is never rewritten
    /// under its own name, so the same names and lengths are the same bytes, and a rendition encoded
    /// again writes its segments under other names.
    /// </summary>
    private static string Hash(IReadOnlyList<string> files, long[] sizes, int count)
    {
        var text = new StringBuilder();
        for (var i = 0; i < count; i++)
            text.Append(files[i]).Append(':').Append(sizes[i].ToString(CultureInfo.InvariantCulture)).Append('\n');
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
        return Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }

    private static string? QuotedUri(string line)
    {
        const string key = "URI=\"";
        var start = line.IndexOf(key, StringComparison.Ordinal);
        if (start < 0) return null;
        start += key.Length;
        var end = line.IndexOf('"', start);
        return end <= start ? null : line[start..end];
    }

    /// <summary>A file directly in the rendition's directory: no separator, no parent, no query.</summary>
    private static bool IsPlainName(string name) =>
        name.Length > 0 && name != "." && name != ".."
        && name.IndexOfAny(['/', '\\', '?', '#']) < 0;
}

/// <summary>
/// The tracks already measured, kept while their playlists are unchanged. Measuring one is a stat of
/// every segment, thousands of files on a disk that may be a platter, and a resumed transfer asks for
/// the same track again; a rendition encoded again rewrites its playlist, which is the key.
/// </summary>
public sealed class TrackIndexes
{
    /// <summary>Enough for every track of the titles being saved at once, and small enough to never matter.</summary>
    private const int Kept = 256;

    private sealed record Entry(DateTime PlaylistWrite, long PlaylistLength, TrackIndex Index);

    private readonly ConcurrentDictionary<string, Entry> _kept = new(StringComparer.Ordinal);

    /// <summary>
    /// The track whose playlist is <paramref name="uri"/> under a title's directory, as the manifest
    /// names it (<c>v1/index.m3u8</c>): the rendition is the directory, which holds every file the
    /// playlist lists. Null when it is not a whole rendition, or with <paramref name="growing"/> one
    /// with its init segment written.
    /// </summary>
    public TrackIndex? Of(string titleDirectory, string uri, bool growing = false)
    {
        var slash = uri.IndexOf('/');
        if (slash <= 0 || uri.IndexOf('/', slash + 1) >= 0) return null;
        var rendition = uri[..slash];
        var directory = Path.Combine(titleDirectory, rendition);
        var playlist = new FileInfo(Path.Combine(directory, uri[(slash + 1)..]));
        if (!playlist.Exists) return null;

        if (_kept.TryGetValue(playlist.FullName, out var known)
            && known.PlaylistWrite == playlist.LastWriteTimeUtc && known.PlaylistLength == playlist.Length)
            return known.Index.Ended || growing ? known.Index : null;

        TrackIndex? index;
        try
        {
            index = TrackIndex.Read(directory, rendition, File.ReadAllText(playlist.FullName), growing);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (index is null) return null;
        if (_kept.Count >= Kept) _kept.Clear();
        _kept[playlist.FullName] = new Entry(playlist.LastWriteTimeUtc, playlist.Length, index);
        return index;
    }
}

/// <summary>What a request's <c>Range</c> header asks of a body of a known length.</summary>
public readonly record struct ByteRange(long Start, long End)
{
    public long Length => End - Start + 1;

    /// <summary>
    /// The one range a request asks for, or null for the whole body: no header, one that is not a
    /// byte range, or several ranges, which a server may answer whole. <paramref name="satisfiable"/>
    /// is false for a range that starts past the end, which is a 416.
    /// </summary>
    public static ByteRange? Parse(string? header, long length, out bool satisfiable)
    {
        satisfiable = true;
        if (string.IsNullOrWhiteSpace(header)) return null;

        var value = header.Trim();
        if (!value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return null;
        value = value["bytes=".Length..].Trim();
        if (value.Contains(',')) return null;

        var dash = value.IndexOf('-');
        if (dash < 0) return null;
        var first = value[..dash].Trim();
        var last = value[(dash + 1)..].Trim();

        if (first.Length == 0)
        {
            // The last N bytes.
            if (!long.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out var suffix)) return null;
            if (suffix == 0 || length == 0)
            {
                satisfiable = false;
                return null;
            }
            return new ByteRange(Math.Max(0, length - suffix), length - 1);
        }

        if (!long.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out var start)) return null;
        long end;
        if (last.Length == 0) end = length - 1;
        else if (!long.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out end) || end < start) return null;

        if (start >= length)
        {
            satisfiable = false;
            return null;
        }
        return new ByteRange(start, Math.Min(end, length - 1));
    }
}
