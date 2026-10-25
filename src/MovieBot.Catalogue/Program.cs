using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Acquire;
using TheKrystalShip.MovieBot.Acquire.Configuration;
using TheKrystalShip.MovieBot.Catalogue.Data;
using TheKrystalShip.MovieBot.Catalogue.Entries;
using TheKrystalShip.MovieBot.Catalogue.Imdb;
using TheKrystalShip.MovieBot.Catalogue.Images;
using TheKrystalShip.MovieBot.Catalogue.Tmdb;
using TheKrystalShip.MovieBot.Catalogue.Wikidata;

namespace TheKrystalShip.MovieBot.Catalogue;

/// <summary>
/// The catalogue: one entry per title, built from IMDb, the film database and Wikidata, kept current by
/// the workers below and written to the file the API reads. Nothing here answers a request.
/// </summary>
public static class CatalogueProgram
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Configuration.AddMovieBotSettings();
        builder.Logging.AddSimpleConsole(o => o.SingleLine = true);

        builder.Services.AddOptions<CatalogueOptions>().Bind(builder.Configuration.GetSection(CatalogueOptions.Section));
        builder.Services.AddOptions<TmdbOptions>().Bind(builder.Configuration.GetSection(TmdbOptions.Section));
        builder.Services.AddSingleton(TimeProvider.System);

        builder.Services.AddSingleton(sp => new CatalogueDatabase(Path.Combine(CatalogueDirectory(sp), CatalogueDatabase.FileName)));
        builder.Services.AddSingleton(sp => new ImageStore(Path.Combine(CatalogueDirectory(sp), "images")));
        builder.Services.AddSingleton<EntryBuilder>();

        builder.Services.AddSingleton<TmdbGate>();
        builder.Services.AddHttpClient<TmdbSource>((sp, http) =>
            http.Timeout = TimeSpan.FromSeconds(Math.Max(15, sp.GetRequiredService<IOptions<TmdbOptions>>().Value.TimeoutSeconds)))
            .KeepConnectionsOpen();
        builder.Services.AddSingleton<TmdbLayer>();
        builder.Services.AddHttpClient<ImdbDatasets>(http => http.Timeout = Timeout.InfiniteTimeSpan);
        builder.Services.AddHttpClient<WikidataSource>(http => http.Timeout = TimeSpan.FromSeconds(90));

        // The library's roots, read for the shows it holds.
        builder.Services.AddSingleton(sp => new LibraryShows(
            new[] { builder.Configuration["Media:Root"], builder.Configuration["Media:ColdRoot"] }
                .Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r!).ToList(),
            sp.GetRequiredService<CatalogueDatabase>(), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<LibraryShows>>()));

        builder.Services.AddHostedService<ImdbWorker>();
        builder.Services.AddHostedService<TmdbWorker>();
        builder.Services.AddHostedService<ChangesWorker>();
        builder.Services.AddHostedService<RankingsWorker>();
        builder.Services.AddHostedService<WikidataWorker>();
        builder.Services.AddHostedService<BuildWorker>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<LibraryShows>());
        builder.Services.AddHostedService<ImageWorker>();

        var app = builder.Build();
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MovieBot.Catalogue");
        log.LogInformation("Settings: {Files}", MovieBotSettings.Describe());
        log.LogInformation("Catalogue: {Path}", app.Services.GetRequiredService<CatalogueDatabase>().Path);
        await app.RunAsync();
    }

    /// <summary>
    /// The directory the database and images live in: the setting, or the state directory systemd hands
    /// the unit (the first, when it names several), or the working directory without one.
    /// </summary>
    private static string CatalogueDirectory(IServiceProvider sp) =>
        sp.GetRequiredService<IOptions<CatalogueOptions>>().Value.Directory is { Length: > 0 } configured
            ? Path.GetFullPath(configured)
            : Environment.GetEnvironmentVariable("STATE_DIRECTORY")?.Split(':').FirstOrDefault() is { Length: > 0 } state
                ? state
                : Directory.GetCurrentDirectory();
}
