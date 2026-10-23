using System.Buffers.Binary;

namespace TheKrystalShip.MovieBot.Ingest.Ffmpeg;

/// <summary>
/// The few fields of a fragmented MP4 segment and its initialisation file that a resumed transcode
/// reads and rewrites: where a fragment starts on its track's clock, its place in the sequence, the
/// track's clock and how its samples are described.
///
/// A resumed ffmpeg starts its own clock at zero and its own sequence at one, and puts where it
/// started in the edit list of an initialisation file of its own. Players keep the first
/// initialisation file they were given, so the resumed fragments are moved onto the clock and into the
/// sequence of the transcode they continue instead (<see cref="Shift"/>): the same samples at the
/// times the uninterrupted transcode would have given them.
/// </summary>
public static class Fmp4
{
    /// <summary>Where a fragment's first sample is decoded, on its track's clock, and its sequence number.</summary>
    public readonly record struct FragmentStart(ulong DecodeTime, uint Sequence);

    /// <summary>The start of the first fragment in a segment.</summary>
    /// <exception cref="InvalidDataException">The file holds no fragment.</exception>
    public static FragmentStart ReadStart(string segmentPath)
    {
        var bytes = File.ReadAllBytes(segmentPath);
        ulong? decodeTime = null;
        uint? sequence = null;
        foreach (var (type, offset) in FragmentFields(bytes))
        {
            if (type == "tfdt" && decodeTime is null) decodeTime = ReadDecodeTime(bytes, offset);
            if (type == "mfhd" && sequence is null) sequence = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 12));
        }
        return decodeTime is { } time && sequence is { } number
            ? new FragmentStart(time, number)
            : throw new InvalidDataException($"{segmentPath} holds no fragment.");
    }

    /// <summary>
    /// Moves every fragment in a segment by <paramref name="decodeTime"/> ticks on its track's clock and
    /// <paramref name="sequence"/> places in the sequence, in place. The fields keep their sizes, so
    /// nothing else in the file moves.
    /// </summary>
    public static void Shift(string segmentPath, long decodeTime, int sequence)
    {
        var bytes = File.ReadAllBytes(segmentPath);
        foreach (var (type, offset) in FragmentFields(bytes))
        {
            if (type == "tfdt")
            {
                var moved = checked((long)ReadDecodeTime(bytes, offset) + decodeTime);
                if (bytes[offset + 8] == 1) BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(offset + 12), checked((ulong)moved));
                else BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset + 12), checked((uint)moved));
            }
            else if (type == "mfhd")
            {
                var number = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 12));
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset + 12), checked((uint)(number + sequence)));
            }
        }
        File.WriteAllBytes(segmentPath, bytes);
    }

    /// <summary>
    /// The track's sample description (its <c>stsd</c> box: the codec, its parameters and the picture
    /// or channel layout) and its clock, from an initialisation file. Two initialisation files with
    /// the same description and clock decode each other's segments.
    /// </summary>
    public static (byte[] Description, uint Timescale) ReadTrack(string initPath)
    {
        var bytes = File.ReadAllBytes(initPath);
        byte[]? description = null;
        uint? timescale = null;
        Walk(bytes, 0, bytes.Length, ["moov", "trak", "mdia", "minf", "stbl"], (type, offset, size) =>
        {
            if (type == "stsd" && description is null) description = bytes.AsSpan(offset, size).ToArray();
            if (type == "mdhd" && timescale is null)
                timescale = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + (bytes[offset + 8] == 1 ? 28 : 20)));
        });
        return description is not null && timescale is { } clock
            ? (description, clock)
            : throw new InvalidDataException($"{initPath} describes no track.");
    }

    private static ulong ReadDecodeTime(byte[] bytes, int offset) =>
        bytes[offset + 8] == 1
            ? BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(offset + 12))
            : BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 12));

    /// <summary>Every <c>tfdt</c> and <c>mfhd</c> box in a segment's fragments, by type and offset.</summary>
    private static List<(string Type, int Offset)> FragmentFields(byte[] bytes)
    {
        var found = new List<(string, int)>();
        Walk(bytes, 0, bytes.Length, ["moof", "traf"], (type, offset, _) =>
        {
            if (type is "tfdt" or "mfhd") found.Add((type, offset));
        });
        return found;
    }

    /// <summary>Visits every box from <paramref name="start"/> to <paramref name="end"/>, descending into the containers named.</summary>
    private static void Walk(byte[] bytes, int start, int end, string[] containers, Action<string, int, int> visit)
    {
        var offset = start;
        while (offset + 8 <= end)
        {
            var size = (long)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset));
            var header = 8;
            if (size == 1)
            {
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(offset + 8));
                header = 16;
            }
            else if (size == 0) size = end - offset;
            if (size < header || offset + size > end) return;

            var type = System.Text.Encoding.ASCII.GetString(bytes, offset + 4, 4);
            visit(type, offset, (int)size);
            if (containers.Contains(type)) Walk(bytes, offset + header, offset + (int)size, containers, visit);
            offset += (int)size;
        }
    }
}
