using System.Buffers.Binary;

namespace TheKrystalShip.MovieBot.Ingest.Probe;

/// <summary>
/// OpenSubtitles' hash of a video file: its length added to every 64-bit little-endian word of its
/// first and last 64 KiB, wrapped at 64 bits.
///
/// It exists because it identifies a release without reading the whole file, and because subtitles
/// are uploaded against it. A subtitle found by this hash was timed against this exact encode,
/// which is the difference between one that fits and one that has to be nudged for two hours.
/// </summary>
public static class SourceHash
{
    private const int ChunkBytes = 64 * 1024;

    /// <summary>
    /// Both chunks have to exist and not overlap, so a file shorter than two of them has no hash.
    /// No film is anywhere near this small; a stray sample or a truncated download is.
    /// </summary>
    public const long MinimumSizeBytes = ChunkBytes * 2;

    /// <summary>
    /// Null when the file is missing or too short. Callers must not compute this over a file that
    /// is still downloading: the tail is reserved on disk before it arrives and reads back as
    /// zeroes, which produces a confident, wrong answer rather than a failure.
    /// </summary>
    public static string? Compute(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length < MinimumSizeBytes) return null;

        using var stream = File.OpenRead(path);
        var chunk = new byte[ChunkBytes];

        var hash = unchecked((ulong)file.Length);

        stream.ReadExactly(chunk);
        hash = unchecked(hash + Fold(chunk));

        stream.Seek(-ChunkBytes, SeekOrigin.End);
        stream.ReadExactly(chunk);
        hash = unchecked(hash + Fold(chunk));

        return hash.ToString("x16");
    }

    private static ulong Fold(ReadOnlySpan<byte> chunk)
    {
        ulong sum = 0;
        for (var i = 0; i + sizeof(ulong) <= chunk.Length; i += sizeof(ulong))
            sum = unchecked(sum + BinaryPrimitives.ReadUInt64LittleEndian(chunk[i..]));

        return sum;
    }
}
