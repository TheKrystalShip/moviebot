using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace TheKrystalShip.MovieBot.Assistant;

/// <summary>Who is asking: a person, by the access token the identity provider minted for Cinema.</summary>
/// <param name="AccessToken">The token itself, which every call the tools make to the API carries.</param>
/// <param name="Via">
/// The client calling on the person's behalf (the token's <c>act</c>), when one is: the bot, relaying
/// what the person said in a voice channel. Only such a client names a room.
/// </param>
/// <param name="Actions">The Cinema actions the token lists, which is what a command is offered and refused by.</param>
public sealed record Caller(string UserId, string DisplayName, string AccessToken, string? Via, IReadOnlySet<string> Actions)
{
    /// <summary>The conversations and streams this caller may see: their own.</summary>
    public string Scope => AssistantTurn.PersonScope(UserId);

    /// <summary>Whether a client is relaying the person, which is what may name a room.</summary>
    public bool Relayed => Via is not null;

    /// <summary>The record's printed form, without the token.</summary>
    public override string ToString() => $"Caller {{ UserId = {UserId}, DisplayName = {DisplayName}, Via = {Via} }}";
}

/// <summary>The identity provider this service accepts tokens from: the same settings the API reads.</summary>
public sealed class AssistantOidcOptions
{
    public const string Section = "Oidc";

    public string Issuer { get; set; } = "";

    public string Audience { get; set; } = "cinema";

    public string ActionsClaim { get; set; } = "tks_actions";
}

/// <summary>
/// Reads the caller off a request. Nobody is signed in here.
/// </summary>
/// <remarks>
/// <para>
/// <b>A person carries an access token the identity provider minted for Cinema's audience</b>, checked
/// against the provider's keys exactly as the API checks it, whether the site sends it or the bot
/// relays it. This service mints nothing and keeps no sessions, and it forwards the token on every
/// call its tools make, so the API is the one place that decides what the person may do.
/// </para>
/// <para>
/// <b>Talking to the assistant takes watching.</b> A person who may not watch has no access to Krystal
/// Cinema, and the assistant is part of it.
/// </para>
/// </remarks>
public sealed class Callers(IOptions<AssistantOidcOptions> options)
{
    public const string Watch = "cinema:watch";

    /// <summary>The caller, or null when the request carries no token this service accepts.</summary>
    public Caller? Resolve(HttpRequest request)
    {
        var identity = request.HttpContext.User.Identities
            .FirstOrDefault(i => i.IsAuthenticated && i.AuthenticationType == JwtBearerDefaults.AuthenticationScheme);
        if (identity?.FindFirst("sub")?.Value is not { Length: > 0 } sub) return null;
        var actions = identity.FindAll(options.Value.ActionsClaim).Select(c => c.Value).ToHashSet(StringComparer.Ordinal);
        if (!actions.Contains(Watch)) return null;

        var header = request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..].Trim() : "";

        var name = identity.FindFirst("name")?.Value is { Length: > 0 } shown ? shown
            : identity.FindFirst("preferred_username")?.Value is { Length: > 0 } username ? username
            : sub;
        return new Caller(sub, name, token, ActorOf(identity.FindFirst("act")?.Value), actions);
    }

    /// <summary>The acting client's id out of an <c>act</c> claim, an object carried as JSON.</summary>
    private static string? ActorOf(string? act)
    {
        if (string.IsNullOrEmpty(act)) return null;
        try
        {
            using var document = JsonDocument.Parse(act);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("sub", out var sub)
                   && sub.ValueKind == JsonValueKind.String
                ? sub.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public static class CallerAuthentication
{
    /// <summary>
    /// Validates the provider's access tokens: issuer and discovery from <c>Oidc:Issuer</c>, audience
    /// <c>Oidc:Audience</c>, ES256, typed <c>at+jwt</c>. A request without one is simply nobody; the
    /// endpoints answer that themselves.
    /// </summary>
    public static IServiceCollection AddCallerAuthentication(this IServiceCollection services)
    {
        services.AddOptions<AssistantOidcOptions>()
            .BindConfiguration(AssistantOidcOptions.Section)
            .Validate(o => Uri.TryCreate(o.Issuer, UriKind.Absolute, out _),
                "Oidc:Issuer must be the identity provider's issuer, an absolute https address.")
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<AssistantOidcOptions>>((jwt, oidc) =>
            {
                var o = oidc.Value;
                jwt.Authority = o.Issuer.TrimEnd('/');
                jwt.RequireHttpsMetadata = o.Issuer.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = o.Issuer,
                    ValidAudience = o.Audience,
                    ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
                    ValidTypes = ["at+jwt"],
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "name",
                    AuthenticationType = JwtBearerDefaults.AuthenticationScheme,
                };
            });
        services.AddSingleton<Callers>();
        return services;
    }
}
