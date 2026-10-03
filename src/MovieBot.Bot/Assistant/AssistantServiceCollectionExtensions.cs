using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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

            // The assistant knows the bot by the same key the API does.
            if (sp.GetRequiredService<IOptions<ApiOptions>>().Value.ServiceKey is { Length: > 0 } key)
                http.DefaultRequestHeaders.Add("X-MovieBot-Service", key);
        });

        services.AddSingleton<RoomAssistantChat>();
        services.AddSingleton<IRoomAssistant>(sp =>
            sp.GetRequiredService<IOptions<AssistantOptions>>().Value.Enabled
                ? sp.GetRequiredService<RoomAssistantChat>()
                : new NoRoomAssistant());

        return services;
    }
}
