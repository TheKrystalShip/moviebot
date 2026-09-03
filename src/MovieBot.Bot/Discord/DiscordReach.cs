using System.Net;
using Discord.Net;

namespace TheKrystalShip.MovieBot.Bot.Discord;

/// <summary>
/// Whether a refusal from Discord is one that another pass will never get past.
///
/// A sweep retries because most failures are a moment's trouble — a timeout, a 500, a gateway
/// still connecting — and the next pass is the cheapest answer to all of them. Being told the
/// bot cannot see a channel, or that a message is not there, is a different kind of answer: it
/// is the same one every ten seconds until somebody changes the server, and each attempt spends
/// the channel's rate allowance to hear it again.
/// </summary>
internal static class DiscordReach
{
    /// <summary>
    /// Refused over permission, or about something that is gone. Matched on the status rather
    /// than the error code behind it, because every refusal in these two families is settled
    /// somewhere other than in a retry.
    /// </summary>
    public static bool IsOutOfReach(HttpException ex) =>
        ex.HttpCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound;

    /// <summary>Discord's own reason, for a log line that names what was refused.</summary>
    public static string Explain(HttpException ex) =>
        ex.Reason is { Length: > 0 } reason ? $"{ex.DiscordCode?.ToString() ?? "?"}: {reason}" : ex.Message;
}
