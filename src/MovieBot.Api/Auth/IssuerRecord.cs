namespace TheKrystalShip.MovieBot.Api.Auth;

/// <summary>
/// The issuer this API keeps its records against, written down the first time it starts.
/// </summary>
/// <remarks>
/// Everything kept about a person — progress, history, preferences, wishes, lists, keeps — is keyed by
/// the <c>sub</c> the provider names them by, and a <c>sub</c> means something only to the issuer that
/// minted it. Against another issuer every record belongs to a stranger and the same string may name
/// somebody else, so a changed <c>Oidc:Issuer</c> stops the API from starting until
/// <c>Oidc:AdoptIssuer</c> names the new issuer, which is the operator saying they meant it.
/// </remarks>
public static class IssuerRecord
{
    /// <summary>Records the issuer, or throws when a different one is recorded and the new one was not adopted.</summary>
    public static void Enforce(OidcOptions options, string path, ILogger logger)
    {
        var configured = options.Issuer.Trim();
        string? recorded = null;
        try
        {
            if (File.Exists(path)) recorded = File.ReadAllText(path).Trim();
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException($"The recorded issuer at {path} could not be read.", ex);
        }

        if (recorded == configured) return;

        if (!string.IsNullOrEmpty(recorded) && options.AdoptIssuer.Trim() != configured)
            throw new InvalidOperationException(
                $"Oidc:Issuer is {configured}, but this API's records were kept against {recorded}, whose accounts "
                + $"the new issuer does not know. Set Oidc:AdoptIssuer to {configured} to start against it anyway, "
                + $"or put Oidc:Issuer back to {recorded}.");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, configured + "\n");
        File.Move(temporary, path, overwrite: true);

        if (string.IsNullOrEmpty(recorded))
            logger.LogInformation("Recorded {Issuer} as the issuer every person's records are kept against.", configured);
        else
            logger.LogWarning("Adopted {Issuer} in place of {Recorded}: records kept under the old issuer's accounts belong to nobody it knows.",
                configured, recorded);
    }
}
