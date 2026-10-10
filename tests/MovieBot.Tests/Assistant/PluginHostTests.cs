using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TheKrystalShip.Agent.Contracts;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.Agent.Plugin.Testing;
using TheKrystalShip.MovieBot.Assistant;
using TheKrystalShip.MovieBot.Assistant.Api;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests.Assistant;

/// <summary>
/// The service built the way it runs, in process: the plugin package's routes behind Cinema's sign-in,
/// registering its profile with a harness the test answers, calling a world the test holds.
/// </summary>
public sealed class PluginHostTests : IAsyncLifetime
{
    private readonly TestIssuer _issuer = new();
    private readonly FakeHarness _harness = new();
    private readonly ReplayHandler _world = CinemaWorld.Build();
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private bool _providerDown;

    public async Task InitializeAsync()
    {
        _app = TheKrystalShip.MovieBot.Assistant.Program.Build(
            [
                "--Assistant:HarnessAddress=http://harness.test",
                "--Assistant:PluginKey=a-test-plugin-key",
                $"--Oidc:Issuer={TestIssuer.Issuer}",
                "--Api:BaseUrl=http://api.test/",
            ],
            builder =>
            {
                builder.Host.WebHost.UseTestServer();
                builder.Services.Configure<JwtBearerOptions>(
                    JwtBearerDefaults.AuthenticationScheme, o => o.BackchannelHttpHandler = new Switchable(_issuer.Handler(), () => _providerDown));
                builder.Services.AddHttpClient<HarnessClient>().ConfigurePrimaryHttpMessageHandler(() => _harness);
                builder.Services.AddHttpClient<CinemaApi>().ConfigurePrimaryHttpMessageHandler(() => _world);
            });
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private HttpRequestMessage As(string path, string? token, HttpMethod? method = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    [Fact]
    public async Task The_service_registers_the_profile_it_ships_under_its_id_with_the_key_it_was_given()
    {
        Assert.Equal("a-test-plugin-key", _harness.Key);
        Assert.Equal("/profiles/moviebot", _harness.RegisteredAt);

        PluginProfile sent = _harness.Registered!;
        Assert.Equal("moviebot", sent.Id);
        Assert.Equal(
            ["search_titles", "title_details", "person_credits", "discover", "find_by_clues", "whats_new", "my_taste", "anticipated"], sent.Tools.Select(t => t.Name));
        Assert.Equal(5, sent.Skills.Count);
        Assert.Equal(_app.Services.GetRequiredService<PluginRegistration>().Profile.ContentHash(), sent.ContentHash());

        using HttpResponseMessage health = await _client.GetAsync("/health");
        using JsonDocument body = JsonDocument.Parse(await health.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("registered").GetBoolean());
        Assert.Equal("fake-profile-hash", body.RootElement.GetProperty("profile").GetString());
    }

    [Fact]
    public async Task The_service_keeps_no_state_and_announces_nothing()
    {
        var options = _app.Services.GetRequiredService<IOptions<AssistantPluginOptions>>().Value;

        Assert.Equal("", options.DataDirectory);
        Assert.False(options.Push.Enabled);
    }

    [Fact]
    public async Task A_request_with_no_token_is_nobody()
    {
        using HttpResponseMessage response = await _client.SendAsync(As("/tools", null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_for_another_audience_is_nobody()
    {
        using HttpResponseMessage response = await _client.SendAsync(
            As("/tools", _issuer.Mint("u", "Sam", audience: "kgsm-cluster")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_that_cannot_watch_is_refused_not_unknown()
    {
        using HttpResponseMessage response = await _client.SendAsync(
            As("/tools", _issuer.Mint("u", "Sam", TestIssuer.SelfOnly)));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("does not let the person watch", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_provider_that_cannot_be_asked_is_a_bad_gateway_not_a_bad_token()
    {
        string token = _issuer.Mint("u", "Sam");
        _providerDown = true;

        using HttpResponseMessage response = await _client.SendAsync(As("/tools", token));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task A_person_who_may_watch_is_told_the_tools()
    {
        using HttpResponseMessage response = await _client.SendAsync(As("/tools", _issuer.Mint("u", "Sam")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string text = await response.Content.ReadAsStringAsync();
        foreach (string tool in new[] { "search_titles", "title_details", "person_credits", "discover", "find_by_clues", "whats_new", "my_taste", "anticipated" })
            Assert.Contains(tool, text);
    }

    [Fact]
    public async Task The_person_a_token_names_is_the_person_a_turn_is_for()
    {
        var people = _app.Services.GetRequiredService<IPluginPeople>();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer the-access-token";
        context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [
                new System.Security.Claims.Claim("sub", "sub-9"),
                new System.Security.Claims.Claim("name", "Sam Rivers"),
                new System.Security.Claims.Claim("tks_actions", "cinema:watch"),
            ],
            JwtBearerDefaults.AuthenticationScheme));

        PluginPerson? person = await people.ResolveAsync(context);

        Assert.Equal("sub-9", person!.Subject);
        Assert.Equal("Sam Rivers", person.DisplayName);
        Assert.Equal("the-access-token", ((Caller)person.Principal!).AccessToken);
    }

    [Fact]
    public async Task A_person_without_a_display_name_is_called_by_their_username_then_their_subject()
    {
        var people = _app.Services.GetRequiredService<IPluginPeople>();
        PluginPerson? Resolve(params System.Security.Claims.Claim[] claims)
        {
            var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            context.Request.Headers.Authorization = "Bearer t";
            context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                [new("sub", "sub-1"), new("tks_actions", "cinema:watch"), .. claims], JwtBearerDefaults.AuthenticationScheme));
            return people.ResolveAsync(context).AsTask().Result;
        }

        Assert.Equal("sam", Resolve(new System.Security.Claims.Claim("preferred_username", "sam"))!.DisplayName);
        Assert.Equal("sub-1", Resolve()!.DisplayName);
    }

    [Fact]
    public async Task The_turn_context_is_the_persons_name_and_nothing_else()
    {
        var context = _app.Services.GetRequiredService<IPluginTurnContext>();

        var blocks = await context.BuildAsync(new PluginPerson("u", "Sam"), "main", CancellationToken.None);

        var block = Assert.Single(blocks);
        Assert.Equal("person", block.Name);
        Assert.Equal("Sam", block.Text);
    }

    [Fact]
    public async Task The_tools_the_service_registers_are_the_ones_the_profile_declares()
    {
        Assert.Null(Record.Exception(() => _app.Services.GetRequiredService<PluginRegistration>().RequireAgreement()));
        await Task.CompletedTask;
    }

    /// <summary>The provider's backchannel, which can be made to fail.</summary>
    private sealed class Switchable(HttpMessageHandler inner, Func<bool> down) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            down() ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)) : base.SendAsync(request, ct);
    }

    /// <summary>The harness as far as a plugin starting up meets it: it takes a profile and answers with a hash.</summary>
    private sealed class FakeHarness : HttpMessageHandler
    {
        public PluginProfile? Registered { get; private set; }
        public string? RegisteredAt { get; private set; }
        public string? Key { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Put && request.RequestUri!.AbsolutePath.StartsWith("/profiles/", StringComparison.Ordinal))
            {
                RegisteredAt = request.RequestUri.AbsolutePath;
                Key = request.Headers.Authorization?.Parameter;
                Registered = PluginProfile.FromJson(await request.Content!.ReadAsStringAsync(ct));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"hash":"fake-profile-hash"}""", Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
