using Microsoft.Extensions.Options;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Acquire.Configuration;
using TheKrystalShip.MovieBot.Assistant.Api;
using TheKrystalShip.MovieBot.Assistant.Tools;

namespace TheKrystalShip.MovieBot.Assistant;

/// <summary>
/// moviebot-assistant: the plugin that helps the people of Krystal Cinema find films and shows. The
/// assistant harness runs the model, the conversations and the memory; this service brings the domain,
/// which is a prompt, six read-only tools over the film database through the API, and five skills.
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

        builder.Services.AddOptions<AssistantOptions>().BindConfiguration(AssistantOptions.Section);
        AssistantOptions assistant = builder.Configuration.GetSection(AssistantOptions.Section).Get<AssistantOptions>()
                                     ?? new AssistantOptions();

        // The API, asked as the person whose turn it is: the token goes on each call, never on the client.
        string api = builder.Configuration["Api:BaseUrl"] ?? "http://127.0.0.1:8099";
        IHttpClientBuilder client = builder.Services.AddHttpClient<CinemaApi>(http =>
        {
            http.BaseAddress = new Uri(api);
            http.Timeout = TimeSpan.FromSeconds(60);
        });
        if (assistant.RecordApiTo.Length > 0)
            client.AddHttpMessageHandler(() => new RecordingHandler(assistant.RecordApiTo));

        builder.Services.AddAssistantPlugin(options =>
            {
                options.PluginId = PluginIdentity.Id;
                options.Version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "";
                options.HarnessAddress = new Uri(assistant.HarnessAddress);
                options.PluginKey = assistant.PluginKey;
                options.Directory = Path.Combine(AppContext.BaseDirectory, "profile");

                // No tool proposes anything and nothing is announced to a phone, so the plugin keeps no
                // state of its own.
                options.DataDirectory = "";
                options.Push.Enabled = false;
            })
            .AddTool<SearchTitlesTool>()
            .AddTool<TitleDetailsTool>()
            .AddTool<PersonCreditsTool>()
            .AddTool<DiscoverTool>()
            .AddTool<WhatsNewTool>()
            .AddTool<MyTasteTool>()
            .AddPeople<CinemaPeople>()
            .AddTurnContext<CinemaTurnContext>();

        // Every caller is a person, by the access token the identity provider minted for Cinema; the
        // site sends it. Without an issuer nobody can be recognised, which is a misconfigured host, said
        // at start-up rather than as a refusal in front of a person.
        builder.Services.AddCallerAuthentication();

        configure?.Invoke(builder);

        var app = builder.Build();
        app.Logger.LogInformation("Settings: {Settings}", MovieBotSettings.Describe());
        if (string.IsNullOrEmpty(assistant.PluginKey))
            app.Logger.LogError("Assistant:PluginKey is not set: the harness will refuse this plugin and the assistant is unavailable.");

        app.UseAuthentication();

        // Alive, and whether the harness holds this plugin's profile. A plugin the harness has not
        // accepted is running and of no use, so the deploy waits for registered.
        app.MapGet("/health", (PluginRegistration registration) =>
            Results.Json(new HealthBody("ok", registration.Hash is not null, registration.Hash)));

        app.MapAssistant();
        return app;
    }

    private sealed record HealthBody(string Status, bool Registered, string? Profile);
}
