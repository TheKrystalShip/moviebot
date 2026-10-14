using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Cinema as a resource server of its identity provider: which tokens it takes, what each action lets
/// a person do, who a change is recorded as, the manifest the provider reads, and the Activity's way in.
/// </summary>
public sealed class AccessTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private HttpClient With(string token)
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ── Which tokens are taken ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_token_the_provider_minted_for_cinema_is_taken()
    {
        using var response = await With(fixture.Issuer.Mint("acc-1")).GetAsync("/api/titles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_kgsm_token_is_refused()
    {
        // KGSM's audience is its cluster id; a token held there is refused here.
        using var response = await With(fixture.Issuer.Mint("acc-2", audience: "a-kgsm-cluster")).GetAsync("/api/titles");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.First().Scheme);
    }

    [Fact]
    public async Task A_token_from_another_issuer_is_refused() =>
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await With(fixture.Issuer.Mint("acc-3", issuer: "https://elsewhere.test")).GetAsync("/api/titles")).StatusCode);

    [Fact]
    public async Task A_token_signed_with_anything_but_es256_is_refused() =>
        // The RSA key is in the provider's key set; only the algorithm is wrong.
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await With(fixture.Issuer.Mint("acc-4", rsa: true)).GetAsync("/api/titles")).StatusCode);

    [Fact]
    public async Task A_token_that_is_not_an_access_token_is_refused() =>
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await With(fixture.Issuer.Mint("acc-5", type: "JWT")).GetAsync("/api/titles")).StatusCode);

    [Fact]
    public async Task An_expired_token_is_refused() =>
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await With(fixture.Issuer.Mint("acc-6", lifetime: TimeSpan.FromMinutes(-5))).GetAsync("/api/titles")).StatusCode);

    // ── What each action lets a person do ─────────────────────────────────────────────────

    [Fact]
    public async Task An_approved_account_without_watch_has_its_own_records_and_nothing_else()
    {
        var client = fixture.CreatePersonClient("acc-7", "Nell", TestIssuer.SelfOnly);

        using var titles = await client.GetAsync("/api/titles");
        using var preferences = await client.GetAsync("/api/me/preferences");
        using var ticket = await client.GetAsync($"/api/titles/{SessionFixture.ReadyTitle}/ticket");

        Assert.Equal(HttpStatusCode.Forbidden, titles.StatusCode);
        Assert.Equal(HttpStatusCode.OK, preferences.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ticket.StatusCode);
    }

    [Fact]
    public async Task Every_route_names_what_it_needs_and_every_action_it_names_is_declared()
    {
        var endpoints = fixture.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        Assert.NotEmpty(endpoints);

        foreach (var endpoint in endpoints)
        {
            var open = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            var access = endpoint.Metadata.GetMetadata<RequiredAccess>();
            var route = endpoint.RoutePattern.RawText ?? endpoint.DisplayName;

            Assert.True(open || access is not null, $"{route} names nothing it needs.");
            if (access is not null) Assert.All(access.AnyOf, a => Assert.True(CinemaActions.IsDeclared(a), $"{route} checks {a}."));
        }
    }

    [Fact]
    public async Task A_route_that_names_nothing_admits_nobody()
    {
        var policies = fixture.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        var fallback = await policies.GetFallbackPolicyAsync();

        Assert.NotNull(fallback);
        Assert.Empty(Assert.IsType<ActionRequirement>(Assert.Single(fallback!.Requirements)).AnyOf);
    }

    [Fact]
    public async Task A_film_s_bytes_open_to_its_ticket_or_to_somebody_who_may_watch_and_its_poster_to_anybody()
    {
        var path = $"/media/{SessionFixture.ReadyTitle}/manifest.json";

        using var nobody = await fixture.CreateClient().GetAsync(path);
        using var viewer = await fixture.CreatePersonClient("acc-8", "Ola", [CinemaActions.Watch]).GetAsync(path);
        using var poster = await fixture.CreateClient().GetAsync($"/media/{SessionFixture.ReadyTitle}/poster.jpg");

        Assert.Equal(HttpStatusCode.Unauthorized, nobody.StatusCode);
        Assert.Equal(HttpStatusCode.OK, viewer.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, poster.StatusCode);
    }

    [Fact]
    public async Task A_ticket_says_how_long_it_has_left_as_well_as_when_it_expires()
    {
        var viewer = fixture.CreatePersonClient("acc-8", "Ola", [CinemaActions.Watch]);
        var reply = await viewer.GetFromJsonAsync($"/api/titles/{SessionFixture.ReadyTitle}/ticket",
            TheKrystalShip.MovieBot.Api.ApiJsonContext.Default.MediaTicketReply);

        Assert.InRange(reply!.ExpiresIn, (long)TimeSpan.FromHours(12).TotalSeconds, (long)TimeSpan.FromHours(13).TotalSeconds);
        Assert.InRange((reply.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds, reply.ExpiresIn - 60, reply.ExpiresIn + 1);
    }

    // ── Who a change is recorded as ───────────────────────────────────────────────────────

    [Fact]
    public async Task The_bot_moving_a_room_is_recorded_as_the_person_through_the_bot()
    {
        var bot = fixture.CreatePersonClient("acc-9", "Pia", via: "cinema-bot");
        using var loaded = await bot.PostAsJsonAsync("/api/sessions/act-room/title", new SetTitleRequest(SessionFixture.ReadyTitle),
            ManifestJsonContext.Default.SetTitleRequest);
        Assert.Equal(HttpStatusCode.OK, loaded.StatusCode);

        using var played = await bot.PostAsJsonAsync("/api/sessions/act-room/play", new RoomPlaybackRequest(),
            ManifestJsonContext.Default.RoomPlaybackRequest);
        var change = await played.Content.ReadFromJsonAsync(ManifestJsonContext.Default.RoomChanged);

        Assert.Equal(new Actor("acc-9", "Pia", "cinema-bot"), change!.Push.State.UpdatedBy);
    }

    [Fact]
    public async Task Machine_work_loading_a_room_is_recorded_as_moviebot()
    {
        using var loaded = await fixture.CreateServiceClient().PostAsJsonAsync("/api/sessions/machine-room/title",
            new SetTitleRequest(SessionFixture.ReadyTitle), ManifestJsonContext.Default.SetTitleRequest);
        var push = await loaded.Content.ReadFromJsonAsync(ManifestJsonContext.Default.SessionStatePush);

        Assert.Equal("MovieBot", push!.State.UpdatedBy!.DisplayName);

        // And the service key moves no room on anybody's behalf.
        using var played = await fixture.CreateServiceClient().PostAsJsonAsync("/api/sessions/machine-room/play",
            new RoomPlaybackRequest(), ManifestJsonContext.Default.RoomPlaybackRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, played.StatusCode);
    }

    [Fact]
    public async Task Whoever_joins_a_room_is_whoever_the_token_names()
    {
        await using var hana = await fixture.ConnectAsync("hub-room", "acc-10", "Hana");

        var participants = fixture.Services.GetRequiredService<SessionStore>().Participants("hub-room");

        var only = Assert.Single(participants);
        Assert.Equal("acc-10", only.UserId);
        Assert.Equal("Hana", only.DisplayName);
    }

    [Fact]
    public async Task Somebody_who_may_not_watch_cannot_open_a_room()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(fixture.Server.BaseAddress, "/hub/session"), o =>
            {
                o.HttpMessageHandlerFactory = _ => fixture.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
                o.AccessTokenProvider = () => Task.FromResult<string?>(fixture.TokenFor("acc-11", "Ivo", TestIssuer.SelfOnly));
            })
            .Build();

        await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync());
        await connection.DisposeAsync();
    }

    // ── What the provider and the page read ───────────────────────────────────────────────

    [Fact]
    public async Task The_manifest_is_served_open_and_names_every_declared_action()
    {
        using var response = await fixture.CreateClient().GetAsync("/.well-known/tks-actions.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var manifest = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = manifest.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("cinema", root.GetProperty("component").GetString());
        Assert.Empty(root.GetProperty("requires").EnumerateArray());

        var actions = root.GetProperty("actions").EnumerateArray().ToList();
        Assert.Equal(CinemaActions.All.Select(a => a.LocalId).Order(StringComparer.Ordinal),
            actions.Select(a => a.GetProperty("id").GetString()!));
        Assert.All(actions, a => Assert.Equal("cluster", a.GetProperty("scope").GetString()));
        Assert.True(actions.Single(a => a.GetProperty("id").GetString() == "profile.read").GetProperty("self").GetBoolean());
        Assert.False(actions.Single(a => a.GetProperty("id").GetString() == "watch").TryGetProperty("self", out _));
    }

    [Fact]
    public async Task The_page_is_told_where_to_sign_in()
    {
        var config = await fixture.CreateClient().GetFromJsonAsync<JsonElement>("/api/config");

        Assert.Equal(TestIssuer.Issuer, config.GetProperty("issuer").GetString());
        Assert.Equal("cinema-site", config.GetProperty("clientId").GetString());
        Assert.Equal("cinema-tv", config.GetProperty("tvClientId").GetString());
        Assert.Equal("tks_actions", config.GetProperty("actionsClaim").GetString());
    }

    // ── The Activity's way in ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_activity_s_code_is_redeemed_at_discord_and_exchanged_at_the_provider()
    {
        var minted = fixture.Issuer.Mint("acc-12", "Jo", actor: "cinema-activity");
        fixture.Issuer.Exchange = form => form["subject_token"] == DiscordStub.AccessToken
            ? (HttpStatusCode.OK, fixture.Issuer.Minted(minted))
            : (HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");

        using var signedIn = await fixture.CreateClient().PostAsJsonAsync("/api/auth/discord/callback", new { code = "a-code" });
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        var body = await signedIn.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(DiscordStub.AccessToken, body.GetProperty("accessToken").GetString());
        Assert.Equal(minted, body.GetProperty("token").GetString());
        Assert.InRange(body.GetProperty("expiresIn").GetInt64(), 1, 300);

        var (client, form) = fixture.Issuer.Exchanges.Last(e => e.Form["subject_token"] == DiscordStub.AccessToken);
        Assert.Equal("cinema-activity", client);
        Assert.Equal(TokenExchangeTypes.Grant, form["grant_type"]);
        Assert.Equal(TokenExchangeTypes.DiscordAccessToken, form["subject_token_type"]);

        // Before the token runs out the Activity asks again with Discord's token, and nothing goes to Discord.
        using var renewed = await fixture.CreateClient().PostAsJsonAsync("/api/auth/discord/callback",
            new { accessToken = DiscordStub.AccessToken });
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);

        // The token it was given is one the API takes.
        Assert.Equal(HttpStatusCode.OK, (await With(minted).GetAsync("/api/titles")).StatusCode);
    }

    [Fact]
    public async Task A_discord_account_waiting_for_approval_is_told_so()
    {
        fixture.Issuer.Exchange = _ => (HttpStatusCode.BadRequest, """{"error":"invalid_grant","account_status":"pending"}""");

        using var response = await fixture.CreateClient().PostAsJsonAsync("/api/auth/discord/callback", new { code = "a-code" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("pending", body.GetProperty("accountStatus").GetString());
    }

    [Fact]
    public async Task A_code_discord_refuses_is_refused()
    {
        using var response = await fixture.CreateClient().PostAsJsonAsync("/api/auth/discord/callback",
            new { code = DiscordStub.RefusedCode });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── The issuer the records are kept against ───────────────────────────────────────────

    [Fact]
    public void The_issuer_is_recorded_the_first_time_and_another_is_refused_until_adopted()
    {
        var path = Path.Combine(Path.GetTempPath(), "moviebot-tests", Guid.NewGuid().ToString("n"), "moviebot.db");
        try
        {
            var database = new CinemaDatabase(path);
            var first = new OidcOptions { Issuer = "https://one.test" };
            IssuerRecord.Enforce(first, database, NullLogger.Instance);
            Assert.Equal("https://one.test", database.Meta("issuer"));
            IssuerRecord.Enforce(first, database, NullLogger.Instance);

            var other = new OidcOptions { Issuer = "https://two.test" };
            Assert.Throws<InvalidOperationException>(() => IssuerRecord.Enforce(other, database, NullLogger.Instance));
            Assert.Equal("https://one.test", database.Meta("issuer"));

            IssuerRecord.Enforce(new OidcOptions { Issuer = "https://two.test", AdoptIssuer = "https://two.test" }, database, NullLogger.Instance);
            Assert.Equal("https://two.test", database.Meta("issuer"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public async Task The_television_app_is_served_to_anybody_when_the_host_has_one()
    {
        var app = Path.Combine(Path.GetTempPath(), "moviebot-tests", Guid.NewGuid().ToString("n"), "tv.apk");
        Directory.CreateDirectory(Path.GetDirectoryName(app)!);
        try
        {
            using var factory = fixture.WithWebHostBuilder(builder =>
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["Tv:App"] = app })));
            var client = factory.CreateClient();

            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/tv.apk")).StatusCode);

            await File.WriteAllBytesAsync(app, [0x50, 0x4b, 0x03, 0x04]);
            var served = await client.GetAsync("/tv.apk");
            Assert.Equal(HttpStatusCode.OK, served.StatusCode);
            Assert.Equal("application/vnd.android.package-archive", served.Content.Headers.ContentType?.MediaType);
            Assert.Equal([0x50, 0x4b, 0x03, 0x04], await served.Content.ReadAsByteArrayAsync());
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(app)!, recursive: true);
        }
    }

    [Fact]
    public void The_api_does_not_start_against_an_issuer_its_records_were_not_kept_against()
    {
        var record = Path.Combine(Path.GetTempPath(), "moviebot-tests", Guid.NewGuid().ToString("n"), "moviebot.db");
        new CinemaDatabase(record).SetMeta("issuer", "https://previous.test");
        try
        {
            using var factory = fixture.WithWebHostBuilder(builder =>
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["Database:Path"] = record })));

            var thrown = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
            Assert.Contains("Oidc:AdoptIssuer", thrown.ToString());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(Path.GetDirectoryName(record)!, recursive: true);
        }
    }
}
