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
    /// Shared with the API. The bot has no Discord user to authenticate as when it loads a film
    /// into a room on somebody's behalf, so it proves itself with a key instead.
    /// </summary>
    public string ServiceKey { get; set; } = "";

    /// <summary>
    /// Where Discord's own servers reach the API, used for the poster in the embed. Discord
    /// fetches an embed image itself, so a loopback address produces an embed with a hole in
    /// it; when this is unset the poster is left off rather than pointing somewhere unreachable.
    /// </summary>
    public string? PublicBaseUrl { get; set; }
}

/// <summary>How long a launch stays usable, and what is remembered about the ones handed out.</summary>
public sealed class LaunchOptions
{
    public const string Section = "Launch";

    /// <summary>Discord refuses an invite asked to live longer than a week.</summary>
    private const double Ceiling = 604800;

    /// <summary>
    /// A floor under the life of any invite. Discord reads an age of zero as never expiring, so
    /// this is what stands between a misconfigured grace and a permanent way into the server.
    /// </summary>
    private const double Floor = 60;

    /// <summary>
    /// How long the Discord invite an Activity launch produces outlives the film, in seconds.
    ///
    /// An invite's life is counted from the moment it is made rather than from the last person
    /// through it, so a window shorter than a film shuts the door on a room that is still
    /// watching: everybody already inside carries on, and the card they came through quietly
    /// stops letting anyone else in. The invite is given the film's running time plus this, which
    /// is the longest the card can still be a way into what it announces. Matching it to the
    /// API's <c>Rooms:IdleTimeout</c> lands the invite and the room behind it at the same moment.
    /// </summary>
    public int InviteGraceSeconds { get; set; } = 1800;

    /// <summary>
    /// Where the standing launch cards are written. Empty means the state directory systemd
    /// hands the service, and the working directory when there is none.
    /// </summary>
    public string CardsPath { get; set; } = "";

    /// <summary>How long an invite handed out for a film of this length is good for.</summary>
    public int InviteMaxAgeFor(double filmSeconds)
    {
        var film = double.IsFinite(filmSeconds) && filmSeconds > 0 ? Math.Ceiling(filmSeconds) : 0;
        return (int)Math.Clamp(film + Math.Max(0, InviteGraceSeconds), Floor, Ceiling);
    }

    public string ResolveCardsPath() => StatePath.Resolve(CardsPath, "launches.json");
}

/// <summary>Where the player is served from. The reply is a link into it.</summary>
public sealed class PlayerOptions
{
    public const string Section = "Player";

    public string BaseUrl { get; set; } = "";
}
