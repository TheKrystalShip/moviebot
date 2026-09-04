using System.Net.Http.Headers;
using System.Text.Json.Serialization;

namespace TheKrystalShip.MovieBot.Api.Discord;

public sealed class DiscordAuthOptions
{
    public const string Section = "Discord";

    /// <summary>The application id, which is also the OAuth2 client id. Not a secret.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>The OAuth2 client secret. A credential: it comes from the environment, never a file in the repo.</summary>
    public string ClientSecret { get; set; } = "";

    public bool IsConfigured => ClientId.Length > 0 && ClientSecret.Length > 0;
}

/// <summary>Who an access token belongs to, as Discord reports them.</summary>
public sealed record DiscordUser
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("username")] public string Username { get; init; } = "";
    [JsonPropertyName("global_name")] public string? GlobalName { get; init; }

    /// <summary>What a person is called on screen, preferring the name they chose to show.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(GlobalName) ? Username : GlobalName;
}

internal sealed record TokenResponse
{
    [JsonPropertyName("access_token")] public string AccessToken { get; init; } = "";
    [JsonPropertyName("token_type")] public string TokenType { get; init; } = "";
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; init; }
}

/// <summary>
/// Exchanges the authorization code an Activity is handed for an access token, and reads who it
/// belongs to.
///
/// The exchange happens here and not in the player because it needs the client secret, and a
/// secret shipped to a browser is not a secret. The player never sees one: it sends the code and
/// receives an access token scoped to itself.
/// </summary>
public sealed class DiscordAuthClient(HttpClient http, ILogger<DiscordAuthClient> logger)
{
    private const string TokenEndpoint = "https://discord.com/api/v10/oauth2/token";
    private const string UserEndpoint = "https://discord.com/api/v10/users/@me";

    public async Task<string?> ExchangeCodeAsync(
        string code, DiscordAuthOptions options, string? redirectUri, CancellationToken ct)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("client_id", options.ClientId),
            new("client_secret", options.ClientSecret),
            new("grant_type", "authorization_code"),
            new("code", code)
        };

        // Discord requires redirect_uri to match the one the code was issued against. An Activity
        // never redirects, so it sends none and none is sent on.
        if (!string.IsNullOrWhiteSpace(redirectUri))
            form.Add(new KeyValuePair<string, string>("redirect_uri", redirectUri));

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

    public async Task<DiscordUser?> GetUserAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UserEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Discord refused the identity lookup: {Status}", (int)response.StatusCode);
            return null;
        }

        return await response.Content.ReadFromJsonAsync(ApiJsonContext.Default.DiscordUser, ct);
    }
}
