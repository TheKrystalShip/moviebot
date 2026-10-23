using System.Net.Http.Headers;
using TheKrystalShip.Agent.Plugin.Testing.Suite;

// The behavioural suite of the MovieBot assistant plugin: the cases in cases.json, played against the
// live harness through the deployed plugin as a person who may watch in Cinema. The plugin is
// the one behind MOVIEBOT_ASSISTANT_URL, and the person is whoever the access token in the file named
// by MOVIEBOT_ASSISTANT_TOKEN_FILE names.
const string Origin = "MOVIEBOT_ASSISTANT_URL";
const string TokenFile = "MOVIEBOT_ASSISTANT_TOKEN_FILE";

return await SuiteCommand.RunAsync(args, "MovieBot assistant", _ =>
{
    string origin = Environment.GetEnvironmentVariable(Origin) is { Length: > 0 } given
        ? given
        : "https://movies.thekrystalship.com";

    if (Environment.GetEnvironmentVariable(TokenFile) is not { Length: > 0 } path)
        throw new InvalidOperationException($"{TokenFile} names a file holding a Cinema access token; it is not set.");

    string token = File.ReadAllText(path).Trim();
    var client = new HttpClient { BaseAddress = new Uri(origin), Timeout = Timeout.InfiniteTimeSpan };

    return new SuiteSetup
    {
        TurnPath = "/assistant/turn",
        OpenCase = (_, _) => Task.FromResult(new SuiteCaseHost(
            client, request => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token))),
    };
});
