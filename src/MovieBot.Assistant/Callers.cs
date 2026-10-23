using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TheKrystalShip.Agent.Plugin;

namespace TheKrystalShip.MovieBot.Assistant;

/// <summary>Who is asking: a person, by the access token the identity provider minted for Cinema.</summary>
/// <param name="AccessToken">The token itself, which every call the tools make to the API carries.</param>
/// <param name="Via">The client calling on the person's behalf (the token's <c>act</c>), when one is.</param>
/// <param name="Actions">The Cinema actions the token lists.</param>
public sealed record Caller(string UserId, string DisplayName, string AccessToken, string? Via, IReadOnlySet<string> Actions)
{
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
/// <b>A person carries an access token the identity provider minted for Cinema's audience</b>, checked
/// against the provider's keys exactly as the API checks it. This service mints nothing and keeps no
/// sessions, and it forwards the token on every call its tools make, so the API is the one place that
/// decides what the person may see. Talking to the assistant takes <see cref="Watch"/>: a person who
/// may not watch has no access to Cinema, and the assistant is part of it.
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

    /// <summary>Whether the request carries a token the provider minted for Cinema, whatever it lets the person do.</summary>
    public bool IsSignedIn(HttpRequest request) =>
        request.HttpContext.User.Identities.Any(i => i.IsAuthenticated
            && i.AuthenticationType == JwtBearerDefaults.AuthenticationScheme && i.FindFirst("sub") is not null);

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

/// <summary>The plugin's sign-in: the person the access token on the request names.</summary>
/// <remarks>
/// No token, or one that is not the provider's, is nobody (<c>401</c>). A token that is the provider's and
/// holds no <see cref="Callers.Watch"/> is somebody who may not use the assistant (<c>403</c>). A provider
/// whose keys cannot be fetched cannot vouch for anyone (<c>502</c>).
/// </remarks>
public sealed class CinemaPeople(Callers callers) : IPluginPeople
{
    public ValueTask<PluginPerson?> ResolveAsync(HttpContext http)
    {
        if (callers.Resolve(http.Request) is { } caller)
            return ValueTask.FromResult<PluginPerson?>(new PluginPerson(caller.UserId, caller.DisplayName, caller));

        if (http.Items.ContainsKey(CallerAuthentication.ProviderUnreachable))
            throw PluginSignInException.UpstreamUnavailable("Sign-in is unavailable right now. Try again in a moment.");

        if (callers.IsSignedIn(http.Request))
            throw PluginSignInException.Forbidden("This account does not let the person watch in Cinema, so the assistant is not available to it.");

        return ValueTask.FromResult<PluginPerson?>(null);
    }
}

public static class CallerAuthentication
{
    /// <summary>Set on the request when the provider's keys could not be fetched to check its token.</summary>
    public const string ProviderUnreachable = "cinema.provider-unreachable";

    /// <summary>
    /// Validates the provider's access tokens: issuer and discovery from <c>Oidc:Issuer</c>, audience
    /// <c>Oidc:Audience</c>, ES256, typed <c>at+jwt</c>. A request without one is simply nobody; the
    /// plugin's routes answer that themselves.
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
                jwt.Events = new JwtBearerEvents
                {
                    // A token that cannot be checked because the provider cannot be asked is not a bad token.
                    OnAuthenticationFailed = context =>
                    {
                        // IDX10500 is the library saying it holds no keys at all, which is what a provider
                        // whose key set could not be fetched leaves it with.
                        if (context.Exception is InvalidOperationException or HttpRequestException
                            || context.Exception.InnerException is HttpRequestException or IOException
                            || context.Exception.Message.StartsWith("IDX10500", StringComparison.Ordinal))
                            context.HttpContext.Items[ProviderUnreachable] = true;
                        return Task.CompletedTask;
                    },
                };
            });
        services.AddSingleton<Callers>();
        return services;
    }
}
