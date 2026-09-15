using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TheKrystalShip.Llm.Conversation;
using TheKrystalShip.Llm.Extensions;
using TheKrystalShip.Llm.Interfaces;

namespace TheKrystalShip.MovieBot.Bot.Assistant;

public static class AssistantServiceCollectionExtensions
{
    /// <summary>
    /// Registers the assistant: the model client and the conversation store from the agent loop's
    /// package, the prompt pack and catalog read from disk, the room's tools, and the chat that
    /// answers. The commands the tools carry out are the bot's own and are registered with the rest of
    /// the bot.
    /// </summary>
    public static IServiceCollection AddRoomAssistant(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AssistantOptions>().Bind(configuration.GetSection(AssistantOptions.Section));
        services.AddLocalLlm(configuration);

        // The loop is built per turn, because its tools act on one room as one person. The package's
        // process-wide loop would need a process-wide dispatcher, which this bot does not have.
        services.RemoveAll<ILlmAgent>();

        // One file is the conversation, in the state directory beside the wish list.
        services.AddOptions<ConversationOptions>()
            .PostConfigure<IOptions<AssistantOptions>>((conversation, assistant) =>
                conversation.DatabasePath = assistant.Value.ResolveDatabasePath());

        services.AddSingleton<PromptPack>();
        services.AddSingleton(sp => ToolCatalog.Load(
            sp.GetRequiredService<IOptions<AssistantOptions>>().Value.ResolvePromptDirectory(), RoomTools.Names));
        services.AddSingleton<RoomFacts>();
        services.AddSingleton<OfferedReleases>();
        services.AddSingleton<StagedActions>();
        services.AddSingleton<RoomToolbox>();
        services.AddSingleton<RoomAssistant>();
        services.AddSingleton<RoomAssistantChat>();

        // Off, a request the gate cannot read is answered by nothing, and nothing of the model is
        // constructed: no client, no database file.
        services.AddSingleton<IRoomAssistant>(sp =>
            sp.GetRequiredService<IOptions<AssistantOptions>>().Value.Enabled
                ? sp.GetRequiredService<RoomAssistantChat>()
                : new NoRoomAssistant());

        services.AddHostedService<AssistantWarmup>();
        return services;
    }
}
