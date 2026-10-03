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

        // Without the signing key no person can be recognised, and without the service key the bot
        // cannot speak for anybody. Either missing is a misconfigured host, said at start-up rather
        // than as a refusal in front of a room.
        builder.Services.AddOptions<AssistantAuthOptions>()
            .Validate(o => o.SigningKey.Length > 0, "Auth:SigningKey is empty; set it to the key the API signs room tokens with.")
            .Validate(o => o.ServiceKey.Length > 0, "Auth:ServiceKey is empty; set it to the key the bot proves itself with.")
            .ValidateOnStart();

        configure?.Invoke(builder);

        var app = builder.Build();
        app.Logger.LogInformation("Settings: {Settings}", MovieBotSettings.Describe());
        app.MapAssistant();
        return app;
    }
}
