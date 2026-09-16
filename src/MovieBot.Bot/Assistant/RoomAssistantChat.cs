using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.Discord.Voice;
using TheKrystalShip.MovieBot.Bot.Launch;

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

/// <summary>The host with no model: requests the gate cannot read are heard and answered by nothing.</summary>
public sealed class NoRoomAssistant : IRoomAssistant
{
    public bool IsEnabled => false;
    public void Answer(VoiceCommand command) { }
    public Task DecideAsync(VoiceCommand command, VoiceWaiting waiting, CancellationToken ct) => Task.CompletedTask;
    public void RecordAct(VoiceCommand command, string tool, IReadOnlyDictionary<string, string?> arguments, string outcome) { }
    public Task HandleButtonAsync(SocketMessageComponent component) => Task.CompletedTask;
}

/// <summary>
/// Answers in the voice channel's own chat.
/// </summary>
/// <remarks>
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
/// <b>A proposal is a message with two buttons, and a spoken yes is the same answer.</b> Both redeem
/// one token, so whichever comes first carries the act out and the other finds it spent. The prompt is
/// edited to say who agreed, or that it was dropped or expired, so a button that does nothing any more
/// says why where it stands.
/// </para>
/// <para>
/// <b>Answering does not hold up the room verbs.</b> A turn takes a second or more, and every spoken
/// command goes through one queue; "pause" said while the model is working on something else would
/// otherwise wait for it. Turns in one room still run one at a time, because they share a
/// conversation.
/// </para>
/// </remarks>
public sealed class RoomAssistantChat(
    DiscordSocketClient discord,
    RoomAssistant assistant,
    StagedActions staged,
    LaunchCards cards,
    VoiceAttention attention,
    IVoiceChimes chimes,
    IOptions<AssistantOptions> options,
    TimeProvider clock,
    ILogger<RoomAssistantChat> logger) : IRoomAssistant
{
    public const string ConfirmPrefix = "assistant:confirm:";
    public const string DropPrefix = "assistant:drop:";

    private const int MessageLimit = 2000;

    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _rooms = new();

    public bool IsEnabled => true;

    public void Answer(VoiceCommand command)
    {
        _ = Task.Run(async () =>
        {
            var room = _rooms.GetOrAdd(command.ChannelId, _ => new SemaphoreSlim(1, 1));
            await room.WaitAsync();
            try
            {
                await AnswerAsync(command, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Assistant: failed to answer {Speaker}", command.SpeakerName);
            }
            finally
            {
                room.Release();
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

        var turn = TurnFor(command);
        RoomAnswer answer;
        using (chat.EnterTypingState())
            answer = await assistant.AskAsync(turn, command.Text, ct);

        foreach (var launch in answer.Launched)
        {
            var posted = await chat.SendMessageAsync(
                launch.Text, embed: launch.Embed, components: launch.Components, allowedMentions: AllowedMentions.None);
            if (launch.Invite is { } invite) cards.Add(invite, command.ChannelId, posted.Id);
        }

        if (!string.IsNullOrWhiteSpace(answer.Text))
            await chat.SendMessageAsync(Fit(answer.Text), allowedMentions: AllowedMentions.None);

        foreach (var proposal in answer.Proposed)
        {
            var prompt = await chat.SendMessageAsync(
                Fit($"{proposal.AskedByName} asked me to **{proposal.Describes}**. Go ahead?"),
                components: Buttons(proposal.Token),
                allowedMentions: AllowedMentions.None);
            proposal.PromptMessageId = prompt.Id;
        }

        if (answer.Proposed.Count > 0 && options.Value.ConfirmWindowSeconds > 0)
        {
            attention.Expect(command.SpeakerId, command.ChannelId, new VoiceWaiting(
                VoiceWaitingFor.Confirmation,
                clock.GetUtcNow() + TimeSpan.FromSeconds(options.Value.ConfirmWindowSeconds),
                [.. answer.Proposed.Select(p => p.Token)],
                string.Join(" and ", answer.Proposed.Select(p => p.Describes)),
                answer.Proposed[0].Kind));

            await chimes.PlayAsync(command.GuildId, VoiceChime.Listening, ct);
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

        var approval = new Approval(command.SpeakerId, command.SpeakerName);
        foreach (var token in waiting.Tokens ?? [])
        {
            if (intent == SpokenIntent.Affirm)
                await CarryOutAsync(chat, token, approval, ct);
            else
                await DropAsync(chat, token, approval);
        }
    }

    public void RecordAct(
        VoiceCommand command, string tool, IReadOnlyDictionary<string, string?> arguments, string outcome) =>
        assistant.RecordAct(TurnFor(command), command.Text, tool, arguments, outcome);

    public async Task HandleButtonAsync(SocketMessageComponent component)
    {
        var id = component.Data.CustomId;
        var confirming = id.StartsWith(ConfirmPrefix, StringComparison.Ordinal);
        var token = confirming ? id[ConfirmPrefix.Length..] : id[DropPrefix.Length..];
        var name = (component.User as IGuildUser)?.DisplayName ?? component.User.Username;

        // Acknowledged at once: carrying out a download takes longer than Discord waits for a press.
        await component.DeferAsync();

        var approval = new Approval(component.User.Id, name);
        if (confirming)
            await CarryOutAsync(component.Channel, token, approval, CancellationToken.None);
        else
            await DropAsync(component.Channel, token, approval);
    }

    private async Task CarryOutAsync(IMessageChannel chat, string token, Approval approval, CancellationToken ct)
    {
        if (staged.Take(token) is not { } proposal)
        {
            await chat.SendMessageAsync(
                $"That proposal has already been answered or has expired, {approval.DisplayName}. Ask again if it is still wanted.",
                allowedMentions: AllowedMentions.None);
            return;
        }

        await ClosePromptAsync(chat, proposal, $"{approval.DisplayName} agreed: {proposal.Describes}.");
        logger.LogInformation(
            "Assistant: {Approver} agreed to {What}, asked for by {Asker}",
            approval.DisplayName, proposal.Describes, proposal.AskedByName);

        StagedOutcome outcome;
        try
        {
            outcome = await proposal.Run(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Assistant: carrying out {What} failed", proposal.Describes);
            outcome = new StagedOutcome($"That did not work: {ex.Message}");
        }

        var posted = await chat.SendMessageAsync(
            Fit(outcome.Message), embed: outcome.Embed, allowedMentions: AllowedMentions.None);

        if (outcome.AfterPosted is { } after)
        {
            try { await after(posted); }
            catch (Exception ex) { logger.LogWarning(ex, "Assistant: follow-up to {What} failed", proposal.Describes); }
        }
    }

    private async Task DropAsync(IMessageChannel chat, string token, Approval approval)
    {
        if (staged.Take(token) is { } proposal)
            await ClosePromptAsync(chat, proposal, $"{approval.DisplayName} dropped it: {proposal.Describes}. Nothing happened.");
    }

    /// <summary>Takes the buttons off a prompt and says what became of it, where the prompt stands.</summary>
    private async Task ClosePromptAsync(IMessageChannel chat, StagedAction proposal, string closing)
    {
        if (proposal.PromptMessageId is not { } messageId) return;

        try
        {
            await chat.ModifyMessageAsync(messageId, message =>
            {
                message.Content = Fit($"{proposal.AskedByName} asked me to {proposal.Describes}.\n{closing}");
                message.Components = new ComponentBuilder().Build();
                message.AllowedMentions = AllowedMentions.None;
            });
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Assistant: the prompt for {What} could not be closed", proposal.Describes);
        }
    }

    private static MessageComponent Buttons(string token) =>
        new ComponentBuilder()
            .WithButton("Go ahead", ConfirmPrefix + token, ButtonStyle.Success)
            .WithButton("Drop it", DropPrefix + token, ButtonStyle.Secondary)
            .Build();

    private RoomTurn TurnFor(VoiceCommand command) =>
        new(command.GuildId, command.ChannelId,
            (discord.GetChannel(command.ChannelId) as IChannel)?.Name ?? "this room",
            command.SpeakerId, command.SpeakerName);

    private static string Heard(VoiceCommand command) => $"**{command.SpeakerName}:** {command.Text}";

    private static string Fit(string text) =>
        text.Length <= MessageLimit ? text : text[..(MessageLimit - 1)] + "…";
}
