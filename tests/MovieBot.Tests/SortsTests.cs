using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Api.People;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The order a person picks on a page follows the person: the site and the Activity read and write the
/// same orders, one per page, and nobody else's.
/// </summary>
public sealed class SortsTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private HttpClient As(string userId, bool site) =>
        fixture.CreatePersonClient(userId, "Cy", via: site ? null : "cinema-activity");

    private static async Task<Dictionary<string, SortOrder>?> Read(HttpClient client) =>
        await client.GetFromJsonAsync<Dictionary<string, SortOrder>>("/api/me/sorts");

    [Fact]
    public async Task Somebody_who_picked_nothing_has_no_orders()
    {
        Assert.Empty((await Read(As("7101", site: true)))!);
    }

    [Fact]
    public async Task What_the_site_picks_the_activity_reads_page_by_page()
    {
        using var list = await As("7102", site: true).PutAsJsonAsync("/api/me/sorts/list", new SortOrder("name", "asc"));
        list.EnsureSuccessStatusCode();
        using var films = await As("7102", site: true).PutAsJsonAsync("/api/me/sorts/library.film", new SortOrder("year", "desc"));
        var answered = await films.Content.ReadFromJsonAsync<Dictionary<string, SortOrder>>();
        Assert.Equal(2, answered!.Count);

        using var again = await As("7102", site: false).PutAsJsonAsync("/api/me/sorts/list", new SortOrder("rating", "desc"));
        again.EnsureSuccessStatusCode();

        var seen = await Read(As("7102", site: false));
        Assert.Equal(new SortOrder("rating", "desc"), seen!["list"]);
        Assert.Equal(new SortOrder("year", "desc"), seen["library.film"]);

        // Somebody else reads their own.
        Assert.Empty((await Read(As("7103", site: true)))!);
    }

    [Theory]
    [InlineData("list", "name", "up")]
    [InlineData("list", "Name", "asc")]
    [InlineData("list", "", "asc")]
    [InlineData("List", "name", "asc")]
    [InlineData("1list", "name", "asc")]
    [InlineData("list_x", "name", "asc")]
    public async Task A_malformed_page_key_or_direction_is_refused(string surface, string key, string direction)
    {
        using var put = await As("7104", site: true).PutAsJsonAsync($"/api/me/sorts/{surface}", new SortOrder(key, direction));

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        Assert.Empty((await Read(As("7104", site: true)))!);
    }

    [Fact]
    public async Task One_person_keeps_orders_for_a_bounded_number_of_pages()
    {
        var client = As("7105", site: true);
        for (var i = 0; i < SortStore.MaxSurfaces; i++)
        {
            using var put = await client.PutAsJsonAsync($"/api/me/sorts/page{i}", new SortOrder("name", "asc"));
            put.EnsureSuccessStatusCode();
        }

        using var more = await client.PutAsJsonAsync("/api/me/sorts/onemore", new SortOrder("name", "asc"));
        Assert.Equal(HttpStatusCode.BadRequest, more.StatusCode);

        // A page already kept still changes.
        using var changed = await client.PutAsJsonAsync("/api/me/sorts/page0", new SortOrder("year", "desc"));
        changed.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task The_service_key_names_nobody()
    {
        using var response = await fixture.CreateServiceClient().GetAsync("/api/me/sorts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void A_file_at_the_first_version_gains_the_orders_and_keeps_what_it_held()
    {
        var path = Path.Combine(Path.GetTempPath(), "moviebot-tests", Guid.NewGuid().ToString("n"), "moviebot.db");
        try
        {
            PreviousSchema.Build(path, ["Tables"], "INSERT INTO meta (key, value) VALUES ('issuer', 'https://kept.test');");

            var database = new CinemaDatabase(path);
            var sorts = new SortStore(database);

            Assert.Equal("https://kept.test", database.Meta("issuer"));
            Assert.NotNull(sorts.Set("7106", "list", new SortOrder("name", "asc")));
            Assert.Equal(new SortOrder("name", "asc"), sorts.For("7106")["list"]);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
