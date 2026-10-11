using Microsoft.AspNetCore.SignalR;
using TheKrystalShip.MovieBot.Api.Sessions;

namespace TheKrystalShip.MovieBot.Api.People;

/// <summary>
/// Tells a person's own connections that their settings changed. The record is the API's: a phone and a
/// television read the same one, and a change to it reaches every connection of the person's that has
/// an account behind it (<see cref="PersonGroups.Everything"/>), which is what a playing television and
/// a phone's open remote are. A connection of somebody else's is in another group, and a guest's in
/// none. Nothing here rides on the room: the room's state, its roster and its groups are never touched.
/// </summary>
public sealed class ViewerPush(IHubContext<SessionHub> hub)
{
    /// <summary>How the person watches changed: the whole record, as the API now keeps it.</summary>
    public Task PreferencesChangedAsync(string userId, ViewerPreferences preferences) =>
        hub.Clients.Group(PersonGroups.Everything(userId)).SendAsync("PreferencesChanged", preferences);

    /// <summary>The tracks the person watches one title with changed: the title's tracks as they now stand.</summary>
    public Task TracksChangedAsync(string userId, FilmTracks tracks) =>
        hub.Clients.Group(PersonGroups.Everything(userId)).SendAsync("TracksChanged", tracks);
}
