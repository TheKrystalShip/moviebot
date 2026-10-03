using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheKrystalShip.MovieBot.Core;

/// <summary>Who the bearer is, proven by Discord and vouched for by the API.</summary>
/// <param name="RoomId">The room the Activity was opened in; empty for a sign-in that names none.</param>
public sealed record RoomTokenPayload
{
    [JsonPropertyName("sub")] public required string UserId { get; init; }
    [JsonPropertyName("name")] public required string DisplayName { get; init; }
    [JsonPropertyName("room")] public required string RoomId { get; init; }
    [JsonPropertyName("exp")] public required long ExpiresAtUnix { get; init; }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RoomTokenPayload))]
internal partial class RoomTokenJsonContext : JsonSerializerContext;

/// <summary>
/// The proof a request came through Discord.
///
/// Discord is the only door: the API signs a person in with Discord, and this is what that sign-in
/// is worth afterwards. A browser that never went through Discord has no way to obtain one, which is
/// the whole point — the films and the rooms are not otherwise protected by anything but the length
/// of a URL. The API issues it and checks it; the assistant checks it with the same key, so it signs
/// nobody in itself.
///
/// Signed rather than stored: a room lives in memory and is forgotten after an evening, and a
/// token that outlived its room would be a puzzle rather than a protection.
/// </summary>
public static class RoomToken
{
    public static string Issue(RoomTokenPayload payload, string signingKey)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, RoomTokenJsonContext.Default.RoomTokenPayload);
        var body = Base64Url(json);
        return $"{body}.{Base64Url(Sign(body, signingKey))}";
    }

    /// <summary>Returns the payload when the signature holds and the token has not expired.</summary>
    public static RoomTokenPayload? Validate(string? token, string signingKey, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(token) || signingKey.Length == 0) return null;

        var dot = token.IndexOf('.');
        if (dot <= 0 || dot == token.Length - 1) return null;

        var body = token[..dot];
        var signature = token[(dot + 1)..];

        // Fixed-time comparison: a signature check that returns early leaks how much of a forgery
        // was right, one byte at a time.
        var expected = Base64Url(Sign(body, signingKey));
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(signature), Encoding.ASCII.GetBytes(expected)))
        {
            return null;
        }

        RoomTokenPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize(
                FromBase64Url(body), RoomTokenJsonContext.Default.RoomTokenPayload);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return null;
        }

        if (payload is null) return null;
        return payload.ExpiresAtUnix <= now.ToUnixTimeSeconds() ? null : payload;
    }

    /// <summary>The token a request carries as <c>Authorization: Bearer</c>, or null.</summary>
    public static string? FromAuthorization(string? header) =>
        header is { } value && value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? value["Bearer ".Length..].Trim()
            : null;

    private static byte[] Sign(string body, string key) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.ASCII.GetBytes(body));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }
}
