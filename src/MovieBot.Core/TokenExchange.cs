using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheKrystalShip.MovieBot.Core;

/// <summary>The words of an OAuth 2.0 token exchange (RFC 8693) as the identity provider takes them.</summary>
public static class TokenExchangeTypes
{
    public const string Grant = "urn:ietf:params:oauth:grant-type:token-exchange";

    /// <summary>A Discord access token, which the provider asks Discord about.</summary>
    public const string DiscordAccessToken = "urn:tks:params:oauth:token-type:discord-access-token";

    /// <summary>A Discord user id, named by a client registered to act for linked Discord users.</summary>
    public const string DiscordUserId = "urn:tks:params:oauth:token-type:discord-user-id";
}

/// <summary>A token the provider minted for a person, and when it stops being good.</summary>
public sealed record ExchangedToken(string AccessToken, DateTimeOffset ExpiresAt);

/// <summary>
/// What an exchange came to: a token, or the provider's refusal. <see cref="AccountStatus"/> is the
/// provider's word for why the person cannot have one — <c>pending</c>, <c>disabled</c> or
/// <c>unknown</c> — when the refusal is about the account.
/// </summary>
public sealed record ExchangeResult(ExchangedToken? Token, string? Error = null, string? AccountStatus = null);

/// <summary>The provider could not be asked: unreachable, or answering with something that is not an answer.</summary>
public sealed class TokenExchangeUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Exchanges something that names a person on Discord for the provider's access token for that person
/// (RFC 8693), as a confidential client authenticating with <c>client_secret_basic</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The provider alone mints.</b> Nothing here makes a token: the Activity's Discord access token and
/// the bot's naming of a Discord user are each handed to the provider, which decides who that is, whether
/// the client may ask, and what the person may do.
/// </para>
/// <para>
/// <b>The token endpoint is read from discovery</b>, once per issuer for the life of the process.
/// </para>
/// </remarks>
public sealed class TokenExchangeClient(HttpClient http, TimeProvider clock)
{
    private static readonly ConcurrentDictionary<string, string> TokenEndpoints = new(StringComparer.Ordinal);

    public async Task<ExchangeResult> ExchangeAsync(
        string issuer, string clientId, string clientSecret, string subjectToken, string subjectTokenType,
        CancellationToken ct)
    {
        var endpoint = await TokenEndpointAsync(issuer, ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = TokenExchangeTypes.Grant,
                ["subject_token"] = subjectToken,
                ["subject_token_type"] = subjectTokenType,
            }),
        };

        // RFC 6749 §2.3.1: each half is form-encoded before the pair is base64-encoded.
        var credentials = $"{Uri.EscapeDataString(clientId)}:{Uri.EscapeDataString(clientSecret)}";
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));

        try
        {
            using var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                var minted = await response.Content.ReadFromJsonAsync(OidcJsonContext.Default.ExchangeReply, ct);
                if (string.IsNullOrEmpty(minted?.AccessToken))
                    throw new TokenExchangeUnavailableException("The provider answered the exchange with no token.");
                return new ExchangeResult(new ExchangedToken(minted.AccessToken,
                    clock.GetUtcNow().AddSeconds(Math.Max(0, minted.ExpiresIn))));
            }

            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                var refusal = await ReadRefusalAsync(response, ct);
                return new ExchangeResult(null, refusal?.Error ?? "invalid_request", refusal?.AccountStatus);
            }

            throw new TokenExchangeUnavailableException($"The provider answered the exchange with {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new TokenExchangeUnavailableException("The provider could not be reached for the exchange.", ex);
        }
    }

    private static async Task<RefusalReply?> ReadRefusalAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync(OidcJsonContext.Default.RefusalReply, ct);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<string> TokenEndpointAsync(string issuer, CancellationToken ct)
    {
        if (TokenEndpoints.TryGetValue(issuer, out var known)) return known;

        try
        {
            var discovery = await http.GetFromJsonAsync(
                issuer.TrimEnd('/') + "/.well-known/openid-configuration", OidcJsonContext.Default.Discovery, ct);
            if (string.IsNullOrEmpty(discovery?.TokenEndpoint))
                throw new TokenExchangeUnavailableException("The provider's discovery names no token endpoint.");
            return TokenEndpoints[issuer] = discovery.TokenEndpoint;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new TokenExchangeUnavailableException("The provider's discovery could not be read.", ex);
        }
    }
}

internal sealed record Discovery
{
    [JsonPropertyName("token_endpoint")] public string? TokenEndpoint { get; init; }
}

internal sealed record ExchangeReply
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; init; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; init; }
}

internal sealed record RefusalReply
{
    [JsonPropertyName("error")] public string? Error { get; init; }
    [JsonPropertyName("account_status")] public string? AccountStatus { get; init; }
}

[JsonSerializable(typeof(Discovery))]
[JsonSerializable(typeof(ExchangeReply))]
[JsonSerializable(typeof(RefusalReply))]
internal partial class OidcJsonContext : JsonSerializerContext;
