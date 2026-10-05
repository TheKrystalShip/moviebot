namespace TheKrystalShip.MovieBot.Api.Auth;

/// <summary>The two keys this service holds of its own. Neither signs anybody in.</summary>
public sealed class AuthOptions
{
    public const string Section = "Auth";

    /// <summary>
    /// Signs media tickets (<see cref="MediaTicket"/>), which open one film's bytes for an hour. Without it
    /// no ticket can be issued or checked, so the API refuses to start rather than serve a player that
    /// cannot open a film.
    /// </summary>
    public string MediaTicketKey { get; set; } = "";

    /// <summary>
    /// Proves machine work that names nobody: the hand-off asking what is due, the transcode plan and its
    /// report, and the bot reading the rooms and the downloads it shows in Discord. It never stands for a
    /// person, so no route that acts for one accepts it.
    /// </summary>
    public string ServiceKey { get; set; } = "";
}

/// <summary>
/// The OpenID Connect provider Cinema is a client and a resource server of. Cinema signs nobody in: it
/// reads the provider's discovery and keys, checks each access token it is handed, and takes the
/// person's actions from one claim.
/// </summary>
public sealed class OidcOptions
{
    public const string Section = "Oidc";

    /// <summary>
    /// The provider's issuer, exactly as its tokens carry it in <c>iss</c>. Discovery is read from under it.
    /// Recorded the first time the API starts against it (<see cref="IssuerRecord"/>).
    /// </summary>
    public string Issuer { get; set; } = "";

    /// <summary>The audience Cinema's access tokens are minted for. A token for any other is refused.</summary>
    public string Audience { get; set; } = "cinema";

    /// <summary>The claim listing the Cinema actions a person holds, an array of action ids.</summary>
    public string ActionsClaim { get; set; } = "tks_actions";

    /// <summary>The public client the site signs in with: authorization code with PKCE.</summary>
    public string SiteClientId { get; set; } = "cinema-site";

    /// <summary>
    /// The confidential client the API presents when it exchanges the Activity's Discord access token for
    /// one of the provider's.
    /// </summary>
    public string ActivityClientId { get; set; } = "cinema-activity";

    /// <summary>That client's secret. A credential: it comes from the environment, never a file.</summary>
    public string ActivityClientSecret { get; set; } = "";

    /// <summary>
    /// Set to <see cref="Issuer"/> to adopt it in place of a different issuer the API recorded before. Every
    /// account the old provider named is a stranger to the new one, so nothing changes issuer unasked.
    /// </summary>
    public string AdoptIssuer { get; set; } = "";

    /// <summary>Where the issuer is recorded; empty is <c>issuer</c> in the state directory.</summary>
    public string IssuerRecord { get; set; } = "";

    /// <summary>Whether the exchange for the Activity can be made: it needs the client's secret.</summary>
    public bool ExchangesForActivity => ActivityClientId.Length > 0 && ActivityClientSecret.Length > 0;

    /// <summary>The issuer without a trailing slash, which is how discovery is addressed under it.</summary>
    public string Authority => Issuer.TrimEnd('/');
}
