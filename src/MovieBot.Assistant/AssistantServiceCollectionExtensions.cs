using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TheKrystalShip.Llm.Conversation;
using TheKrystalShip.Llm.Extensions;
using TheKrystalShip.Llm.Interfaces;
using TheKrystalShip.MovieBot.Acquire.Imdb;
using TheKrystalShip.MovieBot.Assistant.Wire;
using TheKrystalShip.MovieBot.Client;

namespace TheKrystalShip.MovieBot.Assistant;

public static class AssistantServiceCollectionExtensions
{
    /// <summary>
    /// Registers the assistant: the model client and the conversation store from the agent loop's
    /// package, the prompts and catalog read from disk, the tools over the API, and the turns and
    /// streams the wire serves.
    /// </summary>
    public static IServiceCollection AddCinemaAssistant(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AssistantOptions>().Bind(configuration.GetSection(AssistantOptions.Section));
        services.AddOptions<AssistantAuthOptions>().Bind(configuration.GetSection(AssistantAuthOptions.Section));
        services.AddLocalLlm(configuration);

        // The loop is built per turn, because its tools act on one room as one person. The package's
        // process-wide loop would need a process-wide dispatcher, which this service does not have.
        services.RemoveAll<ILlmAgent>();

        services.AddOptions<ConversationOptions>()
            .PostConfigure<IOptions<AssistantOptions>>((conversation, assistant) =>
                conversation.DatabasePath = assistant.Value.ResolveDatabasePath());

        // The title index names a film the tracker has no release of yet, which is what a wish is made
        // from. No key, and named as itself: the index answers an unidentified client inconsistently.
        services.AddHttpClient<ImdbClient>(http =>
        {
            http.Timeout = TimeSpan.FromSeconds(10);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MovieBot/0.1 (+assistant)");
        });

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<PromptPack>();
        services.AddSingleton(sp => ToolCatalog.Load(
            sp.GetRequiredService<IOptions<AssistantOptions>>().Value.ResolvePromptDirectory(), RoomTools.Names));
        services.AddSingleton<TurnFacts>();
        services.AddSingleton<OfferedReleases>();
        services.AddSingleton<StagedActions>();
        services.AddSingleton<RoomToolbox>();
        services.AddSingleton<CinemaAssistant>();
        services.AddSingleton<Callers>();
        services.AddSingleton<EventStreams>();
        services.AddSingleton<TurnHub>();
        return services;
    }

    /// <summary>The API, reached with the service key as the services that act on somebody's behalf reach it.</summary>
    public static IServiceCollection AddMovieBotApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<MovieBotApiClient>(http =>
        {
            // A relative request against a base address without a trailing slash silently drops the
            // last path segment, so the slash is not optional.
            http.BaseAddress = new Uri((configuration["Api:BaseUrl"] ?? "http://127.0.0.1:8099").TrimEnd('/') + "/");
            http.Timeout = TimeSpan.FromSeconds(15);

            if (configuration["Api:ServiceKey"] is { Length: > 0 } key)
                http.DefaultRequestHeaders.Add("X-MovieBot-Service", key);
        });
        return services;
    }
}
