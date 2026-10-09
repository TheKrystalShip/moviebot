using System.Text;
using System.Text.RegularExpressions;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant.Api;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

/// <summary>An actor's or film-maker's work, oldest first, with what is out, coming and only announced.</summary>
internal sealed partial class PersonCreditsTool(CinemaApi api, ILogger<PersonCreditsTool> logger) : CinemaTool(api, logger)
{
    public override string Name => "person_credits";

    /// <summary>The department the database files a person under, by the role asked for.</summary>
    private static readonly Dictionary<string, string> Departments = new()
    {
        ["acting"] = "Acting", ["directing"] = "Directing", ["writing"] = "Writing",
    };

    [GeneratedRegex(@"^(?<name>.*?)\s*\((?<known>.+)\)\s*$")]
    private static partial Regex NameWithKnownTitle();

    protected override async ValueTask<PluginToolResult> RunAsync(ToolCall call)
    {
        if (call.Text("person") is not { } said)
            return PluginToolResult.Error("person is required: the person's name.");

        string? role = call.OneOf("role", "acting", "directing", "writing", "any");
        string? kind = call.OneOf("kind", "film", "show");
        int? from = call.Number("from");
        int? to = call.Number("to");

        string name = said;
        string? known = null;
        if (NameWithKnownTitle().Match(said) is { Success: true } m)
        {
            name = m.Groups["name"].Value;
            known = m.Groups["known"].Value;
        }

        PeopleResult found = await call.Api.PeopleAsync(name, call.Cancellation);
        if (found.People.Count == 0)
            return PluginToolResult.Ok($"The film database has nobody named '{name}'.");

        (PersonMatch person, string? note) = Pick(found.People, name, known, role);
        PersonCredits credits = await call.Api.CreditsAsync(person.PersonId, from, to, role, kind, call.Cancellation);

        var text = new StringBuilder();
        if (note is not null) text.Append(note).Append("\n\n");
        text.Append("Credits of ").Append(credits.Name);
        if (credits.KnownFor is { Length: > 0 } department) text.Append(" (known for ").Append(department).Append(')');
        text.Append(", ").Append(Scope(role, kind, from, to)).Append(", as of ").Append(credits.AsOf).Append(". ");

        if (credits.Credits.Count == 0)
        {
            text.Append("None.");
            return PluginToolResult.Ok(text.ToString());
        }

        text.Append(credits.Credits.Count).Append(credits.Credits.Count == 1 ? " credit" : " credits").Append(", oldest first.");
        if (credits.Total > credits.Credits.Count)
            text.Append($" The database holds {credits.Total}; these are the newest {credits.Credits.Count}.");
        text.Append('\n');

        foreach (Credit credit in credits.Credits)
            text.Append(Write(credit)).Append('\n');

        return PluginToolResult.Ok(text.ToString().TrimEnd(), TitleCards.Of(credits.Credits));
    }

    /// <summary>
    /// The person a name means. One exact match is the person. Where several share the name, the one the
    /// person was written with a title of is chosen, else the one filed under the department the role
    /// asks for, else the database's best match; the others are named in a note so the model can say so
    /// or ask again.
    /// </summary>
    internal static (PersonMatch Person, string? Note) Pick(List<PersonMatch> people, string name, string? known, string? role)
    {
        PersonMatch[] exact = [.. people.Where(p => Say.Normal(p.Name) == Say.Normal(name))];
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
        text.Append(credit.ReleaseDate is { Length: > 0 } date ? date : "no date").Append(" | ").Append(Say.Status(credit.Status))
            .Append(" | ").Append(Say.Named(credit.Title, credit.Year)).Append(", ").Append(Say.KindWord(credit.Kind)).Append('.');
        if (credit.Role is { Length: > 0 } role) text.Append(" Role: ").Append(role).Append('.');
        if (credit.Rating is not null) text.Append(' ').Append(Say.Rating(credit.Rating, credit.Votes));
        if (credit.Watched && credit.Kind != "show") text.Append(" The person has watched it.");
        text.Append(credit.ImdbId is { Length: > 0 } id ? $" id {id}." : " No IMDb id: it has no page here.");
        return text.ToString();
    }
}
