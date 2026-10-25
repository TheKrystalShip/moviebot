using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Wants;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Wanting a show: a person is at their next unwatched episode, read from the watch record when
/// asked, and the want ends when they finish the last episode that has aired.
/// </summary>
public sealed class ShowWantTests(FollowFixture fixture) : IClassFixture<FollowFixture>
{
    private static int s_next = 500;

    private string Day(int fromToday) =>
        DateOnly.FromDateTime(fixture.Clock.GetUtcNow().UtcDateTime).AddDays(fromToday).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>A show the catalogue lists, with its episodes at days from today.</summary>
    private string Show(params (int Number, int Days)[] episodes)
    {
        var n = Interlocked.Increment(ref s_next);
        var imdb = $"tt92{n:00000}";
        var show = new KeptShows.FakeShow { TmdbId = 9500 + n };
        foreach (var (number, days) in episodes) show.Episodes.Add((1, number, Day(days)));
        fixture.Shows[imdb] = show;
        return imdb;
    }

    private HttpClient As(string userId, string name = "Cy")
    {
        fixture.Services.GetRequiredService<PersonDirectory>().Remember(userId, name);
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            fixture.Issuer.Mint(userId, name, lifetime: TimeSpan.FromDays(400)));
        return client;
    }

    private void InLibrary(string id, string imdb, int number)
    {
        ManifestJson.WriteAtomic(Path.Combine(fixture.MediaRoot, id, "manifest.json"), new Manifest
        {
            Id = id, Title = id, Kind = TitleKind.Show, DurationSeconds = 3000, Status = TitleStatus.Ready,
            Film = new FilmIdentity { ImdbId = imdb, Name = "Show" },
            Episode = new EpisodeIdentity { Season = 1, Number = number },
            Video = new VideoInfo
            {
                Width = 1280, Height = 720, SourceCodec = "h264", SourceHdr = "sdr",
                Renditions = [new Rendition { Name = "r", BitrateKbps = 4000, Width = 1280, Height = 720, Uri = "v0/index.m3u8" }],
            },
            Audio = [], Subtitles = [],
        });
        fixture.Services.GetRequiredService<LibraryListing>().Refresh();
    }

    private void Watched(string userId, string titleId) =>
        fixture.Services.GetRequiredService<WatchRecord>().Record(userId, titleId, 3000, 3000, fixture.Clock.GetUtcNow());

    private static async Task<WantView> Want(HttpClient client, string imdb)
    {
        using var response = await client.PutAsJsonAsync($"/api/me/wants/{imdb}", new SetWantRequest { Kind = "show" }, ManifestJsonContext.Default.SetWantRequest);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync(ManifestJsonContext.Default.SetWantReply))!.Want!;
    }

    private static async Task<IReadOnlyList<WantView>> Wants(HttpClient client) =>
        (await client.GetFromJsonAsync("/api/me/wants", ManifestJsonContext.Default.IReadOnlyListWantView))!;

    [Fact]
    public async Task A_person_who_watched_nothing_is_at_the_first_episode_and_moves_on_as_they_finish_each()
    {
        var imdb = Show((1, -30), (2, -10), (3, 5));
        InLibrary($"{imdb}-s01e01", imdb, 1);
        InLibrary($"{imdb}-s01e02", imdb, 2);
        var me = As("9101");

        var made = await Want(me, imdb);
        Assert.Equal("show", made.Kind);
        Assert.True(made.InLibrary);
        Assert.Equal(WantStates.Ready, made.State);
        Assert.Equal(new WantEpisode { Season = 1, Episode = 1, Name = "Episode 1" }, made.NextEpisode);

        Watched("9101", $"{imdb}-s01e01");
        Assert.Equal(2, Assert.Single(await Wants(me)).NextEpisode!.Episode);
    }

    [Fact]
    public async Task Finishing_the_last_episode_that_has_aired_ends_the_want_and_an_unaired_one_does_not_count()
    {
        var imdb = Show((1, -30), (2, -10), (3, 5));
        InLibrary($"{imdb}-s01e01", imdb, 1);
        InLibrary($"{imdb}-s01e02", imdb, 2);
        var me = As("9102");
        await Want(me, imdb);

        Watched("9102", $"{imdb}-s01e01");
        Assert.Single(await Wants(me));

        Watched("9102", $"{imdb}-s01e02");
        Assert.Empty(await Wants(me));
    }

    [Fact]
    public async Task Another_person_sees_where_each_wanter_is_in_a_visible_want_of_a_show()
    {
        var imdb = Show((1, -30), (2, -10));
        InLibrary($"{imdb}-s01e01", imdb, 1);
        var ana = As("9103", "Ana");
        var bo = As("9104", "Bo");
        await Want(ana, imdb);
        await Want(bo, imdb);
        Watched("9104", $"{imdb}-s01e01");

        var wanters = await bo.GetFromJsonAsync($"/api/titles/{imdb}/wanters", ManifestJsonContext.Default.IReadOnlyListWanterView);

        var seen = Assert.Single(wanters!);
        Assert.Equal("Ana", seen.Name);
        Assert.Equal(1, seen.NextEpisode!.Episode);
        Assert.Equal(1, Assert.Single(await Wants(bo)).Others);
    }

    [Fact]
    public void The_next_episode_follows_the_latest_finished_and_ends_with_the_show()
    {
        var show = new Api.Shows.StoredShow
        {
            FetchedAt = DateTimeOffset.UnixEpoch,
            Seasons =
            [
                new Acquire.Tmdb.SeasonDetails
                {
                    Number = 1,
                    Episodes =
                    [
                        new Acquire.Tmdb.EpisodeDetails { Season = 1, Number = 1, AirDate = "2020-01-01" },
                        new Acquire.Tmdb.EpisodeDetails { Season = 1, Number = 2, AirDate = "2020-01-08" },
                    ],
                },
                new Acquire.Tmdb.SeasonDetails
                {
                    Number = 2,
                    Episodes = [new Acquire.Tmdb.EpisodeDetails { Season = 2, Number = 1, AirDate = "2999-01-01" }],
                },
            ],
        };
        var today = new DateOnly(2026, 10, 20);

        Assert.Equal(1, ShowProgress.Next(show, null)!.Episode);
        Assert.Equal(2, ShowProgress.Next(show, new HashSet<(int, int)> { (1, 1) })!.Episode);
        var afterSeasonOne = ShowProgress.Next(show, new HashSet<(int, int)> { (1, 2) })!;
        Assert.Equal((2, 1), (afterSeasonOne.Season, afterSeasonOne.Episode));
        Assert.Null(ShowProgress.Next(show, new HashSet<(int, int)> { (2, 1) }));
        Assert.Null(ShowProgress.Next(null, null));

        Assert.False(ShowProgress.FinishedAired(show, new HashSet<(int, int)> { (1, 1) }, today));
        Assert.True(ShowProgress.FinishedAired(show, new HashSet<(int, int)> { (1, 2) }, today));
        Assert.False(ShowProgress.FinishedAired(show, null, today));
    }
}
