using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The site's pages are paths, answered with its index; the API's own paths are never mistaken for one.
/// </summary>
public sealed class SitePagesTests(SessionFixture fixture) : IClassFixture<SessionFixture>, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "moviebot-tests", "site-" + Guid.NewGuid().ToString("N"));

    private HttpClient Client()
    {
        Directory.CreateDirectory(Path.Combine(_root, "activity"));
        File.WriteAllText(Path.Combine(_root, "index.html"), "<!doctype html><title>Krystal Cinema</title>");
        File.WriteAllText(Path.Combine(_root, "activity", "index.html"), "<!doctype html><title>Player</title>");
        // As a host runs it: in Development the web root is the project's own build, whatever is set.
        return fixture.WithWebHostBuilder(b => b.UseEnvironment("Production").UseWebRoot(_root)).CreateClient();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/library")]
    [InlineData("/title/heat-1995")]
    [InlineData("/search?q=heat")]
    public async Task A_page_of_the_site_is_answered_with_its_index(string path)
    {
        using var response = await Client().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Krystal Cinema", await response.Content.ReadAsStringAsync());
    }

    /// <summary>Discord's launch arrives with its parameters, and the player is an index of its own.</summary>
    [Theory]
    [InlineData("/activity/")]
    [InlineData("/activity/?frame_id=1&instance_id=2")]
    public async Task The_player_is_under_its_own_prefix(string path)
    {
        using var response = await Client().GetAsync(path);

        Assert.Contains("Player", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/api/nothing-here")]
    [InlineData("/assets/missing.js")]
    [InlineData("/activity/assets/missing.js")]
    public async Task What_is_not_a_page_is_not_answered_with_one(string path)
    {
        var client = Client();
        client.DefaultRequestHeaders.Add("X-MovieBot-Service", SessionFixture.ServiceKey);

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
