using System.Security.Cryptography;
using System.Text;

namespace TheKrystalShip.MovieBot.Api.Auth;

/// <summary>
/// The proof carried on a request for a film's bytes.
///
/// A room token says who somebody is, which is what the library and the rooms need to know. A
/// segment needs less: it is a few megabytes of one film that everybody in the room asks for at
/// the same moment, and what matters is that one cached copy can answer all of them. A shared
/// cache will not serve a response to a request carrying an <c>Authorization</c> header, so a
/// ticket takes its place.
///
/// It is the same string for every viewer of a title, so their requests are the same URL. It names
/// a title and an hour and nothing else: it says nothing about a person, and it opens one film
/// rather than the library.
///
/// The expiry is rounded up to a whole hour so two viewers starting minutes apart are handed
/// byte-identical tickets. A cache is keyed on the URL, so tickets differing by seconds would hold
/// a copy of the film per viewer.
/// </summary>
public static class MediaTicket
{
    /// <summary>The query parameter a media request carries it on.</summary>
    public const string QueryName = "mt";

    /// <summary>
    /// How long a ticket stays good for. Long enough that nobody is cut off mid-film, short
    /// enough that one copied out of a URL is worth little by the evening.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    /// <summary>
    /// What expiries are rounded up to. Every viewer who starts a film within the same hour is
    /// handed the identical ticket, which is what makes their requests one cacheable URL.
    /// </summary>
    private static readonly TimeSpan Bucket = TimeSpan.FromHours(1);

    public static string Issue(string titleId, AuthOptions options, DateTimeOffset now)
    {
        var expires = BucketedExpiry(now);
        return $"{expires.ToString(System.Globalization.CultureInfo.InvariantCulture)}.{Signature(titleId, expires, options.SigningKey)}";
    }

    /// <summary>When the ticket issued now stops being valid, for a caller that has to refresh one.</summary>
    public static DateTimeOffset ExpiresAt(DateTimeOffset now) =>
        DateTimeOffset.FromUnixTimeSeconds(BucketedExpiry(now));

    /// <summary>True when the ticket is this title's and has not expired.</summary>
    public static bool IsValid(string? ticket, string titleId, AuthOptions options, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(ticket) || options.SigningKey.Length == 0) return false;

        var dot = ticket.IndexOf('.');
        if (dot <= 0 || dot == ticket.Length - 1) return false;

        if (!long.TryParse(ticket[..dot], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var expires))
        {
            return false;
        }

        // Checked before the signature so an expired ticket costs nothing to refuse, and after
        // parsing so a malformed one cannot be told from a wrong one by how long it took.
        if (expires <= now.ToUnixTimeSeconds()) return false;

        var expected = Signature(titleId, expires, options.SigningKey);

        // Fixed-time: a comparison that returns early leaks how much of a forgery was right, one
        // byte at a time.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(ticket[(dot + 1)..]), Encoding.ASCII.GetBytes(expected));
    }

    private static long BucketedExpiry(DateTimeOffset now)
    {
        var ticks = (now + Lifetime).UtcTicks;
        var bucket = Bucket.Ticks;
        return new DateTimeOffset((ticks + bucket - 1) / bucket * bucket, TimeSpan.Zero)
            .ToUnixTimeSeconds();
    }

    /// <summary>
    /// The title is signed along with the expiry, so a ticket for one film does not open another.
    /// The separator cannot occur in a title id, which is a slug, so no two different pairs can
    /// produce the same signed string.
    /// </summary>
    private static string Signature(string titleId, long expires, string key)
    {
        var body = $"{titleId}|{expires.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(body));
        return Convert.ToBase64String(mac).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
