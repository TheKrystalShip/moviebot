using System.Collections.Concurrent;
using System.Globalization;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.Discord.Voice;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Bot.Download;
using TheKrystalShip.MovieBot.Bot.Keep;
using TheKrystalShip.MovieBot.Bot.Launch;
using TheKrystalShip.MovieBot.Bot.Notify;
using TheKrystalShip.MovieBot.Bot.Sessions;
using TheKrystalShip.MovieBot.Client.Assistant;

namespace TheKrystalShip.MovieBot.Bot.Assistant;

/// <summary>
/// What a spoken request the room verbs do not cover turns into, as the room sees it.
/// </summary>
public interface IRoomAssistant
{
    /// <summary>Whether anything answers such a request on this host.</summary>
    bool IsEnabled { get; }

    /// <summary>Starts answering a request and returns without waiting for the answer.</summary>
    void Answer(VoiceCommand command);

    /// <summary>Reads a spoken reply to a proposal: yes carries it out, no drops it, anything else leaves it to the buttons.</summary>
    Task DecideAsync(VoiceCommand command, VoiceWaiting waiting, CancellationToken ct);

    /// <summary>Writes a room verb the gate carried out into the room's conversation.</summary>
    void RecordAct(VoiceCommand command, string tool, IReadOnlyDictionary<string, string?> arguments, string outcome);

    /// <summary>Answers a press of one of the buttons under a proposal.</summary>
    Task HandleButtonAsync(SocketMessageComponent component);
}

/// <summary>The host with no assistant: requests the gate cannot read are heard and answered by nothing.</summary>
public sealed class NoRoomAssistant : IRoomAssistant
{
    public bool IsEnabled => false;
    public void Answer(VoiceCommand command) { }
    public Task DecideAsync(VoiceCommand command, VoiceWaiting waiting, CancellationToken ct) => Task.CompletedTask;
    public void RecordAct(VoiceCommand command, string tool, IReadOnlyDictionary<string, string?> arguments, string outcome) { }
    public Task HandleButtonAsync(SocketMessageComponent component) => Task.CompletedTask;
}

/// <summary>
/// Puts what is said to moviebot-assistant as the person who said it, and answers in the voice
/// channel's own chat.
/// </summary>
/// <remarks>
/// <para>
/// <b>The bot is a client, with no engine of its own.</b> The turn, the tools, the conversation and
/// the proposals are the assistant's. What stays here is Discord: what was heard, the launch card, the
/// buttons, the progress message and the announcement. An assistant that cannot be reached is said so
/// in the channel, and the room verbs, the slash commands and the announcements go on without it.
/// </para>
/// <para>
/// <b>Written, never spoken.</b> The film's sound plays in every viewer's browser, so anything the bot
/// said would be said over the film for everybody. A message is read by whoever cares and ignored by
/// whoever does not.
/// </para>
/// <para>
/// <b>What was heard goes first.</b> A recogniser mishears, and an answer about the wrong film is
/// inexplicable unless the room can see what the bot thought it was asked.
/// </para>
/// <para>
/// <b>A proposal is a message with two buttons, and a spoken yes is the same answer.</b> Both carry the
/// assistant's token, which redeems once, so whichever comes first carries the act out and the other
/// finds it spent. Whoever agrees is who the bot speaks for, and the act is done as them, with their
/// token. The prompt is edited to say who agreed, or that it was dropped, so a button that does
/// nothing any more says why where it stands.
/// </para>
/// <para>
/// <b>Answering does not hold up the room verbs.</b> A turn takes a second or more, and every spoken
/// command goes through one queue; "pause" said while the assistant is working on something else would
/// otherwise wait for it.
/// </para>
/// </remarks>
public sealed class RoomAssistantChat(
    DiscordSocketClient discord,
    AssistantClient assistant,
    MovieBotApiClient api,
    ILaunchPresenter presenter,
    IFilmRetention retention,
    LaunchCards cards,
    DownloadCommand downloads,
    VoiceAttention attention,
    IVoiceChimes chimes,
    IOptions<AssistantOptions> options,
    TimeProvider clock,
    ILogger<RoomAssistantChat> logger) : IRoomAssistant
{
    public const string ConfirmPrefix = "assistant:confirm:";
    public const string DropPrefix = "assistant:drop:";

    public const string Unreachable =
        "I couldn't reach the assistant just now, so nothing happened. The room verbs still work.";

    private const int MessageLimit = 2000;

    /// <summary>The prompts this process posted, by token, so a spoken answer closes the right message.</summary>
    private readonly ConcurrentDictionary<string, Prompt> _prompts = new();

    private sealed record Prompt(ulong ChannelId, ulong MessageId, string Describes, string AskedBy);

    public bool IsEnabled => true;

    public void Answer(VoiceCommand command)
    {
        _ = Task.Run(async () =>
        {
            // Everything the answer asks of the API is asked as whoever spoke.
            using var acting = ActingFor.Person(command.SpeakerId);
            try
            {
                await AnswerAsync(command, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Assistant: failed to answer {Speaker}", command.SpeakerName);
            }
        });
    }

    private async Task AnswerAsync(VoiceCommand command, CancellationToken ct)
    {
        if (discord.GetChannel(command.ChannelId) is not IMessageChannel chat)
        {
            logger.LogWarning("Assistant: channel {Channel} has no chat to answer {Speaker} in", command.ChannelId, command.SpeakerName);
            return;
        }

        await chat.SendMessageAsync(Heard(command), allowedMentions: AllowedMentions.None);

        TurnReply reply;
        try
        {
            using (chat.EnterTypingState())
                reply = await assistant.TurnAsync(RoomOf(command), SpeakerOf(command), command.Text, ct);
        }
        catch (AssistantUnavailableException ex)
        {
            logger.LogWarning(ex, "Assistant: {Speaker}'s request went unanswered", command.SpeakerName);
            await chat.SendMessageAsync(Unreachable, allowedMentions: AllowedMentions.None);
            return;
        }

        foreach (var launch in reply.Launches)
            await PostLaunchAsync(chat, command, launch, ct);

        if (!string.IsNullOrWhiteSpace(reply.Text))
            await chat.SendMessageAsync(Fit(reply.Text), allowedMentions: AllowedMentions.None);

        foreach (var proposal in reply.Proposals)
        {
            var describes = proposal.Describes ?? proposal.Subject.Id;
            var asker = proposal.AskedBy ?? command.SpeakerName;
            var prompt = await chat.SendMessageAsync(
                Fit($"{asker} asked me to **{describes}**. Go ahead?"),
                components: Buttons(proposal.Token),
                allowedMentions: AllowedMentions.None);
            _prompts[proposal.Token] = new Prompt(command.ChannelId, prompt.Id, describes, asker);
        }

        if (reply.Proposals.Count > 0 && options.Value.ConfirmWindowSeconds > 0)
        {
            attention.Expect(command.SpeakerId, command.ChannelId, new VoiceWaiting(
                VoiceWaitingFor.Confirmation,
                clock.GetUtcNow() + TimeSpan.FromSeconds(options.Value.ConfirmWindowSeconds),
                [.. reply.Proposals.Select(p => p.Token)],
                string.Join(" and ", reply.Proposals.Select(p => p.Describes ?? p.Subject.Id)),
                reply.Proposals[0].Verb));

            await chimes.PlayAsync(command.GuildId, VoiceChime.Listening, ct);
        }
    }

    /// <summary>
    /// The card for a film the assistant put on in the room: the same card <c>/watch</c> posts, worded
    /// by what happened to the room. The room was changed by the assistant before the frame arrived.
    /// </summary>
    private async Task PostLaunchAsync(IMessageChannel chat, VoiceCommand command, LaunchFrame launch, CancellationToken ct)
    {
        try
        {
            var library = await api.ListTitlesAsync(ct);
            if (library.FirstOrDefault(t => t.Id == launch.TitleId) is not { } title)
            {
                logger.LogWarning("Assistant: {Title} was put on and is not in the library", launch.TitleId);
                return;
            }

            var card = await presenter.PresentAsync(new LaunchRequest
            {
                SessionId = launch.SessionId ?? RoomSession.IdFor(command.ChannelId),
                Title = title,
                VoiceChannelId = command.ChannelId,
                VoiceChannelName = ChannelName(command),
                RequestedBy = launch.RequestedBy,
                Replaced = launch.Replaced is { } replaced ? library.FirstOrDefault(t => t.Name == replaced) : null,
                AlreadyWatching = launch.AlreadyWatching,
                OnDisk = await retention.NoticeForAsync(title.Id, ct),
            }, ct);

            var posted = await chat.SendMessageAsync(
                card.Text, embed: card.Embed, components: card.Components, allowedMentions: AllowedMentions.None);
            if (card.Invite is { } invite) cards.Add(invite, command.ChannelId, posted.Id);
        }
        catch (MovieBotApiException ex)
        {
            logger.LogWarning(ex, "Assistant: the card for {Title} could not be made", launch.TitleId);
        }
    }

    public async Task DecideAsync(VoiceCommand command, VoiceWaiting waiting, CancellationToken ct)
    {
        if (discord.GetChannel(command.ChannelId) is not IMessageChannel chat) return;

        var intent = SpokenIntents.Read(command.Text);
        logger.LogInformation("Assistant: read {Speaker}'s reply to a proposal as {Intent}", command.SpeakerName, intent);

        if (intent == SpokenIntent.Unclear)
        {
            // Somebody who addressed the bot and asked something else has moved on rather than
            // failed to answer. The proposal stands under its buttons either way.
            if (command.Triggered && command.Text.Length > 0)
            {
                Answer(command);
                return;
            }

            // Anything else is the room talking, not an answer: the window takes the next thing its
            // speaker says, and in a room watching a film that is usually a remark about the film.
            // It is not asked again, because asking again opens another window that takes the next
            // remark too. The window is spent, and the buttons stay the way to agree.
            return;
        }

        await chat.SendMessageAsync(Heard(command), allowedMentions: AllowedMentions.None);

        var speaker = SpeakerOf(command);
        foreach (var token in waiting.Tokens ?? [])
        {
            var prompt = _prompts.GetValueOrDefault(token);
            if (intent == SpokenIntent.Affirm)
                await CarryOutAsync(chat, token, prompt?.MessageId, speaker, ct);
            else
                await DropAsync(chat, token, prompt?.MessageId, speaker);
        }
    }

    public void RecordAct(VoiceCommand command, string tool, IReadOnlyDictionary<string, string?> arguments, string outcome)
    {
        // Best effort, and never waited on: the film has already moved, and a record that could not be
        // written is not a reason to hold up the next verb.
        _ = Task.Run(async () =>
        {
            try
            {
                await assistant.RecordAsync(
                    new RecordRequest(RoomOf(command), command.Text, tool, arguments, outcome),
                    SpeakerOf(command), CancellationToken.None);
            }
            catch (AssistantUnavailableException ex)
            {
                logger.LogDebug(ex, "Assistant: {Speaker}'s {Tool} was not recorded", command.SpeakerName, tool);
            }
        });
    }

    public async Task HandleButtonAsync(SocketMessageComponent component)
    {
        var id = component.Data.CustomId;
        var confirming = id.StartsWith(ConfirmPrefix, StringComparison.Ordinal);
        var token = confirming ? id[ConfirmPrefix.Length..] : id[DropPrefix.Length..];
        var name = (component.User as IGuildUser)?.DisplayName ?? component.User.Username;

        // Acknowledged at once: carrying out a download takes longer than Discord waits for a press.
        await component.DeferAsync();

        var speaker = new Speaker(component.User.Id, name);
        if (confirming)
            await CarryOutAsync(component.Channel, token, component.Message.Id, speaker, CancellationToken.None);
        else
            await DropAsync(component.Channel, token, component.Message.Id, speaker);
    }

    private async Task CarryOutAsync(IMessageChannel chat, string token, ulong? promptId, Speaker agreed, CancellationToken ct)
    {
        _prompts.TryRemove(token, out var prompt);

        ConfirmReply? reply;
        try
        {
            reply = await assistant.ConfirmAsync(token, agreed, ct);
        }
        catch (AssistantUnavailableException ex)
        {
            logger.LogWarning(ex, "Assistant: {Approver}'s agreement went unanswered", agreed.DisplayName);
            await chat.SendMessageAsync(Unreachable, allowedMentions: AllowedMentions.None);
            return;
        }

        if (reply is null)
        {
            await chat.SendMessageAsync(
                $"That proposal has already been answered or has expired, {agreed.DisplayName}. Ask again if it is still wanted.",
                allowedMentions: AllowedMentions.None);
            return;
        }

        var describes = prompt?.Describes ?? "that";
        await ClosePromptAsync(chat, promptId, prompt, $"{agreed.DisplayName} agreed: {describes}.");
        logger.LogInformation("Assistant: {Approver} agreed to {What} ({Verdict})", agreed.DisplayName, describes, reply.Outcome.Verdict);

        await PostOutcomeAsync(chat, reply, agreed, ct);
    }

    /// <summary>
    /// What was agreed to, told the way the slash commands tell it: the same sentence, the same
    /// embed, and for a download the progress message that follows it and the mention of whoever
    /// agreed, who is the one it was started as.
    /// </summary>
    private async Task PostOutcomeAsync(IMessageChannel chat, ConfirmReply reply, Speaker agreed, CancellationToken ct)
    {
        var result = reply.Result;

        if (result?.Download is { } download)
        {
            var intoRoom = result.Kind == "download_film";
            var told = DownloadCommand.Describe(download, intoRoom);
            var embed = told.Release is { } release && told.Outcome == DownloadOutcome.Started
                ? DownloadEmbed.Starting(release, intoRoom ? (chat as IChannel)?.Name : null)
                : null;
            var posted = await chat.SendMessageAsync(Fit(told.Message), embed: embed, allowedMentions: AllowedMentions.None);

            if (told.Hash is { } hash && told.Outcome == DownloadOutcome.Started)
            {
                await downloads.RecordProgressMessageAsync(hash, chat.Id, posted.Id, ct);
                await downloads.RecordMentionAsync(hash, agreed.UserId, ct);
            }
            return;
        }

        if (result?.Wish is { } wish)
        {
            var told = NotifyCommand.Describe(wish);
            await chat.SendMessageAsync(Fit(told.Message), embed: WishEmbed.For(told), allowedMentions: AllowedMentions.None);
            return;
        }

        await chat.SendMessageAsync(Fit(reply.Text), allowedMentions: AllowedMentions.None);
    }

    /// <summary>
    /// Takes the buttons off a dropped proposal and says so. The token is left to expire: nothing
    /// spends it, and the buttons that could have are gone.
    /// </summary>
    private async Task DropAsync(IMessageChannel chat, string token, ulong? promptId, Speaker dropped)
    {
        _prompts.TryRemove(token, out var prompt);
        var describes = prompt?.Describes ?? "that";
        await ClosePromptAsync(chat, promptId, prompt, $"{dropped.DisplayName} dropped it: {describes}. Nothing happened.");
    }

    /// <summary>Takes the buttons off a prompt and says what became of it, where the prompt stands.</summary>
    private async Task ClosePromptAsync(IMessageChannel chat, ulong? messageId, Prompt? prompt, string closing)
    {
        if (messageId is not { } id) return;

        try
        {
            await chat.ModifyMessageAsync(id, message =>
            {
                message.Content = Fit(prompt is null ? closing : $"{prompt.AskedBy} asked me to {prompt.Describes}.\n{closing}");
                message.Components = new ComponentBuilder().Build();
                message.AllowedMentions = AllowedMentions.None;
            });
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Assistant: a prompt could not be closed");
        }
    }

    private static MessageComponent Buttons(string token) =>
        new ComponentBuilder()
            .WithButton("Go ahead", ConfirmPrefix + token, ButtonStyle.Success)
            .WithButton("Drop it", DropPrefix + token, ButtonStyle.Secondary)
            .Build();

    /// <summary>The room is the voice channel; announcements for it go to that channel's chat.</summary>
    private TurnRoom RoomOf(VoiceCommand command)
    {
        var id = command.ChannelId.ToString(CultureInfo.InvariantCulture);
        return new TurnRoom(RoomSession.IdFor(command.ChannelId), ChannelName(command), id);
    }

    private string ChannelName(VoiceCommand command) =>
        (discord.GetChannel(command.ChannelId) as IChannel)?.Name ?? "this room";

    private static Speaker SpeakerOf(VoiceCommand command) => new(command.SpeakerId, command.SpeakerName);

    private static string Heard(VoiceCommand command) => $"**{command.SpeakerName}:** {command.Text}";

    private static string Fit(string text) =>
        text.Length <= MessageLimit ? text : text[..(MessageLimit - 1)] + "…";
}
