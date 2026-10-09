using System.Net;
using System.Text;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant.Api;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

/// <summary>
/// A film or show found from what somebody half remembers. The model only extracts the clues; this tool
/// resolves the people, reads their dated credits, runs discover on the look and the plot words, merges
/// what each found, checks the best candidates against the database's own page for them, and ranks by
/// how many clues each fits, saying which it fits and which it does not.
/// </summary>
internal sealed class FindByCluesTool(CinemaApi api, TimeProvider clock, ILogger<FindByCluesTool> logger) : CinemaTool(api, logger)
{
    public override string Name => "find_by_clues";

    private const int Shown = 8;
    private const int Verified = 12;

    /// <summary>Looks the database files as genres, not keywords.</summary>
    private static readonly Dictionary<string, string> LookGenres = new(StringComparer.OrdinalIgnoreCase)
    {
        ["animated"] = "Animation", ["animation"] = "Animation", ["cartoon"] = "Animation",
        ["documentary"] = "Documentary", ["musical"] = "Music",
    };

    private const string NotTaggedBlackAndWhite =
        "the database does not tag it black and white; that does not say it is in colour";

    protected override async ValueTask<PluginToolResult> RunAsync(ToolCall call)
    {
        IReadOnlyList<string> people = call.List("people");
        IReadOnlyList<string> look = call.List("look");
        IReadOnlyList<string> about = call.List("about");
        int? from = call.Number("from");
        int? to = call.Number("to");
        string? released = call.OneOf("released", "released", "upcoming", "announced", "any");
        if (released == "any") released = null;
        string? kind = call.OneOf("kind", "film", "show");
        string? language = call.Text("language");

        if (people.Count == 0 && look.Count == 0 && about.Count == 0 && from is null && to is null
            && released is null && language is null)
            return PluginToolResult.Error(
                "Give at least one clue: people, from and to, released, look, about or language.");

        var notes = new List<string>();
        var pool = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        DateOnly today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        // The people: who they are, and everything they are credited on in the years asked for.
        var clues = new List<string>();
        var resolved = new List<string>();
        foreach (Person person in await Task.WhenAll(people.Select(n => ReadPersonAsync(call, n, kind, from, to))))
        {
            if (person.Note is not null) notes.Add(person.Note);
            if (person.Credits is null) continue;

            resolved.Add(person.Credits.Name);
            clues.Add(person.Credits.Name);
            foreach (Credit credit in person.Credits.Credits)
            {
                Candidate c = Get(pool, credit.ImdbId ?? $"tmdb:{credit.TmdbId}");
                c.Fill(credit);
                c.Set(person.Credits.Name, true, credit.Role is { Length: > 0 } role ? $"credited as {role}" : null);
            }
        }

        string? years = from is null && to is null ? null
            : from == to ? $"released in {from}" : $"released {(from is { } f ? $"from {f} " : "")}{(to is { } t ? $"to {t}" : "")}".Trim();
        if (years is not null) clues.Add(years);
        if (released is not null) clues.Add(released);
        clues.AddRange(look);
        clues.AddRange(about.Select(Quoted));
        if (language is not null) clues.Add($"original language {language}");

        // What the database finds by the look and the plot words, with the people when it can.
        string discoverKind = kind ?? "film";
        IReadOnlyList<string> with = discoverKind == "film" ? resolved : [];
        string[] genres = [.. look.Where(LookGenres.ContainsKey).Select(l => LookGenres[l])];
        string[] keywords = [.. look.Where(l => !LookGenres.ContainsKey(l))];

        var searches = new List<Task<Found>>();
        if (look.Count > 0 || language is not null)
        {
            searches.Add(DiscoverAsync(call, new DiscoverQuery
            {
                Kind = discoverKind, Genre = genres, About = keywords, With = with, From = from, To = to,
                Language = language, GoodOnly = false,
            }, with, look, []));
            if (with.Count > 0)
                searches.Add(DiscoverAsync(call, new DiscoverQuery
                {
                    Kind = discoverKind, Genre = genres, About = keywords, From = from, To = to, Language = language, GoodOnly = false,
                }, [], look, []));
        }

        foreach (string word in about)
            searches.Add(DiscoverAsync(call, new DiscoverQuery
            {
                Kind = discoverKind, About = [word], With = with, From = from, To = to, Language = language, GoodOnly = false,
            }, with, [], [word]));

        if (searches.Count == 0 && resolved.Count == 0)
        {
            if (released is "upcoming" or "announced")
                searches.Add(ListAsync(call, discoverKind, today));
            else
                searches.Add(DiscoverAsync(call, new DiscoverQuery
                {
                    Kind = discoverKind, From = from, To = to, Language = language, GoodOnly = true,
                }, [], [], []));
        }

        foreach (Found found in await Task.WhenAll(searches))
        {
            if (found.Note is not null) notes.Add(found.Note);
            foreach (TitleHit hit in found.Hits)
            {
                Candidate c = Get(pool, hit.ImdbId ?? $"tmdb:{hit.TmdbId}");
                c.Fill(hit, today);
                foreach (string name in found.People) c.Set(name, true, null);
                foreach (string word in found.Look) c.Set(word, true, "the database tags it so");
                foreach (string word in found.About) c.Set(Quoted(word), true, "a keyword of it");
            }
        }

        Judge(pool.Values, resolved, years, from, to, released);

        // The best candidates are read as the database's own page for them, which holds the keywords.
        Candidate[] ranked = [.. Rank(pool.Values)];
        await Task.WhenAll(ranked.Take(Verified).Where(c => c.ImdbId is not null).Select(c => ReadAsync(call, c, look, about)));
        ranked = [.. Rank(pool.Values).Take(Shown)];

        if (ranked.Length == 0)
            return PluginToolResult.Ok(
                $"Nothing in the database fits any of these clues: {string.Join("; ", clues)}. {string.Join(' ', notes)} "
                + "A clue the database cannot hold, like a trailer, a scene or news, is for web_search.".Trim());

        var text = new StringBuilder($"Clues: {string.Join("; ", clues)}. Up to {Shown} candidates, the best fit first.\n");
        foreach (string note in notes.Distinct()) text.Append(note).Append('\n');

        // The verdict first, so the answer does not rest on reading eight candidates.
        Candidate[] fits = [.. ranked.Where(c => c.Fits(clues))];
        Candidate[] open = [.. ranked.Where(c => !c.Fits(clues) && c.Contradicted(clues) == 0)];
        if (fits.Length > 0)
            text.Append($"Fits every clue: {string.Join("; ", fits.Select(c => Say.Named(c.Title, c.Year)))}.\n");
        else
        {
            Candidate? closest = ranked.FirstOrDefault(c => c.ImdbId is not null && c.Unchecked(clues) == 0);
            text.Append("No candidate fits every clue.");
            if (closest is not null)
                text.Append($" Closest: {closest.Summary(clues)}");
            text.Append('\n');
        }
        if (open.Length > 0)
            text.Append("Nothing contradicts these, but a clue was not checked: ")
                .Append(string.Join("; ", open.Select(c => Say.Named(c.Title, c.Year)))).Append(".\n");

        foreach (Candidate c in ranked) text.Append(c.Write(clues)).Append('\n');
        text.Append("A clue the database cannot hold, like a trailer, a scene or news, is for web_search. Confirm a candidate with title_details before naming it.");

        return PluginToolResult.Ok(text.ToString(), TitleCards.Of(
            ranked.Where(c => c.ImdbId is not null)
                .Select(c => new TitleCard(c.ImdbId!, c.Kind, c.Title, c.Year, c.PosterUrl, c.InLibrary, c.Watched))));
    }

    private static string Quoted(string word) => $"\"{word}\"";

    private static Candidate Get(Dictionary<string, Candidate> pool, string key) =>
        pool.TryGetValue(key, out Candidate? existing) ? existing : pool[key] = new Candidate();

    private static IEnumerable<Candidate> Rank(IEnumerable<Candidate> candidates) =>
        candidates.OrderByDescending(c => c.Matches).ThenBy(c => c.Misses).ThenByDescending(c => c.Votes);

    /// <summary>The years and release status of every candidate, and the people whose credits it is not among.</summary>
    private static void Judge(
        IEnumerable<Candidate> candidates, List<string> people, string? years, int? from, int? to, string? released)
    {
        foreach (Candidate c in candidates)
        {
            if (years is not null)
            {
                // An announcement with no date yet may fall in the years asked for, so it is not held against them.
                bool inYears = c.Year is { } y
                    ? (from is null || y >= from) && (to is null || y <= to)
                    : c.Status == "announced";
                c.Set(years, inYears, c.Year is null ? "it has no date" : $"it is from {c.Year}");
            }

            if (released is not null)
                c.Set(released, c.Status == released, $"it is {c.Status}");

            foreach (string person in people)
                c.Set(person, false, "not among their credits in those years", onlyIfUnsettled: true);
        }
    }

    private async Task ReadAsync(ToolCall call, Candidate c, IReadOnlyList<string> look, IReadOnlyList<string> about)
    {
        try
        {
            if (c.Kind == "show")
            {
                ShowDetails s = await call.Api.ShowAsync(c.ImdbId!, call.Cancellation);
                c.Read(s.Keywords, s.Genres, s.Overview, s.BlackAndWhite, look, about);
            }
            else
            {
                FilmDetails f = await call.Api.FilmAsync(c.ImdbId!, call.Cancellation);
                c.Read(f.Keywords, f.Genres, f.Overview, f.BlackAndWhite, look, about);
            }
        }
        catch (ApiRefusal)
        {
            // A page that cannot be read leaves the clues it would have settled unchecked.
        }
    }

    private static async Task<Person> ReadPersonAsync(ToolCall call, string name, string? kind, int? from, int? to)
    {
        try
        {
            PeopleResult found = await call.Api.PeopleAsync(name, call.Cancellation);
            if (found.People.Count == 0)
                return new Person(null, $"The database has nobody named '{name}'.");

            (PersonMatch match, string? note) = PersonCreditsTool.Pick(found.People, name, null, null);
            PersonCredits credits = await call.Api.CreditsAsync(match.PersonId, from, to, null, kind, call.Cancellation);
            return new Person(credits, note);
        }
        catch (ApiRefusal refusal)
        {
            return new Person(null, $"{name} could not be read: {refusal.Message}");
        }
    }

    private static async Task<Found> DiscoverAsync(
        ToolCall call, DiscoverQuery query, IReadOnlyList<string> people, IReadOnlyList<string> look, IReadOnlyList<string> about)
    {
        try
        {
            DiscoverResult result = await call.Api.DiscoverAsync(query, call.Cancellation);
            return new Found(result.Titles, people, look, about, null);
        }
        catch (ApiRefusal refusal) when (refusal.Status is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return new Found([], people, look, about, refusal.Message);
        }
    }

    private async Task<Found> ListAsync(ToolCall call, string kind, DateOnly today)
    {
        try
        {
            TitleList list = await call.Api.UpcomingAsync(kind, call.Cancellation);
            return new Found(list.Titles, [], [], [], null);
        }
        catch (ApiRefusal refusal) when (refusal.Status is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return new Found([], [], [], [], refusal.Message);
        }
    }

    private sealed record Person(PersonCredits? Credits, string? Note);

    private sealed record Found(
        IReadOnlyList<TitleHit> Hits, IReadOnlyList<string> People, IReadOnlyList<string> Look, IReadOnlyList<string> About, string? Note);

    private readonly record struct Verdict(bool Ok, string? Why);

    private sealed class Candidate
    {
        private readonly Dictionary<string, Verdict> _verdicts = new(StringComparer.OrdinalIgnoreCase);

        public string Title { get; private set; } = "";
        public int? Year { get; private set; }
        public string Kind { get; private set; } = "film";
        public string? ImdbId { get; private set; }
        public string? Date { get; private set; }
        public string Status { get; private set; } = "released";
        public string? Role { get; private set; }
        public string? Plot { get; private set; }
        public double? Rating { get; private set; }
        public int Votes { get; private set; }
        public bool InLibrary { get; private set; }
        public bool Watched { get; private set; }
        public string? PosterUrl { get; private set; }
        public List<string> Genres { get; } = [];

        public int Matches => _verdicts.Values.Count(v => v.Ok);
        public int Misses => _verdicts.Values.Count(v => !v.Ok);

        /// <summary>
        /// Records whether the candidate fits a clue. A fit is never replaced by a miss, and with
        /// <paramref name="onlyIfUnsettled"/> nothing already recorded is replaced.
        /// </summary>
        public void Set(string clue, bool ok, string? why, bool onlyIfUnsettled = false)
        {
            if (_verdicts.TryGetValue(clue, out Verdict existing) && (existing.Ok || onlyIfUnsettled))
                return;
            _verdicts[clue] = new Verdict(ok, why);
        }

        public void Fill(Credit credit)
        {
            Title = credit.Title; Year = credit.Year; Kind = credit.Kind; ImdbId = credit.ImdbId; Date = credit.ReleaseDate;
            Status = credit.Status; Role ??= credit.Role; Rating ??= credit.Rating; Votes = Math.Max(Votes, credit.Votes);
            InLibrary = credit.InLibrary; Watched = credit.Watched; PosterUrl ??= credit.PosterUrl;
        }

        public void Fill(TitleHit hit, DateOnly today)
        {
            Title = hit.Title; Year = hit.Year; Kind = hit.Kind; ImdbId = hit.ImdbId; Date = hit.ReleaseDate;
            Plot ??= hit.Plot; Rating ??= hit.Rating; Votes = Math.Max(Votes, hit.Votes);
            InLibrary = hit.InLibrary; Watched = hit.Watched; PosterUrl ??= hit.PosterUrl;
            if (Genres.Count == 0) Genres.AddRange(hit.Genres);
            Status = hit.ReleaseDate is null ? "announced"
                : DateOnly.TryParse(hit.ReleaseDate, out DateOnly day) && day > today ? "upcoming" : "released";
        }

        /// <summary>Settles the look and plot clues a database page can settle: its keywords, genres and overview.</summary>
        public void Read(
            List<string> keywords, List<string> genres, string? overview, bool blackAndWhite,
            IReadOnlyList<string> look, IReadOnlyList<string> about)
        {
            if (Plot is null && overview is { Length: > 0 })
                Plot = overview.Length > 200 ? overview[..200].TrimEnd() + "..." : overview;
            if (Genres.Count == 0) Genres.AddRange(genres);

            foreach (string word in look)
            {
                bool tagged = word.Equals("black and white", StringComparison.OrdinalIgnoreCase)
                    ? blackAndWhite || keywords.Any(k => k.Contains(word, StringComparison.OrdinalIgnoreCase))
                    : keywords.Any(k => k.Contains(word, StringComparison.OrdinalIgnoreCase))
                      || (LookGenres.TryGetValue(word, out string? genre) && genres.Contains(genre, StringComparer.OrdinalIgnoreCase));
                Set(word, tagged, tagged ? "the database tags it so"
                    : word.Equals("black and white", StringComparison.OrdinalIgnoreCase) ? NotTaggedBlackAndWhite
                    : "not among its keywords or genres");
            }

            foreach (string word in about)
            {
                bool found = keywords.Any(k => k.Contains(word, StringComparison.OrdinalIgnoreCase))
                    || overview?.Contains(word, StringComparison.OrdinalIgnoreCase) == true;
                Set(Quoted(word), found, found ? "in its keywords or plot" : "not in its keywords or plot");
            }
        }

        /// <summary>Whether every clue was checked and the candidate fits all of them.</summary>
        public bool Fits(IReadOnlyList<string> clues) =>
            clues.All(c => _verdicts.TryGetValue(c, out Verdict v) && v.Ok);

        public int Unchecked(IReadOnlyList<string> clues) => clues.Count(c => !_verdicts.ContainsKey(c));

        /// <summary>The candidate in one sentence, with the clues it fits and the ones it does not.</summary>
        public string Summary(IReadOnlyList<string> clues)
        {
            string fits = string.Join(", ", clues.Where(c => _verdicts.TryGetValue(c, out Verdict v) && v.Ok));
            string misses = string.Join("; ", clues.Where(c => _verdicts.TryGetValue(c, out Verdict v) && !v.Ok)
                .Select(c => _verdicts[c].Why is { } why ? $"{c} ({why})" : c));
            return $"{Say.Named(Title, Year)}, which fits {(fits.Length == 0 ? "none of them" : fits)}"
                   + (misses.Length == 0 ? "." : $" but not {misses}.");
        }

        /// <summary>How many clues the candidate was checked against and does not fit.</summary>
        public int Contradicted(IReadOnlyList<string> clues) =>
            clues.Count(c => _verdicts.TryGetValue(c, out Verdict v) && !v.Ok);

        public string Write(IReadOnlyList<string> clues)
        {
            var text = new StringBuilder("- ").Append(Say.Named(Title, Year)).Append(", ").Append(Say.KindWord(Kind)).Append(". ");
            text.Append(Status switch
            {
                "upcoming" => "Upcoming",
                "announced" => "Announced, no release date",
                _ => "Released",
            });
            if (Date is { Length: > 0 } date && Status != "announced") text.Append(' ').Append(date);
            text.Append('.');
            if (Role is { Length: > 0 } role) text.Append(" Role: ").Append(role).Append('.');
            if (Genres.Count > 0) text.Append(' ').Append(string.Join(", ", Genres)).Append('.');
            if (Rating is not null) text.Append(' ').Append(Say.Rating(Rating, Votes));
            text.Append(ImdbId is { Length: > 0 } id ? $" id {id}." : " No IMDb id: it has no page here.");
            if (Plot is { Length: > 0 } plot) text.Append("\n  ").Append(plot);

            var matched = clues.Where(c => _verdicts.TryGetValue(c, out Verdict v) && v.Ok).ToList();
            var missed = clues.Where(c => _verdicts.TryGetValue(c, out Verdict v) && !v.Ok)
                .Select(c => _verdicts[c].Why is { } why ? $"{c} ({why})" : c).ToList();
            var unchecked_ = clues.Where(c => !_verdicts.ContainsKey(c)).ToList();

            text.Append("\n  Matches: ").Append(matched.Count == 0 ? "none" : string.Join("; ", matched)).Append('.');
            if (missed.Count > 0) text.Append(" Does not match: ").Append(string.Join("; ", missed)).Append('.');
            if (unchecked_.Count > 0) text.Append(" Not checked: ").Append(string.Join("; ", unchecked_)).Append('.');
            return text.ToString();
        }
    }
}
