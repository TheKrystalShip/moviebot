using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.MovieBot.Api.Plans;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A plan through the API and the hub: the maker picks a time, and the keeper makes the room ahead of
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

        // Bea is in and Cal can't make it; answers pick nothing, and the maker picks the first time.
        var first = plan.Times[0].At;
        var answered = await bea.PutAsJsonAsync($"/api/plans/{plan.Id}/answers/{At(first)}", new PlanAnswerRequest { Answer = PlanAnswers.In });
        Assert.Equal(PlanStates.Proposed, (await answered.Content.ReadFromJsonAsync<PlanView>())!.State);
        await cal.PutAsJsonAsync($"/api/plans/{plan.Id}/answers/{At(first)}", new PlanAnswerRequest { Answer = PlanAnswers.Cant });
        var picked = await alice.PostAsJsonAsync($"/api/plans/{plan.Id}/fix", new FixPlanRequest { At = first });
        Assert.Equal(PlanStates.Fixed, (await picked.Content.ReadFromJsonAsync<PlanView>())!.State);

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

        // Cal can make it after all: saying in once the room is open lets him in.
        Assert.Equal(HttpStatusCode.OK, (await cal.PutAsJsonAsync(
            $"/api/plans/{plan.Id}/answers/{At(first)}", new PlanAnswerRequest { Answer = PlanAnswers.In })).StatusCode);
        await using var changed = await fixture.ConnectOutsideAsync("plan-cal", "Cal");
        Assert.True((await changed.InvokeAsync<JoinReply>("Join", open.RoomId!)).Admitted);
    }

    [Fact]
    public async Task Anybody_in_a_plan_suggests_a_time_and_the_maker_moves_the_pick_to_it()
    {
        var alice = fixture.CreatePersonClient("plan-alice-move", "Alice");
        var bea = fixture.CreatePersonClient("plan-bea-move", "Bea");
        await bea.GetAsync("/api/me/plans");
        var later = PlanRules.Minute(DateTimeOffset.UtcNow.AddDays(1));

        var made = await alice.PostAsJsonAsync("/api/plans", new MakePlanRequest
        {
            ImdbId = Heat, Kind = "film", Times = [later], People = ["plan-bea-move"],
        });
        var plan = (await made.Content.ReadFromJsonAsync<PlanView>())!;
        await alice.PostAsJsonAsync($"/api/plans/{plan.Id}/fix", new FixPlanRequest { At = later });

        var suggested = await bea.PostAsJsonAsync($"/api/plans/{plan.Id}/times", new FixPlanRequest { At = later.AddMinutes(30) });
        Assert.Equal(HttpStatusCode.OK, suggested.StatusCode);
        var withIt = (await suggested.Content.ReadFromJsonAsync<PlanView>())!;
        var added = withIt.Times.Single(t => t.At == later.AddMinutes(30));
        Assert.Equal("plan-bea-move", added.SuggestedBy!.UserId);
        Assert.Equal(PlanAnswers.In, added.Mine);
        Assert.Equal(2, withIt.Times.Count);

        Assert.Equal(HttpStatusCode.Conflict, (await bea.PostAsJsonAsync($"/api/plans/{plan.Id}/times", new FixPlanRequest { At = later.AddMinutes(30) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await bea.PostAsJsonAsync($"/api/plans/{plan.Id}/times", new FixPlanRequest { At = DateTimeOffset.UtcNow.AddMinutes(-5) })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bea.PostAsJsonAsync($"/api/plans/{plan.Id}/fix", new FixPlanRequest { At = later.AddMinutes(30) })).StatusCode);

        var moved = await alice.PostAsJsonAsync($"/api/plans/{plan.Id}/fix", new FixPlanRequest { At = later.AddMinutes(30) });
        var after = (await moved.Content.ReadFromJsonAsync<PlanView>())!;
        Assert.Equal(later.AddMinutes(30), after.FixedTime);
        Assert.Equal(PlanStates.Fixed, after.State);
        Assert.Equal(2, after.Times.Count);
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
        Assert.Equal(HttpStatusCode.NotFound, (await alice.DeleteAsync($"/api/plans/{plan.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bea.GetAsync($"/api/plans/{plan.Id}")).StatusCode);
    }

    [Fact]
    public async Task Anyone_in_a_plan_invites_another_person_while_it_is_going_and_nobody_outside_it_can()
    {
        var alice = fixture.CreatePersonClient("plan-alice-invite", "Alice");
        var bea = fixture.CreatePersonClient("plan-bea-invite", "Bea");
        var cal = fixture.CreatePersonClient("plan-cal-invite", "Cal");
        var dee = fixture.CreatePersonClient("plan-dee-invite", "Dee");
        foreach (var client in new[] { bea, cal, dee }) await client.GetAsync("/api/me/plans");

        var made = await alice.PostAsJsonAsync("/api/plans", new MakePlanRequest
        {
            ImdbId = Heat, Kind = "film", Times = [DateTimeOffset.UtcNow.AddDays(2)], People = ["plan-bea-invite"],
        });
        var plan = (await made.Content.ReadFromJsonAsync<PlanView>())!;
        var path = $"/api/plans/{plan.Id}/people";

        // Dee is in no plan, so she has none to invite anybody into.
        Assert.Equal(HttpStatusCode.NotFound, (await dee.PostAsJsonAsync(path, new InviteRequest { UserId = "plan-cal-invite" })).StatusCode);

        var invited = await bea.PostAsJsonAsync(path, new InviteRequest { UserId = "plan-cal-invite" });
        Assert.Equal(HttpStatusCode.OK, invited.StatusCode);
        Assert.Equal("bell", (await invited.Content.ReadFromJsonAsync<InviteReply>())!.Delivery);

        var seen = (await (await cal.GetAsync($"/api/plans/{plan.Id}")).Content.ReadFromJsonAsync<PlanView>())!;
        Assert.Equal(["plan-alice-invite", "plan-bea-invite", "plan-cal-invite"], seen.People.Select(p => p.UserId));
        Assert.Equal(HttpStatusCode.OK, (await cal.PutAsJsonAsync(
            $"/api/plans/{plan.Id}/answers/{At(plan.Times[0].At)}", new PlanAnswerRequest { Answer = PlanAnswers.Maybe })).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await alice.PostAsJsonAsync(path, new InviteRequest { UserId = "plan-cal-invite" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.PostAsJsonAsync(path, new InviteRequest { UserId = "nobody-at-all" })).StatusCode);

        await alice.DeleteAsync($"/api/plans/{plan.Id}");
        Assert.Equal(HttpStatusCode.NotFound, (await alice.PostAsJsonAsync(path, new InviteRequest { UserId = "plan-dee-invite" })).StatusCode);
    }

    [Fact]
    public async Task A_person_invited_once_the_room_is_open_is_let_into_the_room()
    {
        var alice = fixture.CreatePersonClient("plan-alice-open", "Alice");
        var bea = fixture.CreatePersonClient("plan-bea-open", "Bea");
        var dee = fixture.CreatePersonClient("plan-dee-open", "Dee");
        foreach (var client in new[] { bea, dee }) await client.GetAsync("/api/me/plans");

        var soon = DateTimeOffset.UtcNow.AddMinutes(20);
        var made = await alice.PostAsJsonAsync("/api/plans", new MakePlanRequest
        {
            ImdbId = Heat, Kind = "film", Times = [soon], People = ["plan-bea-open"],
        });
        var plan = (await made.Content.ReadFromJsonAsync<PlanView>())!;
        await alice.PostAsJsonAsync($"/api/plans/{plan.Id}/fix", new FixPlanRequest { At = plan.Times[0].At });
        await fixture.Services.GetRequiredService<PlanKeeper>().PassAsync(CancellationToken.None);
        var open = (await (await alice.GetAsync($"/api/plans/{plan.Id}")).Content.ReadFromJsonAsync<PlanView>())!;
        Assert.Equal(PlanStates.Open, open.State);

        await using var before = await fixture.ConnectOutsideAsync("plan-dee-open", "Dee");
        Assert.False((await before.InvokeAsync<JoinReply>("Join", open.RoomId!)).Admitted);

        Assert.Equal(HttpStatusCode.OK, (await bea.PostAsJsonAsync($"/api/plans/{plan.Id}/people", new InviteRequest { UserId = "plan-dee-open" })).StatusCode);

        await using var after = await fixture.ConnectOutsideAsync("plan-dee-open", "Dee");
        Assert.True((await after.InvokeAsync<JoinReply>("Join", open.RoomId!)).Admitted);
    }

    [Fact]
    public async Task A_plan_whose_times_have_all_passed_is_kept_for_a_new_time()
    {
        var store = fixture.Services.GetRequiredService<PlanStore>();
        var past = DateTimeOffset.UtcNow.AddHours(-2);
        store.Make("plan-past", Heat, "film", null, null, "plan-alice-past", false, [PlanRules.Minute(past)], [], past.AddDays(-1));

        await fixture.Services.GetRequiredService<PlanKeeper>().PassAsync(CancellationToken.None);

        Assert.Equal(PlanStates.Proposed, store.Get("plan-past")!.State);
    }

    [Fact]
    public async Task A_film_that_is_not_out_cannot_be_planned()
    {
        var alice = fixture.CreatePersonClient("plan-alice-notout", "Alice");

        // The film database describes it and the tracker holds no release of it at any source.
        var made = await alice.PostAsJsonAsync("/api/plans", new MakePlanRequest
        {
            ImdbId = "tt0325980", Kind = "film", Times = [DateTimeOffset.UtcNow.AddDays(1)], People = [],
        });

        Assert.Equal(HttpStatusCode.Conflict, made.StatusCode);
        Assert.Empty((await (await alice.GetAsync("/api/me/plans")).Content.ReadFromJsonAsync<List<PlanView>>())!);
    }
}
