using Microsoft.Extensions.Options;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Acquire.Configuration;
using TheKrystalShip.MovieBot.Assistant.Api;
using TheKrystalShip.MovieBot.Assistant.Tools;
using TheKrystalShip.MovieBot.Client;

namespace TheKrystalShip.MovieBot.Assistant;

/// <summary>
/// moviebot-assistant: the plugin that helps the people of Cinema find films and shows and plan to watch
/// them together. The assistant harness runs the model, the conversations and the memory; this service
/// brings the domain: a prompt, tools that read the API and propose wants and plans, and the skills.
/// </summary>
public partial class Program
{
    public static void Main(string[] args) => Build(args).Run();

    public static WebApplication Build(string[] args, Action<AssistantPluginApplicationBuilder>? configure = null)
    {
        var builder = AssistantPlugin.CreateBuilder(args, PluginIdentity.Id);
        builder.Configuration.AddMovieBotSettings();

        // Every MovieBot program reads one settings file, and its Kestrel section is the API's
        // address. This service listens where its own section says.
        if (builder.Configuration["Assistant:Listen"] is { Length: > 0 } listen)
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Kestrel:Endpoints:Http:Url"] = listen });

        // A proposal is made while the person is in the chat and waits in memory until they decide, and the
        // people a plan concerns are told by the API's own notifications, so the plugin keeps no state of its
        // own and pushes nothing.
        builder.Services.Configure<AssistantPluginOptions>(options =>
        {
            options.DataDirectory = "";
            options.Push.Enabled = false;
        });

        // The API, asked as the person whose turn it is through the client the bot shares: the token goes
        // on each call, never on the client. A relative path against a base address without a trailing
        // slash drops its last segment, so the slash is kept.
        builder.Services.AddHttpClient<MovieBotApiClient>(http =>
            {
                http.BaseAddress = new Uri((builder.Configuration["Api:BaseUrl"] ?? "http://127.0.0.1:8099").TrimEnd('/') + "/");
                http.Timeout = TimeSpan.FromSeconds(60);
            })
            .RecordTo(builder.Configuration["Assistant:RecordApiTo"] ?? "");
        builder.Services.AddTransient<CinemaApi>();

        builder.Plugin
            .AddGeneratedTools()
            .Guard(CinemaGuards.Declare)
            .Speech(speech => speech.Vocabulary("Cinema"))
            .Cards(CardJson.Default)
            .AddPeople<CinemaPeople>()
            .AddTurnContext<CinemaTurnContext>();

        // Every caller is a person, by the access token the identity provider minted for Cinema; the
        // site sends it. Without an issuer nobody can be recognised, which is a misconfigured host, said
        // at start-up rather than as a refusal in front of a person.
        builder.Services.AddCallerAuthentication();

        configure?.Invoke(builder);

        var app = builder.Build();
        app.Logger.LogInformation("Settings: {Settings}", MovieBotSettings.Describe());

        app.UseAuthentication();
        app.MapAssistantPlugin();
        return app;
    }
}
