using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Api.Acquisition;
using TheKrystalShip.MovieBot.Api.Films;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Shows;
using TheKrystalShip.MovieBot.Api.Wants;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Plans;

/// <summary>
/// Turns stored plans into what a page shows, for the person asking. Readiness is measured when asked:
/// the library as last listed, the torrent client, and the prefetcher's own record of a title the
/// tracker offered nothing for.
/// </summary>
public sealed class PlanPresenter(
    FilmStore films,
    ShowStore shows,
    TitleGlances glances,
    LibraryListing library,
    PersonDirectory people,
    AcquisitionService acquisition,
    WantFetcher fetcher,
    ILogger<PlanPresenter> logger)
{
    public async Task<List<PlanView>> ViewsAsync(string viewer, IReadOnlyList<StoredPlan> list, CancellationToken ct)
    {
        var titles = library.Titles();
        var live = list.Where(p => p.State is PlanStates.Proposed or PlanStates.Fixed or PlanStates.Open).ToList();
        var arriving = live.Any(p => PlanRules.LibraryId(titles, p) is null) ? await DownloadsAsync(ct) : [];
        return [.. list.Select(p => View(p, viewer, titles, arriving))];
    }

    public async Task<PlanView> ViewAsync(string viewer, StoredPlan plan, CancellationToken ct) =>
        (await ViewsAsync(viewer, [plan], ct)).Single();

    private PlanView View(StoredPlan plan, string viewer, IReadOnlyList<TitleSummary> titles, List<DownloadStatus> arriving)
    {
        var isShow = plan.Kind == WantKinds.Show;
        var name = isShow ? shows.Held(plan.ImdbId)?.Details?.Title : films.Held(plan.ImdbId)?.Details?.Title;
        var libraryId = PlanRules.LibraryId(titles, plan);

        var (readiness, progress) = PlanRules.Readiness(
            libraryId is not null, WatchNow.DownloadOf(arriving, plan.ImdbId), fetcher.AskedEmpty(plan.ImdbId));

        return new PlanView
        {
            Id = plan.Id, ImdbId = plan.ImdbId, Kind = plan.Kind, Title = name ?? plan.ImdbId,
            Year = isShow ? shows.Held(plan.ImdbId)?.Details?.FirstYear : films.Held(plan.ImdbId)?.Details?.Year,
            PosterUrl = Poster(plan),
            Season = plan.Season, Number = plan.Number, State = plan.State,
            MadeBy = Person(plan, plan.MadeBy), MadeAt = plan.MadeAt, Mine = plan.MadeBy == viewer,
            FixedTime = plan.FixedTime,
            Times = [.. plan.Times.Select(at => new PlanTimeView
            {
                At = at,
                Answers = [.. plan.Answers.Where(a => a.At == at).Select(a => new PlanAnswerView
                {
                    UserId = a.UserId, Name = people.NameOf(a.UserId), Answer = a.Answer,
                })],
                In = PlanRules.In(plan, at),
                Mine = plan.Answers.FirstOrDefault(a => a.At == at && a.UserId == viewer)?.Answer,
                SuggestedBy = plan.SuggestedBy.TryGetValue(at, out var by) ? Person(plan, by) : null,
            })],
            People = [.. plan.People.Select(u => Person(plan, u))],
            Readiness = readiness, Progress = progress,
            LibraryId = libraryId, RoomId = plan.RoomId,
        };
    }

    private PlanPersonView Person(StoredPlan plan, string userId) =>
        new() { UserId = userId, Name = people.NameOf(userId), Maker = userId == plan.MadeBy };

    private string? Poster(StoredPlan plan) => plan.Kind == WantKinds.Show
        ? shows.HasImage(plan.ImdbId, ShowStore.Poster) ? ShowViews.ImageUrl(plan.ImdbId, ShowStore.Poster) : null
        : films.HasImage(plan.ImdbId, FilmStore.Poster) ? FilmViews.ImageUrl(plan.ImdbId, FilmStore.Poster) : glances.PosterUrl(plan.ImdbId);

    /// <summary>The torrent client's downloads, or none when it cannot be asked: a state that cannot be measured is left out.</summary>
    private async Task<List<DownloadStatus>> DownloadsAsync(CancellationToken ct)
    {
        try
        {
            return [.. await acquisition.ListAsync(ct)];
        }
        catch (QBittorrentException ex)
        {
            logger.LogWarning(ex, "The downloads could not be read for the plans.");
            return [];
        }
    }
}
