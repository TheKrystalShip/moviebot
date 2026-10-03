using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// How long a film stays and who keeps it, over HTTP: a member acts as themselves, a service names
/// the person it acts for, and anybody else is refused.
/// </summary>
public sealed class StayEndpointTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private HttpClient Member(string userId, string name, bool member = true)
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", RoomToken.Issue(new RoomTokenPayload
        {
            UserId = userId, DisplayName = name, RoomId = "", Member = member,
            ExpiresAtUnix = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
        }, SessionFixture.SigningKey));
        return client;
    }

    private static HttpRequestMessage Keep(HttpMethod method, string id, Actor? actor = null) =>
        new(method, $"/api/titles/{id}/keep")
        {
            Content = actor is null
                ? null
                : JsonContent.Create(new KeepRequest { RequestedBy = actor }, ManifestJsonContext.Default.KeepRequest),
        };

    private static async Task<TitleStay?> Read(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync(ManifestJsonContext.Default.TitleStay);

    [Fact]
    public async Task A_settled_film_reports_its_stay_and_an_unsettled_one_reports_none_of_it()
    {
        var service = fixture.CreateServiceClient();

        var settled = await service.GetFromJsonAsync($"/api/titles/{SessionFixture.SettledTitle}/stay", ManifestJsonContext.Default.TitleStay);
        var making = await service.GetFromJsonAsync($"/api/titles/{SessionFixture.TranscodingTitle}/stay", ManifestJsonContext.Default.TitleStay);

        Assert.True(settled!.Settled);
        Assert.NotNull(settled.LeavesAt);
        Assert.InRange(settled.DaysLeft!.Value, 1, 7);
        Assert.False(making!.Settled);
        Assert.Null(making.LeavesAt);
        Assert.Null(making.DaysLeft);
    }

    [Fact]
    public async Task A_member_keeps_as_themselves_whoever_the_body_names()
    {
        var bea = Member("5151", "Bea");

        using var kept = await bea.SendAsync(Keep(HttpMethod.Put, SessionFixture.ReadyTitle, new Actor("1", "Somebody else")));
        var stay = await Read(kept);

        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        Assert.Equal("5151", stay!.KeeperId);
        Assert.NotNull(stay.KeptAt);
        Assert.Null(stay.LeavesAt);

        // A member who sends no body at all is as good: the token is who they are.
        using var again = await Member("5152", "Cy").SendAsync(Keep(HttpMethod.Put, SessionFixture.ReadyTitle));
        Assert.Equal("5151", (await Read(again))!.KeeperId);

        using var released = await bea.SendAsync(Keep(HttpMethod.Delete, SessionFixture.ReadyTitle));
        Assert.Null((await Read(released))!.KeeperId);
    }

    [Fact]
    public async Task A_service_names_who_it_keeps_for()
    {
        var service = fixture.CreateServiceClient();

        using var kept = await service.SendAsync(Keep(HttpMethod.Put, SessionFixture.Heat, new Actor("77", "Bea")));
        var stay = await Read(kept);

        Assert.Equal("77", stay!.KeeperId);
        Assert.Equal("77", stay.KeeperName);

        var listed = await service.GetFromJsonAsync($"/api/titles/{SessionFixture.Heat}/stay", ManifestJsonContext.Default.TitleStay);
        Assert.Equal("77", listed!.KeeperId);

        using var released = await service.SendAsync(Keep(HttpMethod.Delete, SessionFixture.Heat, new Actor("77", "Bea")));
        Assert.Null((await Read(released))!.KeeperId);
    }

    [Fact]
    public async Task A_service_that_names_nobody_keeps_nothing()
    {
        var service = fixture.CreateServiceClient();

        using var none = await service.SendAsync(Keep(HttpMethod.Put, SessionFixture.Collateral));
        using var blank = await service.SendAsync(Keep(HttpMethod.Put, SessionFixture.Collateral, new Actor("", "")));

        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);

        var stay = await service.GetFromJsonAsync($"/api/titles/{SessionFixture.Collateral}/stay", ManifestJsonContext.Default.TitleStay);
        Assert.Null(stay!.KeeperId);
    }

    [Fact]
    public async Task Somebody_who_is_not_a_member_is_refused_every_route()
    {
        var outsider = Member("9", "Visitor", member: false);

        using var stay = await outsider.GetAsync($"/api/titles/{SessionFixture.SettledTitle}/stay");
        using var stays = await outsider.GetAsync("/api/stays");
        using var keep = await outsider.SendAsync(Keep(HttpMethod.Put, SessionFixture.SettledTitle));
        using var release = await outsider.SendAsync(Keep(HttpMethod.Delete, SessionFixture.SettledTitle));

        Assert.Equal(HttpStatusCode.Forbidden, stay.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, stays.StatusCode);
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
        var service = fixture.CreateServiceClient();

        using var stay = await service.GetAsync("/api/titles/nothing-here/stay");
        using var keep = await service.SendAsync(Keep(HttpMethod.Put, "nothing-here", new Actor("1", "A")));
        using var release = await service.SendAsync(Keep(HttpMethod.Delete, "nothing-here", new Actor("1", "A")));
        using var climbing = await service.GetAsync("/api/titles/..%2F..%2Fetc/stay");

        Assert.Equal(HttpStatusCode.NotFound, stay.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, keep.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, release.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, climbing.StatusCode);
    }

    [Fact]
    public async Task The_list_holds_the_settled_films_only()
    {
        var stays = await Member("5153", "Di").GetFromJsonAsync("/api/stays", ManifestJsonContext.Default.IReadOnlyListTitleStay);

        Assert.Equal(SessionFixture.SettledTitle, Assert.Single(stays!).TitleId);
    }

    [Fact]
    public async Task What_is_due_is_the_services_to_ask_and_a_fresh_film_is_not_on_it()
    {
        using var member = await Member("5154", "Ed").GetAsync("/api/library/due");
        var due = await fixture.CreateServiceClient().GetFromJsonAsync("/api/library/due", ManifestJsonContext.Default.StringArray);

        Assert.Equal(HttpStatusCode.Forbidden, member.StatusCode);
        Assert.Empty(due!);
    }
}
