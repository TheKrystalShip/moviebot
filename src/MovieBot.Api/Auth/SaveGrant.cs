using System.Security.Cryptography;
using System.Text;

namespace TheKrystalShip.MovieBot.Api.Auth;

/// <summary>
/// The proof a device's own downloader carries for one save: it opens that save's tracks and its
/// <c>complete</c> route, for the person who made the save, and nothing else.
///
/// An access token lives minutes and only a page can renew one, while a phone's download service
/// fetches a season for hours with no page running. A grant outlives the token and stays narrow: it
/// names one save, so the per-person transfer limits still count it against its owner, and it stops
/// opening anything the moment the save is forgotten, since the save is looked up on every request.
/// It lasts as long as the save holds its title, and saving the title again issues a new one.
///
/// It is carried in <see cref="Header"/>, never on the URL, so it is not written into a proxy's
/// access log. It is signed with <see cref="AuthOptions.MediaTicketKey"/> under a prefix of its own,
/// so no ticket can be taken for a grant or a grant for a ticket.
/// </summary>
public static class SaveGrant
{
    /// <summary>The request header a grant is carried in.</summary>
    public const string Header = "X-Cinema-Save-Grant";

    private const string Domain = "save-grant";

    public static string Issue(string saveId, string userId, string titleId, DateTimeOffset expires, AuthOptions options)
    {
        var at = expires.ToUnixTimeSeconds();
        return $"{saveId}.{at.ToString(System.Globalization.CultureInfo.InvariantCulture)}.{Signature(saveId, userId, titleId, at, options.MediaTicketKey)}";
    }

    /// <summary>The save a grant names and when it ends, or null for one that is not shaped as a grant.</summary>
    public static (string SaveId, long Expires, string Signature)? Parse(string? grant)
    {
        if (string.IsNullOrEmpty(grant)) return null;
        var parts = grant.Split('.');
        if (parts.Length != 3 || parts[0].Length == 0 || parts[2].Length == 0) return null;
        return long.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var expires)
            ? (parts[0], expires, parts[2])
            : null;
    }

    /// <summary>True when the signature is this save's, for this person and title, and the grant has not ended.</summary>
    public static bool IsValid((string SaveId, long Expires, string Signature) grant, string userId, string titleId,
        AuthOptions options, DateTimeOffset now)
    {
        if (options.MediaTicketKey.Length == 0 || grant.Expires <= now.ToUnixTimeSeconds()) return false;
        var expected = Signature(grant.SaveId, userId, titleId, grant.Expires, options.MediaTicketKey);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(grant.Signature), Encoding.ASCII.GetBytes(expected));
    }

    /// <summary>
    /// Every field is signed, so a grant for one save, person or title opens no other. The separator
    /// cannot occur in a save id, a provider's <c>sub</c> as this API stores it, or a title id.
    /// </summary>
    private static string Signature(string saveId, string userId, string titleId, long expires, string key)
    {
        var body = $"{Domain}|{saveId}|{userId}|{titleId}|{expires.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(body));
        return Convert.ToBase64String(mac).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
