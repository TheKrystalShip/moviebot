using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant.Api;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

[AssistantTool("title_details", ToolClass.Read, ResultLimit = 8000, Order = 2)]
[Description("""
    Everything the film database knows about one film or show.
    - Year and release date, runtime, genres, rating and vote count, plot, director and writers, main cast with their parts, original language, keywords, and for a show its seasons.
    - Whether the library has it, and whether the person has watched it.
    - Use to confirm a candidate against what the person remembers before naming it.
    """)]
internal sealed partial class TitleDetailsTool(CinemaApi api) : AssistantTool<TitleDetailsTool.Args, Caller>
{
    public sealed record Args(
        [property: Description("The title with its year, or an id a tool returned.")] string Title);

    private const int CastShown = 10;
    private const int CandidatesShown = 6;

    [GeneratedRegex(@"^tt\d{5,}$", RegexOptions.IgnoreCase)]
    private static partial Regex ImdbId();

    [GeneratedRegex(@"^(?<name>.*?)\s*\((?<year>\d{4})\)\s*$")]
    private static partial Regex NameWithYear();

    protected override async ValueTask<ToolResult> RunAsync(Args args, ToolCall<Caller> call)
    {
        var person = api.As(call.Principal.AccessToken);
        CancellationToken ct = call.CancellationToken;

        (string imdbId, string? kind, string? note) = await ResolveAsync(person, args.Title, ct);

        if (kind != "show")
        {
            // An id the model was given with no kind may name a show.
            FilmDetails? film = kind is null
                ? await person.FilmOrNullAsync(imdbId, ct)
                : await person.FilmAsync(imdbId, ct);
            if (film is not null)
                return Film(film, note);
        }

        return Show(await person.ShowAsync(imdbId, ct), note);
    }

    /// <summary>
    /// The id a title is read by. A title written with its year, or without, is found through the
    /// search first; an id is used as it is. What cannot be told apart is returned to the model as the
    /// candidates, with their ids, rather than guessed at.
    /// </summary>
    private static async Task<(string Id, string? Kind, string? Note)> ResolveAsync(
        CinemaApi.PersonApi person, string said, CancellationToken ct)
    {
        if (ImdbId().IsMatch(said))
            return (said.ToLowerInvariant(), null, null);

        string name = said;
        int? year = null;
        if (NameWithYear().Match(said) is { Success: true } m)
        {
            name = m.Groups["name"].Value;
            year = int.Parse(m.Groups["year"].Value);
        }

        SearchResult found = await person.SearchAsync(name, null, year, ct);
        TitleHit[] pages = [.. found.Titles.Where(t => t.ImdbId is { Length: > 0 })];
        TitleHit[] named = [.. pages.Where(t => TitleText.Normal(t.Title) == TitleText.Normal(name))];
        TitleHit[] sameYear = year is null ? named : [.. named.Where(t => t.Year == year)];

        if (sameYear.Length == 1)
            return (sameYear[0].ImdbId!, sameYear[0].Kind, null);

        if (sameYear.Length == 0 && named.Length == 0 && pages.Length == 1)
            return (pages[0].ImdbId!, pages[0].Kind,
                $"No title is named exactly '{name}'; this is the database's one close match.");

        TitleHit[] candidates = sameYear.Length > 1 ? sameYear : named.Length > 0 ? named : pages;
        string why = candidates.Length == 0
            ? $"The film database has no title matching '{said}' that has a page here."
            : (sameYear.Length > 1 || (sameYear.Length == 0 && named.Length > 1)
                ? $"More than one title is named '{said}'."
                : $"No title is named exactly '{said}'.")
              + " Call title_details again with the id of the one meant:\n"
              + TitleText.Lines(candidates.Take(CandidatesShown));
        throw new ToolRefusal(why);
    }

    private static ToolResult Film(FilmDetails f, string? note)
    {
        var text = new StringBuilder();
        if (note is not null) text.Append(note).Append('\n');
        text.Append(TitleText.Named(f.Title, f.Year)).Append(", ").Append(TitleText.KindWord("film", f.Category)).Append(". id ").Append(f.ImdbId).Append(".\n");
        if (f.OriginalTitle is { Length: > 0 } original && original != f.Title)
            text.Append("Original title: ").Append(original).Append(".\n");
        Line(text, "Release date", f.ReleaseDate);
        if (f.RuntimeMinutes is { } runtime) text.Append("Runtime: ").Append(runtime).Append(" minutes.\n");
        Line(text, "Certification", f.Certification);
        Line(text, "Genres", Join(f.Genres));
        if (f.Rating is not null) text.Append(TitleText.Rating(f.Rating, f.Votes)).Append('\n');
        Line(text, "Tagline", f.Tagline);
        Line(text, "Plot", f.Overview);
        Line(text, "Directed by", Join(f.Directors));
        Line(text, "Written by", Join(f.Writers));
        Cast(text, f.Cast);
        Line(text, "Original language", f.OriginalLanguage);
        Line(text, "Countries", Join(f.Countries));
        Line(text, "Keywords", Join(f.Keywords));
        text.Append(TitleText.Library(f.LibraryId is not null, f.Watched, "film"));

        return ToolResult.Ok(text.ToString(), TitleCards.Of([
            new TitleCard(f.ImdbId, "film", f.Title, f.Year, f.PosterUrl, f.LibraryId is not null, f.Watched)]));
    }

    private static ToolResult Show(ShowDetails s, string? note)
    {
        int? year = s.FirstYear;
        var text = new StringBuilder();
        if (note is not null) text.Append(note).Append('\n');
        text.Append(TitleText.Named(s.Title, year)).Append(", ").Append(TitleText.KindWord("show", s.Category)).Append(". id ").Append(s.ImdbId).Append(".\n");
        if (s.FirstYear is { } first)
            text.Append("Ran from ").Append(first).Append(s.LastYear is { } last && last != first ? $" to {last}" : "").Append(".\n");
        Line(text, "Status", s.Status);
        Line(text, "Certification", s.Certification);
        Line(text, "Genres", Join(s.Genres));
        if (s.Rating is not null) text.Append(TitleText.Rating(s.Rating, s.Votes)).Append('\n');
        Line(text, "Tagline", s.Tagline);
        Line(text, "Plot", s.Overview);
        Line(text, "Created by", Join(s.Creators));
        Cast(text, s.Cast);
        Line(text, "Networks", Join(s.Networks));
        Line(text, "Countries", Join(s.Countries));
        Line(text, "Keywords", Join(s.Keywords));

        foreach (SeasonDetails season in s.Seasons)
        {
            text.Append("Season ").Append(season.Number).Append(": ").Append(season.Episodes.Count).Append(" episodes");
            if (season.AirDate is { Length: > 0 } aired) text.Append(", first aired ").Append(aired);
            text.Append(".\n");
        }

        if (s.NextEpisode is { } next)
            text.Append($"The next episode is season {next.Season} episode {next.Episode}")
                .Append(next.Name is { Length: > 0 } name ? $" ({name})" : "").Append($", {next.AirDate}.\n");
        if (s.Follow is { } follow)
            text.Append("The person follows it, since ").Append(follow.Since.ToString("yyyy-MM-dd")).Append(".\n");

        bool held = s.Seasons.Any(se => se.Episodes.Any(e => e.TitleId is not null));
        text.Append(TitleText.Library(held, false, "show"));

        return ToolResult.Ok(text.ToString(), TitleCards.Of([
            new TitleCard(s.ImdbId, "show", s.Title, year, s.PosterUrl, held, false)]));
    }

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
