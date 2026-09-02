using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Bot.Notify;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A row on the tracker is not a film being available. A film in cinemas has camcorder
/// recordings on the tracker within days, and what somebody waiting means is the digital release.
/// </summary>
public sealed class ReleaseAvailabilityTests
{
    private static Release Offered(string name, Source source) => new()
    {
        TorrentId = name.GetHashCode(),
        ReleaseName = name,
        Title = "Film",
        Source = source,
    };

    private static RankedReleases Ranked(params Release[] candidates) => new(candidates, []);

    [Fact]
    public void A_camcorder_recording_is_not_availability()
    {
        var ranked = Ranked(Offered("Film.2026.1080p.HDCAM-GRP", Source.Cam));

        Assert.Null(ReleaseAvailability.Pick(ranked, Source.Web));
    }

    [Fact]
    public void The_first_release_clearing_the_floor_is_the_one_named()
    {
        var ranked = Ranked(
            Offered("Film.2026.1080p.HDCAM-GRP", Source.Cam),
            Offered("Film.2026.720p.WEB-DL-GRP", Source.Web),
            Offered("Film.2026.1080p.BluRay-GRP", Source.BluRay));

        Assert.Equal("Film.2026.720p.WEB-DL-GRP", ReleaseAvailability.Pick(ranked, Source.Web)?.ReleaseName);
    }

    /// <summary>A source the name does not reveal is not evidence of anything.</summary>
    [Fact]
    public void An_unreadable_source_does_not_count()
    {
        var ranked = Ranked(Offered("Film.2026.1080p-GRP", Source.Unknown));

        Assert.Null(ReleaseAvailability.Pick(ranked, Source.Web));
    }

    [Fact]
    public void Nothing_offered_is_nothing_available()
    {
        Assert.Null(ReleaseAvailability.Pick(new RankedReleases([], []), Source.Web));
    }
}
