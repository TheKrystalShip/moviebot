namespace TheKrystalShip.MovieBot.Core;

/// <summary>How far one rung of a transcoding title has reached, named by the height the player knows it by.</summary>
public sealed record RungHead
{
    public required int Height { get; init; }
    public required double Seconds { get; init; }
}

/// <summary>
/// What a manifest says about how far each part of a transcoding title has got, read the one way
/// everything that asks agrees on.
///
/// The overall head is the shortest playlist, and it is what a title with no per-rung record is
/// judged by. A title that records its rungs separately is playable as far as the best rung that
/// has audio under it, because a viewer plays one rung and a lagging rung is nobody's problem
/// until somebody is on it.
/// </summary>
public static class TranscodeHeads
{
    /// <summary>
    /// Each rung's head, or null when the title is not transcoding or its manifest does not track
    /// rungs separately. A rung with no record of its own is at the overall head.
    /// </summary>
    public static List<RungHead>? RungsOf(Manifest manifest)
    {
        if (manifest.Status != TitleStatus.Transcoding) return null;
        if (!manifest.Video.Renditions.Any(r => r.HeadSeconds is not null)) return null;

        var overall = manifest.HeadSeconds ?? 0;
        return
        [
            .. manifest.Video.Renditions.Select(r => new RungHead
            {
                Height = r.SizeIn(manifest.Video).Height,
                Seconds = r.HeadSeconds ?? overall
            })
        ];
    }

    /// <summary>The audio head, or null when the title is not transcoding or tracks none separately.</summary>
    public static double? AudioOf(Manifest manifest) =>
        manifest.Status == TitleStatus.Transcoding ? manifest.AudioHeadSeconds : null;

    /// <summary>
    /// How far a seek may go: the head of the best rung, held back to the audio head. Null when the
    /// title is complete and the whole film is seekable. A title with no audio has nothing to hold
    /// it back, and one whose audio is not tracked separately is held to the overall head.
    /// </summary>
    public static double? PlayableOf(Manifest manifest)
    {
        if (manifest.Status != TitleStatus.Transcoding || manifest.HeadSeconds is not { } overall) return null;

        var best = RungsOf(manifest) is { } rungs ? rungs.Max(r => r.Seconds) : overall;
        var audio = AudioOf(manifest) ?? (manifest.Audio.Count == 0 ? best : overall);
        return Math.Min(best, audio);
    }
}
