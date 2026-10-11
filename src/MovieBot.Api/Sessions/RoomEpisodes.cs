using System.Collections.Concurrent;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Api.Acquisition;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.Shows;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Sessions;

/// <summary>An episode either side of the one a room holds, as a player's previous and next name it.</summary>
public sealed record RoomEpisode(int Season, int Number, string? Name, string? TitleId);

/// <summary>The episode a room holds and the episodes either side of it; all absent for a film.</summary>
public sealed record RoomAround(RoomEpisode? Current, RoomEpisode? Previous, RoomEpisode? Next);

/// <summary>
/// Moving a room through a show. The room holds one thing, a title or an episode arriving, and the
/// episodes either side of it are decided here and nowhere else: the next of its season, else the
/// first of the next, as the film database lists the show, or as the library holds it for a show the
/// database has not described; previous the other way. Moving to an episode the library holds puts it
/// on; one it does not is Watch now into the room, at the quality of the one playing, for somebody
/// who may download.
/// </summary>
public sealed class RoomEpisodes(
    SessionStore sessions, TitleLibrary library, ShowStore shows, RoomControls controls, WatchNow watch)
{
    /// <summary>The show and the episode a room holds, or null when it holds a film or nothing.</summary>
    public (string Show, EpisodeIdentity Episode, Manifest? Manifest)? Current(string sessionId)
    {
        var state = sessions.GetOrCreate(sessionId);
        if (state.TitleId is { } titleId)
        {
            return library.Get(titleId) is { Episode: { } episode, Film.ImdbId: { Length: > 0 } show } manifest
                ? (show, episode, manifest)
                : null;
        }
        return state.Preparing is { Season: { } season, Episode: { } number } preparing
            ? (preparing.ImdbId, new EpisodeIdentity { Season = season, Number = number }, null)
            : null;
    }

    /// <summary>The episodes either side of what the room holds, among those <paramref name="mayFetch"/> lets the asker play.</summary>
    public RoomAround Around(string sessionId, bool mayFetch)
    {
        if (Current(sessionId) is not { } current) return new RoomAround(null, null, null);
        var holding = sessions.GetOrCreate(sessionId).TitleId;
        return new RoomAround(
            new RoomEpisode(current.Episode.Season, current.Episode.Number, shows.EpisodeName(current.Show, current.Episode), holding),
            Playable(current.Show, Before(current.Show, current.Episode), mayFetch),
            Playable(current.Show, After(current.Show, current.Episode), mayFetch));
    }

    /// <summary>Moves the room to the episode after what it holds; false when there is none to move to.</summary>
    public Task<bool> NextAsync(string sessionId, CinemaPerson person, CancellationToken ct) =>
        StepAsync(sessionId, person, forward: true, ct);

    /// <summary>Moves the room to the episode before what it holds; false when there is none.</summary>
    public Task<bool> PreviousAsync(string sessionId, CinemaPerson person, CancellationToken ct) =>
        StepAsync(sessionId, person, forward: false, ct);

    /// <summary>
    /// Puts an episode of the show the room holds on for the room; false when the library does not
    /// hold it and the person may not fetch it.
    /// </summary>
    public async Task<bool> PlayAsync(string sessionId, int season, int number, CinemaPerson person, CancellationToken ct)
    {
        if (Current(sessionId) is not { } current) return false;
        return await GoAsync(sessionId, current.Show, new EpisodeIdentity { Season = season, Number = number }, current.Manifest, person, ct);
    }

    private async Task<bool> StepAsync(string sessionId, CinemaPerson person, bool forward, CancellationToken ct)
    {
        if (Current(sessionId) is not { } current) return false;
        var to = forward ? After(current.Show, current.Episode) : Before(current.Show, current.Episode);
        return to is not null && await GoAsync(sessionId, current.Show, to, current.Manifest, person, ct);
    }

    /// <summary>The rooms a step is being taken in, which take no other until it is done.</summary>
    private readonly ConcurrentDictionary<string, byte> _stepping = new(StringComparer.Ordinal);

    /// <summary>
    /// Takes the room to an episode, one step at a time. A step asked for while one is under way is
    /// dropped and counts as done: every viewer's press moves the same room, and a second press
    /// before the room has moved is somebody asking for the step already being taken, not for the
    /// one after it. An episode stepped to that is not here is fetched, so pressing faster than the
    /// room moves would otherwise start a download per press.
    /// </summary>
    private async Task<bool> GoAsync(string sessionId, string show, EpisodeIdentity to, Manifest? playing, CinemaPerson person, CancellationToken ct)
    {
        if (!_stepping.TryAdd(sessionId, 0)) return true;
        try
        {
            return await StepToAsync(sessionId, show, to, playing, person, ct);
        }
        finally
        {
            _stepping.TryRemove(sessionId, out _);
        }
    }

    private async Task<bool> StepToAsync(string sessionId, string show, EpisodeIdentity to, Manifest? playing, CinemaPerson person, CancellationToken ct)
    {
        var wanted = new EpisodeNumber(to.Season, to.Number);
        if (WatchNow.PlayableEpisode(library, show, wanted) is { } here)
        {
            await controls.ApplyAsync(sessionId, "load", person.Actor, a => sessions.LoadTitle(sessionId, here, a), ct: ct);
            return true;
        }
        if (!person.Holds(CinemaActions.Download)) return false;

        var reply = await watch.StartAsync(new WatchNowRequest
        {
            ImdbId = show,
            Episodes = [new EpisodePick(to.Season, to.Number)],
            Room = sessionId,
            Quality = playing?.Video?.Width >= 1800 ? "1080p" : "720p",
        }, person.Actor, ct);
        return reply.Outcome is "inLibrary" or "preparing";
    }

    private RoomEpisode? Playable(string show, EpisodeIdentity? episode, bool mayFetch)
    {
        if (episode is null) return null;
        var here = WatchNow.PlayableEpisode(library, show, new EpisodeNumber(episode.Season, episode.Number));
        if (here is null && !mayFetch) return null;
        return new RoomEpisode(episode.Season, episode.Number, shows.EpisodeName(show, episode), here);
    }

    private EpisodeIdentity? After(string show, EpisodeIdentity episode) =>
        shows.Held(show) is { Seasons.Count: > 0 }
            ? shows.EpisodeAfter(show, episode)
            : Held(show).FirstOrDefault(e => Later(e, episode));

    private EpisodeIdentity? Before(string show, EpisodeIdentity episode) =>
        shows.Held(show) is { Seasons.Count: > 0 }
            ? shows.EpisodeBefore(show, episode)
            : Held(show).LastOrDefault(e => Later(episode, e));

    private IEnumerable<EpisodeIdentity> Held(string show) =>
        library.EpisodesOf(show).Where(e => e.Manifest.Status != TitleStatus.Failed).Select(e => e.Manifest.Episode!);

    private static bool Later(EpisodeIdentity a, EpisodeIdentity b) =>
        a.Season > b.Season || (a.Season == b.Season && a.Number > b.Number);
}
