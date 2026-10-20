using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.MovieBot.Api.Plans;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A plan through the API and the hub: answering fixes a time, and the keeper makes the room ahead of
/// it, closed, with the people coming let in and the film loaded.
/// </summary>
public sealed class PlanRoomTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private const string Heat = "tt0113277";

    private static string At(DateTimeOffset time) => Uri.EscapeDataString(time.ToString("O"));

    [Fact]
    public async Task A_fixed_plan_makes_its_room_closed_with_the_people_coming_let_in()
    {
        var alice = fixture.CreatePersonClient("plan-alice", "Alice");
        var bea = fixture.CreatePersonClient("plan-bea", "Bea");
        var cal = fixture.CreatePersonClient("plan-cal", "Cal");
        var dee = fixture.CreatePersonClient("plan-dee", "Dee");
        foreach (var client in new[] { bea, cal, dee }) await client.GetAsync("/api/me/plans");

        var soon = DateTimeOffset.UtcNow.AddMinutes(20);
        var made = await alice.PostAsJsonAsync("/api/plans", new MakePlanRequest
        {
            ImdbId = Heat, Kind = "film", Times = [soon, soon.AddHours(3)], People = ["plan-bea", "plan-cal"],
        });
        Assert.Equal(HttpStatusCode.OK, made.StatusCode);
        var plan = (await made.Content.ReadFromJsonAsync<PlanView>())!;
        Assert.Equal(PlanStates.Proposed, plan.State);

        // A stranger to the plan finds none.
        Assert.Equal(HttpStatusCode.NotFound, (await dee.GetAsync($"/api/plans/{plan.Id}")).StatusCode);

        // Bea saying in brings the first time to two, which fixes it; Cal can't make it.
        var first = plan.Times[0].At;
        var answered = await bea.PutAsJsonAsync($"/api/plans/{plan.Id}/answers/{At(first)}", new PlanAnswerRequest { Answer = PlanAnswers.In });
        Assert.Equal(PlanStates.Fixed, (await answered.Content.ReadFromJsonAsync<PlanView>())!.State);
        await cal.PutAsJsonAsync($"/api/plans/{plan.Id}/answers/{At(first)}", new PlanAnswerRequest { Answer = PlanAnswers.Cant });

        await fixture.Services.GetRequiredService<PlanKeeper>().PassAsync(CancellationToken.None);

        var open = (await (await alice.GetAsync($"/api/plans/{plan.Id}")).Content.ReadFromJsonAsync<PlanView>())!;
        Assert.Equal(PlanStates.Open, open.State);
        Assert.NotNull(open.RoomId);

        await using var maker = await fixture.ConnectOutsideAsync("plan-alice", "Alice");
        var joined = await maker.InvokeAsync<JoinReply>("Join", open.RoomId!);
        Assert.True(joined.Admitted);
        Assert.False(joined.Door!.Open);

        await using var coming = await fixture.ConnectOutsideAsync("plan-bea", "Bea");
        Assert.True((await coming.InvokeAsync<JoinReply>("Join", open.RoomId!)).Admitted);

        await using var declined = await fixture.ConnectOutsideAsync("plan-cal", "Cal");
        Assert.False((await declined.InvokeAsync<JoinReply>("Join", open.RoomId!)).Admitted);
    }

    [Fact]
    public async Task A_film_the_tracker_shows_is_not_out_is_refused_a_plan_and_one_the_library_holds_is_not()
    {
        var alice = fixture.CreatePersonClient("plan-alice3", "Alice");
        var later = DateTimeOffset.UtcNow.AddDays(2);

        var refused = await alice.PostAsJsonAsync("/api/plans", new MakePlanRequest { ImdbId = "tt0325980", Kind = "film", Times = [later], People = [] });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Empty((await (await alice.GetAsync("/api/me/plans")).Content.ReadFromJsonAsync<List<PlanView>>())!);

        var made = await alice.PostAsJsonAsync("/api/plans", new MakePlanRequest { ImdbId = Heat, Kind = "film", Times = [later], People = [] });
        Assert.Equal(HttpStatusCode.OK, made.StatusCode);
    }

    [Fact]
    public async Task Only_the_maker_fixes_or_cancels_a_plan()
    {
        var alice = fixture.CreatePersonClient("plan-alice2", "Alice");
        var bea = fixture.CreatePersonClient("plan-bea2", "Bea");
        await bea.GetAsync("/api/me/plans");
        var later = DateTimeOffset.UtcNow.AddDays(2);

        var made = await alice.PostAsJsonAsync("/api/plans", new MakePlanRequest
        {
            ImdbId = Heat, Kind = "film", Times = [later], People = ["plan-bea2"],
        });
        var plan = (await made.Content.ReadFromJsonAsync<PlanView>())!;

        Assert.Equal(HttpStatusCode.Forbidden, (await bea.PostAsJsonAsync($"/api/plans/{plan.Id}/fix", new FixPlanRequest { At = plan.Times[0].At })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bea.DeleteAsync($"/api/plans/{plan.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/plans/{plan.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await alice.DeleteAsync($"/api/plans/{plan.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_film_that_is_not_out_cannot_be_planned()
    {
        var alice = fixture.CreatePersonClient("plan-alice3", "Alice");

        // The film database describes it and the tracker holds no release of it at any source.
        var made = await alice.PostAsJsonAsync("/api/plans", new MakePlanRequest
        {
            ImdbId = "tt0325980", Kind = "film", Times = [DateTimeOffset.UtcNow.AddDays(1)], People = [],
        });

        Assert.Equal(HttpStatusCode.Conflict, made.StatusCode);
        Assert.Empty((await (await alice.GetAsync("/api/me/plans")).Content.ReadFromJsonAsync<List<PlanView>>())!);
    }
}
