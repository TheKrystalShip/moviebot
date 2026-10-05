using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Bot.Configuration;

namespace TheKrystalShip.MovieBot.Bot.Assistant;

public static class AssistantServiceCollectionExtensions
{
    /// <summary>
    /// Registers the way to moviebot-assistant and the chat that answers through it. Off, a request the
    /// gate cannot read is answered by nothing, and the service is never called.
    /// </summary>
    public static IServiceCollection AddRoomAssistant(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AssistantOptions>().Bind(configuration.GetSection(AssistantOptions.Section));

        services.AddHttpClient<AssistantClient>((sp, http) =>
        {
            // A relative request against a base address without a trailing slash silently drops the
            // last path segment, so the slash is not optional.
            http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<AssistantOptions>>().Value.BaseUrl.TrimEnd('/') + "/");

            // A turn is the model's time plus every tool it calls; a confirmed download waits on the
            // tracker and the torrent client.
            http.Timeout = TimeSpan.FromSeconds(90);
        })
            // Every call is made for a person, with their token; the assistant takes nothing else.
            .AddHttpMessageHandler(sp => new PersonAuthorization(sp.GetRequiredService<PersonTokens>(), serviceKey: null));

        services.AddSingleton<RoomAssistantChat>();
        services.AddSingleton<IRoomAssistant>(sp =>
            sp.GetRequiredService<IOptions<AssistantOptions>>().Value.Enabled
                ? sp.GetRequiredService<RoomAssistantChat>()
                : new NoRoomAssistant());

        return services;
    }
}
