using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant.Api;
using TheKrystalShip.MovieBot.Catalogue;
using TheKrystalShip.MovieBot.Client;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

[AssistantTool("person_credits", ToolClass.Read, ResultLimit = 16000, Order = 3)]
[Description("""
    An actor's or film-maker's work, in date order, oldest first.
    - Each credit has its release date, kind, the part played or the job, and whether it is released, upcoming or only announced.
    - from and to are years; give both as the same year for one year.
    - role: acting, directing, writing, or any (default).
    - A long list is trimmed to the best-known credits with a count of the rest; all true lists every credit.
    - Several people with the name: returns them, each with what they are known for; ask, or pick the one the question makes obvious.
    """)]
internal sealed partial class PersonCreditsTool(CinemaApi api, TimeProvider clock) : AssistantTool<PersonCreditsTool.Args, Caller>
{
    public sealed record Args(
        [property: Description("The person's name.")] string Person,
        [property: Description("First year to include.")] int? From = null,
        [property: Description("Last year to include.")] int? To = null,
        [property: Description("Which credits. Default any.")] CreditRole? Role = null,
        [property: Description("Limit to films or shows.")] TitleKind? Kind = null,
        [property: Description("List every credit. Default false.")] bool? All = null);

    /// <summary>The most released credits listed when a person has more; fewer are never trimmed.</summary>
    internal const int Prominent = 20;

    /// <summary>A list this long or shorter is listed whole.</summary>
    internal const int Whole = 25;

    /// <summary>The most credits one call reads: the newest, which the API gives a page at most of.</summary>
    internal const int Read = 100;

    /// <summary>How many of a person's best-known titles are named to tell them from another of the name.</summary>
    private const int KnownShown = 3;

    /// <summary>The department the database files a person under, by the role asked for.</summary>
    private static readonly Dictionary<string, string> Departments = new()
    {
        ["acting"] = "Acting", ["directing"] = "Directing", ["writing"] = "Writing",
    };

    [GeneratedRegex(@"^(?<name>.*?)\s*\((?<known>.+)\)\s*$")]
    private static partial Regex NameWithKnownTitle();

    protected override async ValueTask<ToolResult> RunAsync(Args args, ToolCall<Caller> call)
    {
        var database = api.As(call.Principal.AccessToken);
        CancellationToken ct = call.CancellationToken;

        string said = args.Person;
        string? role = args.Role.Wire();
        string? kind = args.Kind.Wire();
        int? from = args.From;
        int? to = args.To;
        if (from > to)
            throw new ToolRefusal("from is after to.");

        string name = said;
        string? known = null;
        if (NameWithKnownTitle().Match(said) is { Success: true } m)
        {
            name = m.Groups["name"].Value;
            known = m.Groups["known"].Value;
        }

        (PersonMatch? person, string? note) = await FindAsync(database, name, known, role, ct);
        if (person is null)
            return ToolResult.Ok($"The film database has nobody named '{name}'.");

        Credits credits = await CreditsAsync(database, person, from, to, role, kind, Today(clock), ct);

        var text = new StringBuilder();
        if (note is not null) text.Append(note).Append("\n\n");
        text.Append("Credits of ").Append(person.Name);
        if (person.KnownFor is { Length: > 0 } department) text.Append(" (known for ").Append(department).Append(')');
        text.Append(", ").Append(Scope(role, kind, from, to)).Append(". ");

        if (credits.List.Count == 0)
        {
            text.Append("None.");
            return ToolResult.Ok(text.ToString());
        }

        (List<Credit> listed, int minor, int lesser) = args.All == true ? (credits.List, 0, 0) : Trim(credits.List);
        text.Append(credits.List.Count).Append(credits.List.Count == 1 ? " credit" : " credits").Append(", oldest first.");
        if (credits.Total > credits.List.Count)
            text.Append($" The database holds {credits.Total}; these are the newest {credits.List.Count}.");
        if (listed.Count < credits.List.Count)
            text.Append($" Listed: the {listed.Count} best known with a named part or job, upcoming and announced ones included.")
                .Append($" Not listed: {Not(minor, lesser)}. Call person_credits again with all true to list every credit.");
        text.Append('\n');

        foreach (Credit credit in listed)
            text.Append(Write(credit)).Append('\n');

        return ToolResult.Ok(text.ToString().TrimEnd(), TitleCards.Of(listed.Select(c => c.Card)));
    }

    internal static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    /// <summary>A person the catalogue names: its id, and what tells them from another of the name.</summary>
    internal sealed record PersonMatch(string Id, string Name, string? KnownFor)
    {
        /// <summary>Their best-known titles, read only where they have to be told from others.</summary>
        public List<string> KnownForTitles { get; init; } = [];

        /// <summary>Whether they are credited on the title the person was written with.</summary>
        public bool HasKnown { get; init; }
    }

    /// <summary>One title a person is credited on, with its status on the day it was read.</summary>
    internal sealed record Credit(TitleCard Card, string Status)
    {
        public string? Role => Card.Credit?.Role is { Length: > 0 } role ? role : null;
        public int Votes => Card.Rating?.Votes ?? 0;
    }

    /// <summary>A person's credits, oldest first, and how many the database holds in all.</summary>
    internal sealed record Credits(List<Credit> List, int Total);

    /// <summary>
    /// The people a name means, through the catalogue's terms, and the one to read (<see cref="Pick"/>).
    /// Where several share the name, each one's best-known titles are read so the note can tell them
    /// apart, and with a title written beside the name, whether each is credited on it. Null when the
    /// catalogue names nobody so.
    /// </summary>
    internal static async Task<(PersonMatch? Person, string? Note)> FindAsync(
        CinemaApi.PersonApi database, string name, string? known, string? role, CancellationToken ct)
    {
        List<PersonMatch> people = [.. (await database.TermsAsync(name, ct))
            .Where(t => t.Filter == "person").Select(t => new PersonMatch(t.Value, t.Name, t.Detail))];
        if (people.Count == 0) return (null, null);

        int exact = people.Count(p => TitleText.Normal(p.Name) == TitleText.Normal(name));
        if (exact != 1 && people.Count > 1)
            people = [.. await Task.WhenAll(people.Select(async (p, i) =>
                exact > 1 && TitleText.Normal(p.Name) != TitleText.Normal(name) || exact == 0 && i >= 4
                    ? p
                    : await DescribeAsync(database, p, exact > 1 ? known : null, ct)))];

        return Pick(people, name, known, role);
    }

    private static async Task<PersonMatch> DescribeAsync(CinemaApi.PersonApi database, PersonMatch person, string? known, CancellationToken ct)
    {
        Task<CardPage> best = database.TitlesAsync(new TitleFilter { People = [person.Id], Sort = "votes", Size = KnownShown }, ct);
        Task<CardPage>? on = known is null ? null : database.TitlesAsync(new TitleFilter { People = [person.Id], Words = known, Size = 1 }, ct);
        return person with
        {
            KnownForTitles = [.. (await best).Titles.Select(t => TitleText.Named(t.Title, t.Year))],
            HasKnown = on is not null && (await on).Titles.Count > 0,
        };
    }

    /// <summary>
    /// A person's credits in the years, the role and the kind asked for, oldest first with the undated
    /// last. A person with more than <see cref="Read"/> is read as their newest <see cref="Read"/>: the
    /// last pages of the oldest-first list, which end in the announced credits.
    /// </summary>
    internal static async Task<Credits> CreditsAsync(
        CinemaApi.PersonApi database, PersonMatch person, int? from, int? to, string? role, string? kind, DateOnly today, CancellationToken ct)
    {
        var filter = new TitleFilter
        {
            People = [person.Id], Role = RoleWire(role), Kind = kind, From = from, To = to, Sort = "oldest", Size = Read,
        };
        CardPage first = await database.TitlesAsync(filter, ct);
        List<TitleCard> cards = first.Titles;
        if (first.Total > first.Titles.Count && first.Titles.Count == Read)
        {
            int last = (first.Total + Read - 1) / Read;
            CardPage[] pages = await Task.WhenAll(new[] { last - 1, last }.Where(n => n > 1)
                .Select(n => database.TitlesAsync(filter with { Page = n }, ct)));
            cards = [.. (last - 1 == 1 ? first.Titles : []).Concat(pages.SelectMany(p => p.Titles)).TakeLast(Read)];
        }

        return new Credits([.. cards.Select(c => new Credit(c, TitleText.Status(c, today)))], first.Total);
    }

    /// <summary>The list query's word for a role: acting is the cast, and any is every credit.</summary>
    private static string? RoleWire(string? role) => role switch
    {
        "acting" => "cast",
        "directing" or "writing" => role,
        _ => null,
    };

    /// <summary>
    /// The credits worth listing from a long list, still oldest first. Every upcoming and announced credit
    /// with a part or a job stays, and so do the <see cref="Prominent"/> released ones with a part or a job
    /// that most votes stand behind. What is left out is counted: credits with no named part (a part as
    /// oneself, an uncredited or archive appearance) and the less known. A list of <see cref="Whole"/> or
    /// fewer is returned whole, so a question about a few years is answered with every dated credit.
    /// </summary>
    internal static (List<Credit> Listed, int Minor, int Lesser) Trim(List<Credit> credits)
    {
        if (credits.Count <= Whole) return (credits, 0, 0);

        List<Credit> named = [.. credits.Where(c => !Minor(c))];
        HashSet<Credit> kept = [.. named.Where(c => c.Status != "released")];
        foreach (Credit credit in named.Where(c => c.Status == "released")
                     .OrderByDescending(c => c.Votes).ThenByDescending(c => c.Card.Released, StringComparer.Ordinal).Take(Prominent))
            kept.Add(credit);

        List<Credit> listed = [.. credits.Where(kept.Contains)];
        int minor = credits.Count(Minor);
        return (listed, minor, credits.Count - listed.Count - minor);
    }

    [GeneratedRegex(@"^(self|himself|herself|themselves|archive footage)\b|\buncredited\b", RegexOptions.IgnoreCase)]
    private static partial Regex AsOneself();

    /// <summary>A credit with no named part or job, or only a part as oneself or an uncredited one.</summary>
    private static bool Minor(Credit credit) =>
        credit.Role is not { Length: > 0 } role
        || role.Split(" · ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).All(part => AsOneself().IsMatch(part));

    private static string Not(int minor, int lesser) => string.Join(" and ", new[]
    {
        minor > 0 ? $"{minor} with no named part, as oneself or uncredited" : null,
        lesser > 0 ? $"{lesser} less known" : null,
    }.Where(p => p is not null));

    /// <summary>
    /// The person a name means. One exact match is the person. Where several share the name, the one
    /// credited on the title the person was written with is chosen, else the one filed under the
    /// department the role asks for, else the most credited; the others are named in a note so the model
    /// can say so or ask again.
    /// </summary>
    internal static (PersonMatch Person, string? Note) Pick(List<PersonMatch> people, string name, string? known, string? role)
    {
        PersonMatch[] exact = [.. people.Where(p => TitleText.Normal(p.Name) == TitleText.Normal(name))];
        if (exact.Length == 0)
        {
            return (people[0], people.Count == 1
                ? null
                : $"Nobody is named exactly '{name}'. These are the closest: {Describe(people.Take(4))}. Using the first.");
        }

        if (exact.Length == 1)
            return (exact[0], null);

        PersonMatch chosen =
            (known is null ? null : exact.FirstOrDefault(p => p.HasKnown))
            ?? (role is not null && Departments.TryGetValue(role, out string? department)
                ? exact.FirstOrDefault(p => p.KnownFor == department) : null)
            ?? exact[0];

        PersonMatch[] others = [.. exact.Where(p => p.Id != chosen.Id)];
        return (chosen,
            $"Several people are named {name}. These are the credits of the one known for {Titles(chosen)}. "
            + $"The others: {Describe(others)}. If the question means another, call person_credits again with the name "
            + $"and one of that person's known titles in brackets, as '{name} (Title)'.");
    }

    private static string Describe(IEnumerable<PersonMatch> people) =>
        string.Join("; ", people.Select(p => $"{p.Name}, {p.KnownFor ?? "department unknown"}, known for {Titles(p)}"));

    private static string Titles(PersonMatch p) => p.KnownForTitles.Count == 0 ? "no listed title" : string.Join(", ", p.KnownForTitles);

    private static string Scope(string? role, string? kind, int? from, int? to)
    {
        var parts = new List<string>
        {
            role is null or "any" ? "cast and crew" : role,
            kind is null ? "films and shows" : kind + "s",
        };
        if (from is not null || to is not null)
            parts.Add(from == to ? $"in {from}" : $"{(from is { } f ? $"from {f} " : "")}{(to is { } t ? $"to {t}" : "")}".Trim());
        return string.Join(", ", parts);
    }

    private static string Write(Credit credit)
    {
        TitleCard card = credit.Card;
        var text = new StringBuilder("- ");
        text.Append(card.Released is { Length: > 0 } date ? date : "no date").Append(" | ").Append(TitleText.Status(credit.Status))
            .Append(" | ").Append(TitleText.Named(card.Title, card.Year)).Append(", ").Append(TitleText.KindWord(card.Kind)).Append('.');
        if (credit.Role is { } role) text.Append(" Role: ").Append(role).Append('.');
        if (TitleText.Rating(card.Rating) is { Length: > 0 } rated) text.Append(' ').Append(rated);
        if (card.Watched && card.Kind != "show") text.Append(" The person has watched it.");
        text.Append(" id ").Append(card.Id).Append('.');
        return text.ToString();
    }
}
