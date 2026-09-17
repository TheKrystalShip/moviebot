using System.Text.Json;
using System.Text.Json.Serialization;
using TheKrystalShip.Llm.Models;
using TheKrystalShip.MovieBot.Bot.Assistant;
using TheKrystalShip.MovieBot.Core;
using Xunit;
using Xunit.Abstractions;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Requests put to a room whose real conversation came first, against the live model. A request that
/// routes in a fresh conversation can fail in a room's real one, because earlier turns are examples the
/// model copies.
/// </summary>
/// <remarks>
/// <c>Replays/pirates-room.jsonl</c> is the five turns a live room had before "play the first Pirates of
/// the Caribbean's movie". The first of them called <c>load_title</c> with the film's id retyped wrongly,
/// and from then on every request for the film did the same, with the words shuffled and the year
/// invented. The library here holds both films of the series, as that room's did.
/// </remarks>
public sealed class RoomReplayRoutingTests(SessionFixture fixture, ITestOutputHelper output)
    : IClassFixture<SessionFixture>, IDisposable
{
    private const string BlackPearl = "pirates-of-the-caribbean-the-curse-of-the-black-pearl-2003";
    private const string DeadMansChest = "pirates-of-the-caribbean-dead-man-s-chest-2006";

    private static long _channels = 710_000_000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly AssistantHost _host = WithSeries(fixture);

    [LiveModelTheory]
    [InlineData("play the first Pirates of the Caribbean's movie")]
    [InlineData("Play Pirates of the Caribbean The Curse of the Black Pearl")]
    [InlineData("The Curse of the Black Pearl")]
    public async Task The_first_film_of_a_series_is_put_on_in_a_room_that_has_retyped_its_id(string said)
    {
        var turn = AssistantPartsTests.Turn((ulong)Interlocked.Increment(ref _channels));
        await _host.Api.SetSessionTitleAsync(turn.SessionId, DeadMansChest, "someone", CancellationToken.None);
        Replay(turn, "pirates-room.jsonl");

        var answer = await _host.Assistant.AskAsync(turn, said, CancellationToken.None);

        var last = _host.Conversations.GetHistory(turn.ConversationId)[^1].Turn!;
        output.WriteLine($"\"{said}\" -> {string.Join(", ", last.Tools.Select(t => $"{t.Name.Name}({string.Join(", ", t.Arguments.Select(a => $"{a.Key}={a.Value}"))})"))}; reply: {answer.Text}");
        Assert.Equal(BlackPearl, (await _host.Api.OpenSessionAsync(turn.SessionId, CancellationToken.None)).TitleId);
    }

    public void Dispose() => _host.Dispose();

    /// <summary>Writes the replay's turns into the room's conversation, as just said, so none of it is idle.</summary>
    private void Replay(RoomTurn turn, string file)
    {
        var at = DateTimeOffset.UtcNow.AddMinutes(-2);
        foreach (var line in File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Replays", file)))
        {
            var record = JsonSerializer.Deserialize<ConversationTurnRecord>(line, Json)!;
            _host.Conversations.AppendTurn(record with { ConversationId = turn.ConversationId, StartedAt = at, CompletedAt = at });
        }
    }

    /// <summary>
    /// This class's own fixture, with both films of the series added. They stay out of the shared
    /// library, where the first film being here changes what a request to download the second does.
    /// </summary>
    private static AssistantHost WithSeries(SessionFixture fixture)
    {
        Film(fixture, BlackPearl, "Pirates of the Caribbean: The Curse of the Black Pearl", 2003, "tt0325980");
        Film(fixture, DeadMansChest, "Pirates of the Caribbean: Dead Man's Chest", 2006, "tt0383574");
        return new AssistantHost(fixture, Environment.GetEnvironmentVariable(LiveModelFactAttribute.Variable));
    }

    private static void Film(SessionFixture fixture, string id, string name, int year, string imdbId) =>
        ManifestJson.WriteAtomic(Path.Combine(fixture.MediaRoot, id, "manifest.json"), new Manifest
        {
            Id = id,
            Title = id,
            Film = new FilmIdentity { ImdbId = imdbId, Name = name, Year = year },
            DurationSeconds = 6000,
            Status = TitleStatus.Ready,
            Video = new VideoInfo
            {
                Width = 1920,
                Height = 800,
                SourceCodec = "hevc",
                SourceHdr = "sdr",
                Renditions = [new Rendition { Name = "800p", BitrateKbps = 9000, Width = 1920, Height = 800, Uri = "v0/index.m3u8" }]
            },
            Audio = [],
            Subtitles = []
        });
}
