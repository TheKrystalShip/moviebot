using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Api.Films;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A question asked by rating, on a host whose IMDb import holds a few titles: ranked by IMDb's
/// ratings, widened with IMDb's own candidates only when the question is about kind, genre and years
/// alone, and every view of a title carrying IMDb's rating beside the film database's.
/// </summary>
public sealed class CatalogueImdbRankingTests : IClassFixture<SessionFixture>
{
    private readonly SessionFixture _fixture;

    public CatalogueImdbRankingTests(SessionFixture fixture)
    {
        _fixture = fixture;
        var database = fixture.Services.GetRequiredService<CinemaDatabase>();
        database.Write(c =>
        {
            c.Execute("DELETE FROM imdb_titles");
            Seed(c, 9000101, "The Cellar Door", 2002, ",Horror,", 6.0, 9000);
            Seed(c, 9000102, "Hollow Hill", 2004, ",Horror,", 7.5, 9000);
            Seed(c, 9000777, "Forgotten Reel", 2003, ",Horror,", 8.0, 20000);
            Seed(c, 9000004, "Silver Harbour", FilmDatabaseStub.ThisYear, ",Drama,", 8.5, 3000);
            Seed(c, 9000000, "Old Letters", 1999, ",Drama,", 6.0, 3000);
            Seed(c, 113277, "Heat", 1995, ",Crime,Drama,", 8.3, 600000);
            c.Execute("INSERT OR REPLACE INTO meta (key, value) VALUES ($key, '20261024')", ("$key", ImdbImport.EditionKey));
        });
        fixture.Services.GetRequiredService<ImdbRatings>().Refresh();
    }

    private static void Seed(Microsoft.Data.Sqlite.SqliteConnection c, long id, string title, int year, string genres, double rating, int votes) =>
        c.Execute("INSERT INTO imdb_titles (imdb_id, kind, title, start_year, runtime, genres, rating, votes) VALUES ($id, 'film', $title, $year, NULL, $genres, $rating, $votes)",
            ("$id", id), ("$title", title), ("$year", year), ("$genres", genres), ("$rating", rating), ("$votes", votes));

    private HttpClient Signed()
    {
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _fixture.TokenFor("u12", "Gil"));
        return client;
    }

    private async Task<CatalogueDiscoverView> Discover(string query) =>
        (await Signed().GetFromJsonAsync<CatalogueDiscoverView>("/api/catalogue/discover?" + query))!;

    [Fact]
    public async Task A_question_of_genre_alone_is_widened_with_imdbs_own_titles_and_ranked_by_imdb()
    {
        var view = await Discover("genre=horror&sort=rating&goodOnly=false");

        Assert.Equal(["Forgotten Reel", "Hollow Hill", "The Cellar Door"], view.Titles.Select(t => t.Title));
        Assert.Equal([8.0, 7.5, 6.0], view.Titles.Select(t => t.Rating!.Value));
        Assert.Equal([20000, 9000, 9000], view.Titles.Select(t => t.Votes));
        Assert.All(view.Titles, t => Assert.Equal(TitleScores.ByImdb, t.RatedBy));
        Assert.Equal(FilmDatabaseStub.ForgottenReel, view.Titles[0].ImdbId);
    }

    [Fact]
    public async Task A_title_imdb_adds_is_resolved_through_the_film_databases_lookup_by_imdb_id_and_kept()
    {
        int Asked() => _fixture.FilmDatabase.Requests.Count(r => r.Contains("/discover/") || r.Contains("/find/"));

        await Discover("genre=horror&sort=rating&goodOnly=false&from=2001");
        Assert.Equal(1, _fixture.FilmDatabase.Requests.Count(r => r.Contains($"/find/{FilmDatabaseStub.ForgottenReel}")));
        var asked = Asked();

        var again = await Discover("genre=horror&sort=rating&goodOnly=false&from=2001");

        Assert.Equal(asked, Asked());
        Assert.Equal("Forgotten Reel", again.Titles[0].Title);
    }

    [Fact]
    public async Task The_years_of_the_question_bound_the_titles_imdb_adds()
    {
        var view = await Discover("genre=horror&sort=rating&goodOnly=false&from=2004&to=2004");

        Assert.DoesNotContain(view.Titles, t => t.ImdbId == FilmDatabaseStub.ForgottenReel);
    }

    [Fact]
    public async Task A_question_that_names_a_keyword_is_ranked_by_imdb_but_never_widened()
    {
        var view = await Discover("about=" + Uri.EscapeDataString("silent film") + "&sort=rating&goodOnly=false");

        Assert.Equal(["Silver Harbour", "Old Letters"], view.Titles.Select(t => t.Title));
        Assert.Equal([8.5, 6.0], view.Titles.Select(t => t.Rating!.Value));
        Assert.DoesNotContain(view.Titles, t => t.ImdbId == FilmDatabaseStub.ForgottenReel);
    }

    [Fact]
    public async Task A_sort_other_than_rating_is_left_as_the_film_database_ordered_it()
    {
        var view = await Discover("genre=horror&sort=popular&goodOnly=false");

        Assert.Equal(["The Cellar Door", "Hollow Hill"], view.Titles.Select(t => t.Title));
        Assert.Equal([6.0, 7.5], view.Titles.Select(t => t.Rating!.Value));
    }

    [Fact]
    public async Task A_rating_floor_holds_for_the_rating_shown()
    {
        var view = await Discover("genre=horror&minRating=7&goodOnly=false");

        Assert.Equal(["Hollow Hill"], view.Titles.Select(t => t.Title));
    }

    [Fact]
    public async Task A_films_page_is_rated_by_imdb_where_the_import_holds_it()
    {
        var film = (await Signed().GetFromJsonAsync<FilmView>("/api/films/tt0113277"))!;

        Assert.Equal((8.3, 600000, TitleScores.ByImdb), (film.Rating, film.Votes, film.RatedBy));
    }

    [Fact]
    public async Task A_title_the_import_does_not_hold_is_rated_by_the_film_database()
    {
        var view = await Discover("genre=adventure&goodOnly=false");

        Assert.NotEmpty(view.Titles);
        Assert.All(view.Titles, t => Assert.Equal(TitleScores.ByDatabase, t.RatedBy));
        Assert.Equal(7.8, view.Titles[0].Rating);
    }
}
