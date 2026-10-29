using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.MovieBot.Api;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Votes;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// What people like and dislike: a vote is its person's alone, is kept under an id that outlives the
/// catalogue (a person by the film database's id) and is told back under the id the catalogue's routes
/// take, and a subject the catalogue lists is described from it.
/// </summary>
public sealed class VoteTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private const string Heat = "tt0113277";
    private const string Jared = "jared";
    private const int JaredTmdb = 501;
    private const string Collection = "c9485";
    private const string Hbo = "49";

    private HttpClient As(string userId) => fixture.CreatePersonClient(userId, "Cy", [CinemaActions.Watch]);

    private static Task<HttpResponseMessage> Cast(HttpClient client, string kind, string id, int vote) =>
        client.PutAsJsonAsync($"/api/me/votes/{kind}/{id}", new SetVoteRequest { Vote = vote }, ApiJsonContext.Default.SetVoteRequest);

    private static async Task<List<VoteView>> Votes(HttpClient client) =>
        (await client.GetFromJsonAsync("/api/me/votes", ApiJsonContext.Default.ListVoteView))!;

    private async Task<string> JaredId(HttpClient client)
    {
        var terms = await client.GetFromJsonAsync<List<TheKrystalShip.MovieBot.Catalogue.Term>>(
            "/api/catalogue/terms?q=jared", new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        return terms!.First(t => t.Filter == "person").Value;
    }

    private VoteStore Store => fixture.Services.GetRequiredService<VoteStore>();

    [Fact]
    public async Task A_vote_is_cast_moved_and_cleared()
    {
        var me = As("vote-1");

        using var liked = await Cast(me, "title", Heat, 1);
        var first = (await liked.Content.ReadFromJsonAsync(ApiJsonContext.Default.VoteView))!;
        using var moved = await Cast(me, "title", Heat, -1);
        var second = (await moved.Content.ReadFromJsonAsync(ApiJsonContext.Default.VoteView))!;

        Assert.Equal(HttpStatusCode.OK, liked.StatusCode);
        Assert.Equal(("title", Heat, 1), (first.Kind, first.Id, first.Vote));
        Assert.Equal(-1, second.Vote);
        Assert.True(second.VotedAt >= first.VotedAt);
        Assert.Equal(-1, Assert.Single(await Votes(me)).Vote);

        using var cleared = await me.DeleteAsync($"/api/me/votes/title/{Heat}");
        using var again = await me.DeleteAsync($"/api/me/votes/title/{Heat}");

        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Empty(await Votes(me));
    }

    [Fact]
    public async Task Every_kind_is_named_from_the_catalogue_and_a_title_carries_its_card_joined_for_the_person()
    {
        var me = As("vote-2");
        var jared = await JaredId(me);

        foreach (var (kind, id) in new[] { ("title", Heat), ("person", jared), ("franchise", Collection), ("company", Hbo) })
            (await Cast(me, kind, id, 1)).EnsureSuccessStatusCode();
        var votes = (await Votes(me)).ToDictionary(v => v.Kind);

        Assert.Equal(4, votes.Count);
        Assert.Equal("Heat", votes["title"].Name);
        Assert.Null(votes["title"].Image);
        Assert.Equal(Heat, votes["title"].Card!.Id);
        Assert.True(votes["title"].Card!.You!.InLibrary);

        Assert.Equal((jared, "Jared Harris"), (votes["person"].Id, votes["person"].Name));
        Assert.Equal("/images/w185/jared.jpg", votes["person"].Image);
        Assert.Null(votes["person"].Card);

        Assert.Equal((Collection, "The Fast and the Furious Collection"), (votes["franchise"].Id, votes["franchise"].Name));
        Assert.StartsWith("/images/w500/", votes["franchise"].Image);

        Assert.Equal((Hbo, "HBO"), (votes["company"].Id, votes["company"].Name));
        Assert.Null(votes["company"].Image);
    }

    [Fact]
    public async Task A_person_is_kept_by_the_film_database_s_id_and_told_back_by_the_catalogue_s()
    {
        var me = As("vote-3");
        var jared = await JaredId(me);

        var reply = (await (await Cast(me, "person", jared, -1)).Content.ReadFromJsonAsync(ApiJsonContext.Default.VoteView))!;

        Assert.Equal(jared, reply.Id);
        var stored = Assert.Single(Store.For("vote-3"));
        Assert.Equal(("person", JaredTmdb.ToString()), (stored.Kind, stored.Id));
        Assert.NotEqual(jared, stored.Id);
        Assert.Equal(jared, Assert.Single(await Votes(me)).Id);

        using var cleared = await me.DeleteAsync($"/api/me/votes/person/{jared}");
        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);
        Assert.Empty(Store.For("vote-3"));
    }

    [Fact]
    public async Task A_vote_is_nobody_else_s_and_the_service_key_names_nobody()
    {
        await Cast(As("vote-4"), "title", Heat, 1);

        Assert.Empty(await Votes(As("vote-5")));
        using var service = await fixture.CreateServiceClient().GetAsync("/api/me/votes");
        Assert.Equal(HttpStatusCode.Unauthorized, service.StatusCode);
    }

    [Theory]
    [InlineData("film", Heat, 1)]
    [InlineData("title", "heat", 1)]
    [InlineData("title", "tt", 1)]
    [InlineData("person", "jared", 1)]
    [InlineData("person", "007", 1)]
    [InlineData("franchise", "9485", 1)]
    [InlineData("franchise", "c", 1)]
    [InlineData("company", "s49", 1)]
    [InlineData("title", Heat, 0)]
    [InlineData("title", Heat, 2)]
    public async Task A_kind_an_id_or_a_vote_the_route_does_not_take_is_refused(string kind, string id, int vote)
    {
        var me = As("vote-6");

        using var response = await Cast(me, kind, id, vote);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await Votes(me));
    }

    [Theory]
    [InlineData("title", "tt9999999")]
    [InlineData("person", "987654321")]
    [InlineData("franchise", "c1")]
    [InlineData("franchise", "Q1")]
    [InlineData("company", "987654321")]
    public async Task A_subject_the_catalogue_does_not_list_is_not_found(string kind, string id)
    {
        var me = As("vote-7");

        using var response = await Cast(me, kind, id, 1);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(Store.For("vote-7"));
    }

    [Fact]
    public async Task A_vote_the_catalogue_no_longer_describes_stays_with_only_what_was_voted_and_an_unmapped_person_is_left_out()
    {
        var at = DateTimeOffset.Parse("2026-03-01T10:00:00Z");
        Store.Set("vote-8", "title", "tt9999999", 1, at);
        Store.Set("vote-8", "franchise", "c1", -1, at.AddMinutes(1));
        Store.Set("vote-8", "company", "987654321", 1, at.AddMinutes(2));
        Store.Set("vote-8", "person", "424242", 1, at.AddMinutes(3));

        var votes = await Votes(As("vote-8"));

        Assert.Equal(["company", "franchise", "title"], votes.Select(v => v.Kind).Order(StringComparer.Ordinal));
        Assert.All(votes, v => Assert.True(v.Name is null && v.Image is null && v.Card is null));
        Assert.Equal(4, Store.For("vote-8").Count);
    }

    [Fact]
    public void Votes_are_listed_newest_first_and_casting_again_moves_one_to_the_front()
    {
        var at = DateTimeOffset.Parse("2026-03-01T10:00:00Z");
        var store = Store;
        store.Set("vote-9", "title", "tt0000001", 1, at);
        store.Set("vote-9", "company", "7", -1, at.AddMinutes(1));
        store.Set("vote-9", "title", "tt0000002", 1, at.AddMinutes(2));

        Assert.Equal(["tt0000002", "7", "tt0000001"], store.For("vote-9").Select(v => v.Id));

        store.Set("vote-9", "title", "tt0000001", -1, at.AddMinutes(3));

        Assert.Equal(["tt0000001", "tt0000002", "7"], store.For("vote-9").Select(v => v.Id));
        Assert.Equal(-1, store.For("vote-9")[0].Vote);
        Assert.Equal(at.AddMinutes(3), store.For("vote-9")[0].VotedAt);
    }
}
