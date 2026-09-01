using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Bot.Configuration;
using TheKrystalShip.MovieBot.Bot.Discord;
using TheKrystalShip.MovieBot.Bot.Find;
using TheKrystalShip.MovieBot.Acquire;
using TheKrystalShip.MovieBot.Bot.Launch;
using TheKrystalShip.MovieBot.Bot.Watch;

var builder = Host.CreateApplicationBuilder(args);

// The host publishes the two credentials under their own names. They go in underneath every
// other source, so a developer who cannot read them can still put a token in user-secrets and
// have it win.
builder.Configuration.Sources.Insert(0, new MemoryConfigurationSource
{
    InitialData = new Dictionary<string, string?>
    {
        ["Discord:Token"] = Environment.GetEnvironmentVariable("MOVIEBOT_TOKEN"),
        ["Discord:ApplicationId"] = Environment.GetEnvironmentVariable("MOVIEBOT_CLIENTID")
    }.Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
     .ToDictionary(pair => pair.Key, pair => pair.Value)
});

builder.Services.AddOptions<DiscordOptions>()
    .Bind(builder.Configuration.GetSection(DiscordOptions.Section));

builder.Services.AddOptions<ApiOptions>()
    .Bind(builder.Configuration.GetSection(ApiOptions.Section))
    .Validate(o => IsWebAddress(o.BaseUrl),
        "Api:BaseUrl must be an absolute http or https address.")
    .Validate(o => o.PublicBaseUrl is null or "" || IsWebAddress(o.PublicBaseUrl),
        "Api:PublicBaseUrl must be an absolute http or https address when it is set.")
    .ValidateOnStart();

// Checked at startup rather than when somebody asks for a film: the launch link is the entire
// point of the reply, and a bad one is only discovered by the room.
builder.Services.AddOptions<PlayerOptions>()
    .Bind(builder.Configuration.GetSection(PlayerOptions.Section))
    .Validate(o => IsWebAddress(o.BaseUrl),
        "Player:BaseUrl must be an absolute http or https address. It is the link people are handed.")
    .ValidateOnStart();

builder.Services.AddHttpClient<MovieBotApiClient>((sp, http) =>
{
    var api = sp.GetRequiredService<IOptions<ApiOptions>>().Value;

    // A relative request against a base address without a trailing slash silently drops the
    // last path segment, so the slash is not optional.
    http.BaseAddress = new Uri(api.BaseUrl.TrimEnd('/') + "/");
    http.Timeout = TimeSpan.FromSeconds(10);

    if (api.ServiceKey.Length > 0)
        http.DefaultRequestHeaders.Add("X-MovieBot-Service", api.ServiceKey);
});

builder.Services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
{
    // Guilds carries the channel graph and GuildVoiceStates says who is in which voice channel,
    // which is the whole of what the bot needs to know about a server. Both are unprivileged.
    GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildVoiceStates,
    AlwaysDownloadUsers = false
}));

// The Activity is the front door; the link is what answers when it cannot be opened — no
// application id, a channel the bot cannot see, or a missing Create Instant Invite. Registering
// the link presenter as the concrete fallback keeps that path exercised rather than theoretical.
builder.Services.AddOptions<LaunchOptions>().Bind(builder.Configuration.GetSection(LaunchOptions.Section));
builder.Services.AddSingleton<LinkLaunchPresenter>();
builder.Services.AddSingleton<ILaunchPresenter>(sp => new ActivityLaunchPresenter(
    sp.GetRequiredService<DiscordSocketClient>(),
    sp.GetRequiredService<IOptions<DiscordOptions>>(),
    sp.GetRequiredService<IOptions<ApiOptions>>(),
    sp.GetRequiredService<IOptions<LaunchOptions>>(),
    sp.GetRequiredService<LinkLaunchPresenter>(),
    sp.GetRequiredService<ILogger<ActivityLaunchPresenter>>()));
builder.Services.AddSingleton<WatchCommand>();

// The acquiring half. It brings its own options, its tracker client and its torrent client, and
// it is reflection-free, so it adds nothing to this project's startup beyond what it is asked.
builder.Services.AddAcquire(builder.Configuration);
builder.Services.AddSingleton<FindCommand>();

builder.Services.AddHostedService<DiscordBotService>();

// Announces a finished download in the channel it was asked for. Separate from the gateway
// service because it has to keep running between commands, which is the whole point of it.
builder.Services.AddHostedService<DownloadWatcher>();

// Keeps each download's own message showing where it has got to. Separate from the announcement
// because editing a message notifies nobody: this is for whoever checks back, the announcement is
// what reaches whoever walked away.
builder.Services.AddHostedService<DownloadProgressUpdater>();

await builder.Build().RunAsync();

static bool IsWebAddress(string? value) =>
    Uri.TryCreate(value, UriKind.Absolute, out var uri)
    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
