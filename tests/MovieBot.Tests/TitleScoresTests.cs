using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TheKrystalShip.MovieBot.Acquire.Tmdb;
using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Api.Films;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A title has one rating wherever it is shown: the newest the film database has given, served as
/// none while too few people voted, whichever answer a view was built from.
/// </summary>
public sealed class TitleScoresTests : IDisposable
{
    private static readonly DateTimeOffset Oct9 = DateTimeOffset.Parse("2026-10-09T10:00:00Z");
    private static readonly DateTimeOffset Oct16 = DateTimeOffset.Parse("2026-10-16T10:00:00Z");
    private static readonly DateTimeOffset Oct20 = DateTimeOffset.Parse("2026-10-20T10:00:00Z");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "moviebot-tests", "scores-" + Guid.NewGuid().ToString("n"));

    private string DbPath => Path.Combine(_dir, "moviebot.db");

    private CinemaDatabase Database => field ??= new CinemaDatabase(DbPath);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void The_newest_observation_wins_whatever_order_they_arrive_in()
    {
        var scores = new TitleScores(Database, new ImdbRatings(Database));

        scores.Observe(1288069, "film", 6.2, 40, Oct16);
        scores.Observe(1288069, "film", 4.0, 30, Oct9);
        Assert.Equal((6.2, 40), scores.Serve(1288069, "film", 1.0, 99));

        scores.Observe(1288069, "film", 8.3, 105, Oct20);
        Assert.Equal((8.3, 105), scores.Serve(1288069, "film", 1.0, 99));
    }

    [Fact]
    public void A_score_is_kept_across_a_restart()
    {
        new TitleScores(Database, new ImdbRatings(Database)).Observe(7, "show", 7.7, 500, Oct16);

        Assert.Equal((7.7, 500), new TitleScores(Database, new ImdbRatings(Database)).Serve(7, "show", null, 0));
    }

    [Fact]
    public void A_film_and_a_show_with_one_id_are_two_titles()
    {
        var scores = new TitleScores(Database, new ImdbRatings(Database));
        scores.Observe(5, "film", 6.0, 100, Oct16);
        scores.Observe(5, "show", 9.0, 100, Oct16);

        Assert.Equal(6.0, scores.Serve(5, "film", null, 0).Rating);
        Assert.Equal(9.0, scores.Serve(5, "show", null, 0).Rating);
    }

    [Fact]
    public void A_title_nothing_is_held_for_is_served_as_its_answer_has_it()
    {
        var scores = new TitleScores(Database, new ImdbRatings(Database));

        Assert.Equal((7.1, 300), scores.Serve(99, "film", 7.1, 300));
        Assert.Equal((null, 0), scores.Serve(99, "film", null, 0));
    }

    [Fact]
    public void Too_few_votes_is_no_rating_but_the_votes_are_still_said()
    {
        var scores = new TitleScores(Database, new ImdbRatings(Database));

        Assert.Equal((null, CatalogueSearch.RatedVotes - 1), scores.Serve(1, "film", 9.5, CatalogueSearch.RatedVotes - 1));
        Assert.Equal((9.5, CatalogueSearch.RatedVotes), scores.Serve(1, "film", 9.5, CatalogueSearch.RatedVotes));

        scores.Observe(2, "film", 9.9, 3, Oct20);
        Assert.Equal((null, 3), scores.Serve(2, "film", 7.0, 5000));
    }

    [Fact]
    public void A_rating_nobody_gave_is_not_recorded()
    {
        var scores = new TitleScores(Database, new ImdbRatings(Database));
        scores.Observe(3, "film", 7.0, 400, Oct9);
        scores.Observe(3, "film", null, 0, Oct20);

        Assert.Equal((7.0, 400), scores.Serve(3, "film", null, 0));
    }

    [Fact]
    public void Descriptions_and_glances_already_kept_seed_the_table_with_the_newest_of_each_title()
    {
        PreviousSchema.Build(DbPath, ["Tables", "Sorts", "Wants", "Together", "Plans"], """
            INSERT INTO glances (imdb_id, fetched_at, glance) VALUES
              ('tt6262770', '2026-10-09T10:00:00.0000000Z', '{"tmdbId":1288069,"kind":"film","rating":4,"votes":1}'),
              ('tt0000002', '2026-10-18T10:00:00.0000000Z', '{"tmdbId":2,"kind":"film","rating":8.1,"votes":900}'),
              ('tt0000003', '2026-10-18T10:00:00.0000000Z', NULL);
            INSERT INTO film_descriptions (imdb_id, fetched_at, details) VALUES
              ('tt6262770', '2026-10-16T10:00:00.0000000Z', '{"tmdbId":1288069,"rating":6.2,"votes":4}'),
              ('tt0000002', '2026-10-10T10:00:00.0000000Z', '{"tmdbId":2,"rating":5.5,"votes":800}');
            INSERT INTO show_descriptions (imdb_id, fetched_at, details, seasons) VALUES
              ('tt0000004', '2026-10-12T10:00:00.0000000Z', '{"tmdbId":2,"rating":7.4,"votes":60}', '[]');
            """);

        var scores = new TitleScores(Database, new ImdbRatings(Database));

        Assert.Equal((null, 4), scores.Serve(1288069, "film", null, 0));
        Assert.Equal((8.1, 900), scores.Serve(2, "film", null, 0));
        Assert.Equal((7.4, 60), scores.Serve(2, "show", null, 0));
    }

    [Fact]
    public void A_film_page_and_its_listing_row_show_the_newest_rating_over_their_own_description()
    {
        var films = NewFilmStore(Oct20);
        Database.Write(c => c.Execute(
            "INSERT INTO film_descriptions (imdb_id, fetched_at, details) VALUES ('tt6262770', $at, $details)",
            ("$at", Oct16), ("$details", """{"tmdbId":1288069,"imdbId":"tt6262770","title":"Tenzing","rating":6.2,"votes":4,"genres":[],"directors":[]}""")));
        new TitleScores(Database, new ImdbRatings(Database)).Observe(1288069, "film", 8.3, 105, Oct20);

        var facts = films.Store.Facts("tt6262770");

        Assert.Equal((8.3, 105), (facts!.Rating, facts.Votes));
    }

    [Fact]
    public void A_description_read_seeds_the_score_stamped_with_when_it_was_asked()
    {
        var films = NewFilmStore(Oct20);
        Database.Write(c => c.Execute(
            "INSERT INTO film_descriptions (imdb_id, fetched_at, details) VALUES ('tt0000002', $at, $details)",
            ("$at", Oct16), ("$details", """{"tmdbId":2,"imdbId":"tt0000002","title":"Two","rating":7.0,"votes":300}""")));

        Assert.NotNull(films.Store.Held("tt0000002"));
        Assert.Equal((7.0, 300), films.Scores.Serve(2, "film", null, 0));

        films.Scores.Observe(2, "film", 6.0, 200, Oct9);
        Assert.Equal(7.0, films.Scores.Serve(2, "film", null, 0).Rating);
    }

    [Fact]
    public void An_answer_kept_by_the_catalogue_is_recorded_as_it_is_read()
    {
        var scores = new TitleScores(Database, new ImdbRatings(Database));
        var clock = new FakeTimeProvider(Oct20);
        var keeper = new AnswerKeeper(Database, scores, null!, clock, NullLogger<AnswerKeeper>.Instance);
        var person = new PersonCredits
        {
            TmdbId = 91606,
            Name = "Tom Hiddleston",
            Titles = [new CatalogueTitle { TmdbId = 1288069, ImdbId = "tt6262770", Kind = "film", Title = "Tenzing", Rating = 8.3, Votes = 105 }],
        };
        Database.Write(c => c.Execute(
            "INSERT INTO answers (name, fetched_at, asked_at, value) VALUES ('people/91606', $at, $at, $value)",
            ("$at", Oct20), ("$value", System.Text.Json.JsonSerializer.Serialize(person, AnswerJson.Default.PersonCredits))));

        var kept = keeper.KeptAsync("people/91606", AnswerJson.Default.PersonCredits, TimeSpan.FromDays(7),
            (_, _) => throw new InvalidOperationException("The kept answer is not due."), null, CancellationToken.None).GetAwaiter().GetResult();

        Assert.NotNull(kept);
        Assert.Equal((8.3, 105), scores.Serve(1288069, "film", 4.0, 1));
    }

    [Fact]
    public void A_film_released_within_sixty_days_is_due_after_a_day_and_an_older_one_is_not()
    {
        var now = DateTimeOffset.Parse("2026-10-20T12:00:00Z");
        var store = NewFilmStore(now).Store;
        StoredFilm Film(string released, DateTimeOffset fetched) => new()
        {
            FetchedAt = fetched,
            Details = new FilmDetails
            {
                TmdbId = 1, ImdbId = "tt0000001", Title = "T", ReleaseDate = released,
                Cast = [new CastMember { Name = "A", TmdbId = 5 }], Keywords = [], Trailers = [],
            },
        };

        Assert.True(store.IsDue(Film("2026-10-01", now.AddHours(-30))));
        Assert.False(store.IsDue(Film("2026-10-01", now.AddHours(-10))));
        Assert.False(store.IsDue(Film("2025-10-01", now.AddHours(-30))));
        Assert.False(store.IsDue(Film("2027-01-01", now.AddHours(-30))));
        Assert.True(store.IsDue(Film("2025-10-01", now.AddDays(-15))));
    }

    private (FilmStore Store, TitleScores Scores) NewFilmStore(DateTimeOffset now)
    {
        var scores = new TitleScores(Database, new ImdbRatings(Database));
        var store = new FilmStore(Path.Combine(_dir, "films"), Options.Create(new FilmStoreOptions()), null!,
            new PosterPins(Database), new KeptImages(Database), null!, Database, scores, new FakeTimeProvider(now),
            NullLogger<FilmStore>.Instance);
        return (store, scores);
    }
}
