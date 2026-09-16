using System.Text.RegularExpressions;
using TheKrystalShip.Agent.Replies;

namespace TheKrystalShip.MovieBot.Bot.Assistant;

/// <summary>
/// A reply announcing a film going on, on a turn that put nothing on.
/// </summary>
/// <remarks>
/// <para>
/// "Loading Pirates of the Caribbean: The Curse of the Black Pearl..." has no subject, so the
/// first-person claim check reads past it, and the room waits for a film nothing is loading. Measured on
/// hotbox against a room's real conversation: after <c>load_title</c> answered that several films
/// matched, "the first one" came back as that sentence with no second call, every time.
/// </para>
/// <para>
/// Only a turn that moved no room, launched nothing and proposed nothing is asked, so "Playing from
/// 0:18." after a real play passes.
/// </para>
/// </remarks>
public static partial class RoomLoadingClaim
{
    public const string Name = "announces-a-film-nothing-loaded";

    public static ReplyCheck Check(string said, Func<bool> acted) => new(Name, reply =>
        acted() || !AnnouncesLoading().IsMatch(reply)
            ? null
            : new ReplyFault(
                "Your last reply said a film is going on, but nothing was put on: load_title did not load "
                + "anything this turn. Call load_title with the film's name written exactly as the library "
                + $"lists it. Answer the request \"{Excerpt(said)}\" again, replying with the tool call itself "
                + "and no prose.",
                "",
                "\n\n**Correction:** nothing was put on. Ask again with the film's name."));

    private static string Excerpt(string said) => said.Length <= 200 ? said : said[..200];

    [GeneratedRegex(@"^\W*(?:now\s+)?(?:loading|putting on|switching to|starting|playing)\b(?!\s+from\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AnnouncesLoading();
}
