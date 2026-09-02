using TheKrystalShip.MovieBot.Acquire.Search;

namespace TheKrystalShip.MovieBot.Bot.Notify;

/// <summary>
/// Whether a film can be downloaded, from what the tracker offers for it.
///
/// A row on the tracker is not enough. A film in cinemas has camcorder recordings on the tracker
/// within days, and the selection policy offers them — weighed down, but offered, because
/// somebody who asks for one on purpose should be able to have it. Somebody waiting to be told
/// means the film's digital release, so a floor on the source is what turns "offered" into
/// "available", and the release named is the first the policy would offer that clears it.
/// </summary>
public static class ReleaseAvailability
{
    public static Release? Pick(RankedReleases ranked, Source floor) =>
        ranked.Candidates.FirstOrDefault(r => r.Source >= floor);
}
