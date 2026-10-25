using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Catalogue;

namespace TheKrystalShip.MovieBot.Api.Catalogue;

/// <summary>Where the catalogue <c>moviebot-catalogue</c> writes is (<c>Catalogue</c> in <c>moviebot.settings.json</c>).</summary>
public sealed class CatalogueReadOptions
{
    public const string Section = "Catalogue";

    /// <summary>The catalogue's directory, holding <c>catalogue.db</c> and <c>images/</c>.</summary>
    public string Directory { get; set; } = "/var/lib/moviebot-catalogue";
}

/// <summary>
/// Whether the catalogue can be read, measured at most every <see cref="Recheck"/>: a route that cannot
/// answer says why (<c>503</c>) rather than answering nothing as though nothing matched.
/// </summary>
public sealed class CatalogueState(CatalogueReader reader, TimeProvider clock, ILogger<CatalogueState> logger)
{
    private static readonly TimeSpan Recheck = TimeSpan.FromSeconds(30);
    private (string? Problem, DateTimeOffset At) _held = (null, DateTimeOffset.MinValue);

    public string? Problem()
    {
        var held = _held;
        if (clock.GetUtcNow() - held.At < Recheck) return held.Problem;
        var problem = reader.Problem();
        if (problem != held.Problem)
        {
            if (problem is null) logger.LogInformation("The catalogue at {Path} can be read.", reader.Path);
            else logger.LogWarning("{Problem}", problem);
        }
        _held = (problem, clock.GetUtcNow());
        return problem;
    }
}

public static class CatalogueSetup
{
    /// <summary>The catalogue as this service reads it: the file, the queries, the person's joins and the facet index.</summary>
    public static IServiceCollection AddCatalogue(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CatalogueReadOptions>().Bind(configuration.GetSection(CatalogueReadOptions.Section));
        services.AddSingleton(sp => new CatalogueReader(
            Path.Combine(sp.GetRequiredService<IOptions<CatalogueReadOptions>>().Value.Directory, "catalogue.db")));
        services.AddSingleton<CatalogueQueries>();
        services.AddSingleton<CatalogueTitles>();
        services.AddSingleton<CatalogueState>();
        services.AddSingleton<CatalogueViewers>();
        services.AddSingleton<FacetIndex>();
        services.AddHostedService(sp => sp.GetRequiredService<FacetIndex>());
        services.AddSingleton<CatalogueQuestions>();
        services.AddSingleton<DiscoverIndex>();
        services.AddHostedService(sp => sp.GetRequiredService<DiscoverIndex>());
        return services;
    }

    /// <summary>Where an image the catalogue keeps is, or null for a size or a name it never keeps.</summary>
    public static string? ImageFile(CatalogueReadOptions options, string size, string file) =>
        ImageUrls.Sizes.Contains(size) && ImageUrls.IsFile(file) ? Path.Combine(options.Directory, "images", size, file) : null;
}
