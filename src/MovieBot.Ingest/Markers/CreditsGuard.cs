using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Ingest.Markers;

/// <summary>
/// Keeps the credits from taking the last lines of the scene before them.
///
/// The closing music often starts under the episode's last exchange, and the music is what two
/// episodes share, so the stretch can begin with somebody still talking. A line spoken at the head
/// of the credits moves their start to just after it, as many times as there are such lines; with
/// too little left they are dropped. A line further in is left alone: the stretch is audio every
/// episode has, so a voice there (a studio's spoken tag) is part of the credits and not the story.
/// </summary>
public static class CreditsGuard
{
    /// <summary>How far into the credits a spoken line is taken for the scene running on.</summary>
    public const double LeadSeconds = 10;

    /// <summary>How long after the last line of the scene the credits begin.</summary>
    public const double MarginSeconds = 0.5;

    /// <summary>
    /// The credits clear of the scene before them, or null when what is left is shorter than
    /// <see cref="CreditsFinder.MinSeconds"/>. Credits with no line at their head are returned as
    /// they are.
    /// </summary>
    public static Marker? Guard(Marker credits, IReadOnlyList<Cue> cues)
    {
        var start = credits.StartSeconds;
        foreach (var cue in cues.Where(c => IntroGuard.IsDialogue(c.Text)).OrderBy(c => c.StartSeconds))
        {
            if (cue.StartSeconds >= start + LeadSeconds) break;
            if (cue.EndSeconds <= start) continue;
            start = cue.EndSeconds + MarginSeconds;
        }

        if (start == credits.StartSeconds) return credits;
        return credits.EndSeconds - start >= CreditsFinder.MinSeconds
            ? credits with { StartSeconds = Math.Round(start, 3) }
            : null;
    }
}
