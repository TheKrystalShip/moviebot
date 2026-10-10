using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The site's pages are paths, answered with its index; the API's own paths are never mistaken for one.
/// </summary>
public sealed class SitePagesTests(SessionFixture fixture) : IClassFixture<SessionFixture>, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "moviebot-tests", "site-" + Guid.NewGuid().ToString("N"));

    private WebApplicationFactory<Program>? _host;

    /// <summary>A client of one host per test, so what one request does to a room the next one sees.</summary>
    private HttpClient Client()
    {
        if (_host is null)
        {
            Directory.CreateDirectory(_root);
            File.WriteAllText(Path.Combine(_root, "index.html"),
                "<!doctype html><html><head><title>Krystal Cinema</title></head><body></body></html>");
            // As a host runs it: in Development the web root is the project's own build, whatever is set.
            _host = fixture.WithWebHostBuilder(b => b.UseEnvironment("Production").UseWebRoot(_root));
        }
        return _host.CreateClient();
    }

    /// <summary>The rooms of the host <see cref="Client"/> reaches.</summary>
    private Api.Sessions.SessionStore Rooms
    {
        get
        {
            Client();
            return _host!.Services.GetRequiredService<Api.Sessions.SessionStore>();
        }
    }

    /// <summary>
    /// The web root is read from disk on every request, so a new build installed there is served
    /// without a restart.
    /// </summary>
    [Fact]
    public async Task A_new_build_in_the_web_root_is_served_without_a_restart()
    {
        var site = Path.Combine(_root, "installed");
        Directory.CreateDirectory(Path.Combine(site, "assets"));
        File.WriteAllText(Path.Combine(site, "index.html"), "<!doctype html><title>first build</title>");
        File.WriteAllText(Path.Combine(site, "assets", "app-1.js"), "first");
        using var host = fixture.WithWebHostBuilder(b => b.UseEnvironment("Production").UseWebRoot(site));
        var client = host.CreateClient();

        Assert.Contains("first build", await client.GetStringAsync("/library"));
        Assert.Equal("first", await client.GetStringAsync("/assets/app-1.js"));

        File.WriteAllText(Path.Combine(site, "index.html"), "<!doctype html><title>second build</title>");
        File.WriteAllText(Path.Combine(site, "assets", "app-2.js"), "second");

        Assert.Contains("second build", await client.GetStringAsync("/"));
        Assert.Contains("second build", await client.GetStringAsync("/library"));
        Assert.Equal("second", await client.GetStringAsync("/assets/app-2.js"));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/library")]
    [InlineData("/movies/heat-1995")]
    [InlineData("/tv/tt11280740/season/2")]
    [InlineData("/discover/film/tt0113277")]
    [InlineData("/profile/requests")]
    [InlineData("/discover?q=heat")]
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

    /// <summary>
    /// A room's link unfurls with the film and how far in, and with nothing about the people: the
    /// crawler that reads it carries no token, so whatever is here is public.
    /// </summary>
    [Fact]
    public async Task A_room_s_page_unfurls_with_the_film_and_never_the_people()
    {
        var client = Client();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestIssuer.Token("u-alice", "Alice"));
        var room = "preview-" + Guid.NewGuid().ToString("N")[..8];
        (await client.GetAsync("/api/films/tt0113277")).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/sessions/{room}/title",
            new TheKrystalShip.MovieBot.Core.SetTitleRequest(SessionFixture.Heat))).EnsureSuccessStatusCode();
        Rooms.SetOpen(room, true);

        // As a crawler asks: no token, no service key.
        var page = await Client().GetStringAsync($"/r/{room}");

        Assert.Contains("<meta property=\"og:title\" content=\"Watch Heat on Krystal Cinema\" />", page);
        Assert.Contains("<meta property=\"og:description\" content=\"1995\" />", page);
        Assert.Contains("property=\"og:image\" content=\"http://localhost/films/tt0113277/poster.jpg\"", page);
        Assert.DoesNotContain("Alice", page);
        Assert.DoesNotContain("watching", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Krystal Cinema</title>", page);
    }

    [Fact]
    public async Task A_link_naming_a_film_unfurls_with_it_before_anybody_has_opened_the_room()
    {
        var page = await Client().GetStringAsync($"/r/fresh-{Guid.NewGuid():N}?title={SessionFixture.Heat}");

        Assert.Contains("Watch Heat on Krystal Cinema", page);
    }

    /// <summary>A private room's link says nothing of it, not even its film, wherever it is pasted.</summary>
    [Fact]
    public async Task A_private_room_unfurls_as_the_site()
    {
        var client = Client();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestIssuer.Token("u-alice", "Alice"));
        var room = "private-" + Guid.NewGuid().ToString("N")[..8];
        (await client.PostAsJsonAsync($"/api/sessions/{room}/title",
            new TheKrystalShip.MovieBot.Core.SetTitleRequest(SessionFixture.Heat))).EnsureSuccessStatusCode();

        var page = await Client().GetStringAsync($"/r/{room}?title={SessionFixture.Heat}");

        Assert.Contains("<meta property=\"og:title\" content=\"Krystal Cinema\" />", page);
        Assert.DoesNotContain("Heat", page);
    }

    [Fact]
    public async Task A_private_room_s_guest_link_unfurls_with_its_film()
    {
        var client = Client();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestIssuer.Token("u-alice", "Alice"));
        var room = "guests-" + Guid.NewGuid().ToString("N")[..8];
        (await client.PostAsJsonAsync($"/api/sessions/{room}/title",
            new TheKrystalShip.MovieBot.Core.SetTitleRequest(SessionFixture.Heat))).EnsureSuccessStatusCode();
        using var issued = await client.PostAsync($"/api/rooms/{room}/guests", null);
        var link = (await issued.Content.ReadFromJsonAsync<TheKrystalShip.MovieBot.Core.GuestLink>())!;

        var page = await Client().GetStringAsync(link.Path);
        var gone = await Client().GetStringAsync("/g/not-a-link");

        Assert.Contains("<meta property=\"og:title\" content=\"Watch Heat on Krystal Cinema\" />", page);
        Assert.DoesNotContain("og:title", gone);
    }

    [Fact]
    public async Task A_room_holding_nothing_unfurls_as_the_site()
    {
        var page = await Client().GetStringAsync($"/r/empty-{Guid.NewGuid():N}");

        Assert.Contains("<meta property=\"og:title\" content=\"Krystal Cinema\" />", page);
        Assert.DoesNotContain("og:image", page);
    }

    [Fact]
    public async Task Only_a_room_s_page_carries_the_tags()
    {
        var page = await Client().GetStringAsync("/library");

        Assert.DoesNotContain("og:title", page);
    }

    [Theory]
    [InlineData(30, null)]
    [InlineData(31 * 60 + 5, "31 min in")]
    [InlineData(3600 + 9 * 60, "1 h 09 min in")]
    public void How_far_in_is_said_in_minutes(double seconds, string? said) =>
        Assert.Equal(said, TheKrystalShip.MovieBot.Api.Sessions.RoomPreview.HowFarIn(seconds));

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
        _host?.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
