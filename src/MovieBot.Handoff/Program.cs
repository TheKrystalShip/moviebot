using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Acquire;
using TheKrystalShip.MovieBot.Handoff;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<HandoffOptions>(
    builder.Configuration.GetSection(HandoffOptions.Section));

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

// A one-shot pass over films already in the library, for the ones that arrived before there was
// anywhere to keep a name or a poster. It is the same resolver the ingest uses, pointed at
// directories rather than at a download, so the two cannot disagree about what a film is called.
if (args.Contains("--backfill"))
{
    builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
    using var host = builder.Build();

    var root = host.Services.GetRequiredService<IOptions<HandoffOptions>>().Value.MediaRoot;
    return await host.Services.GetRequiredService<Backfill>()
        .RunAsync(root, CancellationToken.None);
}

builder.Services.AddHostedService<HandoffWorker>();

builder.Logging.AddSimpleConsole(o => o.SingleLine = true);

await builder.Build().RunAsync();
return 0;
