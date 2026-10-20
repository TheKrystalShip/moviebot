using TheKrystalShip.MovieBot.Api.Data;

namespace TheKrystalShip.MovieBot.Api.Auth;

/// <summary>
/// The issuer this API keeps its records against, written down the first time it starts.
/// </summary>
/// <remarks>
/// Everything kept about a person — progress, history, preferences, wants, keeps — is keyed by
/// the <c>sub</c> the provider names them by, and a <c>sub</c> means something only to the issuer that
/// minted it. Against another issuer every record belongs to a stranger and the same string may name
/// somebody else, so a changed <c>Oidc:Issuer</c> stops the API from starting until
/// <c>Oidc:AdoptIssuer</c> names the new issuer, which is the operator saying they meant it.
/// </remarks>
public static class IssuerRecord
{
    private const string Key = "issuer";

    /// <summary>Records the issuer, or throws when a different one is recorded and the new one was not adopted.</summary>
    public static void Enforce(OidcOptions options, CinemaDatabase database, ILogger logger)
    {
        var configured = options.Issuer.Trim();
        var recorded = database.Meta(Key);
        if (recorded == configured) return;

        if (!string.IsNullOrEmpty(recorded) && options.AdoptIssuer.Trim() != configured)
            throw new InvalidOperationException(
                $"Oidc:Issuer is {configured}, but this API's records were kept against {recorded}, whose accounts "
                + $"the new issuer does not know. Set Oidc:AdoptIssuer to {configured} to start against it anyway, "
                + $"or put Oidc:Issuer back to {recorded}.");

        database.SetMeta(Key, configured);

        if (string.IsNullOrEmpty(recorded))
            logger.LogInformation("Recorded {Issuer} as the issuer every person's records are kept against.", configured);
        else
            logger.LogWarning("Adopted {Issuer} in place of {Recorded}: records kept under the old issuer's accounts belong to nobody it knows.",
                configured, recorded);
    }
}
