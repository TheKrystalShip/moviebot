using System.ComponentModel;
using System.Text;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant.Api;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

[AssistantTool("anticipated", ToolClass.Read, ResultLimit = 8000, Order = 8)]
[Description("""
    The films that are not out yet and that people on Cinema are waiting for, most waiters first, with how many wait on each and whether the asking person is one of them.
    - Use for "what are people waiting for", "what is anticipated", "anything coming that people are excited about".
    - Not the release calendar: whats_new upcoming lists films by date.
    """)]
internal sealed partial class AnticipatedTool(CinemaApi api) : AssistantTool<AnticipatedTool.Args, Caller>
{
    public sealed record Args();

    protected override async ValueTask<ToolResult> RunAsync(Args args, ToolCall<Caller> call)
    {
        List<AnticipatedTitle> titles = await api.As(call.Principal.AccessToken).AnticipatedAsync(call.CancellationToken);
        if (titles.Count == 0)
            return ToolResult.Ok("Nobody is waiting on a film that is not out yet.");

        var text = new StringBuilder("Films people are waiting for, most waiters first:\n");
        foreach (AnticipatedTitle t in titles)
        {
            text.Append("- ").Append(TitleText.Named(t.Title, t.Year)).Append(", ").Append(TitleText.KindWord(t.Kind)).Append(". ")
                .Append(TitleText.Count(t.Waiters)).Append(" waiting.");
            if (t.Waiting) text.Append(" The person is one of them.");
            text.Append(t.ImdbId is { Length: > 0 } id ? $" id {id}.\n" : " No IMDb id: it has no page here.\n");
        }

        return ToolResult.Ok(text.ToString().TrimEnd(), TitleCards.Of(
            titles.Where(t => t.ImdbId is { Length: > 0 })
                .Select(t => new TitleCard(t.ImdbId, t.Kind, t.Title, t.Year, t.PosterUrl, false, false))));
    }
}
