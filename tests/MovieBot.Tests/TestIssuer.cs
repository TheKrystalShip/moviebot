using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TheKrystalShip.MovieBot.Api.Auth;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// An identity provider for the tests: discovery, a key set with an ES256 key and an RSA one, a token
/// endpoint whose exchanges a test answers, and tokens minted the way tks-auth mints Cinema's.
/// </summary>
public sealed class TestIssuer
{
    public const string Issuer = "https://auth.test";

    /// <summary>Every Cinema action, which is what an Owner's token lists.</summary>
    public static readonly string[] Everything = [.. CinemaActions.All.Select(a => a.Id)];

    /// <summary>What every approved account holds: the self actions alone.</summary>
    public static readonly string[] SelfOnly = [.. CinemaActions.All.Where(a => a.Self).Select(a => a.Id)];

    // One provider's keys for every test, so a token minted anywhere is good at every host a test stands up.
    private static readonly ECDsa _ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static readonly RSA _rsa = RSA.Create(2048);
    private static readonly JsonWebTokenHandler _handler = new();

    private static readonly TestIssuer Default = new();

    /// <summary>A Cinema access token for a person, holding every action unless named, through <paramref name="via"/> when given.</summary>
    public static string Token(string sub, string name, IEnumerable<string>? actions = null, string? via = null) =>
        Default.Mint(sub, name, actions, actor: via);

    /// <summary>The exchanges asked of the token endpoint, as their form fields, with the client that authenticated.</summary>
    public List<(string Client, Dictionary<string, string> Form)> Exchanges { get; } = [];

    /// <summary>How the token endpoint answers an exchange: a status and a JSON body. A refusal by default.</summary>
    public Func<Dictionary<string, string>, (HttpStatusCode Status, string Body)> Exchange { get; set; } =
        _ => (HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");

    /// <summary>The handler that answers discovery, the key set and the token endpoint.</summary>
    public HttpMessageHandler Handler() => new Endpoints(this);

    /// <summary>A Cinema access token as tks-auth mints one, with whatever a test changes.</summary>
    public string Mint(
        string sub, string name = "Someone", IEnumerable<string>? actions = null, string audience = "cinema",
        string issuer = Issuer, string type = "at+jwt", string? actor = null, TimeSpan? lifetime = null, bool rsa = false)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = sub,
            ["name"] = name,
            ["client_id"] = actor ?? "cinema-site",
            ["tks_actions"] = (actions ?? Everything).ToArray(),
        };
        if (actor is not null) claims["act"] = new Dictionary<string, object> { ["sub"] = actor };

        var now = DateTime.UtcNow;
        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = now,
            NotBefore = now,
            Expires = now + (lifetime ?? TimeSpan.FromMinutes(5)),
            TokenType = type,
            SigningCredentials = rsa
                ? new SigningCredentials(new RsaSecurityKey(_rsa) { KeyId = "rsa" }, SecurityAlgorithms.RsaSha256)
                : new SigningCredentials(new ECDsaSecurityKey(_ecdsa) { KeyId = "ec" }, SecurityAlgorithms.EcdsaSha256),
        });
    }

    /// <summary>A token exchange's answer carrying a minted token, as the provider writes it.</summary>
    public string Minted(string token) =>
        $$"""{"access_token":"{{token}}","issued_token_type":"urn:ietf:params:oauth:token-type:access_token","token_type":"Bearer","expires_in":300}""";

    private string Discovery() => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["issuer"] = Issuer,
        ["jwks_uri"] = Issuer + "/jwks",
        ["token_endpoint"] = Issuer + "/token",
        ["authorization_endpoint"] = Issuer + "/authorize",
        ["id_token_signing_alg_values_supported"] = new[] { "ES256" },
    });

    private string KeySet()
    {
        var ec = _ecdsa.ExportParameters(false);
        var rsa = _rsa.ExportParameters(false);
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["keys"] = new object[]
            {
                new Dictionary<string, string>
                {
                    ["kty"] = "EC", ["crv"] = "P-256", ["kid"] = "ec", ["use"] = "sig", ["alg"] = "ES256",
                    ["x"] = Base64UrlEncoder.Encode(ec.Q.X!), ["y"] = Base64UrlEncoder.Encode(ec.Q.Y!),
                },
                new Dictionary<string, string>
                {
                    ["kty"] = "RSA", ["kid"] = "rsa", ["use"] = "sig", ["alg"] = "RS256",
                    ["n"] = Base64UrlEncoder.Encode(rsa.Modulus!), ["e"] = Base64UrlEncoder.Encode(rsa.Exponent!),
                },
            },
        });
    }

    private sealed class Endpoints(TestIssuer issuer) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/.well-known/openid-configuration") return Json(HttpStatusCode.OK, issuer.Discovery());
            if (path == "/jwks") return Json(HttpStatusCode.OK, issuer.KeySet());
            if (path != "/token" || request.Method != HttpMethod.Post) return new HttpResponseMessage(HttpStatusCode.NotFound);

            var client = request.Headers.Authorization is { Scheme: "Basic", Parameter: { } basic }
                ? Encoding.UTF8.GetString(Convert.FromBase64String(basic)).Split(':')[0]
                : "";
            var form = (await request.Content!.ReadAsStringAsync(ct)).Split('&')
                .Select(pair => pair.Split('='))
                .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));
            lock (issuer.Exchanges) issuer.Exchanges.Add((client, form));

            var (status, body) = issuer.Exchange(form);
            return Json(status, body);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
