using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TheKrystalShip.MovieBot.Api.Lists;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Lists of films and shows to watch later: every person's own Watch later, the lists they name, and
/// the people they put on them. A list is shown to its owner and its members and to nobody else.
/// </summary>
public sealed class ListTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private const string HeatImdb = "tt0113277";

    private HttpClient As(string userId, string name = "Cy")
    {
        fixture.Services.GetRequiredService<PersonDirectory>().Remember(userId, name);
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", RoomToken.Issue(new RoomTokenPayload
        {
            UserId = userId, DisplayName = name, RoomId = "", Member = true,
            ExpiresAtUnix = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
        }, SessionFixture.SigningKey));
        return client;
    }

    private static Task<HttpResponseMessage> Save(HttpClient client, string list, string imdbId = HeatImdb, string kind = "film", string? note = null) =>
        client.PutAsJsonAsync($"/api/lists/{list}/entries/{imdbId}", new SaveEntryRequest { Kind = kind, Note = note });

    private static async Task<ListView> Open(HttpClient client, string list) =>
        (await client.GetFromJsonAsync<ListView>($"/api/lists/{list}"))!;

    private static async Task<string> Create(HttpClient client, string name)
    {
        using var created = await client.PostAsJsonAsync("/api/lists", new ListNameRequest { Name = name });
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<ListView>())!.List.Id;
    }

    [Fact]
    public async Task Everybody_has_a_watch_later_listed_first_before_saving_anything()
    {
        var lists = await As("8001").GetFromJsonAsync<List<ListSummary>>("/api/me/lists");

        var later = Assert.Single(lists!);
        Assert.Equal(ListStore.WatchLaterId, later.Id);
        Assert.True(later.WatchLater);
        Assert.Equal(ListRoles.Owner, later.Role);
        Assert.Equal(0, later.Count);
    }

    [Fact]
    public async Task A_saved_film_is_described_joined_with_the_library_and_keeps_when_it_was_first_saved()
    {
        var me = As("8002");
        using var first = await Save(me, "later");
        first.EnsureSuccessStatusCode();
        var saved = (await first.Content.ReadFromJsonAsync<ListEntryView>())!;

        Assert.Equal("Heat", saved.Title);
        Assert.Equal(1995, saved.Year);
        Assert.Equal(SessionFixture.Heat, saved.LibraryId);
        Assert.True(saved.InLibrary);
        Assert.Equal("8002", saved.AddedBy.UserId);
        Assert.Equal("Cy", saved.AddedBy.Name);

        // Saving it again changes the note and nothing else.
        using var again = await Save(me, "later", note: "  the one with the bank  ");
        again.EnsureSuccessStatusCode();
        var view = await Open(me, "later");
        var entry = Assert.Single(view.Entries);
        Assert.Equal(saved.AddedAt, entry.AddedAt);
        Assert.Equal("the one with the bank", entry.Note);

        // An absent note leaves it; an empty one clears it.
        (await Save(me, "later")).EnsureSuccessStatusCode();
        Assert.Equal("the one with the bank", Assert.Single((await Open(me, "later")).Entries).Note);
        (await Save(me, "later", note: "")).EnsureSuccessStatusCode();
        Assert.Null(Assert.Single((await Open(me, "later")).Entries).Note);
    }

    [Fact]
    public async Task Entries_are_listed_newest_first_and_come_off_again()
    {
        var store = fixture.Services.GetRequiredService<ListStore>();
        var at = DateTimeOffset.UtcNow;
        store.Save("8003", "later", "tt0369339", EntryKinds.Film, null, at.AddDays(-3));
        store.Save("8003", "later", HeatImdb, EntryKinds.Film, null, at);

        var me = As("8003");
        Assert.Equal([HeatImdb, "tt0369339"], (await Open(me, "later")).Entries.Select(e => e.ImdbId));

        using var removed = await me.DeleteAsync($"/api/lists/later/entries/{HeatImdb}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(["tt0369339"], (await Open(me, "later")).Entries.Select(e => e.ImdbId));
    }

    [Fact]
    public async Task A_title_the_film_database_does_not_know_or_a_kind_that_is_neither_is_refused()
    {
        var me = As("8004");

        using var unknown = await Save(me, "later", imdbId: "tt9999999");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        using var episode = await Save(me, "later", kind: "episode");
        Assert.Equal(HttpStatusCode.BadRequest, episode.StatusCode);

        using var typo = await Save(me, "later", imdbId: "heat");
        Assert.Equal(HttpStatusCode.BadRequest, typo.StatusCode);

        Assert.Empty((await Open(me, "later")).Entries);
    }

    [Fact]
    public async Task A_film_watched_through_is_marked_watched_for_the_person_who_watched_it()
    {
        var record = fixture.Services.GetRequiredService<WatchRecord>();
        record.Record("8005", SessionFixture.Heat, 5900, 6000, DateTimeOffset.UtcNow);

        var watcher = As("8005");
        (await Save(watcher, "later")).EnsureSuccessStatusCode();
        Assert.True(Assert.Single((await Open(watcher, "later")).Entries).Watched);

        var other = As("8006");
        (await Save(other, "later")).EnsureSuccessStatusCode();
        Assert.False(Assert.Single((await Open(other, "later")).Entries).Watched);
    }

    [Fact]
    public async Task Asked_about_a_title_each_list_says_whether_it_holds_it()
    {
        var me = As("8007");
        var named = await Create(me, "Heists");
        (await Save(me, named)).EnsureSuccessStatusCode();

        var lists = (await me.GetFromJsonAsync<List<ListSummary>>($"/api/me/lists?holding={HeatImdb}"))!;

        Assert.Equal([ListStore.WatchLaterId, named], lists.Select(l => l.Id));
        Assert.Equal([false, true], lists.Select(l => l.Holds));
        Assert.All((await me.GetFromJsonAsync<List<ListSummary>>("/api/me/lists"))!, l => Assert.Null(l.Holds));
    }

    [Fact]
    public async Task A_shared_list_is_read_by_a_viewer_added_to_by_an_editor_and_hidden_from_everyone_else()
    {
        var owner = As("8010", "Ana");
        var member = As("8011", "Bo");
        var stranger = As("8012", "Cid");
        var id = await Create(owner, "Friday night");

        using (var share = await owner.PutAsJsonAsync($"/api/lists/{id}/members/8011", new ListMemberRequest { Role = "viewer" }))
            share.EnsureSuccessStatusCode();

        var seen = await Open(member, id);
        Assert.Equal("Friday night", seen.List.Name);
        Assert.Equal(ListRoles.Viewer, seen.List.Role);
        Assert.Equal("Ana", seen.List.Owner.Name);

        using (var refused = await Save(member, id))
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        using (var promote = await owner.PutAsJsonAsync($"/api/lists/{id}/members/8011", new ListMemberRequest { Role = "editor" }))
            promote.EnsureSuccessStatusCode();
        (await Save(member, id)).EnsureSuccessStatusCode();
        Assert.Equal("Bo", Assert.Single((await Open(owner, id)).Entries).AddedBy.Name);

        // A member renames, deletes and shares nothing; somebody not on the list cannot tell it exists.
        using (var rename = await member.PatchAsJsonAsync($"/api/lists/{id}", new ListNameRequest { Name = "Mine now" }))
            Assert.Equal(HttpStatusCode.Forbidden, rename.StatusCode);
        using (var delete = await member.DeleteAsync($"/api/lists/{id}"))
            Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        using (var reshare = await member.PutAsJsonAsync($"/api/lists/{id}/members/8012", new ListMemberRequest { Role = "editor" }))
            Assert.Equal(HttpStatusCode.Forbidden, reshare.StatusCode);
        using (var hidden = await stranger.GetAsync($"/api/lists/{id}"))
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using (var blind = await Save(stranger, id))
            Assert.Equal(HttpStatusCode.NotFound, blind.StatusCode);
        Assert.Single((await stranger.GetFromJsonAsync<List<ListSummary>>("/api/me/lists"))!);
    }

    [Fact]
    public async Task Somebody_put_on_a_list_is_told_once()
    {
        var owner = As("8020", "Ana");
        var member = As("8021", "Bo");
        var id = await Create(owner, "Documentaries");

        (await owner.PutAsJsonAsync($"/api/lists/{id}/members/8021", new ListMemberRequest { Role = "viewer" })).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"/api/lists/{id}/members/8021", new ListMemberRequest { Role = "editor" })).EnsureSuccessStatusCode();

        var inbox = (await member.GetFromJsonAsync<NotificationsReply>("/api/me/notifications"))!;
        var told = Assert.Single(inbox.Items, n => n.Kind == NotificationKinds.Shared);
        Assert.Equal("Ana shared their list Documentaries with you.", told.Text);
        Assert.Equal($"/saved/{id}", told.Url);
    }

    [Fact]
    public async Task A_member_leaves_and_the_owner_deletes()
    {
        var owner = As("8030", "Ana");
        var member = As("8031", "Bo");
        var id = await Create(owner, "Leaving soon");
        (await owner.PutAsJsonAsync($"/api/lists/{id}/members/8031", new ListMemberRequest { Role = "editor" })).EnsureSuccessStatusCode();

        using (var leave = await member.DeleteAsync($"/api/lists/{id}/members/8031"))
            Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);
        using (var gone = await member.GetAsync($"/api/lists/{id}"))
            Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);

        using (var delete = await owner.DeleteAsync($"/api/lists/{id}"))
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        using (var deleted = await owner.GetAsync($"/api/lists/{id}"))
            Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
    }

    [Fact]
    public async Task Watch_later_is_its_owners_alone()
    {
        var me = As("8040");
        As("8041");

        using (var share = await me.PutAsJsonAsync("/api/lists/later/members/8041", new ListMemberRequest { Role = "viewer" }))
            Assert.Equal(HttpStatusCode.Conflict, share.StatusCode);
        using (var rename = await me.PatchAsJsonAsync("/api/lists/later", new ListNameRequest { Name = "Later" }))
            Assert.Equal(HttpStatusCode.Conflict, rename.StatusCode);
        using (var delete = await me.DeleteAsync("/api/lists/later"))
            Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);

        // Somebody else's "later" is their own.
        (await Save(me, "later")).EnsureSuccessStatusCode();
        Assert.Empty((await Open(As("8041"), "later")).Entries);
    }

    [Fact]
    public async Task A_name_is_trimmed_and_a_blank_one_refused()
    {
        var me = As("8050");

        using (var blank = await me.PostAsJsonAsync("/api/lists", new ListNameRequest { Name = "   " }))
            Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);

        var id = await Create(me, "  Rainy days  ");
        Assert.Equal("Rainy days", (await Open(me, id)).List.Name);

        using var renamed = await me.PatchAsJsonAsync($"/api/lists/{id}", new ListNameRequest { Name = "Sunday" });
        renamed.EnsureSuccessStatusCode();
        Assert.Equal("Sunday", (await Open(me, id)).List.Name);
    }

    [Fact]
    public void Lists_survive_a_restart()
    {
        var path = Path.Combine(fixture.StateRoot, $"lists-{Guid.NewGuid():N}.json");
        var at = DateTimeOffset.UtcNow;
        var first = new ListStore(path, NullLogger<ListStore>.Instance);
        var (list, _) = first.Create("8060", "Kept", at);
        first.Save("8060", list!.Id, HeatImdb, EntryKinds.Film, "note", at);
        first.SetMember("8060", list.Id, "8061", ListRoles.Viewer, at);

        var again = new ListStore(path, NullLogger<ListStore>.Instance);
        var read = again.Get("8061", list.Id, at);
        Assert.NotNull(read);
        Assert.Equal("note", Assert.Single(read.Entries).Note);
        Assert.Equal(ListRoles.Viewer, read.RoleOf("8061"));
    }
}
