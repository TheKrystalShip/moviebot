using TheKrystalShip.MovieBot.Client.Assistant;

namespace TheKrystalShip.MovieBot.Assistant;

/// <summary>
/// Who asked, and what about. Everything a tool acts on is this room, when there is one, and
/// everything it does is done as this person.
/// </summary>
/// <remarks>
/// <b>What a turn is about decides its tools and its conversation.</b> A turn from a voice channel
/// names its room: it can move the film, and its conversation is the room's, shared by everyone in it.
/// A turn from the site names none: it gets the library and the fetching tools, and its conversation
/// is the person's own.
/// </remarks>
public sealed record AssistantTurn(
    TurnRoom? Room,
    string SpeakerUserId,
    string SpeakerName,
    string ConversationId,
    bool Spoken = false)
{
    /// <summary>
    /// The access token the turn arrived with, which every call its tools make to the API carries, so
    /// the API decides what the person may do. Left out of the record's printed form.
    /// </summary>
    public string AccessToken { get; init; } = "";

    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append($"Room = {Room}, SpeakerUserId = {SpeakerUserId}, SpeakerName = {SpeakerName}, ");
        builder.Append($"ConversationId = {ConversationId}, Spoken = {Spoken}");
        return true;
    }

    /// <summary>The room's conversation, shared by everyone in it for the life of the room.</summary>
    public static string RoomConversation(string sessionId) => $"room:{sessionId}";

    /// <summary>The scope every conversation of one person on the site sits under.</summary>
    public static string PersonScope(string userId) => $"person:{userId}";

    /// <summary>One of a person's conversations, named by the id the client holds for it.</summary>
    public static string PersonConversation(string userId, string chatId) => $"{PersonScope(userId)}:{chatId}";

    public static AssistantTurn InRoom(TurnRoom room, string userId, string name, bool spoken = false) =>
        new(room, userId, name, RoomConversation(room.SessionId), spoken);

    public static AssistantTurn ForPerson(string userId, string name, string chatId) =>
        new(null, userId, name, PersonConversation(userId, chatId));

    public string RoomName => Room?.Name ?? "the site";
}
