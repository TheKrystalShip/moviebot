using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Acquire;
using TheKrystalShip.MovieBot.Acquire.Configuration;
using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.MovieBot.Handoff;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddMovieBotSettings();

builder.Services.Configure<HandoffOptions>(
    builder.Configuration.GetSection(HandoffOptions.Section));

// Everything below resolves a title against this, so a blank one is answered here rather than by
// a background service dying on its first tick.
if (string.IsNullOrWhiteSpace(
        builder.Configuration.GetSection(HandoffOptions.Section)["MediaRoot"]))
{
    Console.Error.WriteLine(
        "No media root. Set Handoff.MediaRoot in moviebot.settings.json to the directory the API "
        + $"serves from. Settings read: {MovieBotSettings.Describe()}");
    return 1;
}

// Only the torrent client half of this is used. The tracker half comes along because it is one
// library, and it costs nothing while nothing calls it.
builder.Services.AddAcquire(builder.Configuration);

// The title index, and something to fetch artwork with. No key: the index that answers the site's
// own search box is open, and what it holds — the name, the year, the billing and the poster — is
// all of what a message about a film shows.
builder.Services.AddHttpClient<FilmMetadata>(http =>
{
    http.Timeout = TimeSpan.FromSeconds(20);
    http.DefaultRequestHeaders.UserAgent.ParseAdd("MovieBot/0.1 (+handoff)");
});

builder.Services.AddSingleton<Backfill>();

// The disk films are made on, and the one they are kept on. Resolved here so that every worker
// asks one thing where a title's files are, rather than each of them combining a root with an id
// and disagreeing the first time a film moves.
builder.Services.AddSingleton(sp =>
{
    var handoff = sp.GetRequiredService<IOptions<HandoffOptions>>().Value;
    return new MediaRoots(handoff.MediaRoot, handoff.ColdRoot);
});
builder.Services.AddSingleton<ColdStore>();

// What can be skipped in an episode: found once it is ready, from its chapters or by comparing its
// audio with the show's other episodes. The locks keep that pass and settling off one title at once.
builder.Services.AddSingleton<TitleLocks>();
builder.Services.AddSingleton<FingerprintStore>();
builder.Services.AddSingleton<MarkerPass>();
builder.Services.AddSingleton<MarkerBackfill>();

// What this asks the API: which films the rooms hold, so none is removed from under a room, and
// which settled films the library's clock says are due. Reached with the service key, because both
// are behind the same door as the films.
builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection(ApiOptions.Section));
void ReachApi(IServiceProvider sp, HttpClient http)
{
    var api = sp.GetRequiredService<IOptions<ApiOptions>>().Value;
    http.BaseAddress = new Uri(api.BaseUrl.TrimEnd('/') + "/");
    http.Timeout = TimeSpan.FromSeconds(10);

    if (api.ServiceKey.Length > 0)
        http.DefaultRequestHeaders.Add("X-MovieBot-Service", api.ServiceKey);
}

builder.Services.AddHttpClient<OccupiedRooms>(ReachApi);
builder.Services.AddHttpClient<DueTitles>(ReachApi);
builder.Services.AddSingleton<ISeedingDownloads, ClientDownloads>();

// A one-shot pass over films already in the library, for the ones that arrived before there was
// anywhere to keep a name or a poster. It is the same resolver the ingest uses, pointed at
// directories rather than at a download, so the two cannot disagree about what a film is called.
if (args.Contains("--backfill"))
{
    builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
    using var host = builder.Build();

    return await host.Services.GetRequiredService<Backfill>()
        .RunAsync(host.Services.GetRequiredService<MediaRoots>(), CancellationToken.None);
}

// The same, for the markers of episodes already in the library.
if (args.Contains("--backfill-markers"))
{
    builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
    using var host = builder.Build();

    return await host.Services.GetRequiredService<MarkerBackfill>()
        .RunAsync(host.Services.GetRequiredService<MediaRoots>(), CancellationToken.None);
}

// Encodes the settled films again at the library's quality, while nobody is watching. It runs for
// hours, so a stop signal ends the encode in flight and leaves that film as it was.
if (args.Contains("--reencode"))
{
    builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
    builder.Services.AddSingleton<LibraryReencode>();
    using var host = builder.Build();

    using var stopping = new CancellationTokenSource();
    void Stop(PosixSignalContext context) { context.Cancel = true; stopping.Cancel(); }
    using var onTerm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop);
    using var onInt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop);

    var only = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
    try
    {
        return await host.Services.GetRequiredService<LibraryReencode>()
            .RunAsync(only, waitForIdle: !args.Contains("--now"), stopping.Token);
    }
    catch (OperationCanceledException) when (stopping.IsCancellationRequested)
    {
        return 130;
    }
}

builder.Services.AddHostedService<HandoffWorker>();
builder.Services.AddHostedService<RetentionWorker>();
builder.Services.AddHostedService<LibraryWorker>();
builder.Services.AddHostedService<SettleWorker>();

builder.Logging.AddSimpleConsole(o => o.SingleLine = true);

var app = builder.Build();
app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MovieBot.Handoff")
    .LogInformation("Settings: {Files}", MovieBotSettings.Describe());
await app.RunAsync();
return 0;
