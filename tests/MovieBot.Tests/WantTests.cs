using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Wants;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// What people want to watch: a want is the person's the token names, is named to others only while it
/// is visible, carries what the token could spend when it was written, and ends when the person
/// finishes the title.
/// </summary>
public sealed class WantTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private const string HeatImdb = "tt0113277";
    private const string NotOutImdb = "tt0325980";
    private const string QuietImdb = FilmDatabaseStub.SilverHarbour;

    private HttpClient As(string userId, string name = "Cy", IEnumerable<string>? actions = null)
    {
        fixture.Services.GetRequiredService<PersonDirectory>().Remember(userId, name);
        return fixture.CreatePersonClient(userId, name, actions);
    }

    private static Task<HttpResponseMessage> Want(HttpClient client, string imdbId = HeatImdb, string kind = "film", bool? visible = null) =>
        client.PutAsJsonAsync($"/api/me/wants/{imdbId}", new SetWantRequest { Kind = kind, Visible = visible }, ManifestJsonContext.Default.SetWantRequest);

    private static async Task<SetWantReply> Made(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync(ManifestJsonContext.Default.SetWantReply))!;
    }

    private static async Task<IReadOnlyList<WantView>> Wants(HttpClient client) =>
        (await client.GetFromJsonAsync("/api/me/wants", ManifestJsonContext.Default.IReadOnlyListWantView))!;

    private static async Task<IReadOnlyList<WanterView>> Wanters(HttpClient client, string imdbId = HeatImdb) =>
        (await client.GetFromJsonAsync($"/api/titles/{imdbId}/wanters", ManifestJsonContext.Default.IReadOnlyListWanterView))!;

    [Fact]
    public async Task Nobody_wants_anything_until_they_say_so()
    {
        Assert.Empty(await Wants(As("9001")));
    }

    [Fact]
    public async Task A_want_of_a_film_the_library_holds_is_described_ready_and_visible()
    {
        var me = As("9002");

        var reply = await Made(await Want(me));

        Assert.Equal(WantOutcomes.InLibrary, reply.Outcome);
        Assert.Equal(SessionFixture.Heat, reply.LibraryId);
        Assert.Equal("Heat (1995)", reply.LibraryName);
        var want = Assert.Single(await Wants(me));
        Assert.Equal(HeatImdb, want.ImdbId);
        Assert.Equal("Heat", want.Title);
        Assert.Equal(1995, want.Year);
        Assert.Equal("film", want.Kind);
        Assert.True(want.Visible);
        Assert.True(want.InLibrary);
        Assert.Equal(SessionFixture.Heat, want.LibraryId);
        Assert.Equal(WantStates.Ready, want.State);
        Assert.False(want.Watched);
        Assert.Null(want.Wish);
    }

    [Fact]
    public async Task Wanting_again_changes_only_whether_it_is_named_and_keeps_when_it_began()
    {
        var me = As("9003");
        var first = (await Made(await Want(me))).Want!;

        var again = await Made(await Want(me, visible: false));
        Assert.Equal(WantOutcomes.AlreadyWanted, again.Outcome);
        Assert.False(again.Want!.Visible);
        Assert.Equal(first.Since, again.Want.Since);

        // A request that says nothing of visibility leaves it as it is.
        Assert.False((await Made(await Want(me))).Want!.Visible);
        Assert.True((await Made(await Want(me, visible: true))).Want!.Visible);
    }

    [Fact]
    public async Task A_want_comes_off_and_a_second_removal_says_there_was_none()
    {
        var me = As("9004");
        await Made(await Want(me));

        using var removed = await me.DeleteAsync($"/api/me/wants/{HeatImdb}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Empty(await Wants(me));

        using var again = await me.DeleteAsync($"/api/me/wants/{HeatImdb}");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task A_title_the_film_database_does_not_know_a_kind_that_is_neither_or_a_typo_is_refused()
    {
        var me = As("9005");

        using var unknown = await Want(me, "tt9999999");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        using var episode = await Want(me, kind: "episode");
        Assert.Equal(HttpStatusCode.BadRequest, episode.StatusCode);

        using var typo = await Want(me, "heat");
        Assert.Equal(HttpStatusCode.BadRequest, typo.StatusCode);

        Assert.Empty(await Wants(me));
    }

    [Fact]
    public async Task Other_people_see_a_visible_want_as_a_count_and_a_name_and_a_just_me_want_as_nothing()
    {
        var ana = As("9010", "Ana");
        var bo = As("9011", "Bo");
        var cid = As("9012", "Cid");
        await Made(await Want(ana, QuietImdb));
        await Made(await Want(bo, QuietImdb, visible: false));

        // Ana is named to Bo and Cid; Bo is named to nobody, and counts for nobody but Bo.
        Assert.Equal(["Ana"], (await Wanters(bo, QuietImdb)).Select(w => w.Name));
        Assert.Equal(["Ana"], (await Wanters(cid, QuietImdb)).Select(w => w.Name));
        Assert.Empty(await Wanters(ana, QuietImdb));
        Assert.Equal(0, Assert.Single(await Wants(ana)).Others);
        Assert.Equal(1, Assert.Single(await Wants(bo)).Others);

        // Making Bo's visible names both to the other.
        await Made(await Want(bo, QuietImdb, visible: true));
        Assert.Equal(["Bo"], (await Wanters(ana, QuietImdb)).Select(w => w.Name));
        Assert.Equal(1, Assert.Single(await Wants(ana)).Others);

        // Nothing about whose want it is rides in a want.
        var raw = await ana.GetStringAsync("/api/me/wants");
        Assert.DoesNotContain("9010", raw);
        Assert.DoesNotContain("userId", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_token_decides_what_the_server_may_spend_on_a_want_each_time_it_is_written()
    {
        var store = fixture.Services.GetRequiredService<WantStore>();

        await Made(await Want(As("9020", "Dee", [CinemaActions.Watch])));
        var plain = store.Get("9020", HeatImdb)!;
        Assert.False(plain.MayFetch);
        Assert.False(plain.MayWish);

        await Made(await Want(As("9021", "Eli", [CinemaActions.Watch, CinemaActions.Download, CinemaActions.Wish])));
        var able = store.Get("9021", HeatImdb)!;
        Assert.True(able.MayFetch);
        Assert.True(able.MayWish);

        // The same person writing it again with a token that holds less.
        await Made(await Want(As("9021", "Eli", [CinemaActions.Watch])));
        var after = store.Get("9021", HeatImdb)!;
        Assert.False(after.MayFetch);
        Assert.False(after.MayWish);
    }

    [Fact]
    public async Task A_film_nothing_has_gets_a_wish_for_somebody_who_may_wish_and_none_for_somebody_who_may_not()
    {
        var wishes = fixture.Services.GetRequiredService<Api.Acquisition.WishList>();
        var able = As("9030", "Fay", [CinemaActions.Watch, CinemaActions.Wish]);

        var reply = await Made(await Want(able, NotOutImdb));

        Assert.Equal(WantOutcomes.Waiting, reply.Outcome);
        Assert.Equal(WantStates.Waiting, reply.Want!.State);
        Assert.NotNull(reply.Want.Wish);
        Assert.Null(reply.Want.Wish.Release);
        var kept = Assert.Single(wishes.All(), w => w.ImdbId == NotOutImdb);
        Assert.Equal("9030", Assert.Single(kept.Subscribers).UserId);

        // Somebody who may only watch is not asked for a wish, and is not a subscriber to the one kept.
        var plain = await Made(await Want(As("9031", "Gus", [CinemaActions.Watch]), NotOutImdb));
        Assert.Equal(WantOutcomes.Wanted, plain.Outcome);
        Assert.Equal(["9030"], wishes.Find(NotOutImdb)!.Subscribers.Select(s => s.UserId));

        // The wish goes with the last wanter that could ask for it.
        using var removed = await able.DeleteAsync($"/api/me/wants/{NotOutImdb}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Null(wishes.Find(NotOutImdb));
    }

    [Fact]
    public async Task Wanting_takes_the_watch_action_and_listing_takes_the_self_action_and_the_service_key_is_refused()
    {
        var listOnly = fixture.CreatePersonClient("9040", "Hal", [CinemaActions.ProfileRead]);
        using var refused = await Want(listOnly);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Empty(await Wants(listOnly));

        var service = fixture.CreateServiceClient();
        using var listed = await service.GetAsync("/api/me/wants");
        Assert.Equal(HttpStatusCode.Unauthorized, listed.StatusCode);
        using var wanted = await Want(service);
        Assert.Equal(HttpStatusCode.Unauthorized, wanted.StatusCode);
    }

    [Fact]
    public async Task Finishing_a_film_ends_the_want_and_stopping_short_does_not()
    {
        var record = fixture.Services.GetRequiredService<WatchRecord>();
        var me = As("9050");
        await Made(await Want(me));

        record.Record("9050", SessionFixture.Heat, 3000, 6000, DateTimeOffset.UtcNow);
        Assert.Single(await Wants(me));

        record.Record("9050", SessionFixture.Heat, 5900, 6000, DateTimeOffset.UtcNow);
        Assert.Empty(await Wants(me));
    }

    [Fact]
    public async Task What_a_person_watched_marks_a_film_they_want_again()
    {
        var record = fixture.Services.GetRequiredService<WatchRecord>();
        record.Record("9051", SessionFixture.Heat, 5900, 6000, DateTimeOffset.UtcNow);
        var me = As("9051");

        await Made(await Want(me));

        Assert.True(Assert.Single(await Wants(me)).Watched);
    }
}
