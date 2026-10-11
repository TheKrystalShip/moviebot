using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Data;

namespace TheKrystalShip.MovieBot.Api.Sessions;

/// <summary>The code a screen shows for a phone to pair with it, and when to ask for the next one.</summary>
/// <param name="Path">The site's address the code opens, <c>/p/&lt;code&gt;</c>.</param>
/// <param name="ExpiresAt">When the code stops pairing anything.</param>
/// <param name="RenewSeconds">
/// How long the screen shows it before asking for the next, well within its life. A span rather than an
/// instant, so a television whose clock is wrong still asks in time.
/// </param>
public sealed record PairingCodeView(string Path, DateTimeOffset ExpiresAt, int RenewSeconds);

/// <summary>A phone paired with a screen, as the screen lists it.</summary>
/// <param name="Id">The phone's anchor session (<c>sid</c>).</param>
/// <param name="Name">What the phone is, read from its browser when it paired: "Chrome on Android".</param>
/// <param name="PairedAt">When it first paired with this screen.</param>
public sealed record PairedPhone(string Id, string Name, DateTimeOffset PairedAt);

/// <summary>A phone asking to pair with the screen whose code it scanned.</summary>
public sealed record PairRequest(string Code);

/// <summary>Why a code paired nothing.</summary>
public enum PairingRefusal
{
    /// <summary>No screen showed it: mistyped, tampered with, or the screen has gone.</summary>
    Unknown,

    /// <summary>A screen showed it, and it has run out.</summary>
    Expired,

    /// <summary>A screen of another person's showed it.</summary>
    AnotherPerson,
}

/// <summary>
/// The code a screen shows: when it runs out, a tag saying which screen showed it, and a signature over
/// the expiry, the screen and its person, made with a key derived from <c>Auth:MediaTicketKey</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing is kept.</b> The code carries its own expiry and is checked against the screens the server
/// knows, so a code is good from the moment it is made until it runs out, wherever it was shown, and
/// there is no table of codes to sweep.
/// </para>
/// <para>
/// <b>It is short, because it is a QR code read across a room.</b> A TV's code holds the site's address
/// and these sixteen bytes, written URL-safe in 22 characters, which keeps it at a QR version whose
/// modules are big enough to read from a sofa. The signature is eight bytes: a code is only ever checked
/// online, for minutes, against one person's screens, so nobody can try enough of them to matter.
/// </para>
/// <para>
/// <b>The key is derived rather than shared,</b> under a label of its own, so neither a media ticket's
/// signature nor a guest link's is ever a pairing code's.
/// </para>
/// </remarks>
public sealed class PairingCodes(IOptions<AuthOptions> auth, TimeProvider clock)
{
    /// <summary>
    /// How long a code pairs. Long enough for a phone that must sign in first to do it and come back with
    /// the code; a photo of it pairs nothing once it has run out.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    /// <summary>How often a screen shows a new code, so the one on screen always has most of its life left.</summary>
    public static readonly TimeSpan Rotation = TimeSpan.FromMinutes(1);

    private const int ExpiryBytes = 4;
    private const int TagBytes = 4;
    private const int SignatureBytes = 8;
    private const int CodeBytes = ExpiryBytes + TagBytes + SignatureBytes;
    private static readonly int CodeLength = (CodeBytes * 4 + 2) / 3;

    private readonly byte[] _key = HMACSHA256.HashData(
        Encoding.UTF8.GetBytes(auth.Value.MediaTicketKey), "moviebot remote pairing"u8);

    /// <summary>A code read back: when it says it runs out, the screen's tag and the signature.</summary>
    public sealed record Opened(DateTimeOffset ExpiresAt, byte[] Tag, byte[] Signature);

    /// <summary>A new code for a person's screen.</summary>
    public PairingCodeView Issue(string userId, string screenId)
    {
        var now = clock.GetUtcNow();
        var expires = DateTimeOffset.FromUnixTimeSeconds((now + Lifetime).ToUnixTimeSeconds());
        var code = new byte[CodeBytes];
        BinaryPrimitives.WriteUInt32BigEndian(code, (uint)expires.ToUnixTimeSeconds());
        Tag(screenId).CopyTo(code, ExpiryBytes);
        Signature(expires, userId, screenId).CopyTo(code, ExpiryBytes + TagBytes);
        return new PairingCodeView("/p/" + Base64Url.EncodeToString(code), expires, (int)Rotation.TotalSeconds);
    }

    /// <summary>The tag a screen's codes carry: which screen it is, and nothing an outsider can read back.</summary>
    public byte[] Tag(string screenId) =>
        HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes("screen\n" + screenId))[..TagBytes];

    /// <summary>A code taken apart, or null when it is not the shape of one.</summary>
    public static Opened? Open(string? code)
    {
        if (code is null || code.Length != CodeLength) return null;
        var bytes = new byte[CodeBytes];
        if (!Base64Url.TryDecodeFromChars(code, bytes, out var written) || written != CodeBytes) return null;
        var expires = DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadUInt32BigEndian(bytes));
        return new Opened(expires, bytes[ExpiryBytes..(ExpiryBytes + TagBytes)], bytes[(ExpiryBytes + TagBytes)..]);
    }

    /// <summary>Whether this service signed the code for this person's screen.</summary>
    public bool Signed(Opened code, string userId, string screenId) =>
        CryptographicOperations.FixedTimeEquals(code.Signature, Signature(code.ExpiresAt, userId, screenId));

    /// <summary>Whether a signed code has run out, or names a time further out than any code is made for.</summary>
    public bool Expired(Opened code)
    {
        var now = clock.GetUtcNow();
        return code.ExpiresAt <= now || code.ExpiresAt > now + Lifetime + TimeSpan.FromMinutes(1);
    }

    private byte[] Signature(DateTimeOffset expires, string userId, string screenId) =>
        HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"pair\n{expires.ToUnixTimeSeconds()}\n{userId}\n{screenId}"))[..SignatureBytes];
}

/// <summary>
/// Which phones are paired with which screens, each named by its anchor session, kept in the database
/// with the person both belong to.
/// </summary>
/// <remarks>
/// <para>
/// <b>A pairing lasts until somebody ends it, or a session it names has ended.</b> Cinema hears no
/// revocation, so a session is taken to have ended once it has gone unseen for longer than the provider
/// keeps a session it has not renewed (<c>Oidc:SessionLifetime</c>): a session renewed within that
/// window was used, and a session used here is seen here, since a phone with pairings and every screen
/// hold a hub connection while they are open. A screen connected now is seen now. A television signed in
/// again under the name of one of the person's screens that is not connected ends that screen's
/// pairings: it is the same television, and its old session is no longer used.
/// </para>
/// <para>
/// <b>Every read and write names the person.</b> A session belongs to one account, and the person is
/// matched as well, so a row is never reached under anybody else's.
/// </para>
/// </remarks>
public sealed class ScreenPairings(CinemaDatabase database, IOptions<OidcOptions> oidc, TimeProvider clock)
{
    /// <summary>A pairing as it is kept.</summary>
    public sealed record Row(string PhoneId, string ScreenId, string PhoneName, string? ScreenName, DateTimeOffset PairedAt);

    /// <summary>
    /// Pairs a phone with a screen, keeping when it first paired. Pairing again renames the phone to
    /// what it says it is now.
    /// </summary>
    public void Pair(string userId, string phoneId, string phoneName, string screenId, string? screenName)
    {
        var now = clock.GetUtcNow();
        database.Write(c => c.Execute("""
            INSERT INTO screen_pairings (phone_sid, screen_sid, user_id, phone_name, screen_name, paired_at, phone_seen_at, screen_seen_at)
            VALUES ($phone, $screen, $user, $phoneName, $screenName, $now, $now, $now)
            ON CONFLICT (phone_sid, screen_sid) DO UPDATE SET
                phone_name = excluded.phone_name, screen_name = excluded.screen_name,
                phone_seen_at = excluded.phone_seen_at, screen_seen_at = excluded.screen_seen_at
            WHERE screen_pairings.user_id = excluded.user_id
            """,
            ("$phone", phoneId), ("$screen", screenId), ("$user", userId), ("$phoneName", phoneName),
            ("$screenName", screenName), ("$now", now)));
    }

    /// <summary>Ends one pairing. True when there was one.</summary>
    public bool Unpair(string userId, string phoneId, string screenId) =>
        database.Write(c => c.Execute(
            "DELETE FROM screen_pairings WHERE user_id = $user AND phone_sid = $phone AND screen_sid = $screen",
            ("$user", userId), ("$phone", phoneId), ("$screen", screenId)) > 0);

    /// <summary>The screens a phone is paired with, oldest pairing first.</summary>
    public List<Row> OfPhone(string userId, string phoneId, IReadOnlySet<string> connectedScreens) =>
        Live(userId, connectedScreens, "phone_sid = $id", phoneId);

    /// <summary>The phones paired with a screen, oldest pairing first.</summary>
    public List<Row> OfScreen(string userId, string screenId, IReadOnlySet<string> connectedScreens) =>
        Live(userId, connectedScreens, "screen_sid = $id", screenId);

    /// <summary>A phone's session was seen: a connection of it opened or closed, or it asked about its screens.</summary>
    public void PhoneSeen(string userId, string phoneId) =>
        database.Write(c => c.Execute(
            "UPDATE screen_pairings SET phone_seen_at = $now WHERE user_id = $user AND phone_sid = $phone",
            ("$now", clock.GetUtcNow()), ("$user", userId), ("$phone", phoneId)));

    /// <summary>
    /// A screen's session was seen, under the name it has now. Ends the pairings of the person's other
    /// screens of that name that are not connected, since it is the same television signed in again,
    /// and answers them.
    /// </summary>
    public List<Row> ScreenSeen(string userId, string screenId, string? name, IReadOnlySet<string> connectedScreens) =>
        database.Write(c =>
        {
            c.Execute("""
                UPDATE screen_pairings SET screen_seen_at = $now, screen_name = coalesce($name, screen_name)
                WHERE user_id = $user AND screen_sid = $screen
                """, ("$now", clock.GetUtcNow()), ("$name", name), ("$user", userId), ("$screen", screenId));
            if (name is null) return [];

            var superseded = c.Query(
                    $"SELECT {Columns} FROM screen_pairings WHERE user_id = $user AND screen_sid <> $screen AND screen_name = $name COLLATE NOCASE",
                    Read, ("$user", userId), ("$screen", screenId), ("$name", name))
                .Where(row => !connectedScreens.Contains(row.ScreenId))
                .ToList();
            foreach (var row in superseded)
                c.Execute("DELETE FROM screen_pairings WHERE user_id = $user AND phone_sid = $phone AND screen_sid = $screen",
                    ("$user", userId), ("$phone", row.PhoneId), ("$screen", row.ScreenId));
            return superseded;
        });

    private const string Columns = "phone_sid, screen_sid, phone_name, screen_name, paired_at";

    private static Row Read(SqliteDataReader r) =>
        new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetStringOrNull(3), r.GetInstant(4));

    /// <summary>
    /// The person's pairings that match, after forgetting those whose phone or screen has gone unseen for
    /// longer than a session lasts unrenewed.
    /// </summary>
    private List<Row> Live(string userId, IReadOnlySet<string> connectedScreens, string where, string id)
    {
        var cutoff = clock.GetUtcNow() - oidc.Value.SessionLifetime;
        var rows = database.Read(c => c.Query(
            $"SELECT {Columns}, phone_seen_at, screen_seen_at FROM screen_pairings WHERE user_id = $user AND {where} ORDER BY paired_at",
            r => (Row: Read(r), PhoneSeen: r.GetInstant(5), ScreenSeen: r.GetInstant(6)), ("$user", userId), ("$id", id)));

        var live = new List<Row>();
        var ended = new List<Row>();
        foreach (var (row, phoneSeen, screenSeen) in rows)
            (phoneSeen >= cutoff && (screenSeen >= cutoff || connectedScreens.Contains(row.ScreenId)) ? live : ended).Add(row);
        if (ended.Count > 0)
            database.Write(c =>
            {
                foreach (var row in ended)
                    c.Execute("DELETE FROM screen_pairings WHERE user_id = $user AND phone_sid = $phone AND screen_sid = $screen",
                        ("$user", userId), ("$phone", row.PhoneId), ("$screen", row.ScreenId));
            });
        return live;
    }
}

/// <summary>
/// What a connection's token says about its anchor session, read from the validated token alone.
/// </summary>
public static class SessionClaims
{
    /// <summary>The anchor session the token belongs to, or null for one that belongs to none (a token exchange).</summary>
    public static string? SidOf(ClaimsIdentity identity) =>
        identity.FindFirst("sid")?.Value is { Length: > 0 } sid ? sid : null;
}

/// <summary>
/// What a phone is called on a screen's list of paired phones. A phone's session carries no name, so it
/// is read from the browser it paired with: the browser and the device, and nothing a person typed.
/// It is a label for a person to recognise, never a fact anything decides by.
/// </summary>
public static class PhoneNames
{
    public static string Of(string? userAgent)
    {
        if (string.IsNullOrEmpty(userAgent)) return "Phone";
        var ua = userAgent;
        var browser =
            ua.Contains("SamsungBrowser/", StringComparison.Ordinal) ? "Samsung Internet"
            : ua.Contains("EdgA/", StringComparison.Ordinal) || ua.Contains("EdgiOS/", StringComparison.Ordinal) || ua.Contains("Edg/", StringComparison.Ordinal) ? "Edge"
            : ua.Contains("Firefox/", StringComparison.Ordinal) || ua.Contains("FxiOS/", StringComparison.Ordinal) ? "Firefox"
            : ua.Contains("OPR/", StringComparison.Ordinal) ? "Opera"
            : ua.Contains("CriOS/", StringComparison.Ordinal) || ua.Contains("Chrome/", StringComparison.Ordinal) ? "Chrome"
            : ua.Contains("Safari/", StringComparison.Ordinal) ? "Safari"
            : null;
        var device =
            ua.Contains("Android", StringComparison.Ordinal) ? "Android"
            : ua.Contains("iPhone", StringComparison.Ordinal) ? "iPhone"
            : ua.Contains("iPad", StringComparison.Ordinal) ? "iPad"
            : ua.Contains("Windows", StringComparison.Ordinal) ? "Windows"
            : ua.Contains("Mac OS X", StringComparison.Ordinal) ? "Mac"
            : ua.Contains("Linux", StringComparison.Ordinal) ? "Linux"
            : null;
        return browser is not null && device is not null ? $"{browser} on {device}" : browser ?? device ?? "Phone";
    }
}
