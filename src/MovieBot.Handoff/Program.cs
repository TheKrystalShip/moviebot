using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Acquire;
using TheKrystalShip.MovieBot.Handoff;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<HandoffOptions>(
    builder.Configuration.GetSection(HandoffOptions.Section));

// Only the torrent client half of this is used. The tracker half comes along because it is one
// library, and it costs nothing while nothing calls it.
builder.Services.AddAcquire(builder.Configuration);

builder.Services.AddHostedService<HandoffWorker>();

builder.Logging.AddSimpleConsole(o => o.SingleLine = true);

await builder.Build().RunAsync();
