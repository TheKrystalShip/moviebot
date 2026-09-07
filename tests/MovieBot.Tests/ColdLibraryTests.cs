using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The API over two disks. A film that has moved to the one films are kept on is in the library
/// and opens exactly as one still on the disk it was made on: which disk a film is on is not
/// something anything outside the service is told, and no URL changes when it moves.
/// </summary>
public sealed class ColdLibraryTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    /// <summary>The server writes enums as the strings the player reads, so this client does too.</summary>
    private static readonly JsonSerializerOptions AsThePlayerReadsIt = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task A_settled_film_is_in_the_library()
    {
        var client = fixture.CreateServiceClient();

        var titles = await client.GetFromJsonAsync<List<TitleSummary>>("/api/titles", AsThePlayerReadsIt);

        Assert.NotNull(titles);
        Assert.Contains(titles, t => t.Id == SessionFixture.SettledTitle);
        Assert.Contains(titles, t => t.Id == SessionFixture.ReadyTitle);
    }

    [Fact]
    public async Task A_settled_film_answers_by_name()
    {
        var client = fixture.CreateServiceClient();

        var manifest = await client.GetFromJsonAsync<Manifest>(
            $"/api/titles/{SessionFixture.SettledTitle}", AsThePlayerReadsIt);

        Assert.NotNull(manifest);
        Assert.Equal(SessionFixture.SettledTitle, manifest.Id);
    }

    [Fact]
    public async Task A_settled_film_serves_its_files()
    {
        var segment = Path.Combine(fixture.ColdRoot, SessionFixture.SettledTitle, "v0", "0.m4s");
        Directory.CreateDirectory(Path.GetDirectoryName(segment)!);
        await File.WriteAllTextAsync(segment, "the picture");

        var client = fixture.CreateServiceClient();
        var response = await client.GetAsync($"/media/{SessionFixture.SettledTitle}/v0/0.m4s");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("the picture", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The containment check is against the root the title actually resolved to, so a traversal
    /// out of a film kept on the cold disk is refused the same way as one out of a film on the
    /// disk it was made on.
    /// </summary>
    [Fact]
    public async Task A_traversal_out_of_a_settled_film_is_refused()
    {
        var client = fixture.CreateServiceClient();

        var response = await client.GetAsync(
            $"/media/{SessionFixture.SettledTitle}/../{MediaRoots.ColdMarker}");

        // Refused, whether by the framework before the endpoint sees the shape or by the
        // containment check after it. What matters is that neither answers with the file.
        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Health_says_nothing_while_the_disk_is_there()
    {
        var client = fixture.CreateServiceClient();

        var health = await client.GetFromJsonAsync<HealthView>("/health");

        Assert.NotNull(health);
        Assert.Null(health.ColdStorage);
    }

    private sealed record HealthView(string Status, int Rooms, int Watching, string? ColdStorage);
}
