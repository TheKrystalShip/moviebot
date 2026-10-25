using System.Net;
using Microsoft.AspNetCore.SignalR.Client;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A room nobody is in can be forgotten at once by somebody it admits, as the reaper forgets it once it
/// has stood idle; a room with anybody in it, or private to the asker, stays.
/// </summary>
public sealed class RoomForgetTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private static string NewRoom() => "forget-" + Guid.NewGuid().ToString("n")[..8];

    /// <summary>Asks to forget the room until the server has seen the last connection leave.</summary>
    private static async Task<HttpStatusCode> ForgetOnceEmptyAsync(HttpClient client, string room)
    {
        for (var i = 0; ; i++)
        {
            using var response = await client.DeleteAsync($"/api/rooms/{room}");
            if (response.StatusCode != HttpStatusCode.Conflict || i == 50) return response.StatusCode;
            await Task.Delay(100);
        }
    }

    [Fact]
    public async Task An_empty_room_is_forgotten_and_is_then_gone()
    {
        var room = NewRoom();
        await using (var alice = await fixture.ConnectOutsideAsync("f-alice-1", "Alice"))
            await alice.InvokeAsync<JoinReply>("Join", room);

        var client = fixture.CreatePersonClient("f-alice-1", "Alice");
        Assert.Equal(HttpStatusCode.NoContent, await ForgetOnceEmptyAsync(client, room));
        using var again = await client.DeleteAsync($"/api/rooms/{room}");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task A_room_with_somebody_in_it_stays()
    {
        var room = NewRoom();
        await using var bob = await fixture.ConnectOutsideAsync("f-bob-2", "Bob");
        await bob.InvokeAsync<JoinReply>("Join", room);

        using var response = await fixture.CreatePersonClient("f-alice-2", "Alice").DeleteAsync($"/api/rooms/{room}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True((await bob.InvokeAsync<JoinReply>("Join", room)).Admitted);
    }

    [Fact]
    public async Task A_room_private_to_the_asker_is_not_theirs_to_forget()
    {
        var room = NewRoom();
        await using (var alice = await fixture.ConnectOutsideAsync("f-alice-3", "Alice"))
        {
            await alice.InvokeAsync<JoinReply>("Join", room);
            await alice.InvokeAsync("SetOpen", false);
        }

        await Task.Delay(300);
        using var refused = await fixture.CreatePersonClient("f-carol-3", "Carol").DeleteAsync($"/api/rooms/{room}");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, await ForgetOnceEmptyAsync(fixture.CreatePersonClient("f-alice-3", "Alice"), room));
    }

    [Fact]
    public async Task A_room_there_is_none_of_is_not_made_by_asking()
    {
        var room = NewRoom();
        var client = fixture.CreatePersonClient("f-alice-4", "Alice");
        using var first = await client.DeleteAsync($"/api/rooms/{room}");
        using var second = await client.DeleteAsync($"/api/rooms/{room}");
        Assert.Equal(HttpStatusCode.NotFound, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }
}
