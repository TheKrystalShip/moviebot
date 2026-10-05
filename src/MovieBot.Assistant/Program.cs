using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Acquire.Configuration;
using TheKrystalShip.MovieBot.Assistant.Wire;

namespace TheKrystalShip.MovieBot.Assistant;

/// <summary>
/// moviebot-assistant: the brain behind MovieBot's conversation. The bot relays what people say in a
/// voice channel, the site sends what people type, and both read the same turns, proposals and
/// conversations from here.
/// </summary>
public partial class Program
{
    public static void Main(string[] args) => Build(args).Run();

    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Configuration.AddMovieBotSettings();

        // One line per entry, with the priority journald files it under.
        builder.Logging.ClearProviders();
        builder.Logging.AddSystemdConsole();

        // Every MovieBot program reads one settings file, and its Kestrel section is the API's
        // address. This service listens where its own section says.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Kestrel:Endpoints:Http:Url"] = builder.Configuration[$"{AssistantOptions.Section}:Listen"]
                                             ?? new AssistantOptions().Listen,
        });

        builder.Services.AddCinemaAssistant(builder.Configuration);
        builder.Services.AddMovieBotApi(builder.Configuration);
        builder.Services.AddHostedService<AssistantWarmup>();

        // Every caller is a person, by the access token the identity provider minted for Cinema; the
        // site sends it and the bot relays it. Without an issuer nobody can be recognised, which is a
        // misconfigured host, said at start-up rather than as a refusal in front of a room.
        builder.Services.AddCallerAuthentication();

        configure?.Invoke(builder);

        var app = builder.Build();
        app.Logger.LogInformation("Settings: {Settings}", MovieBotSettings.Describe());
        app.UseAuthentication();
        app.MapAssistant();
        return app;
    }
}
