using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// What a person has watched, from a room playing to the routes that read it back. The room is the
/// one source: a stretch of playing is written down when it ends — a pause, a film changed, somebody
/// leaving, the server stopping — and what a room is playing right now is read from the room. Every
/// test names its own people, because the record is shared by the class.
/// </summary>
public sealed class WatchRecordTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private const string Film = SessionFixture.Heat;
    private const string Other = SessionFixture.Collateral;

    private static string NewSession() => $"watch-{Guid.NewGuid():n}";

    private WatchRecord Record => fixture.Services.GetRequiredService<WatchRecord>();
    private TheKrystalShip.MovieBot.Api.Sessions.SessionStore Sessions =>
        fixture.Services.GetRequiredService<TheKrystalShip.MovieBot.Api.Sessions.SessionStore>();

    /// <summary>A position a room reached a moment after it was asked to be somewhere.</summary>
    private static void Near(double expected, double actual) => Assert.InRange(actual, expected, expected + 3);

    /// <summary>Pausing wherever the room is, which is the plainest way to end a stretch of playing.</summary>
    private void Pause(string session) => Sessions.Pause(session, null, new Actor("w-pauser", "Pauser"));

    private HttpClient As(string userId, string name = "Cy") => fixture.CreatePersonClient(userId, name);

    /// <summary>Somebody in a room holding a film, playing it from a position, with the room open to whoever joins them.</summary>
    private async Task<HubConnection> Watching(string session, string userId, string name, string title, double at)
    {
        var connection = await fixture.ConnectAsync(session, userId, name);
        await connection.InvokeAsync<SessionStatePush>("LoadTitle", title, null);
        await connection.InvokeAsync<SessionStatePush>("Play", at);
        return connection;
    }

    private static async Task<JsonElement> Listing(HttpClient client, string titleId)
    {
        using var response = await client.GetAsync("/api/titles");
        response.EnsureSuccessStatusCode();
        var all = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return all.EnumerateArray().First(t => t.GetProperty("id").GetString() == titleId).Clone();
    }

    [Fact]
    public async Task A_pause_credits_everybody_in_a_playing_room_with_where_it_got_to()
    {
        var playing = NewSession();
        await using var alice = await Watching(playing, "w-alice-1", "Alice", Film, 3000);
        await using var bob = await fixture.ConnectAsync(playing, "w-bob-1", "Bob");

        // A paused room holding the same film, and somebody who signed in and joined nothing.
        var paused = NewSession();
        await using var carol = await fixture.ConnectAsync(paused, "w-carol-1", "Carol");
        await carol.InvokeAsync<SessionStatePush>("LoadTitle", Film, null);
        Pause(paused);

        Pause(playing);

        Near(3000, Record.EntryOf("w-alice-1", Film)!.PositionSeconds);
        Near(3000, Record.EntryOf("w-bob-1", Film)!.PositionSeconds);
        Assert.Null(Record.EntryOf("w-carol-1", Film));
        Assert.Null(Record.EntryOf("w-dave-1", Film));
    }

    [Fact]
    public async Task Leaving_a_playing_room_credits_whoever_left()
    {
        var session = NewSession();
        var alice = await Watching(session, "w-alice-2", "Alice", Film, 100);
        await using var bob = await fixture.ConnectAsync(session, "w-bob-2", "Bob");

        await alice.DisposeAsync();
        // The hub drops a connection when it learns of the disconnect, a moment later, and the record
        // is credited after that.
        await Conditions.Eventually(() => Record.EntryOf("w-alice-2", Film) is not null);
        Assert.Single(Sessions.Participants(session));

        Near(100, Record.EntryOf("w-alice-2", Film)!.PositionSeconds);
        // Bob is still watching: nothing ended for him.
        Assert.Null(Record.EntryOf("w-bob-2", Film));
    }

    [Fact]
    public async Task A_paused_room_credits_nobody_with_what_it_did_while_paused()
    {
        var session = NewSession();
        var alice = await Watching(session, "w-alice-8", "Alice", Film, 500);
        Pause(session);
        // Moved while paused, then left: nothing was watched past the pause.
        await alice.InvokeAsync<SessionStatePush>("Seek", 4000.0);
        await alice.DisposeAsync();
        await Conditions.Eventually(() => Sessions.Participants(session).Count == 0);

        Near(500, Record.EntryOf("w-alice-8", Film)!.PositionSeconds);
    }

    [Fact]
    public async Task A_server_that_stops_credits_everybody_still_watching()
    {
        var session = NewSession();
        await using var alice = await Watching(session, "w-alice-9", "Alice", Film, 2500);
        Assert.Null(Record.EntryOf("w-alice-9", Film));

        Sessions.EndAllSpans();

        Near(2500, Record.EntryOf("w-alice-9", Film)!.PositionSeconds);
    }

    [Fact]
    public async Task What_a_room_is_playing_now_is_read_from_the_room()
    {
        var session = NewSession();
        await using var alice = await Watching(session, "w-alice-10", "Alice", Film, 1000);
        await Task.Delay(1200);

        var progress = await As("w-alice-10").GetFromJsonAsync<JsonElement>("/api/me/progress");

        // Nothing has ended, so nothing is written down; the room says where it is.
        Assert.Null(Record.EntryOf("w-alice-10", Film));
        var entry = Assert.Single(progress.EnumerateArray());
        Assert.InRange(entry.GetProperty("positionSeconds").GetDouble(), 1001, 1004);
    }

    [Fact]
    public async Task Past_ninety_percent_is_finished()
    {
        var session = NewSession();
        await using var alice = await Watching(session, "w-alice-3", "Alice", Film, SessionFixture.TitleDuration * 0.95);

        Pause(session);

        Assert.True(Record.EntryOf("w-alice-3", Film)!.Finished);
    }

    [Fact]
    public async Task Progress_and_history_are_the_tokens_own_and_newest_first()
    {
        var session = NewSession();
        await using var alice = await Watching(session, "w-alice-4", "Alice", Film, 2000);
        // Changing the film ends the first one's stretch.
        await alice.InvokeAsync<SessionStatePush>("LoadTitle", Other, 5800.0);
        await alice.InvokeAsync<SessionStatePush>("Play", 5800.0);
        await Task.Delay(20);
        Pause(session);

        var progress = await As("w-alice-4").GetFromJsonAsync<JsonElement>("/api/me/progress");
        var history = await As("w-alice-4").GetFromJsonAsync<JsonElement>("/api/me/history");

        // The second film was finished, so it is history and not progress.
        var inProgress = Assert.Single(progress.EnumerateArray());
        Assert.Equal(Film, inProgress.GetProperty("titleId").GetString());
        Near(2000, inProgress.GetProperty("positionSeconds").GetDouble());
        Assert.Equal(SessionFixture.TitleDuration, inProgress.GetProperty("durationSeconds").GetDouble());
        Assert.InRange(inProgress.GetProperty("remainingSeconds").GetDouble(),
            SessionFixture.TitleDuration - 2003, SessionFixture.TitleDuration - 2000);
        Assert.Equal("Heat (1995)", inProgress.GetProperty("name").GetString());

        var watched = history.EnumerateArray().ToList();
        Assert.Equal([Other, Film], watched.Select(h => h.GetProperty("titleId").GetString()));
        Assert.True(watched[0].GetProperty("finished").GetBoolean());
        Assert.False(watched[1].GetProperty("finished").GetBoolean());

        // Somebody else has nothing of Alice's.
        Assert.Empty((await As("w-nobody-4").GetFromJsonAsync<JsonElement>("/api/me/history")).EnumerateArray());
        Assert.Empty((await As("w-nobody-4").GetFromJsonAsync<JsonElement>("/api/me/progress")).EnumerateArray());
    }

    [Fact]
    public async Task Forgetting_a_film_removes_the_persons_entry_and_leaves_the_films_last_play()
    {
        var session = NewSession();
        await using var alice = await Watching(session, "w-alice-5", "Alice", Film, 1500);
        Pause(session);
        var playedBefore = Record.LastPlayedAt(Film);
        Assert.NotNull(playedBefore);

        using var gone = await As("w-alice-5").DeleteAsync($"/api/me/history/{Film}");

        Assert.Equal(HttpStatusCode.NoContent, gone.StatusCode);
        Assert.Empty((await As("w-alice-5").GetFromJsonAsync<JsonElement>("/api/me/history")).EnumerateArray());
        Assert.Empty((await As("w-alice-5").GetFromJsonAsync<JsonElement>("/api/me/progress")).EnumerateArray());
        Assert.Null((await Listing(As("w-alice-5"), Film)).GetPropertyOrNull("progress"));
        Assert.Equal(playedBefore, Record.LastPlayedAt(Film));

        // A second forget is the same end state.
        using var again = await As("w-alice-5").DeleteAsync($"/api/me/history/{Film}");
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
    }

    [Fact]
    public async Task The_listing_carries_the_askers_progress_and_a_name_for_who_played_last()
    {
        var session = NewSession();
        await using var alice = await Watching(session, "w-alice-6", "Alice Liddell", Film, 1200);

        // Still playing: the listing reads the room.
        var mine = await Listing(As("w-alice-6"), Film);
        var progress = mine.GetProperty("progress");
        Near(1200, progress.GetProperty("positionSeconds").GetDouble());
        Assert.Equal("w-alice-6", mine.GetProperty("lastPlayed").GetProperty("byId").GetString());
        Assert.False(progress.GetProperty("finished").GetBoolean());
        Assert.Equal("Alice Liddell", mine.GetProperty("lastPlayed").GetProperty("byName").GetString());

        // Somebody who watched nothing has no progress, but the film still says when it was played.
        var theirs = await Listing(As("w-nobody-6"), Film);
        Assert.Null(theirs.GetPropertyOrNull("progress"));
        Assert.Equal("Alice Liddell", theirs.GetProperty("lastPlayed").GetProperty("byName").GetString());

        // The service key is nobody, so it is asked about nobody's progress.
        var service = await Listing(fixture.CreateServiceClient(), Film);
        Assert.Null(service.GetPropertyOrNull("progress"));
        Assert.NotNull(service.GetPropertyOrNull("lastPlayed"));
    }

    [Fact]
    public async Task A_film_nobody_played_has_no_last_play()
    {
        var listed = await Listing(As("w-nobody-7"), SessionFixture.SettledTitle);

        Assert.Null(listed.GetPropertyOrNull("lastPlayed"));
    }

    [Fact]
    public async Task Names_are_learned_from_tokens_and_from_joining()
    {
        var directory = fixture.Services.GetRequiredService<PersonDirectory>();

        using var seen = await fixture.CreatePersonClient("w-bot-8", "Beatrice", via: "cinema-bot").GetAsync("/api/titles");
        seen.EnsureSuccessStatusCode();
        Assert.Equal("Beatrice", directory.NameOf("w-bot-8"));

        await using var joined = await fixture.ConnectAsync(NewSession(), "w-joiner-8", "Jo");
        Assert.Equal("Jo", directory.NameOf("w-joiner-8"));

        Assert.Null(directory.NameOf("w-never-8"));
    }

    [Fact]
    public async Task The_service_key_names_nobody()
    {
        var client = fixture.CreateServiceClient();

        using var progress = await client.GetAsync("/api/me/progress");
        using var history = await client.GetAsync("/api/me/history");
        using var forget = await client.DeleteAsync($"/api/me/history/{Film}");

        Assert.Equal(HttpStatusCode.Unauthorized, progress.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, history.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, forget.StatusCode);
    }

    [Fact]
    public async Task Loading_a_film_with_a_start_puts_the_room_there()
    {
        var session = NewSession();
        await using var alice = await fixture.ConnectAsync(session, "w-alice-9", "Alice");

        var resumed = await alice.InvokeAsync<SessionStatePush>("LoadTitle", Film, 1234.5);
        Assert.Equal(Film, resumed.State.TitleId);
        Assert.Equal(1234.5, resumed.State.PositionSeconds, 3);

        // No start is the beginning.
        var fresh = await alice.InvokeAsync<SessionStatePush>("LoadTitle", Other, null);
        Assert.Equal(0, fresh.State.PositionSeconds);
    }

    [Fact]
    public async Task A_start_past_what_has_been_transcoded_is_held_to_the_head()
    {
        var session = NewSession();
        await using var alice = await fixture.ConnectAsync(session, "w-alice-10", "Alice");

        var pushed = await alice.InvokeAsync<SessionStatePush>("LoadTitle", SessionFixture.TranscodingTitle, 5000.0);

        Assert.True(pushed.State.PositionSeconds < SessionFixture.TranscodingHead);
    }

    [Fact]
    public async Task The_title_route_takes_a_start_too()
    {
        var session = NewSession();

        using var response = await fixture.CreatePersonClient("w-bot-11", "Bot").PostAsJsonAsync(
            $"/api/sessions/{session}/title", new SetTitleRequest(Film, 600));
        response.EnsureSuccessStatusCode();
        var push = await response.Content.ReadFromJsonAsync<SessionStatePush>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });

        Assert.Equal(600, push!.State.PositionSeconds, 3);
    }

    [Fact]
    public void What_was_watched_survives_a_restart()
    {
        var path = Path.Combine(fixture.StateRoot, $"watch-{Guid.NewGuid():n}.db");
        var at = DateTimeOffset.UtcNow;

        var first = new WatchRecord(new CinemaDatabase(path), TimeProvider.System, NothingHeld.Instance);
        first.Record("u", Film, 100, 6000, at);

        var second = new WatchRecord(new CinemaDatabase(path), TimeProvider.System, NothingHeld.Instance);
        Assert.Equal(100, second.EntryOf("u", Film)!.PositionSeconds);
        Assert.Equal(at, second.LastPlayedAt(Film));
        Assert.Equal(first.RecordingSince, second.RecordingSince);
    }
}

internal static class JsonElementExtensions
{
    /// <summary>The property, or null when the object leaves it out, which is how an absent field reads on the wire.</summary>
    public static JsonElement? GetPropertyOrNull(this JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value : null;
}

/// <summary>A library that holds nothing, for a record read on its own.</summary>
internal sealed class NothingHeld : IHeldTitles
{
    public static readonly NothingHeld Instance = new();

    public IReadOnlyList<TheKrystalShip.MovieBot.Api.Library.TitleSummary> Titles() => [];
}
