using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TheKrystalShip.Agent.Commands;
using TheKrystalShip.Agent.Service;
using TheKrystalShip.Agent.Service.Commands;
using TheKrystalShip.Agent.Service.Push;
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
        services.AddSingleton<EventStreams>();
        services.AddSingleton<TurnHub>();

        // What every TheKrystalShip assistant offers around the conversation, over this service's callers.
        services.AddSingleton<IAssistantCallers, CinemaCallers>();
        services.AddSingleton<IAssistantToolOffer, CinemaToolOffer>();
        services.AddSingleton<IAssistantConversationEvents, CinemaConversationEvents>();
        services.AddAssistantCommands(Commands, commands => commands.AddBaseline(clearSharedAction: Callers.Watch));
        services.AddAssistantPush<StagedAction>();
        services.AddSingleton<IConfirmationPushHost<StagedAction>, CinemaConfirmationPushHost>();
        services.Configure<AssistantPushOptions>(push =>
        {
            push.NotificationTag = "moviebot-confirmation";
            push.InterruptedMessage = "MovieBot restarted while this was going through. Check on it before asking again.";
            push.ConfirmUnavailable = CinemaConfirmationPushHost.ConfirmUnavailable;
        });
        return services;
    }

    /// <summary>
    /// The commands a person may type: the organization's baseline, each needing what talking to the
    /// assistant needs, and starting a room over needing the same, since anybody in a room may move it.
    /// <c>think</c> is left out: every request is sent with thinking off, because this model reasoning
    /// before a tool call triples the wait for it.
    /// </summary>
    public static readonly ChatCommandCatalog Commands =
        new(BaselineCommands.All(Callers.Watch).Where(c => c.Name != BaselineCommands.Think));

    /// <summary>
    /// The API. It carries no credential of its own: every call is made as the person a turn is for,
    /// with their token (<see cref="MovieBotApiClient.As"/>).
    /// </summary>
    public static IServiceCollection AddMovieBotApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<MovieBotApiClient>(http =>
        {
            // A relative request against a base address without a trailing slash silently drops the
            // last path segment, so the slash is not optional.
            http.BaseAddress = new Uri((configuration["Api:BaseUrl"] ?? "http://127.0.0.1:8099").TrimEnd('/') + "/");
            http.Timeout = TimeSpan.FromSeconds(15);
        });
        return services;
    }
}
