using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Bot.Configuration;

namespace TheKrystalShip.MovieBot.Bot.Notify;

/// <summary>How films people are waiting on are watched for.</summary>
public sealed class NotifyOptions
{
    public const string Section = "Notify";

    /// <summary>
    /// Where the wish list is written. Empty means the state directory systemd hands the service,
    /// and the working directory when there is none.
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// How often the tracker is asked about every film on the list, in minutes. A film that was
    /// not out this morning is not out this minute, and the tracker caps how often a member may
    /// call it; an hour answers within the day a film appears without spending the cap on it.
    /// </summary>
    public int SweepMinutes { get; set; } = 60;

    /// <summary>
    /// The least a release's source may be for the film to count as available.
    ///
    /// A film in cinemas is on the tracker within days as a camcorder recording, and the ranker
    /// only weighs those down rather than dropping them. What somebody waiting on a film means by
    /// available is its digital release, and a web encode is the first form that takes.
    /// </summary>
    public Source MinimumSource { get; set; } = Source.Web;

    public string ResolvePath() =>
        StatePath.Resolve(Path, "wishes.json");
}
