using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Bot.Api;

/// <summary>
/// The bot as a client of the identity provider: who it is there, and the provider it asks.
/// </summary>
public sealed class BotOidcOptions
{
    public const string Section = "Oidc";

    /// <summary>The provider's issuer, the one the API checks tokens against.</summary>
    public string Issuer { get; set; } = "";

    /// <summary>
    /// The confidential client the bot exchanges with, registered at the provider as able to act for
    /// people by their linked Discord accounts.
    /// </summary>
    public string BotClientId { get; set; } = "cinema-bot";

    /// <summary>That client's secret. A credential: it comes from the environment, never a file.</summary>
    public string BotClientSecret { get; set; } = "";
}

/// <summary>
/// The provider would not mint a token for the person: their account is waiting for approval, is
/// switched off, or no account holds their Discord identity. <see cref="Sentence"/> is what they are told.
/// </summary>
public sealed class AccountRefusedException(string? accountStatus)
    : Exception($"The identity provider refused a token: {accountStatus ?? "no account status"}.")
{
    public string? AccountStatus { get; } = accountStatus;

    public string Sentence => AccountStatus switch
    {
        "disabled" => "Your account is switched off, so MovieBot cannot act for you.",
        "pending" or "unknown" => "An administrator has to approve your account before MovieBot can act for you.",
        _ => "MovieBot could not act for you: the sign-in service refused.",
    };
}

/// <summary>
/// Whose the bot's calls are, for the stretch of work being done for one person.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything done inside a command is done as the person who ran it.</b> A command opens
/// <see cref="Person"/> for the Discord user who ran it, and every call to the API
/// made inside it, however deep, carries that person's token (<see cref="PersonAuthorization"/>). A
/// call made outside any — the presence line, a download's progress, loading a finished download into
/// the room that asked for it — is machine work that names nobody, made on the service key.
/// </para>
/// <para>
/// <b>It flows with the work, not the thread.</b> The scope is an <see cref="AsyncLocal{T}"/>, so it
/// follows a command through every await and reaches nothing the command did not start.
/// </para>
/// </remarks>
public static class ActingFor
{
    private static readonly AsyncLocal<ulong?> Current = new();

    /// <summary>The Discord user the current work is for, or null for machine work.</summary>
    public static ulong? DiscordUser => Current.Value;

    /// <summary>Makes every call until the returned scope ends one made as <paramref name="discordUserId"/>.</summary>
    public static IDisposable Person(ulong discordUserId)
    {
        var previous = Current.Value;
        Current.Value = discordUserId;
        return new Scope(previous);
    }

    private sealed class Scope(ulong? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}

/// <summary>
/// A person's token, exchanged at the provider by the bot as a client that may act for linked Discord
/// users (RFC 8693), and kept for its lifetime.
/// </summary>
/// <remarks>
/// The provider names the person in the token and the bot as the client acting for them, so the API
/// checks the person's actions exactly as it does on the site and records that the bot carried the act.
/// A token is asked for again once it is within half a minute of running out.
/// </remarks>
public sealed class PersonTokens(
    TokenExchangeClient exchange, IOptions<BotOidcOptions> options, TimeProvider clock, ILogger<PersonTokens> logger)
{
    private static readonly TimeSpan Margin = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<ulong, ExchangedToken> _held = new();

    /// <summary>The person's access token. Throws <see cref="AccountRefusedException"/> when the provider will mint none.</summary>
    public async Task<string> ForAsync(ulong discordUserId, CancellationToken ct)
    {
        if (_held.TryGetValue(discordUserId, out var held) && held.ExpiresAt - Margin > clock.GetUtcNow())
            return held.AccessToken;

        var o = options.Value;
        var result = await exchange.ExchangeAsync(o.Issuer, o.BotClientId, o.BotClientSecret,
            discordUserId.ToString(System.Globalization.CultureInfo.InvariantCulture), TokenExchangeTypes.DiscordUserId, ct);

        if (result.Token is not { } token)
        {
            _held.TryRemove(discordUserId, out _);
            logger.LogInformation("The identity provider would not act for Discord user {User}: {Error} ({Status}).",
                discordUserId, result.Error, result.AccountStatus ?? "no account status");
            throw new AccountRefusedException(result.AccountStatus);
        }

        _held[discordUserId] = token;
        return token.AccessToken;
    }
}

/// <summary>
/// Puts the right credential on a request the bot makes: the token of the person the work is for
/// (<see cref="ActingFor"/>), and otherwise, where the destination takes it, the service key.
/// </summary>
public sealed class PersonAuthorization(PersonTokens tokens, string? serviceKey) : DelegatingHandler
{
    public const string ServiceHeader = "X-MovieBot-Service";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Headers.Authorization is null)
        {
            if (ActingFor.DiscordUser is { } person)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.ForAsync(person, ct));
            else if (serviceKey is { Length: > 0 })
                request.Headers.Add(ServiceHeader, serviceKey);
        }

        return await base.SendAsync(request, ct);
    }
}
