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
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "index.html"), "<!doctype html><title>Krystal Cinema</title>");
        // As a host runs it: in Development the web root is the project's own build, whatever is set.
        return fixture.WithWebHostBuilder(b => b.UseEnvironment("Production").UseWebRoot(_root)).CreateClient();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/library")]
    [InlineData("/title/heat-1995")]
    [InlineData("/search?q=heat")]
    [InlineData("/r/a1b2c3d4?title=heat-1995")]
    [InlineData("/embed/")]
    // Discord launches the Activity at the root with its parameters; the index sends it to /embed/.
    [InlineData("/?frame_id=1&instance_id=2")]
    public async Task A_page_of_the_site_is_answered_with_its_index(string path)
    {
        using var response = await Client().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Krystal Cinema", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/api/nothing-here")]
    [InlineData("/assets/missing.js")]
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
