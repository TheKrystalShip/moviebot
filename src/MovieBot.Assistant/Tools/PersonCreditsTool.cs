using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant.Api;

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
internal sealed partial class PersonCreditsTool(CinemaApi api) : AssistantTool<PersonCreditsTool.Args, Caller>
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

        string name = said;
        string? known = null;
        if (NameWithKnownTitle().Match(said) is { Success: true } m)
        {
            name = m.Groups["name"].Value;
            known = m.Groups["known"].Value;
        }

        PeopleResult found = await database.PeopleAsync(name, ct);
        if (found.People.Count == 0)
            return ToolResult.Ok($"The film database has nobody named '{name}'.");

        (PersonMatch person, string? note) = Pick(found.People, name, known, role);
        PersonCredits credits = await database.CreditsAsync(person.PersonId, from, to, role, kind, ct);

        var text = new StringBuilder();
        if (note is not null) text.Append(note).Append("\n\n");
        text.Append("Credits of ").Append(credits.Name);
        if (credits.KnownFor is { Length: > 0 } department) text.Append(" (known for ").Append(department).Append(')');
        text.Append(", ").Append(Scope(role, kind, from, to)).Append(", as of ").Append(credits.AsOf).Append(". ");

        if (credits.Credits.Count == 0)
        {
            text.Append("None.");
            return ToolResult.Ok(text.ToString());
        }

        (List<Credit> listed, int minor, int lesser) = args.All == true ? (credits.Credits, 0, 0) : Trim(credits.Credits);
        text.Append(credits.Credits.Count).Append(credits.Credits.Count == 1 ? " credit" : " credits").Append(", oldest first.");
        if (credits.Total > credits.Credits.Count)
            text.Append($" The database holds {credits.Total}; these are the newest {credits.Credits.Count}.");
        if (listed.Count < credits.Credits.Count)
            text.Append($" Listed: the {listed.Count} best known with a named part or job, upcoming and announced ones included.")
                .Append($" Not listed: {Not(minor, lesser)}. Call person_credits again with all true to list every credit.");
        text.Append('\n');

        foreach (Credit credit in listed)
            text.Append(Write(credit)).Append('\n');

        return ToolResult.Ok(text.ToString().TrimEnd(), TitleCards.Of(listed));
    }

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
                     .OrderByDescending(c => c.Votes).ThenByDescending(c => c.ReleaseDate, StringComparer.Ordinal).Take(Prominent))
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
    /// The person a name means. One exact match is the person. Where several share the name, the one the
    /// person was written with a title of is chosen, else the one filed under the department the role
    /// asks for, else the database's best match; the others are named in a note so the model can say so
    /// or ask again.
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
            (known is null ? null : exact.FirstOrDefault(p =>
                p.KnownForTitles.Any(t => t.Contains(known, StringComparison.OrdinalIgnoreCase))))
            ?? (role is not null && Departments.TryGetValue(role, out string? department)
                ? exact.FirstOrDefault(p => p.KnownFor == department) : null)
            ?? exact[0];

        PersonMatch[] others = [.. exact.Where(p => p.PersonId != chosen.PersonId)];
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
        var text = new StringBuilder("- ");
        text.Append(credit.ReleaseDate is { Length: > 0 } date ? date : "no date").Append(" | ").Append(TitleText.Status(credit.Status))
            .Append(" | ").Append(TitleText.Named(credit.Title, credit.Year)).Append(", ").Append(TitleText.KindWord(credit.Kind)).Append('.');
        if (credit.Role is { Length: > 0 } role) text.Append(" Role: ").Append(role).Append('.');
        if (credit.Rating is not null) text.Append(' ').Append(TitleText.Rating(credit.Rating, credit.Votes));
        if (credit.Watched && credit.Kind != "show") text.Append(" The person has watched it.");
        text.Append(credit.ImdbId is { Length: > 0 } id ? $" id {id}." : " No IMDb id: it has no page here.");
        return text.ToString();
    }
}
