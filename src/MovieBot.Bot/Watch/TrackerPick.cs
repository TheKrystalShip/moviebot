using System.Globalization;

namespace TheKrystalShip.MovieBot.Bot.Watch;

/// <summary>
/// The value behind a tracker row in the title menu, which is what the surface sends back when
/// one is picked.
///
/// The menu offers two kinds of row through one option: a film already in the library, whose
/// value is its library id, and a release on the tracker, whose value is its torrent id. The two
/// have to be told apart from the value alone, because the value is all that comes back — and a
/// library id is a slug that can be entirely digits, so a bare number is not enough. The prefix
/// is what makes a torrent id unmistakable.
/// </summary>
public static class TrackerPick
{
    private const string Prefix = "torrent:";

    public static string Value(long torrentId) =>
        Prefix + torrentId.ToString(CultureInfo.InvariantCulture);

    /// <summary>The torrent id a value stands for, or null when it is anything else.</summary>
    public static long? Parse(string value) =>
        value.StartsWith(Prefix, StringComparison.Ordinal)
        && long.TryParse(value[Prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
}
