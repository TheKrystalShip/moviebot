using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TheKrystalShip.Llm.Interfaces;
using TheKrystalShip.Llm.Models;
using TheKrystalShip.MovieBot.Acquire.Imdb;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Assistant;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The assistant built the way the service builds it, against the in-process API, with the tracker and
/// the catalogue answering from the test and the torrent client unreachable. The model is scripted
/// unless an endpoint is given, in which case it is the real one.
/// </summary>
internal sealed class AssistantHost : IDisposable
{
    private readonly string _state = Path.Combine(
        Path.GetTempPath(), "moviebot-tests", "assistant-" + Guid.NewGuid().ToString("N"));

    public AssistantHost(SessionFixture fixture, string? modelEndpoint = null)
    {
        Directory.CreateDirectory(_state);
        var configuration = Configuration(_state, modelEndpoint);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        // No credential of its own: the tools call the API with each turn's token.
        services.AddSingleton(new MovieBotApiClient(fixture.CreateClient()));
        services.AddCinemaAssistant(configuration);
        services.AddHttpClient<ImdbClient>().ConfigurePrimaryHttpMessageHandler(() => new CatalogueStub());

        if (modelEndpoint is null)
        {
            services.RemoveAll<ILlmClient>();
            services.AddSingleton<ILlmClient, ScriptedModel>();
        }

        Provider = services.BuildServiceProvider();
    }

    /// <summary>The model settings the service ships with, and a state directory of the test's own.</summary>
    public static IConfiguration Configuration(string state, string? modelEndpoint = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:Provider"] = "LlamaCpp",
                ["Llm:ContextWindow"] = "8192",
                ["Llm:Temperature"] = "0",
                ["LlmAgent:MaxIterations"] = "6",

                ["Assistant:DatabasePath"] = Path.Combine(state, "assistant.db"),
                ["Llm:Endpoint"] = modelEndpoint ?? "http://127.0.0.1:1",
            }).Build();

    public ServiceProvider Provider { get; }

    /// <summary>The API as somebody setting a room up for a test.</summary>
    public MovieBotApiClient Api => Provider.GetRequiredService<MovieBotApiClient>().As(TestIssuer.Token("1", "someone"));

    /// <summary>The API as the person a turn is for, with the turn's token.</summary>
    public MovieBotApiClient ApiAs(AssistantTurn turn) => Provider.GetRequiredService<MovieBotApiClient>().As(turn.AccessToken);

    /// <summary>Somebody agreeing to a proposal: the API as them.</summary>
    public Agreement Agreement(string userId, string name) =>
        new(Provider.GetRequiredService<MovieBotApiClient>().As(TestIssuer.Token(userId, name)), name);

    public RoomToolbox Tools => Provider.GetRequiredService<RoomToolbox>();

    public CinemaAssistant Assistant => Provider.GetRequiredService<CinemaAssistant>();

    public StagedActions Staged => Provider.GetRequiredService<StagedActions>();

    public ScriptedModel Model => (ScriptedModel)Provider.GetRequiredService<ILlmClient>();

    public IConversationStore Conversations => Provider.GetRequiredService<IConversationStore>();

    public void Dispose()
    {
        Provider.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_state)) Directory.Delete(_state, recursive: true);
    }

    /// <summary>
    /// The tracker holds one release of Inception, as torrent 41, and one of Heat, which the library
    /// already has, as torrent 42. A search finds whichever the words or the id name.
    /// </summary>
    public sealed class OneRelease : IReleaseSearch
    {
        private static readonly Release Inception = new()
        {
            TorrentId = 41, ReleaseName = "Inception.2010.1080p.BluRay-GRP", Title = "Inception", Year = 2010,
            ImdbId = "tt1375666", SizeBytes = 8L << 30, Seeders = 12,
        };

        private static readonly Release Heat = new()
        {
            TorrentId = 42, ReleaseName = "Heat.1995.1080p.BluRay-GRP", Title = "Heat", Year = 1995,
            ImdbId = "tt0113277", SizeBytes = 9L << 30, Seeders = 20,
        };

        private static readonly Release DeadMansChest = new()
        {
            TorrentId = 43, ReleaseName = "Pirates.of.the.Caribbean.Dead.Mans.Chest.2006.1080p.BluRay-GRP",
            Title = "Pirates of the Caribbean Dead Mans Chest", Year = 2006,
            ImdbId = "tt0383574", SizeBytes = 12L << 30, Seeders = 30,
        };

        /// <summary>The films the title index names from words, one of which the tracker has no release of.</summary>
        private static readonly ImdbTitle[] Named =
        [
            new() { ImdbId = "tt1375666", Title = "Inception", Year = 2010 },
            new() { ImdbId = "tt0113277", Title = "Heat", Year = 1995 },
            new() { ImdbId = "tt0383574", Title = "Pirates of the Caribbean: Dead Man's Chest", Year = 2006 },
            new() { ImdbId = "tt0449088", Title = "Pirates of the Caribbean: At World's End", Year = 2007 },
        ];

        // The real search names the film in the title index before asking the tracker, so the
        // catalogue's spelling of the name finds it as surely as the id does.
        private static Task<RankedReleases> Find(string said) => Task.FromResult(new RankedReleases(
            [.. new[] { Inception, Heat, DeadMansChest }.Where(r =>
                said.Contains(r.Title, StringComparison.OrdinalIgnoreCase) || said == r.ImdbId
                || (r == DeadMansChest && said.Contains("Dead Man", StringComparison.OrdinalIgnoreCase)))], []));

        public async Task<RankedReleases> ByTextAsync(string typed, CancellationToken ct) =>
            await Find(typed) with
            {
                Film = Named.FirstOrDefault(f =>
                    typed.Contains(f.Title.Split(": ")[^1], StringComparison.OrdinalIgnoreCase)),
            };

        public Task<RankedReleases> ByTitleAsync(string query, CancellationToken ct) => Find(query);
        public Task<RankedReleases> ByImdbAsync(string imdbId, CancellationToken ct) => Find(imdbId);
    }

    /// <summary>
    /// The catalogue knows Inception, Heat and the Pirates of the Caribbean series, and finds whichever
    /// the words name. The series comes back as the index returns it: ordered by what people search
    /// for rather than by when the films came out, with people who share the words mixed in.
    /// </summary>
    internal sealed class CatalogueStub : HttpMessageHandler
    {
        private const string Pirates = """
            {"d":[
              {"id":"tt0325980","l":"Pirates of the Caribbean: The Curse of the Black Pearl","y":2003,"qid":"movie","s":"Johnny Depp, Geoffrey Rush","i":{"imageUrl":"https://m.media-amazon.com/images/M/pearl._V1_.jpg","width":1000,"height":1500}},
              {"id":"tt9900001","l":"Pirates of the Caribbean: The Series","y":2027,"qid":"tvSeries","s":"Keira Knightley"},
              {"id":"tt1790809","l":"Pirates of the Caribbean: Dead Men Tell No Tales","y":2017,"qid":"movie","s":"Johnny Depp, Geoffrey Rush"},
              {"id":"tt1298650","l":"Pirates of the Caribbean: On Stranger Tides","y":2011,"qid":"movie","s":"Johnny Depp, Penélope Cruz"},
              {"id":"tt0383574","l":"Pirates of the Caribbean: Dead Man's Chest","y":2006,"qid":"movie","s":"Johnny Depp, Orlando Bloom"},
              {"id":"tt0449088","l":"Pirates of the Caribbean: At World's End","y":2007,"qid":"movie","s":"Johnny Depp, Orlando Bloom"},
              {"id":"nm0809688","l":"Rex Smith","s":"Actor, The Pirates of Penzance (1983)"}
            ],"q":"x","v":1}
            """;

        private const string Inception = """
            {"d":[{"id":"tt1375666","l":"Inception","y":2010,"qid":"movie","s":"Leonardo DiCaprio, Joseph Gordon-Levitt"}],"q":"x","v":1}
            """;

        private const string Heat = """
            {"d":[{"id":"tt0113277","l":"Heat","y":1995,"qid":"movie","s":"Al Pacino, Robert De Niro"}],"q":"x","v":1}
            """;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;

            // An id is looked up under its own key, and the index answers with the film it names.
            var tag = path.Split('/')[^1].Replace(".json", "");
            if (tag.StartsWith("tt", StringComparison.Ordinal))
                path = new[] { Heat, Inception, Pirates }.FirstOrDefault(b => b.Contains($"\"{tag}\"")) switch
                {
                    Heat => "heat", Inception => "incep", Pirates => "pirates", _ => path,
                };

            var body = path.Contains("heat", StringComparison.OrdinalIgnoreCase) ? Heat
                : path.Contains("incep", StringComparison.OrdinalIgnoreCase) ? Inception
                : path.Contains("pirates", StringComparison.OrdinalIgnoreCase) ? Pirates
                : null;

            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
                });
        }
    }

    /// <summary>A model that says what the test queued, and keeps what it was sent.</summary>
    public sealed class ScriptedModel : ILlmClient
    {
        public Queue<LlmResponse> Answers { get; } = new();
        public List<IReadOnlyList<LlmMessage>> Requests { get; } = [];

        public Task<Result<LlmResponse>> ChatAsync(
            IReadOnlyList<LlmMessage> messages, IReadOnlyList<LlmToolDefinition>? tools = null,
            bool think = false, CancellationToken cancellationToken = default)
        {
            Requests.Add([.. messages]);
            return Task.FromResult(Answers.TryDequeue(out var answer)
                ? Result.Success(answer)
                : Result.Failure<LlmResponse>("nothing queued"));
        }

        /// <summary>The queued answer as one chunk, which is how a stream that has finished reads.</summary>
        public async IAsyncEnumerable<LlmStreamChunk> ChatStreamAsync(
            IReadOnlyList<LlmMessage> messages, IReadOnlyList<LlmToolDefinition>? tools = null,
            bool think = false, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            Requests.Add([.. messages]);
            if (!Answers.TryDequeue(out var answer)) throw new HttpRequestException("nothing queued");

            yield return new LlmStreamChunk(answer.Content, answer.ToolCalls, Done: true, answer.Usage);
        }
    }
}
