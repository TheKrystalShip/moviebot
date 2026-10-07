using System.Net.Http.Json;
using System.Text.Json;
using TheKrystalShip.MovieBot.Api.People;
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

    /// <summary>A show of its own for one test: S1E1, S1E2, S1E3 and S2E1, which the film database has not described.</summary>
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
    /// A show the film database has described: S1E1 and S1E2 in the library, S1E3 aired and not here,
    /// S1E4 airing in a month.
    /// </summary>
    private string Described(string imdbId, string name)
    {
        fixture.AddEpisode($"{name}-s01e01", imdbId, name, 1, 1);
        fixture.AddEpisode($"{name}-s01e02", imdbId, name, 1, 2);
        var today = DateTime.UtcNow.Date;
        var show = new Api.Shows.StoredShow
        {
            FetchedAt = DateTimeOffset.UtcNow,
            Seasons =
            [
                new Acquire.Tmdb.SeasonDetails
                {
                    Number = 1,
                    Episodes =
                    [
                        new() { Season = 1, Number = 1, AirDate = today.AddDays(-30).ToString("yyyy-MM-dd") },
                        new() { Season = 1, Number = 2, AirDate = today.AddDays(-23).ToString("yyyy-MM-dd") },
                        new() { Season = 1, Number = 3, Name = "Gone Fishing", RuntimeMinutes = 44, AirDate = today.AddDays(-16).ToString("yyyy-MM-dd") },
                        new() { Season = 1, Number = 4, AirDate = today.AddDays(30).ToString("yyyy-MM-dd") },
                    ],
                },
            ],
        };
        var directory = Path.Combine(fixture.StateRoot, "shows", imdbId);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "show.json"),
            JsonSerializer.Serialize(show, Api.Shows.StoredShowJson.Default.StoredShow));
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
}
