namespace TheKrystalShip.MovieBot.Api.Auth;

public sealed class AuthOptions
{
    public const string Section = "Auth";

    /// <summary>
    /// Signs the tokens the player carries (<see cref="Core.RoomToken"/>). Without it nothing can be
    /// minted or checked, so the API refuses to start rather than serving films to anybody who knows
    /// the hostname. The assistant checks the same tokens with the same key.
    /// </summary>
    public string SigningKey { get; set; } = "";

    /// <summary>
    /// Shared with the bot and the assistant, which have no Discord user to authenticate as when
    /// they act on somebody's behalf.
    /// </summary>
    public string ServiceKey { get; set; } = "";

    /// <summary>Long enough for a film and the evening around it, short enough to be worth little if it leaks.</summary>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(8);
}
