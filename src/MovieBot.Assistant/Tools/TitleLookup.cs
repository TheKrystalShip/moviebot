using System.Text.RegularExpressions;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant.Api;
using TheKrystalShip.MovieBot.Catalogue;
using TheKrystalShip.MovieBot.Client;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

/// <summary>A title as the model names it, an id or a name with its year, resolved to the catalogue's id.</summary>
internal static partial class TitleLookup
{
    private const int CandidatesShown = 6;

    [GeneratedRegex(@"^tt\d{5,}$", RegexOptions.IgnoreCase)]
    private static partial Regex ImdbId();

    [GeneratedRegex(@"^(?<name>.*?)\s*\((?<year>\d{4})\)\s*$")]
    private static partial Regex NameWithYear();

    /// <summary>
    /// The id a title is read by. A title written with its year, or without, is found through the
    /// search first; an id is used as it is. What cannot be told apart is returned to the model as the
    /// candidates, with their ids, rather than guessed at.
    /// </summary>
    public static async Task<(string Id, string? Note)> ResolveAsync(
        CinemaApi.PersonApi person, string said, string tool, CancellationToken ct)
    {
        if (ImdbId().IsMatch(said))
            return (said.ToLowerInvariant(), null);

        string name = said;
        int? year = null;
        if (NameWithYear().Match(said) is { Success: true } m)
        {
            name = m.Groups["name"].Value;
            year = int.Parse(m.Groups["year"].Value);
        }

        CardPage found = await person.TitlesAsync(new TitleFilter { Words = name, From = year - 1, To = year + 1, Size = 10 }, ct);
        List<TitleCard> pages = found.Titles;
        TitleCard[] named = [.. pages.Where(t => TitleText.Normal(t.Title) == TitleText.Normal(name))];
        TitleCard[] sameYear = year is null ? named : [.. named.Where(t => t.Year == year)];

        if (sameYear.Length == 1)
            return (sameYear[0].Id, null);

        if (sameYear.Length == 0 && named.Length == 0 && pages.Count == 1)
            return (pages[0].Id, $"No title is named exactly '{name}'; this is the database's one close match.");

        TitleCard[] candidates = sameYear.Length > 1 ? sameYear : named.Length > 0 ? named : [.. pages];
        string why = candidates.Length == 0
            ? $"The film database has no title matching '{said}' that has a page here."
            : (sameYear.Length > 1 || (sameYear.Length == 0 && named.Length > 1)
                ? $"More than one title is named '{said}'."
                : $"No title is named exactly '{said}'.")
              + $" Call {tool} again with the id of the one meant:\n"
              + TitleText.Lines(candidates.Take(CandidatesShown));
        throw new ToolRefusal(why);
    }
}
