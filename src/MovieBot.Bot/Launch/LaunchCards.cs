using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Bot.Configuration;

namespace TheKrystalShip.MovieBot.Bot.Launch;

/// <summary>A launch card standing in a channel, and what it takes to go back to it.</summary>
public sealed record LaunchCard
{
    public required ulong ChannelId { get; init; }
    public required ulong MessageId { get; init; }

    /// <summary>The room the card is a way into.</summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// The film it announces. A room that has moved on to another one leaves the card describing
    /// something that is no longer playing, which is its own kind of expired.
    /// </summary>
    public required string TitleId { get; init; }

    /// <summary>The invite the card hands out, taken away when the card stops being a way in.</summary>
    public required string InviteCode { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(List<LaunchCard>))]
internal partial class LaunchCardsJson : JsonSerializerContext;

/// <summary>
/// The launch cards still standing, kept on disk.
///
/// A card outlives the interaction that posted it by hours, and everything needed to go back to
/// it — which message, in which channel, handing out which invite — exists in Discord and
/// nowhere else. The API has never heard of a Discord message, so there is nothing to read this
/// back from: unwritten, a restarted bot leaves every card it had posted looking live for good.
///
/// Written whole and moved into place on every change, as the wish list is. Changes are a launch
/// and a room closing, which are rare enough that there is nothing to batch, and a half-written
/// file read at the next start is worse than none.
/// </summary>
public sealed class LaunchCards
{
    private readonly ILogger<LaunchCards> _logger;
    private readonly Lock _gate = new();
    private List<LaunchCard>? _cards;

    public LaunchCards(IOptions<LaunchOptions> options, ILogger<LaunchCards> logger)
        : this(options.Value.ResolveCardsPath(), logger)
    {
    }

    public LaunchCards(string path, ILogger<LaunchCards> logger)
    {
        Path = path;
        _logger = logger;
    }

    public string Path { get; }

    public IReadOnlyList<LaunchCard> All()
    {
        lock (_gate) return [.. Loaded()];
    }

    /// <summary>
    /// Remembers a card that has just been posted. Recorded after the message exists, because
    /// the message is half of what is being recorded.
    /// </summary>
    public void Add(LaunchInvite invite, ulong channelId, ulong messageId)
    {
        // A message that cannot be named is one no pass could ever find again. Keeping it would
        // put a row in the file that nothing can act on and nothing can clear.
        if (channelId == 0 || messageId == 0) return;

        lock (_gate)
        {
            var cards = Loaded();
            cards.Add(new LaunchCard
            {
                ChannelId = channelId,
                MessageId = messageId,
                SessionId = invite.SessionId,
                TitleId = invite.TitleId,
                InviteCode = invite.Code
            });

            Save(cards);
        }
    }

    /// <summary>Drops a card that has been dealt with, or that there is no longer anything to deal with.</summary>
    public void Forget(LaunchCard card)
    {
        lock (_gate)
        {
            var cards = Loaded();
            if (cards.RemoveAll(c => c.ChannelId == card.ChannelId && c.MessageId == card.MessageId) == 0) return;
            Save(cards);
        }
    }

    /// <summary>
    /// Read once, on first use. Never throws: a file that cannot be read is no cards, which
    /// leaves every card standing rather than editing one it cannot describe.
    /// </summary>
    private List<LaunchCard> Loaded()
    {
        if (_cards is not null) return _cards;

        try
        {
            _cards = File.Exists(Path)
                ? JsonSerializer.Deserialize(File.ReadAllText(Path), LaunchCardsJson.Default.ListLaunchCard) ?? []
                : [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The launch cards at {Path} could not be read; starting with none.", Path);
            _cards = [];
        }

        return _cards;
    }

    private void Save(List<LaunchCard> cards)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);

            var temporary = Path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(cards, LaunchCardsJson.Default.ListLaunchCard));
            File.Move(temporary, Path, overwrite: true);
        }
        catch (Exception ex)
        {
            // What is lost is the next restart, and with it every card left looking like a way
            // into a room that has closed.
            _logger.LogError(ex, "The launch cards at {Path} could not be written.", Path);
        }
    }
}
