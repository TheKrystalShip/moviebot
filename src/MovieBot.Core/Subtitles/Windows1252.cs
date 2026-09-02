using System.Text;

namespace TheKrystalShip.MovieBot.Core.Subtitles;

/// <summary>
/// The encoding subtitles keep arriving in, and the one they keep being mangled through.
///
/// Latin-1 and Windows-1252 agree everywhere except the block from 0x80 to 0x9F, so everything
/// outside that block is simply its own byte. The block is the whole difference between them and
/// the reason this corruption is recognisable at all.
/// </summary>
public static class Windows1252
{
    /// <summary>
    /// The characters Windows-1252 puts in 0x80-0x9F, which is the whole difference between it and
    /// Latin-1 and the reason this corruption is recognisable at all.
    /// </summary>
    private static readonly (char Char, byte Byte)[] High =
    [
        ('€', 0x80), ('‚', 0x82), ('ƒ', 0x83), ('„', 0x84),
        ('…', 0x85), ('†', 0x86), ('‡', 0x87), ('ˆ', 0x88),
        ('‰', 0x89), ('Š', 0x8A), ('‹', 0x8B), ('Œ', 0x8C),
        ('Ž', 0x8E), ('‘', 0x91), ('’', 0x92), ('“', 0x93),
        ('”', 0x94), ('•', 0x95), ('–', 0x96), ('—', 0x97),
        ('˜', 0x98), ('™', 0x99), ('š', 0x9A), ('›', 0x9B),
        ('œ', 0x9C), ('ž', 0x9E), ('Ÿ', 0x9F),
    ];


    /// <summary>
    /// The byte this character came from, or false when it cannot have come from this encoding.
    ///
    /// The five bytes with no character of their own map through as controls, which is what a
    /// Latin-1 reader would have produced from them. Accepting both is what lets one reversal undo
    /// mangling through either encoding.
    /// </summary>
    public static bool TryEncode(char c, out byte b)
    {
        if (c <= '\u00ff')
        {
            b = (byte)c;
            return true;
        }

        foreach (var (mapped, value) in High)
        {
            if (mapped == c)
            {
                b = value;
                return true;
            }
        }

        b = 0;
        return false;
    }

    /// <summary>
    /// Reads bytes as Windows-1252, which cannot fail: every byte has a character, and the five
    /// with none of their own come back as the controls a Latin-1 read would have given.
    /// </summary>
    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        var text = new StringBuilder(bytes.Length);

        foreach (var b in bytes)
        {
            if (b < 0x80 || b > 0x9F)
            {
                text.Append((char)b);
                continue;
            }

            var mapped = (char)b;
            foreach (var (character, value) in High)
            {
                if (value != b) continue;
                mapped = character;
                break;
            }

            text.Append(mapped);
        }

        return text.ToString();
    }
}
