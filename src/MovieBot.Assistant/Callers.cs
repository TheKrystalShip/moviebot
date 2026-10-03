using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Assistant;

/// <summary>Who is asking: a person, or a service speaking for one.</summary>
/// <param name="Service">Whether the caller proved itself with the service key. Only a service may name a room.</param>
public sealed record Caller(string UserId, string DisplayName, bool Service)
{
    /// <summary>The conversations and streams this caller may see: their own.</summary>
    public string Scope => AssistantTurn.PersonScope(UserId);
}

/// <summary>
/// Reads the caller off a request. Nobody is signed in here.
/// </summary>
/// <remarks>
/// <para>
/// <b>A person carries the token the API gave them</b> when they signed in with Discord, checked with
/// the key the API signed it with. This service mints nothing and keeps no sessions.
/// </para>
/// <para>
/// <b>A service carries the service key and names the person it speaks for</b> in
/// <c>X-MovieBot-Acting: userId|displayName</c>, the name percent-encoded because a header is ASCII
/// and a name is not. It names who, never what: the person's tools and
/// conversation follow from the turn, so a service cannot ask for more than the person it names could.
/// </para>
/// </remarks>
public sealed class Callers(IOptions<AssistantAuthOptions> options, TimeProvider clock)
{
    public const string ServiceHeader = "X-MovieBot-Service";
    public const string ActingHeader = "X-MovieBot-Acting";

    /// <summary>The caller, or null when the request proves nothing.</summary>
    public Caller? Resolve(HttpRequest request)
    {
        var auth = options.Value;

        if (request.Headers.TryGetValue(ServiceHeader, out var offered) && auth.ServiceKey.Length > 0)
        {
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(offered.ToString()), Encoding.UTF8.GetBytes(auth.ServiceKey)))
                return null;

            var acting = request.Headers[ActingHeader].ToString();
            var bar = acting.IndexOf('|');
            return bar > 0 && bar < acting.Length - 1
                ? new Caller(acting[..bar].Trim(), Uri.UnescapeDataString(acting[(bar + 1)..].Trim()), Service: true)
                : new Caller("", "MovieBot", Service: true);
        }

        var payload = RoomToken.Validate(
            RoomToken.FromAuthorization(request.Headers.Authorization.ToString()), auth.SigningKey, clock.GetUtcNow());

        return payload is null ? null : new Caller(payload.UserId, payload.DisplayName, Service: false);
    }
}
