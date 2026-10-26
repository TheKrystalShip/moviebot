using System.ComponentModel;
using System.Text;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant.Api;
using TheKrystalShip.MovieBot.Catalogue;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

[AssistantTool("title_details", ToolClass.Read, ResultLimit = 8000, Order = 2)]
[Description("""
    Everything the film database knows about one film or show.
    - Year and release date, runtime, genres, rating and vote count, plot, director and writers, main cast with their parts, original language, keywords, and for a show its seasons.
    - Whether the library has it, whether the person has watched it or wants it, and who else wants it.
    - Use to confirm a candidate against what the person remembers before naming it.
    """)]
internal sealed partial class TitleDetailsTool(CinemaApi api) : AssistantTool<TitleDetailsTool.Args, Caller>
{
    public sealed record Args(
        [property: Description("The title with its year, or an id a tool returned.")] string Title);

    private const int CastShown = 10;

    /// <summary>The jobs a page's crew holds that are its writing.</summary>
    private static readonly HashSet<string> WritingJobs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Writer", "Screenplay", "Story", "Teleplay", "Novel", "Author", "Original Story", "Book",
    };

    protected override async ValueTask<ToolResult> RunAsync(Args args, ToolCall<Caller> call)
    {
        var person = api.As(call.Principal.AccessToken);
        CancellationToken ct = call.CancellationToken;

        (string imdbId, string? note) = await TitleLookup.ResolveAsync(person, args.Title, "title_details", ct);
        TitlePage page = await person.TitleAsync(imdbId, ct);

        // Who else wants it is said only when it could be read: a part the API declines leaves it unsaid.
        List<WanterView>? wanters;
        try
        {
            wanters = await person.WantersAsync(imdbId, ct);
        }
        catch (ToolRefusal)
        {
            wanters = null;
        }

        if (page.Kind != "show")
            return Film(page, wanters, note);

        // What the person has of the show is theirs: a part the API declines leaves it unsaid, never denied.
        PersonShowView? theirs;
        try
        {
            theirs = await person.PersonShowAsync(imdbId, ct);
        }
        catch (ToolRefusal)
        {
            theirs = null;
        }

        return Show(page, theirs, wanters, note);
    }

    private static ToolResult Film(TitlePage f, List<WanterView>? wanters, string? note)
    {
        var text = new StringBuilder();
        if (note is not null) text.Append(note).Append('\n');
        text.Append(TitleText.Named(f.Title, f.Year)).Append(", ").Append(TitleText.KindWord("film", f.Category)).Append(". id ").Append(f.Id).Append(".\n");
        if (f.OriginalTitle is { Length: > 0 } original && original != f.Title)
            text.Append("Original title: ").Append(original).Append(".\n");
        Line(text, "Release date", f.Released);
        if (f.Runtime is { } runtime) text.Append("Runtime: ").Append(runtime).Append(" minutes.\n");
        Line(text, "Certificates", Certificates(f.Certificates));
        Line(text, "Genres", Join(f.Genres));
        if (TitleText.Rating(f.Rating) is { Length: > 0 } rated) text.Append(rated).Append('\n');
        Line(text, "Tagline", f.Tagline);
        Line(text, "Plot", f.Overview);
        Line(text, "Directed by", Join(Crew(f, j => j == "Director")));
        Line(text, "Written by", Join(Crew(f, WritingJobs.Contains)));
        Cast(text, f.Cast);
        Common(text, f);
        text.Append(TitleText.Library(f.InLibrary, f.Watched, "film"));
        Wanted(text, f, wanters);

        return ToolResult.Ok(text.ToString(), TitleCards.Of([f]));
    }

    private static ToolResult Show(TitlePage s, PersonShowView? theirs, List<WanterView>? wanters, string? note)
    {
        var text = new StringBuilder();
        if (note is not null) text.Append(note).Append('\n');
        text.Append(TitleText.Named(s.Title, s.Year)).Append(", ").Append(TitleText.KindWord("show", s.Category)).Append(". id ").Append(s.Id).Append(".\n");
        if (s.OriginalTitle is { Length: > 0 } original && original != s.Title)
            text.Append("Original title: ").Append(original).Append(".\n");
        if (s.Year is { } first)
            text.Append("Ran from ").Append(first).Append(s.EndYear is { } last && last != first ? $" to {last}" : "").Append(".\n");
        Line(text, "Status", s.Status);
        Line(text, "Certificates", Certificates(s.Certificates));
        Line(text, "Genres", Join(s.Genres));
        if (TitleText.Rating(s.Rating) is { Length: > 0 } rated) text.Append(rated).Append('\n');
        Line(text, "Tagline", s.Tagline);
        Line(text, "Plot", s.Overview);
        Line(text, "Created by", Join(Crew(s, j => j == "Creator")));
        Cast(text, s.Cast);
        Line(text, "Networks", Join([.. s.Networks.Select(n => n.Name)]));
        Common(text, s);

        foreach (SeasonSummary season in s.Seasons)
        {
            text.Append("Season ").Append(season.Number).Append(": ").Append(season.Episodes).Append(" episodes");
            if (season.AirDate is { Length: > 0 } aired) text.Append(", first aired ").Append(aired);
            text.Append(".\n");
        }

        if (s.NextEpisode is { } next)
            text.Append($"The next episode is season {next.Season} episode {next.Number}")
                .Append(next.Name is { Length: > 0 } name ? $" ({name})" : "").Append($", {next.AirDate}.\n");
        if (theirs?.Follow is { } follow)
            text.Append("The person follows it, since ").Append(follow.Since.ToString("yyyy-MM-dd")).Append(".\n");
        if (theirs?.Resume is { } resume)
            text.Append($"The person picks up at season {resume.Season} episode {resume.Episode}.\n");

        text.Append(TitleText.Library(s.InLibrary, false, "show"));
        Wanted(text, s, wanters);

        return ToolResult.Ok(text.ToString(), TitleCards.Of([s with { You = s.You is { } you ? you with { Watched = false } : null }]));
    }

    /// <summary>Whether the person wants the title, and who else visibly does.</summary>
    private static void Wanted(StringBuilder text, TitlePage page, List<WanterView>? wanters)
    {
        if (page.You?.Wanted == true) text.Append(" The person wants to watch it.");
        if (wanters is null) return;
        text.Append(wanters.Count == 0
            ? " Nobody else wants to watch it."
            : " Also wanted by " + string.Join(", ", wanters.Select(w => w.Name ?? "somebody with no name")) + ".");
    }

    /// <summary>What a film's and a show's pages both hold, after the people.</summary>
    private static void Common(StringBuilder text, TitlePage page)
    {
        Line(text, "Original language", page.Language);
        Line(text, "Countries", Join(page.Countries));
        Line(text, "Set in", Join([.. page.Facts.SetIn.Select(p => p.Name)]));
        Line(text, "Based on", Join([.. page.Facts.BasedOn.Select(b =>
            b.Authors.Count > 0 ? $"{b.Work.Name} by {string.Join(" and ", b.Authors.Select(a => a.Name))}" : b.Work.Name)]));
        Line(text, "Franchises", Join([.. page.Franchises.Select(f => f.Ordinal is { Length: > 0 } ordinal ? $"{f.Name} (part {ordinal})" : f.Name)]));
        Line(text, "Keywords", Join([.. page.Keywords.Select(k => k.Name)]));
    }

    private static List<string> Crew(TitlePage page, Func<string, bool> job) =>
        [.. page.Crew.Where(c => job(c.Job)).Select(c => c.Name).Distinct()];

    private static string? Certificates(Dictionary<string, string> certificates) =>
        certificates.Count == 0 ? null
            : string.Join(", ", certificates.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => $"{c.Value} ({c.Key})"));

    private static void Cast(StringBuilder text, List<CastMember> cast)
    {
        if (cast.Count == 0) return;
        text.Append("Main cast: ")
            .Append(string.Join(", ", cast.Take(CastShown).Select(c =>
                c.Character is { Length: > 0 } part ? $"{c.Name} ({part})" : c.Name)))
            .Append(".\n");
    }

    private static string? Join(List<string> values) => values.Count == 0 ? null : string.Join(", ", values);

    private static void Line(StringBuilder text, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            text.Append(label).Append(": ").Append(value).Append(value.EndsWith('.') ? "" : ".").Append('\n');
    }
}
