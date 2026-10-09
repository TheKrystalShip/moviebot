using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Api.Auth;

namespace TheKrystalShip.MovieBot.Api.Sessions;

/// <summary>
/// A room's guest link, as the room keeps it: which link it is, who made it, and until when it lets
/// anybody holding it watch.
/// </summary>
/// <remarks>
/// A guest is nobody. The pass names the room and the person who made it, and the guests who come in
/// on it are named only by the connection they hold, so nothing anywhere is keyed by a guest.
/// </remarks>
public sealed record GuestPass
{
    /// <summary>Random, and the half of the link's code that says which pass it is.</summary>
    public required string Id { get; init; }

    /// <summary>The account that made it.</summary>
    public required string CreatedBy { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public bool LiveAt(DateTimeOffset now) => now < ExpiresAt;
}

/// <summary>
/// The code a guest link carries: the pass's id and a signature over it, made with a key derived from
/// <c>Auth:MediaTicketKey</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing secret is kept.</b> The code is computed from the pass's id whenever it is asked for, so
/// the journal holds the id alone and anybody in the room may be handed the same link again. A code
/// that does not carry its own signature is refused before anything is looked up.
/// </para>
/// <para>
/// <b>The key is derived rather than shared.</b> The media ticket key signs tickets; the link's key is
/// that key run through a label of its own, so a ticket's signature is never a link's.
/// </para>
/// </remarks>
public sealed class GuestCodes(IOptions<AuthOptions> auth)
{
    /// <summary>What a guest's credential is prefixed with wherever a bearer token would stand.</summary>
    public const string CredentialPrefix = "guest.";

    private const int IdBytes = 12;
    private const int SignatureBytes = 16;
    private static readonly int IdLength = Base64UrlLength(IdBytes);
    private static readonly int CodeLength = IdLength + Base64UrlLength(SignatureBytes);

    private readonly byte[] _key = HMACSHA256.HashData(
        Encoding.UTF8.GetBytes(auth.Value.MediaTicketKey), "moviebot guest link"u8);

    /// <summary>A new pass id: twelve random bytes, written URL-safe.</summary>
    public static string NewPassId() => Base64Url(RandomNumberGenerator.GetBytes(IdBytes));

    /// <summary>The code that opens the pass.</summary>
    public string Code(string passId) => passId + Signature(passId);

    /// <summary>The pass a code opens, or null when it is not a code this service made.</summary>
    public string? PassIdOf(string? code)
    {
        if (code is null || code.Length != CodeLength) return null;
        var id = code[..IdLength];
        var offered = Encoding.ASCII.GetBytes(code[IdLength..]);
        var expected = Encoding.ASCII.GetBytes(Signature(id));
        return CryptographicOperations.FixedTimeEquals(offered, expected) ? id : null;
    }

    /// <summary>The site's address for a pass.</summary>
    public string PathOf(string passId) => "/g/" + Code(passId);

    private string Signature(string passId) =>
        Base64Url(HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(passId))[..SignatureBytes]);

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static int Base64UrlLength(int bytes) => (bytes * 4 + 2) / 3;
}
