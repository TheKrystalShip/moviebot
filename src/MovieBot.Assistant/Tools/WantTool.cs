using System.ComponentModel;
using System.Text;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant.Api;
using TheKrystalShip.MovieBot.Catalogue;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

[AssistantTool("want", ToolClass.Staged, Parallel = false, Order = 11)]
[Description("""
    Propose putting titles on the person's Want to watch, or taking them off. Nothing changes until the person confirms; say it is waiting for them.
    - titles: up to 8, each an id a tool returned or a title with its year.
    - remove: take them off instead.
    - just_me: keep the wants to the person. By default everybody signed in sees them, which is how friends find something to watch together.
    - Once confirmed, the person is told where each one stands: ready to watch, arriving, out, or not out yet.
    - Not for watching at a time or with people: that is a plan, make_plan.
    """)]
internal sealed partial class WantTool(CinemaApi api) : AssistantTool<WantTool.Args, Caller>
{
    public sealed record Args(
        [property: Description("Ids a tool returned, or titles with their year.")] string[] Titles,
        [property: Description("Take them off instead. Default false.")] bool Remove = false,
        [property: Description("Nobody else sees the want. Default false.")] bool JustMe = false);

    private const int MostTitles = 8;

    protected override async ValueTask<ToolResult> RunAsync(Args args, ToolCall<Caller> call)
    {
        var person = api.As(call.Principal.AccessToken);
        CancellationToken ct = call.CancellationToken;
        string[] said = [.. args.Titles.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct()];
        if (said.Length == 0) throw new ToolRefusal("Name at least one title.");
        if (said.Length > MostTitles) throw new ToolRefusal($"Propose at most {MostTitles} titles at a time.");

        // Each title is its page, read before anything is proposed: one already as asked is said, not proposed again.
        var pages = new List<TitlePage>();
        foreach (string title in said)
        {
            (string id, _) = await TitleLookup.ResolveAsync(person, title, "want", ct);
            TitlePage page = await person.TitleAsync(id, ct);
            if (pages.All(p => p.Id != page.Id)) pages.Add(page);
        }

        TitlePage[] change = [.. pages.Where(p => (p.You?.Wanted == true) == args.Remove)];
        TitlePage[] already = [.. pages.Except(change)];
        string Names(IEnumerable<TitlePage> titles) => string.Join(", ", titles.Select(t => TitleText.Named(t.Title, t.Year)));

        var note = new StringBuilder();
        if (already.Length > 0)
            note.Append(args.Remove ? "Not on the person's Want to watch: " : "Already on the person's Want to watch: ")
                .Append(Names(already)).Append(".\n");
        TitlesCard? cards = TitleCards.Of(pages.Select(p => TitleCards.Of(p)));
        if (change.Length == 0)
            return ToolResult.Ok(note.Append("Nothing was proposed.").ToString(), cards);

        string count = change.Length == 1 ? Names(change) : $"{change.Length} titles";
        string summary = args.Remove
            ? $"Take {count} off your Want to watch" + (change.Length > 1 ? $"\n{Names(change)}" : "")
            : $"Put {count} on your Want to watch" + (change.Length > 1 ? $"\n{Names(change)}" : "")
              + (args.JustMe ? "\nKept to you." : "\nEverybody signed in sees them.");
        bool visible = !args.JustMe;

        ToolResult proposed = Proposals.Propose(call, api, args.Remove ? "unwant" : "want", Names(change), summary,
            async (confirmed, _, token) =>
            {
                var told = new List<string>();
                int failed = 0;
                foreach (TitlePage page in change)
                {
                    string name = TitleText.Named(page.Title, page.Year);
                    try
                    {
                        told.Add(args.Remove
                            ? await confirmed.RemoveWantAsync(page.Id, token)
                                ? $"{name} is off your Want to watch."
                                : $"{name} was not on your Want to watch."
                            : Outcome(name, await confirmed.SetWantAsync(page.Id, page.Kind == "show" ? "show" : "film", visible, token)));
                    }
                    catch (ToolRefusal refused)
                    {
                        failed++;
                        told.Add($"{name}: {refused.Message}");
                    }
                }

                string text = string.Join('\n', told);
                return failed == change.Length ? StagedResult.Fail(text) : StagedResult.Ok(text);
            });
        return proposed with { Text = note + proposed.Text, Card = cards };
    }

    /// <summary>Where a title stands once it is wanted, as the API found it in the library, the torrent client and the tracker.</summary>
    private static string Outcome(string name, SetWantReply reply) => reply.Outcome switch
    {
        WantOutcomes.InLibrary => $"{name} is on your Want to watch, and ready to watch.",
        WantOutcomes.Downloading => $"{name} is on your Want to watch, and arriving.",
        WantOutcomes.Available => $"{name} is on your Want to watch. It is out, not downloaded yet.",
        WantOutcomes.Waiting => $"{name} is on your Want to watch. It is not out yet, and you will be told when it is.",
        WantOutcomes.AlreadyWanted => $"{name} was already on your Want to watch.",
        _ => $"{name} is on your Want to watch.",
    };
}
