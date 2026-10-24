using Microsoft.Data.Sqlite;
using TheKrystalShip.MovieBot.Acquire.Tmdb;
using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Api.Films;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Ranking a question asked by rating on IMDb: the vote floor that scales with the question's breadth,
/// the order above the floor, the film database's own order for what IMDb does not rate, and the table
/// the candidates and ratings are read from.
/// </summary>
public sealed class ImdbRankingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "moviebot-tests", "ranking-" + Guid.NewGuid().ToString("n"));

    private CinemaDatabase Database => field ??= new CinemaDatabase(Path.Combine(_dir, "moviebot.db"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static ListedTitle Listed(int id, double? rating, int votes) =>
        new() { TmdbId = id, Kind = "film", Title = "T" + id, Rating = rating, Votes = votes };

    private static ImdbRanking.Entry Entry(int id, double? imdb, int imdbVotes, double? rating = 5, int votes = 100) =>
        new(Listed(id, rating, votes), imdb is { } r ? new ImdbRatings.Rating(r, imdbVotes) : null);

    [Fact]
    public void The_floor_is_the_votes_of_the_hundredth_most_voted_kept_between_the_ends()
    {
        var broad = Enumerable.Range(1, 500).Select(i => 2_000_000 - i * 1000).ToList();
        Assert.Equal(ImdbRanking.Broadest, ImdbRanking.FloorFor(broad));

        var middle = Enumerable.Range(1, 300).Select(i => 20_000 - i * 10).ToList();
        Assert.Equal(20_000 - ImdbRanking.Breadth * 10, ImdbRanking.FloorFor(middle));

        Assert.Equal(ImdbRanking.Narrowest, ImdbRanking.FloorFor([900, 300, 120]));
        Assert.Equal(ImdbRanking.Narrowest, ImdbRanking.FloorFor([]));
        Assert.Equal(700, ImdbRanking.FloorFor([5000, 700]));
    }

    [Fact]
    public void Titles_above_the_floor_are_ordered_by_rating_weighted_by_votes_so_one_just_over_it_does_not_outrank_a_known_title()
    {
        var ranked = ImdbRanking.Order(
        [
            Entry(1, 7.0, 90_000),
            Entry(2, 8.1, 5_200),
            Entry(3, 8.0, 400_000),
            Entry(4, 7.5, 60_000),
        ], floor: 5_000);

        Assert.Equal([3, 2, 4, 1], ranked.Select(t => t.TmdbId));
    }

    [Fact]
    public void A_title_imdb_lacks_or_under_the_floor_follows_in_the_film_databases_weighted_order()
    {
        var ranked = ImdbRanking.Order(
        [
            Entry(1, null, 0, rating: 8.9, votes: 30),
            Entry(2, 6.0, 10_000, rating: 9.5, votes: 40),
            Entry(3, 5.5, 200, rating: 7.0, votes: 20_000),
            Entry(4, null, 0, rating: 7.4, votes: 90_000),
        ], floor: 5_000);

        Assert.Equal(2, ranked[0].TmdbId);
        Assert.Equal(WeightedRating.Order([Listed(1, 8.9, 30), Listed(3, 7.0, 20_000), Listed(4, 7.4, 90_000)]).Select(t => t.TmdbId),
            ranked.Skip(1).Select(t => t.TmdbId));
    }

    [Fact]
    public void With_nothing_rated_by_imdb_the_order_is_the_film_databases()
    {
        var titles = new[] { Listed(1, 6.0, 100), Listed(2, 8.0, 5000), Listed(3, 7.0, 900) };

        Assert.Equal(WeightedRating.Order(titles).Select(t => t.TmdbId),
            ImdbRanking.Order([.. titles.Select(t => new ImdbRanking.Entry(t, null))], 500).Select(t => t.TmdbId));
    }

    [Fact]
    public void Genres_map_by_name_and_those_imdb_has_no_name_for_map_to_none()
    {
        Assert.Equal(["Sci-Fi"], ImdbGenres.Of("Science Fiction"));
        Assert.Equal(["Action", "Adventure"], ImdbGenres.Of("action & adventure"));
        Assert.Equal(["Music", "Musical"], ImdbGenres.Of("Music"));
        Assert.Null(ImdbGenres.Of("TV Movie"));
        Assert.Null(ImdbGenres.Of("Soap"));
    }

    private void Seed(params (long Id, string Kind, string Title, int? Year, string Genres, double Rating, int Votes)[] rows) =>
        Database.Write(c =>
        {
            foreach (var r in rows)
                c.Execute("INSERT INTO imdb_titles (imdb_id, kind, title, start_year, runtime, genres, rating, votes) VALUES ($id, $kind, $title, $year, NULL, $genres, $rating, $votes)",
                    ("$id", r.Id), ("$kind", r.Kind), ("$title", r.Title), ("$year", r.Year), ("$genres", r.Genres), ("$rating", r.Rating), ("$votes", r.Votes));
            c.Execute("INSERT INTO meta (key, value) VALUES ($key, '20261024')", ("$key", ImdbImport.EditionKey));
        });

    [Fact]
    public void Candidates_are_the_best_rated_of_a_kind_genre_and_years_above_the_floor()
    {
        Seed(
            (1, "film", "Horror A", 2000, ",Horror,Thriller,", 7.6, 800_000),
            (2, "film", "Horror B", 2001, ",Horror,", 7.0, 260_000),
            (3, "film", "Horror C", 1999, ",Horror,", 8.5, 900_000),
            (4, "film", "Horror D", 2002, ",Horror,Comedy,", 6.0, 90_000),
            (5, "film", "Horror E", 2003, ",Horror,", 9.0, 800),
            (6, "film", "Drama F", 2000, ",Drama,", 9.0, 900_000),
            (7, "show", "Horror Show", 2000, ",Horror,", 9.0, 900_000),
            (8, "film", "Cartoon Horror", 2004, ",Horror,Animation,", 8.0, 500_000));
        var ratings = new ImdbRatings(Database);

        var spec = new ImdbSpec("film", [["Horror"]], ["Animation"], 2000, null);

        Assert.Equal(["Horror A", "Horror B", "Horror D"], ratings.Candidates(spec, 1000, null, 10).Select(c => c.Title));
        Assert.Equal(["Horror A", "Horror B"], ratings.Candidates(spec, 1000, 7.0, 10).Select(c => c.Title));
        Assert.Equal(["Horror A"], ratings.Candidates(spec, 1000, null, 1).Select(c => c.Title));
        Assert.Equal([800_000, 260_000, 90_000, 800], ratings.MostVoted(spec, 10));
        Assert.Equal(2, ratings.MostVoted(spec, 2).Count);
    }

    [Fact]
    public void A_genre_stands_for_any_of_its_imdb_names_and_every_genre_asked_for_must_hold()
    {
        Seed(
            (1, "film", "Both", 2000, ",Action,Sci-Fi,", 7.0, 5000),
            (2, "film", "Adventure Only", 2000, ",Adventure,", 7.5, 5000),
            (3, "film", "Neither", 2000, ",Drama,", 8.0, 5000),
            (4, "film", "Sci-Fi Only", 2000, ",Sci-Fi,", 6.0, 5000));
        var ratings = new ImdbRatings(Database);

        Assert.Equal(["Adventure Only", "Both"],
            ratings.Candidates(new ImdbSpec("film", [["Action", "Adventure"]], [], null, null), 100, null, 10).Select(c => c.Title));
        Assert.Equal(["Both"],
            ratings.Candidates(new ImdbSpec("film", [["Action", "Adventure"], ["Sci-Fi"]], [], null, null), 100, null, 10).Select(c => c.Title));
    }

    [Fact]
    public void A_host_that_never_imported_holds_nothing()
    {
        var ratings = new ImdbRatings(Database);

        Assert.Null(ratings.Edition);
        Assert.Null(ratings.Of("tt0000001"));
    }

    [Fact]
    public void A_title_is_looked_up_by_either_spelling_of_its_id_and_a_new_import_is_seen_once_refreshed()
    {
        Seed((1234567, "film", "Film", 2000, ",Drama,", 7.4, 5000));
        var ratings = new ImdbRatings(Database);

        Assert.Equal(new ImdbRatings.Rating(7.4, 5000), ratings.Of("tt1234567"));
        Assert.Equal(new ImdbRatings.Rating(7.4, 5000), ratings.Of("1234567"));
        Assert.Null(ratings.Of("tt7654321"));
        Assert.Null(ratings.Of(null));

        Database.Write(c => c.Execute("UPDATE imdb_titles SET rating = 8.8"));
        Assert.Equal(7.4, ratings.Of("tt1234567")!.Value.Value);
        ratings.Refresh();
        Assert.Equal(8.8, ratings.Of("tt1234567")!.Value.Value);
    }
}
