using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A person saying they watched something, here or anywhere, and taking it back: what each list,
/// page and Continue watching then says. Every test names its own people and shows, because the
/// fixture is shared by the class.
/// </summary>
public sealed class WatchedMarkTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string Day(int days) => DateTime.UtcNow.Date.AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// A show the catalogue lists: S1E1 to S1E3 aired, S1E4 airing in a month; the library holds the
    /// episodes named in <paramref name="held"/>.
    /// </summary>
    private string Show(string imdbId, string name, params int[] held)
    {
        foreach (var number in held) fixture.AddEpisode($"{name}-s01e{number:00}", imdbId, name, 1, number);
        fixture.KeepShow(imdbId, int.Parse(imdbId[2..], CultureInfo.InvariantCulture), name,
        [
            new(1, 1, "One", Day(-30), 44),
            new(1, 2, "Two", Day(-23), 44),
            new(1, 3, "Three", Day(-16), 44),
            new(1, 4, "Four", Day(30), 44),
        ]);
        return imdbId;
    }

    private static async Task<HttpResponseMessage> Mark(HttpClient client, object change) =>
        await client.PostAsJsonAsync("/api/me/watched", change);

    private static async Task Marked(HttpClient client, object change)
    {
        using var response = await Mark(client, change);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<List<ProgressEntry>> Progress(HttpClient client) =>
        (await client.GetFromJsonAsync<List<ProgressEntry>>("/api/me/progress", Json))!;

    private static async Task<Dictionary<int, bool>> Watched(HttpClient client, string imdbId)
    {
        var show = await client.GetFromJsonAsync<JsonElement>($"/api/me/shows/{imdbId}");
        return show.GetProperty("seasons")[0].GetProperty("episodes").EnumerateArray()
            .ToDictionary(e => e.GetProperty("number").GetInt32(), e => e.GetProperty("watched").GetBoolean());
    }

    private static async Task<JsonElement?> HeatProgress(HttpClient client)
    {
        var all = await client.GetFromJsonAsync<JsonElement>("/api/titles");
        var heat = all.EnumerateArray().First(t => t.GetProperty("id").GetString() == SessionFixture.Heat);
        return heat.TryGetProperty("progress", out var progress) && progress.ValueKind == JsonValueKind.Object ? progress.Clone() : null;
    }

    private static async Task<bool> SawHeat(HttpClient client)
    {
        var page = await client.GetFromJsonAsync<JsonElement>("/api/catalogue/titles/tt0113277");
        return page.GetProperty("you").GetProperty("watched").GetBoolean();
    }

    [Fact]
    public async Task A_film_marked_watched_reads_as_watched_through_and_taken_back_reads_as_never_watched()
    {
        var client = fixture.CreatePersonClient("wm-film");
        using (var sent = await client.PostAsJsonAsync("/api/me/progress",
                   new[] { new { titleId = SessionFixture.Heat, positionSeconds = 600.0, watchedAt = DateTimeOffset.UtcNow.AddHours(-1) } }))
            sent.EnsureSuccessStatusCode();

        await Marked(client, new { imdbId = "tt0113277", kind = "film", watched = true });

        Assert.True((await HeatProgress(client))!.Value.GetProperty("finished").GetBoolean());
        Assert.True(await SawHeat(client));
        Assert.DoesNotContain(await Progress(client), e => e.TitleId == SessionFixture.Heat);

        await Marked(client, new { imdbId = "tt0113277", kind = "film", watched = false });

        Assert.Null(await HeatProgress(client));
        Assert.False(await SawHeat(client));
    }

    [Fact]
    public async Task Watching_again_after_marking_is_partway_through_again()
    {
        var client = fixture.CreatePersonClient("wm-again");
        await Marked(client, new { imdbId = "tt0113277", kind = "film", watched = true });
        await Task.Delay(20);
        using (var sent = await client.PostAsJsonAsync("/api/me/progress",
                   new[] { new { titleId = SessionFixture.Heat, positionSeconds = 300.0, watchedAt = DateTimeOffset.UtcNow } }))
            sent.EnsureSuccessStatusCode();

        var progress = (await HeatProgress(client))!.Value;
        Assert.False(progress.GetProperty("finished").GetBoolean());
        Assert.Equal(300, progress.GetProperty("positionSeconds").GetDouble());
    }

    [Fact]
    public async Task Marking_a_film_watched_ends_the_want_for_it()
    {
        var client = fixture.CreatePersonClient("wm-want");
        using (var wanted = await client.PutAsJsonAsync("/api/me/wants/tt0113277", new { kind = "film" }))
            wanted.EnsureSuccessStatusCode();

        await Marked(client, new { imdbId = "tt0113277", kind = "film", watched = true });

        var wants = await client.GetFromJsonAsync<JsonElement>("/api/me/wants");
        Assert.DoesNotContain(wants.EnumerateArray(), w => w.GetProperty("imdbId").GetString() == "tt0113277");
    }

    [Fact]
    public async Task A_season_marked_watched_is_every_episode_of_it_that_has_aired()
    {
        var show = Show("tt7710001", "wm-season", 1, 2);
        var client = fixture.CreatePersonClient("wm-season");

        await Marked(client, new { imdbId = show, kind = "show", season = 1, watched = true });

        Assert.Equal(new Dictionary<int, bool> { [1] = true, [2] = true, [3] = true, [4] = false }, await Watched(client, show));
        Assert.DoesNotContain(await Progress(client), e => e.ShowImdbId == show);

        await Marked(client, new { imdbId = show, kind = "show", season = 1, episode = 2, watched = false });

        Assert.False((await Watched(client, show))[2]);
    }

    [Fact]
    public async Task An_episode_watched_elsewhere_is_carried_on_from_the_one_after_it_here()
    {
        var show = Show("tt7710002", "wm-elsewhere", 2, 3);
        var client = fixture.CreatePersonClient("wm-elsewhere");

        await Marked(client, new { imdbId = show, kind = "show", season = 1, episode = 1, watched = true });

        var next = Assert.Single(await Progress(client), e => e.ShowImdbId == show);
        Assert.Equal("wm-elsewhere-s01e02", next.TitleId);
        Assert.Equal(0, next.PositionSeconds);
    }

    [Fact]
    public async Task A_held_episode_marked_watched_is_followed_by_the_next_one()
    {
        var show = Show("tt7710003", "wm-held", 1, 2);
        var client = fixture.CreatePersonClient("wm-held");

        await Marked(client, new { imdbId = show, kind = "show", season = 1, episode = 1, watched = true });

        var next = Assert.Single(await Progress(client), e => e.ShowImdbId == show);
        Assert.Equal("wm-held-s01e02", next.TitleId);
    }

    [Fact]
    public async Task A_show_marked_watched_through_its_aired_episodes_ends_the_want_for_it()
    {
        var show = Show("tt7710004", "wm-showwant", 1);
        var client = fixture.CreatePersonClient("wm-showwant");
        using (var wanted = await client.PutAsJsonAsync($"/api/me/wants/{show}", new { kind = "show" }))
            wanted.EnsureSuccessStatusCode();

        await Marked(client, new { imdbId = show, kind = "show", watched = true });

        var wants = await client.GetFromJsonAsync<JsonElement>("/api/me/wants");
        Assert.DoesNotContain(wants.EnumerateArray(), w => w.GetProperty("imdbId").GetString() == show);
    }

    [Theory]
    [InlineData("""{"imdbId":"tt0113277","kind":"film","season":1,"watched":true}""", HttpStatusCode.BadRequest)]
    [InlineData("""{"imdbId":"heat","kind":"film","watched":true}""", HttpStatusCode.BadRequest)]
    [InlineData("""{"imdbId":"tt0113277","kind":"book","watched":true}""", HttpStatusCode.BadRequest)]
    [InlineData("""{"imdbId":"tt7710005","kind":"show","episode":2,"watched":true}""", HttpStatusCode.BadRequest)]
    [InlineData("""{"imdbId":"tt7710005","kind":"show","season":1,"episode":4,"watched":true}""", HttpStatusCode.NotFound)]
    public async Task A_change_that_names_nothing_watchable_is_refused(string body, HttpStatusCode expected)
    {
        Show("tt7710005", "wm-refused", 1);
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using var response = await fixture.CreatePersonClient("wm-refused").PostAsync("/api/me/watched", content);

        Assert.Equal(expected, response.StatusCode);
    }
}
