using System.Net;
using System.Net.Http.Json;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// How long a film stays and who keeps it, over HTTP: a person holding the keep action keeps as
/// themselves, whoever carried the request, and anybody else is refused.
/// </summary>
public sealed class StayEndpointTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private static readonly string[] WatchOnly = [CinemaActions.Watch];

    private static HttpRequestMessage Keep(HttpMethod method, string id) => new(method, $"/api/titles/{id}/keep");

    private static async Task<TitleStay?> Read(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync(ManifestJsonContext.Default.TitleStay);

    [Fact]
    public async Task A_settled_film_reports_its_stay_and_an_unsettled_one_reports_none_of_it()
    {
        var person = fixture.CreatePersonClient("5150", "Al", WatchOnly);

        var settled = await person.GetFromJsonAsync($"/api/titles/{SessionFixture.SettledTitle}/stay", ManifestJsonContext.Default.TitleStay);
        var making = await fixture.CreateServiceClient().GetFromJsonAsync($"/api/titles/{SessionFixture.TranscodingTitle}/stay", ManifestJsonContext.Default.TitleStay);

        Assert.True(settled!.Settled);
        Assert.NotNull(settled.LeavesAt);
        Assert.InRange(settled.DaysLeft!.Value, 1, 7);
        Assert.False(making!.Settled);
        Assert.Null(making.LeavesAt);
        Assert.Null(making.DaysLeft);
    }

    [Fact]
    public async Task A_person_keeps_as_themselves()
    {
        var bea = fixture.CreatePersonClient("5151", "Bea");

        using var kept = await bea.SendAsync(Keep(HttpMethod.Put, SessionFixture.ReadyTitle));
        var stay = await Read(kept);

        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        Assert.Equal("5151", stay!.KeeperId);
        Assert.Equal("Bea", stay.KeeperName);
        Assert.NotNull(stay.KeptAt);
        Assert.Null(stay.LeavesAt);

        // A film already kept stays its first keeper's.
        using var again = await fixture.CreatePersonClient("5152", "Cy").SendAsync(Keep(HttpMethod.Put, SessionFixture.ReadyTitle));
        Assert.Equal("5151", (await Read(again))!.KeeperId);

        using var released = await bea.SendAsync(Keep(HttpMethod.Delete, SessionFixture.ReadyTitle));
        Assert.Null((await Read(released))!.KeeperId);
    }

    [Fact]
    public async Task The_bot_keeps_as_the_person_it_carries()
    {
        var bot = fixture.CreatePersonClient("77", "Bea", via: "cinema-bot");

        using var kept = await bot.SendAsync(Keep(HttpMethod.Put, SessionFixture.Heat));

        Assert.Equal("77", (await Read(kept))!.KeeperId);

        using var released = await bot.SendAsync(Keep(HttpMethod.Delete, SessionFixture.Heat));
        Assert.Null((await Read(released))!.KeeperId);
    }

    [Fact]
    public async Task The_service_key_keeps_nothing()
    {
        var service = fixture.CreateServiceClient();

        using var keep = await service.SendAsync(Keep(HttpMethod.Put, SessionFixture.Collateral));

        Assert.Equal(HttpStatusCode.Unauthorized, keep.StatusCode);
        var stay = await service.GetFromJsonAsync($"/api/titles/{SessionFixture.Collateral}/stay", ManifestJsonContext.Default.TitleStay);
        Assert.Null(stay!.KeeperId);
    }

    [Fact]
    public async Task Somebody_without_the_keep_action_reads_stays_and_keeps_nothing()
    {
        var viewer = fixture.CreatePersonClient("9", "Visitor", WatchOnly);

        using var stay = await viewer.GetAsync($"/api/titles/{SessionFixture.SettledTitle}/stay");
        using var stays = await viewer.GetAsync("/api/stays");
        using var keep = await viewer.SendAsync(Keep(HttpMethod.Put, SessionFixture.SettledTitle));
        using var release = await viewer.SendAsync(Keep(HttpMethod.Delete, SessionFixture.SettledTitle));

        Assert.Equal(HttpStatusCode.OK, stay.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stays.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, keep.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, release.StatusCode);
    }

    [Fact]
    public async Task Nobody_without_a_token_or_the_key_reads_a_stay()
    {
        using var response = await fixture.CreateClient().GetAsync($"/api/titles/{SessionFixture.SettledTitle}/stay");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_film_the_library_does_not_hold_is_a_404_everywhere()
    {
        var person = fixture.CreatePersonClient("5155", "Fay");

        using var stay = await person.GetAsync("/api/titles/nothing-here/stay");
        using var keep = await person.SendAsync(Keep(HttpMethod.Put, "nothing-here"));
        using var release = await person.SendAsync(Keep(HttpMethod.Delete, "nothing-here"));
        using var climbing = await person.GetAsync("/api/titles/..%2F..%2Fetc/stay");

        Assert.Equal(HttpStatusCode.NotFound, stay.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, keep.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, release.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, climbing.StatusCode);
    }

    [Fact]
    public async Task The_list_holds_the_settled_films_only()
    {
        var stays = await fixture.CreatePersonClient("5153", "Di").GetFromJsonAsync("/api/stays", ManifestJsonContext.Default.IReadOnlyListTitleStay);

        Assert.Equal(SessionFixture.SettledTitle, Assert.Single(stays!).TitleId);
    }

    [Fact]
    public async Task What_is_due_is_the_services_to_ask_and_a_fresh_film_is_not_on_it()
    {
        using var person = await fixture.CreatePersonClient("5154", "Ed").GetAsync("/api/library/due");
        var due = await fixture.CreateServiceClient().GetFromJsonAsync("/api/library/due", ManifestJsonContext.Default.StringArray);

        Assert.Equal(HttpStatusCode.Forbidden, person.StatusCode);
        Assert.Empty(due!);
    }
}
