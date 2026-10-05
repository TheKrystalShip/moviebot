using System.Text.Json.Serialization;

namespace TheKrystalShip.MovieBot.Api.Discord;

public sealed class DiscordAuthOptions
{
    public const string Section = "Discord";

    /// <summary>
    /// The application id, which is also the OAuth2 client id. Not a secret. Taken from
    /// <c>Discord:ApplicationId</c>, the key the settings file names it by, when this is unset.
    /// </summary>
    public string ClientId { get; set; } = "";

    /// <summary>The OAuth2 client secret. A credential: it comes from the environment, never a file in the repo.</summary>
    public string ClientSecret { get; set; } = "";

    public bool IsConfigured => ClientId.Length > 0 && ClientSecret.Length > 0;
}

internal sealed record TokenResponse
{
    [JsonPropertyName("access_token")] public string AccessToken { get; init; } = "";
    [JsonPropertyName("token_type")] public string TokenType { get; init; } = "";
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; init; }
}

/// <summary>
/// Redeems the authorization code an Activity is handed for a Discord access token.
///
/// The redemption happens here and not in the player because it needs the client secret, and a
/// secret shipped to a browser is not a secret. The player never sees one: it sends the code and
/// receives an access token scoped to itself.
/// </summary>
public sealed class DiscordAuthClient(HttpClient http, ILogger<DiscordAuthClient> logger)
{
    private const string TokenEndpoint = "https://discord.com/api/v10/oauth2/token";

    public async Task<string?> ExchangeCodeAsync(string code, DiscordAuthOptions options, CancellationToken ct)
    {
        // An Activity never redirects, so no redirect_uri is sent: Discord matches one only when the
        // code was issued against it.
        var form = new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
        };

        using var response = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), ct);

        if (!response.IsSuccessStatusCode)
        {
            // The body carries Discord's reason and nothing secret, which is the difference
            // between fixing this in a minute and guessing at it.
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Discord refused the code exchange: {Status} {Body}",
                (int)response.StatusCode, body);
            return null;
        }

        var token = await response.Content.ReadFromJsonAsync(ApiJsonContext.Default.TokenResponse, ct);
        return string.IsNullOrEmpty(token?.AccessToken) ? null : token.AccessToken;
    }
}
