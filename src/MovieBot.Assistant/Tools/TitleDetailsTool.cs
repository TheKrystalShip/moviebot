using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant.Api;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

/// <summary>Everything the film database knows about one film or show.</summary>
internal sealed partial class TitleDetailsTool(CinemaApi api, ILogger<TitleDetailsTool> logger) : CinemaTool(api, logger)
{
    public override string Name => "title_details";

    private const int CastShown = 10;
    private const int CandidatesShown = 6;

    [GeneratedRegex(@"^tt\d{5,}$", RegexOptions.IgnoreCase)]
    private static partial Regex ImdbId();

    [GeneratedRegex(@"^(?<name>.*?)\s*\((?<year>\d{4})\)\s*$")]
    private static partial Regex NameWithYear();

    protected override async ValueTask<PluginToolResult> RunAsync(ToolCall call)
    {
        if (call.Text("title") is not { } said)
            return PluginToolResult.Error("title is required: the title with its year, or an id a tool returned.");

        (string imdbId, string? kind, string? note) = await ResolveAsync(call, said);

        if (kind != "show")
        {
            try
            {
                return Film(await call.Api.FilmAsync(imdbId, call.Cancellation), note);
            }
            catch (ApiRefusal refusal) when (kind is null && refusal.Status == HttpStatusCode.NotFound)
            {
                // An id the model was given with no kind may name a show.
            }
        }

        return Show(await call.Api.ShowAsync(imdbId, call.Cancellation), note);
    }

    /// <summary>
    /// The id a title is read by. A title written with its year, or without, is found through the
    /// search first; an id is used as it is. What cannot be told apart is returned to the model as the
    /// candidates, with their ids, rather than guessed at.
    /// </summary>
    private static async Task<(string Id, string? Kind, string? Note)> ResolveAsync(ToolCall call, string said)
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

        SearchResult found = await call.Api.SearchAsync(name, null, year, call.Cancellation);
        TitleHit[] pages = [.. found.Titles.Where(t => t.ImdbId is { Length: > 0 })];
        TitleHit[] named = [.. pages.Where(t => Say.Normal(t.Title) == Say.Normal(name))];
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
              + Say.Lines(candidates.Take(CandidatesShown));
        throw new ApiRefusal(HttpStatusCode.NotFound, why);
    }

    private static PluginToolResult Film(FilmDetails f, string? note)
    {
        var text = new StringBuilder();
        if (note is not null) text.Append(note).Append('\n');
        text.Append(Say.Named(f.Title, f.Year)).Append(", film. id ").Append(f.ImdbId).Append(".\n");
        if (f.OriginalTitle is { Length: > 0 } original && original != f.Title)
            text.Append("Original title: ").Append(original).Append(".\n");
        Line(text, "Release date", f.ReleaseDate);
        if (f.RuntimeMinutes is { } runtime) text.Append("Runtime: ").Append(runtime).Append(" minutes.\n");
        Line(text, "Certification", f.Certification);
        Line(text, "Genres", Join(f.Genres));
        if (f.Rating is not null) text.Append(Say.Rating(f.Rating, f.Votes)).Append('\n');
        Line(text, "Tagline", f.Tagline);
        Line(text, "Plot", f.Overview);
        Line(text, "Directed by", Join(f.Directors));
        Line(text, "Written by", Join(f.Writers));
        Cast(text, f.Cast);
        Line(text, "Original language", f.OriginalLanguage);
        Line(text, "Countries", Join(f.Countries));
        text.Append(Colour(f.BlackAndWhite, f.Keywords)).Append('\n');
        Line(text, "Keywords", Join(f.Keywords));
        text.Append(Say.Library(f.LibraryId is not null, f.Watched, "film"));

        return PluginToolResult.Ok(text.ToString(), TitleCards.Of([
            new TitleCard(f.ImdbId, "film", f.Title, f.Year, f.PosterUrl, f.LibraryId is not null, f.Watched)]));
    }

    private static PluginToolResult Show(ShowDetails s, string? note)
    {
        int? year = s.FirstYear;
        var text = new StringBuilder();
        if (note is not null) text.Append(note).Append('\n');
        text.Append(Say.Named(s.Title, year)).Append(", show. id ").Append(s.ImdbId).Append(".\n");
        if (s.FirstYear is { } first)
            text.Append("Ran from ").Append(first).Append(s.LastYear is { } last && last != first ? $" to {last}" : "").Append(".\n");
        Line(text, "Status", s.Status);
        Line(text, "Certification", s.Certification);
        Line(text, "Genres", Join(s.Genres));
        if (s.Rating is not null) text.Append(Say.Rating(s.Rating, s.Votes)).Append('\n');
        Line(text, "Tagline", s.Tagline);
        Line(text, "Plot", s.Overview);
        Line(text, "Created by", Join(s.Creators));
        Cast(text, s.Cast);
        Line(text, "Networks", Join(s.Networks));
        Line(text, "Countries", Join(s.Countries));
        text.Append(Colour(s.BlackAndWhite, s.Keywords)).Append('\n');
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
        text.Append(Say.Library(held, false, "show"));

        return PluginToolResult.Ok(text.ToString(), TitleCards.Of([
            new TitleCard(s.ImdbId, "show", s.Title, year, s.PosterUrl, held, false)]));
    }

    /// <summary>
    /// What the database says of colour. It has a tag for black and white and none for colour, so a title
    /// without the tag is not known to be in colour, and a title whose keywords have not arrived yet is
    /// not known either way.
    /// </summary>
    private static string Colour(bool blackAndWhite, List<string> keywords) =>
        blackAndWhite
            ? "Black and white: the database tags it black and white."
            : keywords.Count == 0
                ? "Black and white: not known; the database's keywords for it have not arrived."
                : "Black and white: the database does not tag it black and white. It has no tag for colour, so this does not say it is in colour.";

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
