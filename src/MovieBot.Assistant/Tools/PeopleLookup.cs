using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant.Api;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

/// <summary>
/// The people a person names, as accounts: a name matched whole, else by every word it holds, or an
/// account id a tool returned. A name that fits several people, or nobody, is refused with who it could be,
/// so somebody is never invited on a guess.
/// </summary>
internal static class PeopleLookup
{
    private const int CandidatesShown = 6;

    public static async Task<List<PersonView>> ResolveAsync(
        CinemaApi.PersonApi api, IEnumerable<string> names, Caller caller, CancellationToken ct)
    {
        List<PersonView> everybody = await api.PeopleAsync(ct);
        var found = new List<PersonView>();
        foreach (string said in names.Select(n => n.Trim()).Where(n => n.Length > 0))
        {
            if (TitleText.Normal(said) == TitleText.Normal(caller.DisplayName) || said == caller.UserId)
                continue;
            PersonView one = One(everybody, said);
            if (found.All(p => p.UserId != one.UserId)) found.Add(one);
        }

        return found;
    }

    private static PersonView One(List<PersonView> everybody, string said)
    {
        if (everybody.FirstOrDefault(p => p.UserId == said) is { } byId) return byId;

        string whole = TitleText.Normal(said);
        PersonView[] named = [.. everybody.Where(p => TitleText.Normal(p.Name) == whole)];
        if (named.Length == 1) return named[0];

        string[] words = [.. said.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(TitleText.Normal).Where(w => w.Length > 0)];
        PersonView[] holding = named.Length > 1 ? named
            : [.. everybody.Where(p => words.All(w => p.Name.Split(' ').Any(part => TitleText.Normal(part).StartsWith(w, StringComparison.Ordinal))))];
        if (holding.Length == 1) return holding[0];

        throw new ToolRefusal(holding.Length == 0
            ? $"Nobody on Cinema is named '{said}'. Ask the person who they mean."
            : $"More than one person on Cinema could be '{said}'. Ask the person which, or call again with the id of the one meant:\n"
              + string.Join('\n', holding.Take(CandidatesShown).Select(p =>
                  $"- {p.Name}, id {p.UserId}" + (p.LastSeenAt is { } seen ? $", last seen {seen:yyyy-MM-dd}." : "."))));
    }
}
