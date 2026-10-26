using TheKrystalShip.MovieBot.Api.Catalogue;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Shows;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Wants;

/// <summary>The titles somebody still means to watch, which the library keeps.</summary>
public interface IWantHolds
{
    /// <summary>The IMDb ids of the titles held at <paramref name="now"/>.</summary>
    IReadOnlySet<string> Holding(DateTimeOffset now);
}

/// <summary>
/// A title is held while some want on it is from a person who has not watched it and is younger than
/// <see cref="WantOptions.HoldDays"/>. A film is watched when the person finished it; a show, when they
/// finished every episode of it that has aired. A want older than the limit holds nothing and stays on
/// the person's page. The title of a plan with a time fixed is held until that time has passed, so a
/// title fetched for a plan is still here when its people sit down.
/// </summary>
public sealed class WantHolds(
    WantStore wants,
    Plans.PlanStore plans,
    LibraryListing listing,
    WatchRecord record,
    CatalogueTitles shows,
    IOptions<WantOptions> options,
    TimeProvider clock) : IWantHolds
{
    public IReadOnlySet<string> Holding(DateTimeOffset now)
    {
        var oldest = now - TimeSpan.FromDays(Math.Max(0, options.Value.HoldDays));
        var held = plans.Titles(now);
        var titles = listing.Titles();
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        foreach (var person in wants.All().Where(w => w.Since > oldest).GroupBy(w => w.UserId, StringComparer.Ordinal))
        {
            var shelf = LibraryShelf.Of(titles, record, person.Key);
            var finished = ShowProgress.Finished(titles, record, person.Key);
            foreach (var want in person)
            {
                var watched = want.Kind == WantKinds.Show
                    ? ShowProgress.FinishedAired(shows.Show(want.ImdbId), finished.GetValueOrDefault(want.ImdbId), today)
                    : shelf.Saw(want.Kind, want.ImdbId);
                if (!watched) held.Add(want.ImdbId);
            }
        }
        return held;
    }
}
