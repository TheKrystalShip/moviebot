using System.Globalization;

namespace TheKrystalShip.MovieBot.Ingest.Pipeline;

/// <summary>
/// The second mix of a feature track, made for hearing the dialogue over everything else.
///
/// A film is mixed for a cinema: dialogue sits in the centre channel at a level set against
/// explosions and a score played through a room of speakers. Folded down to stereo by the standard
/// matrix, the surrounds arrive at the same level as the voices, and on laptop speakers or
/// headphones at a sane volume the talking is twenty decibels under the loudest scenes. The centre
/// channel exists as its own signal only until the downmix, so this is the one place the dialogue
/// can be raised by itself rather than by squashing the whole mix.
///
/// Three stages. The downmix weights the centre above the fronts and the surrounds below both; a
/// compressor pulls the peaks down toward the dialogue; <c>loudnorm</c> rides the level toward a
/// fixed loudness with a true-peak ceiling, which is what makes one film as loud as the next.
/// Measured on a 5.1 feature, the gap between the dialogue and the loudest ten seconds goes from
/// 20 dB to 9 and nothing clips.
///
/// <c>loudnorm</c> oversamples to 192 kHz to find true peaks, and that is nearly all of the cost:
/// the chain encodes at 8x realtime on hotbox's CPU against 27x without it. Cheaper riders were
/// measured against it and each lands on a different sound. The mix is therefore made after the
/// main pass rather than inside it, where it would be the slowest rendition and hold back the
/// head every viewer is waiting on.
/// </summary>
internal static class DialogueBoost
{
    public const string LabelSuffix = " — Dialogue boost";

    private const double Centre = 0.9;
    private const double Front = 0.6;
    private const double Surround = 0.45;
    private const double Lfe = 0.15;

    private const string Dynamics =
        "acompressor=threshold=-24dB:ratio=3:attack=15:release=250:knee=6,"
        + "loudnorm=I=-18:LRA=9:TP=-2,"
        // loudnorm hands back 192 kHz whatever it was given.
        + "aresample=48000";

    /// <summary>
    /// The channels of each layout ffprobe names, as ffmpeg orders them. A layout missing here is
    /// one nothing is known about, and it gets the standard downmix rather than a guessed one.
    /// </summary>
    private static readonly Dictionary<string, string[]> Layouts = new(StringComparer.Ordinal)
    {
        ["3.0"] = ["FL", "FR", "FC"],
        ["3.1"] = ["FL", "FR", "FC", "LFE"],
        ["4.0"] = ["FL", "FR", "FC", "BC"],
        ["4.1"] = ["FL", "FR", "FC", "LFE", "BC"],
        ["5.0"] = ["FL", "FR", "FC", "BL", "BR"],
        ["5.0(side)"] = ["FL", "FR", "FC", "SL", "SR"],
        ["5.1"] = ["FL", "FR", "FC", "LFE", "BL", "BR"],
        ["5.1(side)"] = ["FL", "FR", "FC", "LFE", "SL", "SR"],
        ["6.0"] = ["FL", "FR", "FC", "BC", "SL", "SR"],
        ["hexagonal"] = ["FL", "FR", "FC", "BL", "BR", "BC"],
        ["6.1"] = ["FL", "FR", "FC", "LFE", "BC", "SL", "SR"],
        ["6.1(back)"] = ["FL", "FR", "FC", "LFE", "BL", "BR", "BC"],
        ["7.0"] = ["FL", "FR", "FC", "BL", "BR", "SL", "SR"],
        ["7.0(front)"] = ["FL", "FR", "FC", "FLC", "FRC", "SL", "SR"],
        ["7.1"] = ["FL", "FR", "FC", "LFE", "BL", "BR", "SL", "SR"],
        ["7.1(wide)"] = ["FL", "FR", "FC", "LFE", "BL", "BR", "FLC", "FRC"],
        ["7.1(wide-side)"] = ["FL", "FR", "FC", "LFE", "FLC", "FRC", "SL", "SR"]
    };

    /// <summary>The whole filter chain for a track of the given layout, ending in stereo.</summary>
    public static string FilterFor(string? channelLayout) =>
        $"{Downmix(channelLayout) ?? StandardDownmix},{Dynamics}";

    /// <summary>
    /// A source with no centre channel has no dialogue to single out, so it is folded down the
    /// ordinary way and gets the dynamics alone — which still lifts the quiet passages, voices
    /// among them, toward the loud ones.
    /// </summary>
    private const string StandardDownmix = "aformat=channel_layouts=stereo";

    /// <summary>
    /// The dialogue-weighted downmix, or null when the layout carries no centre to weight.
    ///
    /// Each role's weight is shared across the speakers that fill it at equal power, so a 7.1
    /// source with side and back surrounds sends as much surround into the mix as a 5.1 does with
    /// one pair, and the balance against the dialogue holds whatever the layout.
    /// </summary>
    internal static string? Downmix(string? channelLayout)
    {
        if (channelLayout is null || !Layouts.TryGetValue(channelLayout, out var channels))
            return null;

        var present = channels.ToHashSet(StringComparer.Ordinal);
        if (!present.Contains("FC")) return null;

        return $"pan=stereo|FL={Side(present, "FL", "FLC", "SL", "BL")}"
               + $"|FR={Side(present, "FR", "FRC", "SR", "BR")}";
    }

    private static string Side(
        HashSet<string> present, string front, string frontOfCentre, string side, string back)
    {
        var terms = new List<string> { Term(Centre, "FC") };

        string[] fronts = [.. new[] { front, frontOfCentre }.Where(present.Contains)];
        foreach (var channel in fronts)
            terms.Add(Term(Front / Math.Sqrt(fronts.Length), channel));

        // A back centre belongs to both sides, so it is one of the surrounds on each.
        string[] surrounds = [.. new[] { side, back, "BC" }.Where(present.Contains)];
        foreach (var channel in surrounds)
            terms.Add(Term(Surround / Math.Sqrt(surrounds.Length), channel));

        if (present.Contains("LFE")) terms.Add(Term(Lfe, "LFE"));

        return string.Join('+', terms);
    }

    private static string Term(double weight, string channel) =>
        $"{weight.ToString("0.###", CultureInfo.InvariantCulture)}*{channel}";
}
