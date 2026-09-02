using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The rooms as the bot reads them: what each is watching and how many are in it, and never who.
/// </summary>
public sealed class RoomListingTests : IClassFixture<SessionFixture>
{
    private readonly SessionFixture _fixture;

    public RoomListingTests(SessionFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_room_is_listed_with_its_film_and_its_occupancy()
    {
        const string room = "listing-room";
        var service = _fixture.CreateServiceClient();

        // A viewer in the room, over the hub, as the Activity would be.
        var connection = new HubConnectionBuilder()
            .WithUrl(_fixture.Server.BaseAddress + "hub/session", o =>
            {
                o.HttpMessageHandlerFactory = _ => _fixture.Server.CreateHandler();
                o.AccessTokenProvider = () => Task.FromResult<string?>(_fixture.TokenFor(room));
            })
            .Build();
        await connection.StartAsync();
        await connection.InvokeAsync<SessionStatePush>("Join", room, "u1", "Someone");
        await connection.InvokeAsync<SessionStatePush>("LoadTitle", SessionFixture.ReadyTitle);
        await connection.InvokeAsync<SessionStatePush>("Play", 120.0);

        var rooms = await service.GetFromJsonAsync(
            "api/sessions", ManifestJsonContext.Default.IReadOnlyListRoomSummary);

        var listed = Assert.Single(rooms!, r => r.SessionId == room);
        Assert.Equal(SessionFixture.ReadyTitle, listed.TitleId);
        Assert.Equal(SessionFixture.ReadyTitle, listed.Name);
        Assert.Equal(SessionFixture.TitleDuration, listed.DurationSeconds);
        Assert.False(listed.Paused);
        Assert.InRange(listed.PositionSeconds, 120, 130);
        Assert.Equal(1, listed.Participants);

        await connection.StopAsync();

        rooms = await service.GetFromJsonAsync(
            "api/sessions", ManifestJsonContext.Default.IReadOnlyListRoomSummary);
        Assert.Equal(0, Assert.Single(rooms!, r => r.SessionId == room).Participants);
    }

    [Fact]
    public async Task The_listing_is_closed_to_anyone_without_a_key()
    {
        var response = await _fixture.CreateClient().GetAsync("api/sessions");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
