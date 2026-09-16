using System.Text.RegularExpressions;
using TheKrystalShip.Agent.Replies;

namespace TheKrystalShip.MovieBot.Bot.Assistant;

/// <summary>
/// A reply that asks the room for an id the tools find by themselves.
/// </summary>
/// <remarks>
/// <para>
/// Nobody in a voice channel has an IMDb or torrent id to give, and every id the tools take comes out
/// of another tool: <c>search_catalogue</c> names a film by what it is called, <c>search_tracker</c>
/// finds its releases. A reply asking for one is the model stopping at a step it could have taken.
/// </para>
/// <para>
/// <b>The room's history is what produces it, so the instructions cannot prevent it.</b> Measured on
/// hotbox against a room's real conversation: once a turn had answered "I need the IMDb ID" without
/// calling anything, the same request in that conversation got the same sentence back, every time,
/// while in a fresh conversation it searched and proposed the film. Re-asking the turn with what it
/// missed is what gets past an example the model is copying.
/// </para>
/// </remarks>
public static partial class RoomIdRequest
{
    public const string Name = "asks-for-an-id";

    public static ReplyCheck Check(string said) => new(Name, reply =>
        !AsksForAnId().IsMatch(reply)
            ? null
            : new ReplyFault(
                "Your last reply asked for an id. Nobody in the room has one, and the tools find every id "
                + "from a film's name: search_catalogue takes the name and gives the film's imdb id and year, "
                + "and search_tracker takes the name or that id and gives the torrent ids. "
                + $"Answer the request \"{Excerpt(said)}\" again, replying with the tool call itself and no prose.",
                "",
                "\n\nNobody needs to find an id for this: saying the film's name is enough."));

    private static string Excerpt(string said) => said.Length <= 200 ? said : said[..200];

    [GeneratedRegex(@"\b(?:imdb|torrent)\b[^.?!\n]{0,20}\bids?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AsksForAnId();
}
