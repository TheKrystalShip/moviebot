using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Text.Json;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Continue watching: each film started and not finished, and for each show the episode to carry on
/// with, whichever episode the person started on.
/// </summary>
public sealed class ContinueWatchingTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A position past the share of an episode that counts as having watched it.</summary>
    private const double Finished = SessionFixture.TitleDuration * 0.95;

    /// <summary>A show of its own for one test: S1E1, S1E2, S1E3 and S2E1, which the catalogue does not list.</summary>
    private string Show(string name)
    {
        var imdbId = $"tt-{name}";
        foreach (var (season, number) in new[] { (1, 1), (1, 2), (1, 3), (2, 1) })
            fixture.AddEpisode($"{name}-s{season:00}e{number:00}", imdbId, name, season, number);
        return name;
    }

    private static async Task<List<ProgressEntry>> Watch(HttpClient client, params (string TitleId, double Position, int MinutesAgo)[] plays)
    {
        using var sent = await client.PostAsJsonAsync("/api/me/progress", plays.Select(p => new
        {
            titleId = p.TitleId, positionSeconds = p.Position, watchedAt = DateTimeOffset.UtcNow.AddMinutes(-p.MinutesAgo),
        }).ToArray());
        sent.EnsureSuccessStatusCode();
        return (await client.GetFromJsonAsync<List<ProgressEntry>>("/api/me/progress", Json))!;
    }

    [Fact]
    public async Task An_episode_watched_to_the_end_is_followed_by_the_next_one()
    {
        var show = Show("cw-next");
        var listed = await Watch(fixture.CreatePersonClient("cw1"), ($"{show}-s01e02", Finished, 5));

        var entry = Assert.Single(listed, e => e.ShowImdbId == $"tt-{show}");
        Assert.Equal($"{show}-s01e03", entry.TitleId);
        Assert.Equal(0, entry.PositionSeconds);
    }

    [Fact]
    public async Task The_last_episode_of_a_season_is_followed_by_the_first_of_the_next()
    {
        var show = Show("cw-season");
        var listed = await Watch(fixture.CreatePersonClient("cw2"), ($"{show}-s01e03", Finished, 5));

        Assert.Equal($"{show}-s02e01", Assert.Single(listed, e => e.ShowImdbId == $"tt-{show}").TitleId);
    }

    [Fact]
    public async Task A_show_is_listed_once_by_the_episode_watched_last()
    {
        var show = Show("cw-once");
        var listed = await Watch(fixture.CreatePersonClient("cw3"),
            ($"{show}-s01e01", 600, 60),
            ($"{show}-s01e03", 1200, 5));

        var entry = Assert.Single(listed, e => e.ShowImdbId == $"tt-{show}");
        Assert.Equal($"{show}-s01e03", entry.TitleId);
        Assert.Equal(1200, entry.PositionSeconds);
    }

    [Fact]
    public async Task The_next_episode_carries_on_from_where_it_was_left()
    {
        var show = Show("cw-begun");
        var listed = await Watch(fixture.CreatePersonClient("cw4"),
            ($"{show}-s01e03", 900, 60),
            ($"{show}-s01e02", Finished, 5));

        var entry = Assert.Single(listed, e => e.ShowImdbId == $"tt-{show}");
        Assert.Equal($"{show}-s01e03", entry.TitleId);
        Assert.Equal(900, entry.PositionSeconds);
    }

    /// <summary>
    /// A show the catalogue lists: S1E1 and S1E2 in the library, S1E3 aired and not here,
    /// S1E4 airing in a month.
    /// </summary>
    private string Described(string imdbId, string name)
    {
        fixture.AddEpisode($"{name}-s01e01", imdbId, name, 1, 1);
        fixture.AddEpisode($"{name}-s01e02", imdbId, name, 1, 2);
        string Day(int days) => DateTime.UtcNow.Date.AddDays(days).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        fixture.KeepShow(imdbId, int.Parse(imdbId[2..], System.Globalization.CultureInfo.InvariantCulture), name,
        [
            new(1, 1, null, Day(-30)),
            new(1, 2, null, Day(-23)),
            new(1, 3, "Gone Fishing", Day(-16), 44),
            new(1, 4, null, Day(30)),
        ]);
        return name;
    }

    [Fact]
    public async Task An_aired_next_episode_the_library_lacks_is_listed_to_fetch()
    {
        var show = Described("tt7700001", "cw-fetch");
        var listed = await Watch(fixture.CreatePersonClient("cw6"), ($"{show}-s01e02", Finished, 5));

        var entry = Assert.Single(listed, e => e.ShowImdbId == "tt7700001");
        Assert.Null(entry.TitleId);
        Assert.Equal(new Core.EpisodeIdentity { Season = 1, Number = 3 }, entry.Episode);
        Assert.Equal("Gone Fishing", entry.EpisodeName);
        Assert.Equal(44 * 60, entry.DurationSeconds);
        Assert.Equal(0, entry.PositionSeconds);
    }

    [Fact]
    public async Task A_next_episode_the_library_lacks_is_not_listed_to_somebody_who_may_not_fetch()
    {
        var show = Described("tt7700002", "cw-nofetch");
        var listed = await Watch(fixture.CreatePersonClient("cw7", actions: ["cinema:watch", "cinema:profile.read", "cinema:profile.write"]),
            ($"{show}-s01e02", Finished, 5));

        Assert.DoesNotContain(listed, e => e.ShowImdbId == "tt7700002");
    }

    [Fact]
    public async Task A_next_episode_that_has_not_aired_is_not_listed()
    {
        var show = Described("tt7700003", "cw-unaired");
        fixture.AddEpisode($"{show}-s01e03", "tt7700003", show, 1, 3);
        var listed = await Watch(fixture.CreatePersonClient("cw8"), ($"{show}-s01e03", Finished, 5));

        Assert.DoesNotContain(listed, e => e.ShowImdbId == "tt7700003");
    }

    [Fact]
    public async Task A_show_watched_to_its_last_episode_is_not_listed()
    {
        var show = Show("cw-done");
        var listed = await Watch(fixture.CreatePersonClient("cw5"), ($"{show}-s02e01", Finished, 5));

        Assert.DoesNotContain(listed, e => e.ShowImdbId == $"tt-{show}");
    }

    private static async Task Hide(HttpClient client, object body)
    {
        using var sent = await client.PostAsJsonAsync("/api/me/progress/hidden", body);
        sent.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_film_taken_off_is_left_out_until_it_is_watched_again()
    {
        fixture.AddTitle("cw-hide-film", new Core.FilmIdentity { ImdbId = "tt7700101", Name = "Hidden" });
        var client = fixture.CreatePersonClient("cw9");
        Assert.Contains(await Watch(client, ("cw-hide-film", 600, 5)), e => e.TitleId == "cw-hide-film");

        await Hide(client, new { titleId = "cw-hide-film", hidden = true });
        Assert.DoesNotContain((await client.GetFromJsonAsync<List<ProgressEntry>>("/api/me/progress", Json))!, e => e.TitleId == "cw-hide-film");
        Assert.Contains((await client.GetFromJsonAsync<List<HistoryEntry>>("/api/me/history", Json))!, e => e.TitleId == "cw-hide-film");

        var again = await Watch(client, ("cw-hide-film", 900, 0));
        Assert.Equal(900, Assert.Single(again, e => e.TitleId == "cw-hide-film").PositionSeconds);
    }

    [Fact]
    public async Task A_show_taken_off_is_left_out_by_every_episode_until_one_is_watched_again()
    {
        const string show = "tt7700102";
        foreach (var number in new[] { 1, 2, 3 }) fixture.AddEpisode($"cw-hide-show-s01e{number:00}", show, "cw-hide-show", 1, number);
        var client = fixture.CreatePersonClient("cw10");
        await Watch(client, ("cw-hide-show-s01e01", Finished, 60), ("cw-hide-show-s01e02", 600, 5));

        await Hide(client, new { showImdbId = show, hidden = true });
        Assert.DoesNotContain((await client.GetFromJsonAsync<List<ProgressEntry>>("/api/me/progress", Json))!, e => e.ShowImdbId == show);

        var again = await Watch(client, ("cw-hide-show-s01e02", Finished, 0));
        Assert.Equal("cw-hide-show-s01e03", Assert.Single(again, e => e.ShowImdbId == show).TitleId);
    }

    [Fact]
    public async Task Putting_back_what_was_taken_off_lists_it_as_before()
    {
        fixture.AddTitle("cw-unhide", new Core.FilmIdentity { ImdbId = "tt7700103", Name = "Back" });
        var client = fixture.CreatePersonClient("cw11");
        await Watch(client, ("cw-unhide", 600, 5));

        await Hide(client, new { titleId = "cw-unhide", hidden = true });
        await Hide(client, new { titleId = "cw-unhide", hidden = false });

        Assert.Contains((await client.GetFromJsonAsync<List<ProgressEntry>>("/api/me/progress", Json))!, e => e.TitleId == "cw-unhide");
    }

    [Fact]
    public async Task Taking_off_names_one_film_or_one_show()
    {
        var client = fixture.CreatePersonClient("cw12");
        fixture.AddEpisode("cw-hide-ep", "tt7700104", "cw-hide-ep", 1, 1);

        using var both = await client.PostAsJsonAsync("/api/me/progress/hidden", new { titleId = "x", showImdbId = "tt7700104", hidden = true });
        using var neither = await client.PostAsJsonAsync("/api/me/progress/hidden", new { hidden = true });
        using var episode = await client.PostAsJsonAsync("/api/me/progress/hidden", new { titleId = "cw-hide-ep", hidden = true });
        using var missing = await client.PostAsJsonAsync("/api/me/progress/hidden", new { titleId = "cw-nowhere", hidden = true });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, both.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, neither.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, episode.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, missing.StatusCode);
    }
}
