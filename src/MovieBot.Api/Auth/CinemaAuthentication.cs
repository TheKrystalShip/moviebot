using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Auth;

/// <summary>
/// The person a request acts as, read from the access token the provider minted: who they are, what they
/// are called, the Cinema actions they hold, and the client acting for them when one is.
/// </summary>
/// <param name="UserId">The token's <c>sub</c>: the account, which every record here is keyed by.</param>
/// <param name="Via">The <c>act</c> claim's <c>sub</c>: the client calling on the person's behalf.</param>
public sealed record CinemaPerson(string UserId, string DisplayName, IReadOnlySet<string> Actions, string? Via)
{
    private const string ItemKey = "cinema-person";

    public bool Holds(string action) => Actions.Contains(action);

    /// <summary>The person as a room or a record names them.</summary>
    public Actor Actor => new(UserId, DisplayName, Via);

    /// <summary>The person the request's token names, or null when it carries none: a service, a media ticket, nobody.</summary>
    public static CinemaPerson? Of(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var person) ? person as CinemaPerson : null;

    internal void AttachTo(HttpContext context) => context.Items[ItemKey] = this;

    /// <summary>Reads the person off a validated token, or null when it names nobody.</summary>
    public static CinemaPerson? From(ClaimsIdentity identity, string actionsClaim)
    {
        if (identity.FindFirst("sub")?.Value is not { Length: > 0 } sub) return null;

        var name = identity.FindFirst("name")?.Value is { Length: > 0 } shown ? shown
            : identity.FindFirst("preferred_username")?.Value is { Length: > 0 } username ? username
            : sub;

        var actions = identity.FindAll(actionsClaim)
            .Select(c => c.Value)
            .Where(CinemaActions.IsDeclared)
            .ToHashSet(StringComparer.Ordinal);

        return new CinemaPerson(sub, name, actions, ActorOf(identity.FindFirst("act")?.Value));
    }

    /// <summary>The acting client's id out of an <c>act</c> claim (RFC 8693 §4.1), an object carried as JSON.</summary>
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

/// <summary>The ways a request proves something here, as authentication schemes.</summary>
public static class CinemaSchemes
{
    /// <summary>An access token the provider minted for Cinema's audience.</summary>
    public const string Bearer = JwtBearerDefaults.AuthenticationScheme;

    /// <summary>The service key, for machine work that names nobody.</summary>
    public const string Service = "Service";

    /// <summary>A media ticket, which opens one film's bytes.</summary>
    public const string MediaTicket = "MediaTicket";

    /// <summary>A room's guest link, which lets its holder watch that room and nothing else.</summary>
    public const string Guest = "Guest";

    /// <summary>The header the service key is carried in.</summary>
    public const string ServiceHeader = "X-MovieBot-Service";
}

/// <summary>
/// What a route needs: any one of its actions, or the service key, or a ticket for the film, or a
/// room's guest link.
/// </summary>
public sealed record ActionRequirement(IReadOnlyList<string> AnyOf, bool Service = false, bool Ticket = false, bool Guest = false)
    : IAuthorizationRequirement;

/// <summary>
/// Admits a request to a route when one of the identities it proved meets the route's requirement: a
/// person whose token lists one of its actions, the service key where the route takes it, a ticket where
/// the route is a film's bytes.
/// </summary>
internal sealed class ActionRequirementHandler(IOptionsMonitor<OidcOptions> oidc) : AuthorizationHandler<ActionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ActionRequirement requirement)
    {
        var claim = oidc.CurrentValue.ActionsClaim;
        foreach (var identity in context.User.Identities.Where(i => i.IsAuthenticated))
        {
            var met = identity.AuthenticationType switch
            {
                CinemaSchemes.Service => requirement.Service,
                CinemaSchemes.MediaTicket => requirement.Ticket,
                CinemaSchemes.Guest => requirement.Guest,
                CinemaSchemes.Bearer => requirement.AnyOf.Any(action => identity.HasClaim(claim, action)),
                _ => false,
            };
            if (!met) continue;
            context.Succeed(requirement);
            break;
        }
        return Task.CompletedTask;
    }
}

/// <summary>The service key, checked in fixed time. A request that carries none is no business of this scheme.</summary>
internal sealed class ServiceKeyHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, IOptions<AuthOptions> auth)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(CinemaSchemes.ServiceHeader, out var offered))
            return Task.FromResult(AuthenticateResult.NoResult());

        var key = auth.Value.ServiceKey;
        if (key.Length == 0 || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(offered.ToString()), Encoding.UTF8.GetBytes(key)))
            return Task.FromResult(AuthenticateResult.Fail("The service key is wrong."));

        var identity = new ClaimsIdentity([new Claim("service", "moviebot")], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}

/// <summary>
/// A media ticket on a request for a film's bytes. The title is taken from the path rather than the
/// ticket, so a ticket is only ever checked against the film actually asked for.
/// </summary>
internal sealed class MediaTicketHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    IOptions<AuthOptions> auth, TimeProvider clock)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Query.TryGetValue(MediaTicket.QueryName, out var ticket) || TitleOf(Request.Path) is not { } title)
            return Task.FromResult(AuthenticateResult.NoResult());

        if (!MediaTicket.IsValid(ticket, title, auth.Value, clock.GetUtcNow()))
            return Task.FromResult(AuthenticateResult.Fail("The ticket is not this film's, or has expired."));

        var identity = new ClaimsIdentity([new Claim("ticket", title)], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }

    /// <summary>The title a media path names, or null when the path is not one.</summary>
    private static string? TitleOf(PathString path)
    {
        if (!path.StartsWithSegments("/media", out var rest)) return null;

        var value = rest.Value;
        if (string.IsNullOrEmpty(value) || value[0] != '/') return null;

        var slash = value.IndexOf('/', 1);
        var id = slash < 0 ? value[1..] : value[1..slash];
        return id.Length == 0 ? null : id;
    }
}

/// <summary>
/// A room's guest link, carried where a person's access token would be: <c>Bearer guest.&lt;code&gt;</c>,
/// or on the hub's query string as SignalR carries a token. It is checked against the room on every
/// request, so a link taken away stops opening anything at once.
/// </summary>
internal sealed class GuestHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    GuestCodes codes, SessionStore sessions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (GuestCredential.Of(Request) is not { } code)
            return Task.FromResult(AuthenticateResult.NoResult());

        if (codes.PassIdOf(code) is not { } passId || sessions.RoomOfGuests(passId) is not { } room)
            return Task.FromResult(AuthenticateResult.Fail("The guest link has ended."));

        var identity = new ClaimsIdentity([new Claim(GuestViewer.PassClaim, passId), new Claim(GuestViewer.RoomClaim, room)], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}

/// <summary>Where a guest's link is carried on a request.</summary>
internal static class GuestCredential
{
    /// <summary>The code a request carries as a guest, or null when it carries none.</summary>
    public static string? Of(HttpRequest request)
    {
        var offered = request.Headers.Authorization.ToString() is { } header
                      && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : request.Path.StartsWithSegments("/hub") && request.Query.TryGetValue("access_token", out var token)
                ? token.ToString()
                : null;
        return offered is not null && offered.StartsWith(GuestCodes.CredentialPrefix, StringComparison.Ordinal)
            ? offered[GuestCodes.CredentialPrefix.Length..]
            : null;
    }
}

/// <summary>A request made on a room's guest link: which link, and the room it opens.</summary>
public sealed record GuestViewer(string PassId, string SessionId)
{
    internal const string PassClaim = "guest-pass";
    internal const string RoomClaim = "room";

    /// <summary>The guest a request or a connection is, or null when it is anybody else.</summary>
    public static GuestViewer? Of(ClaimsPrincipal? user) =>
        user?.Identities.FirstOrDefault(i => i.IsAuthenticated && i.AuthenticationType == CinemaSchemes.Guest) is { } identity
        && identity.FindFirst(PassClaim)?.Value is { } pass && identity.FindFirst(RoomClaim)?.Value is { } room
            ? new GuestViewer(pass, room)
            : null;
}

/// <summary>What of a room a guest may reach on a route that admits one.</summary>
public enum GuestReach
{
    /// <summary>The route names no room of its own: the hub, which decides for itself.</summary>
    Hub,

    /// <summary>The route's <c>{sessionId}</c> is the guest's room.</summary>
    Room,

    /// <summary>The route's <c>{id}</c> is the title the guest's room holds.</summary>
    RoomTitle,
}

/// <summary>
/// Answers a refused request with the reason as the API's one error shape: a 401 with
/// <c>WWW-Authenticate: Bearer</c> for no usable token, a 403 for a person who may not do this.
/// </summary>
internal sealed class RefusalWriter : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _admitted = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult result)
    {
        // A request no route matched is nobody's to refuse: what answers it is the 404 it would have had.
        if (context.GetEndpoint() is null)
        {
            await next(context);
            return;
        }

        if (result.Challenged)
        {
            await context.ChallengeAsync(CinemaSchemes.Bearer);
            await context.Response.WriteAsJsonAsync(
                new ErrorReply("Sign in to Krystal Cinema."), ApiJsonContext.Default.ErrorReply);
            return;
        }

        if (result.Forbidden)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(
                new ErrorReply("Your account does not let you do that in Krystal Cinema."), ApiJsonContext.Default.ErrorReply);
            return;
        }

        await _admitted.HandleAsync(next, context, policy, result);
    }
}

/// <summary>What a route asks of its caller, kept on the endpoint so it can be listed and checked.</summary>
public sealed record RequiredAccess(IReadOnlyList<string> AnyOf, bool Service, bool Ticket, GuestReach? Guest = null);

public static class CinemaAccess
{
    /// <summary>
    /// Admits a person holding any one of <paramref name="anyOf"/>. Each id must be one
    /// <see cref="CinemaActions"/> declares, so nothing is checked that the manifest does not name.
    /// </summary>
    public static TBuilder RequireAction<TBuilder>(this TBuilder builder, params string[] anyOf)
        where TBuilder : IEndpointConventionBuilder => Require(builder, new ActionRequirement(Declared(anyOf)));

    /// <summary>As <see cref="RequireAction{TBuilder}"/>, and also the service key, for a route machine work reads too.</summary>
    public static TBuilder RequireActionOrService<TBuilder>(this TBuilder builder, params string[] anyOf)
        where TBuilder : IEndpointConventionBuilder => Require(builder, new ActionRequirement(Declared(anyOf), Service: true));

    /// <summary>The service key alone: machine work that names nobody.</summary>
    public static TBuilder RequireService<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder => Require(builder, new ActionRequirement([], Service: true));

    /// <summary>A film's bytes: its media ticket, or a person who may watch.</summary>
    public static TBuilder RequireMediaAccess<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder => Require(builder, new ActionRequirement([CinemaActions.Watch], Ticket: true));

    /// <summary>
    /// As <see cref="RequireAction{TBuilder}"/>, and also a guest, reaching only what
    /// <paramref name="reach"/> says of the room their link opens.
    /// </summary>
    public static TBuilder RequireActionOrGuest<TBuilder>(this TBuilder builder, GuestReach reach, params string[] anyOf)
        where TBuilder : IEndpointConventionBuilder =>
        Require(builder, new ActionRequirement(Declared(anyOf), Guest: true), reach);

    /// <summary>As <see cref="RequireActionOrService{TBuilder}"/>, and also a guest, as <see cref="RequireActionOrGuest{TBuilder}"/>.</summary>
    public static TBuilder RequireActionServiceOrGuest<TBuilder>(this TBuilder builder, GuestReach reach, params string[] anyOf)
        where TBuilder : IEndpointConventionBuilder =>
        Require(builder, new ActionRequirement(Declared(anyOf), Service: true, Guest: true), reach);

    /// <summary>What every route that names no requirement answers: nobody is admitted.</summary>
    internal static AuthorizationPolicy Nobody { get; } = Policy(new ActionRequirement([]));

    private static TBuilder Require<TBuilder>(TBuilder builder, ActionRequirement requirement, GuestReach? reach = null)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.RequireAuthorization(Policy(requirement))
            .WithMetadata(new RequiredAccess(requirement.AnyOf, requirement.Service, requirement.Ticket, reach));
        if (reach is GuestReach.Room or GuestReach.RoomTitle) builder.AddEndpointFilter(GuestBound(reach.Value));
        return builder;
    }

    /// <summary>
    /// Holds a guest to the room their link opens: its own route, or the title it holds this moment.
    /// Anybody else passes, having been admitted on their own terms.
    /// </summary>
    private static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> GuestBound(GuestReach reach) =>
        async (context, next) =>
        {
            var http = context.HttpContext;
            if (CinemaPerson.Of(http) is null && GuestViewer.Of(http.User) is { } guest)
            {
                var within = reach == GuestReach.Room
                    ? http.GetRouteValue("sessionId") as string == guest.SessionId
                    : http.GetRouteValue("id") is string id
                      && http.RequestServices.GetRequiredService<SessionStore>().Get(guest.SessionId)?.TitleId == id;
                if (!within) return GuestOutOfReach;
            }
            return await next(context);
        };

    private static readonly IResult GuestOutOfReach = Results.Json(
        new ErrorReply("A guest link opens its own room and nothing else."),
        ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status403Forbidden);

    private static AuthorizationPolicy Policy(ActionRequirement requirement)
    {
        var schemes = new List<string> { CinemaSchemes.Bearer };
        if (requirement.Service) schemes.Add(CinemaSchemes.Service);
        if (requirement.Ticket) schemes.Add(CinemaSchemes.MediaTicket);
        if (requirement.Guest) schemes.Add(CinemaSchemes.Guest);
        return new AuthorizationPolicyBuilder([.. schemes]).AddRequirements(requirement).Build();
    }

    private static string[] Declared(string[] actions) =>
        actions.FirstOrDefault(a => !CinemaActions.IsDeclared(a)) is { } stray
            ? throw new InvalidOperationException($"{stray} is checked but not declared in CinemaActions.")
            : actions;
}

public static class CinemaAuthenticationExtensions
{
    /// <summary>
    /// Cinema as a resource server of its OpenID Connect provider: access tokens validated against the
    /// issuer's discovery and keys, its audience and ES256; the service key and media tickets beside them;
    /// every route refused unless it names what it needs.
    /// </summary>
    public static IServiceCollection AddCinemaAuthentication(this IServiceCollection services)
    {
        services.AddOptions<OidcOptions>()
            .BindConfiguration(OidcOptions.Section)
            .Validate(o => Uri.TryCreate(o.Issuer, UriKind.Absolute, out var issuer)
                           && (issuer.Scheme == Uri.UriSchemeHttps || issuer.Scheme == Uri.UriSchemeHttp),
                "Oidc:Issuer must be the identity provider's issuer, an absolute https address.")
            .Validate(o => o.Audience.Length > 0, "Oidc:Audience must name the audience Cinema's tokens are minted for.")
            .Validate(o => o.ActionsClaim.Length > 0, "Oidc:ActionsClaim must name the claim listing a person's actions.")
            .ValidateOnStart();

        services.AddAuthentication(CinemaSchemes.Bearer)
            .AddJwtBearer(CinemaSchemes.Bearer, _ => { })
            .AddScheme<AuthenticationSchemeOptions, ServiceKeyHandler>(CinemaSchemes.Service, null)
            .AddScheme<AuthenticationSchemeOptions, MediaTicketHandler>(CinemaSchemes.MediaTicket, null)
            .AddScheme<AuthenticationSchemeOptions, GuestHandler>(CinemaSchemes.Guest, null);
        services.AddSingleton<GuestCodes>();

        // Read when the options are first asked for, after every configuration source is in.
        services.AddOptions<JwtBearerOptions>(CinemaSchemes.Bearer)
            .Configure<IOptions<OidcOptions>>((jwt, oidc) => Configure(jwt, oidc.Value));

        services.AddAuthorizationBuilder().SetFallbackPolicy(CinemaAccess.Nobody);
        services.AddSingleton<IAuthorizationHandler, ActionRequirementHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, RefusalWriter>();
        return services;
    }

    private static void Configure(JwtBearerOptions jwt, OidcOptions oidc)
    {
        jwt.Authority = oidc.Authority;
        jwt.RequireHttpsMetadata = oidc.Authority.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        jwt.MapInboundClaims = false;
        jwt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = oidc.Issuer,
            ValidAudience = oidc.Audience,
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            // RFC 9068: an access token says it is one, which no id token does.
            ValidTypes = ["at+jwt"],
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            // The token lives five minutes; a skew of the framework's own five would double that.
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name",
            AuthenticationType = CinemaSchemes.Bearer,
        };
        jwt.Events = new JwtBearerEvents
        {
            // SignalR's WebSocket transport cannot set headers, so its token arrives on the query string
            // the way the protocol expects, for the hub alone.
            OnMessageReceived = context =>
            {
                // A guest's link is the guest scheme's, and is no token for this one to fail on.
                if (GuestCredential.Of(context.Request) is not null)
                {
                    context.NoResult();
                    return Task.CompletedTask;
                }
                if (context.Request.Path.StartsWithSegments("/hub")
                    && context.Request.Query.TryGetValue("access_token", out var token))
                    context.Token = token.ToString();
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                if (context.Principal?.Identity is not ClaimsIdentity identity
                    || CinemaPerson.From(identity, oidc.ActionsClaim) is not { } person)
                {
                    context.Fail("The token names nobody.");
                    return Task.CompletedTask;
                }

                person.AttachTo(context.HttpContext);
                var people = context.HttpContext.RequestServices.GetRequiredService<PersonDirectory>();
                people.Remember(person.UserId, person.DisplayName);
                if (person.Via is null)
                    people.Seen(person.UserId, context.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow());
                return Task.CompletedTask;
            },
        };
    }
}
