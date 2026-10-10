using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.Sessions;

namespace TheKrystalShip.MovieBot.Api.People;

/// <summary>
/// Writes down what the rooms played, as they play it.
///
/// The room is the one source of where a film is: its state is a position at an instant, and between
/// two changes everything else follows from that. So nothing here looks at the rooms on a timer. The
/// store says when a stretch of playing ends — a pause, a seek, a film changed, somebody leaving, the
/// server stopping — and each person who was watching is credited with where it got to. Somebody who
/// joined halfway is credited from where they joined: they watched it from there. What is happening
/// in a room right now is newer than anything written down, and is read from the room itself
/// (<see cref="SessionStore.Viewing"/>).
/// </summary>
public sealed class WatchRecorder(
    SessionStore sessions,
    TitleLibrary library,
    WatchRecord record,
    PersonDirectory people) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        sessions.Watched += Credit;
        return Task.CompletedTask;
    }

    /// <summary>The people still in the rooms are disconnected by the stop, not one by one, so their stretch ends here.</summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        sessions.EndAllSpans();
        sessions.Watched -= Credit;
        return Task.CompletedTask;
    }

    public void Credit(WatchedSpan span)
    {
        if (library.Get(span.TitleId) is not { } manifest) return;

        foreach (var viewer in span.Viewers)
        {
            people.Remember(viewer.UserId, viewer.DisplayName);
            record.Record(viewer.UserId, span.TitleId, span.PositionSeconds, manifest.DurationSeconds, span.At);
        }
    }
}
