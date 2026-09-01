namespace TheKrystalShip.MovieBot.Bot.Configuration;

/// <summary>
/// The gateway credentials and the guilds this bot answers in.
/// </summary>
public sealed class DiscordOptions
{
    public const string Section = "Discord";

    /// <summary>
    /// The bot token. It is a credential and lives in the environment or in user-secrets, never
    /// in a file under the repository.
    /// </summary>
    public string Token { get; set; } = "";

    /// <summary>
    /// The application's own id, which is also its OAuth2 client id. It names the application in
    /// the invite the bot logs at startup.
    /// </summary>
    public ulong? ApplicationId { get; set; }

    /// <summary>
    /// The guilds the bot serves. Commands are registered per guild, which lands them instantly
    /// rather than on Discord's global propagation delay, and an interaction from anywhere else
    /// is refused: a verified application cannot stop being addable, so the guard is in code.
    /// </summary>
    public ulong[] GuildIds { get; set; } = [];
}

/// <summary>
/// Where the API is, from the two vantage points that differ.
/// </summary>
public sealed class ApiOptions
{
    public const string Section = "Api";

    /// <summary>Where the bot reaches the API. Loopback is the normal answer.</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:8099";

    /// <summary>
    /// Where Discord's own servers reach the API, used for the poster in the embed. Discord
    /// fetches an embed image itself, so a loopback address produces an embed with a hole in
    /// it; when this is unset the poster is left off rather than pointing somewhere unreachable.
    /// </summary>
    public string? PublicBaseUrl { get; set; }
}

/// <summary>
/// Where the player is served from. The reply is a link into it.
/// </summary>
/// <summary>How long a launch stays usable.</summary>
public sealed class LaunchOptions
{
    public const string Section = "Launch";

    /// <summary>
    /// How long the Discord invite an Activity launch produces remains valid, in seconds.
    ///
    /// It gates joining and nothing else: people already watching are unaffected when it lapses.
    /// Matching it to the API's idle timeout means a link left lying around stops being a way in
    /// at roughly the moment the room behind it is forgotten.
    /// </summary>
    public int InviteMaxAgeSeconds { get; set; } = 1800;
}

public sealed class PlayerOptions
{
    public const string Section = "Player";

    public string BaseUrl { get; set; } = "";
}
