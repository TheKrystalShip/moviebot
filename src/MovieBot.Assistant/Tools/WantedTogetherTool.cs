using System.ComponentModel;
using System.Text;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant.Api;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

[AssistantTool("wanted_together", ToolClass.Read, ResultLimit = 8000, Order = 9)]
[Description("""
    The titles the asking person wants to watch that other people want too, with who, the ones ready to watch first.
    - For a show, only the people at the same next episode as the person are listed.
    - Use for "what could I watch with someone", "does anyone want what I want", and to choose a title for a watch party.
    - Leaves out films the person has watched and wants anybody keeps to themselves.
    """)]
internal sealed partial class WantedTogetherTool(CinemaApi api) : AssistantTool<WantedTogetherTool.Args, Caller>
{
    public sealed record Args();

    protected override async ValueTask<ToolResult> RunAsync(Args args, ToolCall<Caller> call)
    {
        List<TogetherView> shared = await api.As(call.Principal.AccessToken).TogetherAsync(call.CancellationToken);
        if (shared.Count == 0)
            return ToolResult.Ok("Nobody else visibly wants any title the person wants. A want everybody can see is how others find something to watch with them.");

        var text = new StringBuilder("Titles the person wants that others want too, ready ones first:\n");
        foreach (TogetherView t in shared)
        {
            WantView w = t.Want;
            text.Append("- ").Append(TitleText.Named(w.Title, w.Card?.Year)).Append(", ").Append(TitleText.KindWord(w.Kind)).Append('.')
                .Append(TitleText.WantState(w.State, w.Progress));
            if (w.NextEpisode is { } next) text.Append($" Everybody listed is at season {next.Season} episode {next.Episode}.");
            text.Append(" Also wanted by ").Append(string.Join(", ", t.With.Select(p => p.Name ?? "somebody with no name"))).Append('.')
                .Append($" id {w.ImdbId}.\n");
        }

        return ToolResult.Ok(text.ToString().TrimEnd(), TitleCards.Of(shared.Select(t =>
            new CardTitle(t.Want.ImdbId, t.Want.Kind, t.Want.Title, t.Want.Card?.Year, t.Want.Card?.Poster, t.Want.InLibrary, t.Want.Watched))));
    }
}
