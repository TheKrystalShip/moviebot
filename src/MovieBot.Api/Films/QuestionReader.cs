using TheKrystalShip.MovieBot.Acquire.Tmdb;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>One thing a question was read to ask: what kind of part it is, and the words it is shown with.</summary>
/// <param name="Part">
/// <c>count</c>, <c>sort</c>, <c>kind</c>, <c>genre</c>, <c>exclude</c>, <c>category</c>, <c>years</c>,
/// <c>language</c>, <c>runtime</c>, <c>person</c>, <c>about</c> or <c>like</c>.
/// </param>
public sealed record QuestionPart(string Part, string Text);

/// <summary>
/// What the words of a question ask: the discovery that answers them, the parts they were read as, in
/// the order they were typed, and the names that are not yet known to be people or subjects.
/// </summary>
public sealed record Question
{
    public required DiscoverRequest Request { get; init; }

    public required IReadOnlyList<QuestionPart> Parts { get; init; }

    /// <summary>
    /// Words before the rest of the question that no rule reads (<c>keanu reeves</c> of
    /// <c>keanu reeves movies</c>): a person or a subject by its exact name, or the words are no question.
    /// </summary>
    public string? Named { get; init; }

    /// <summary>What followed a bare <c>with</c>: each a person, else a subject.</summary>
    public IReadOnlyList<string> WithEither { get; init; } = [];
}

/// <summary>
/// The names the film database itself lists, which the reader recognises genres and languages from:
/// its genres for films and for shows (two lists) and its languages with their English names.
/// </summary>
public sealed record QuestionVocabulary
{
    public IReadOnlyList<CatalogueGenre> FilmGenres { get; init; } = [];

    public IReadOnlyList<CatalogueGenre> ShowGenres { get; init; } = [];

    public IReadOnlyList<LanguageName> Languages { get; init; } = [];
}

/// <summary>
/// Reads the words typed into Discover as a question of the catalogue (<c>top 10 horror movies from
/// 2000</c>, <c>korean thrillers of the 2010s</c>, <c>sci-fi with keanu reeves</c>), or as no question,
/// when they are a title's name.
///
/// Words are a question when every one of them is read and they ask something besides a bare year:
/// <c>Horror Express</c> leaves a word unread and <c>1917</c> is a year alone, so both are names. The
/// one exception is words before the rest that no rule reads, which are kept as <see cref="Question.Named"/>
/// for the catalogue to recognise as a person or a subject. Nothing here asks the database: the reader
/// is the closed grammar (counts, kinds, years, runtimes, markers) over a <see cref="QuestionVocabulary"/>
/// it is given for genres and languages, and people and subjects are resolved by whoever answers the question.
/// </summary>
public static class QuestionReader
{
    /// <summary>The most a question's count asks for, which the discovery lists across its pages.</summary>
    public const int MostCounted = 100;

    /// <summary>The votes a question for the newest titles needs behind one, so what nobody has seen is left out.</summary>
    public const int NewestVotes = 20;

    /// <summary>
    /// The common words the film database spells otherwise, with the genre names to look for in its
    /// lists (a show's list spells science fiction <c>Sci-Fi &amp; Fantasy</c>) and the label a pair of
    /// genres is shown with. A word the lists name themselves never needs an entry.
    /// </summary>
    private static readonly Dictionary<string, (string[] Names, string? Label)> GenreAliases = BuildAliases();

    private static Dictionary<string, (string[] Names, string? Label)> BuildAliases()
    {
        var aliases = new Dictionary<string, (string[], string?)>(StringComparer.Ordinal);
        foreach (var word in new[] { "sci-fi", "scifi", "sci fi", "science fiction" }) aliases[word] = (["Science Fiction", "Sci-Fi & Fantasy"], null);
        foreach (var word in new[] { "romcom", "romcoms", "rom-com", "rom-coms", "rom com", "rom coms", "romantic comedy", "romantic comedies" })
            aliases[word] = (["Romance", "Comedy"], "Romantic comedy");
        foreach (var word in new[] { "animated", "cartoon", "cartoons" }) aliases[word] = (["Animation"], null);
        aliases["scary"] = (["Horror"], null);
        aliases["romantic"] = (["Romance"], null);
        aliases["musical"] = aliases["musicals"] = (["Music"], null);
        aliases["historical"] = (["History"], null);
        return aliases;
    }

    /// <summary>The kind each word for one names.</summary>
    private static readonly Dictionary<string, string> KindWords = new(StringComparer.Ordinal)
    {
        ["movie"] = "film", ["movies"] = "film", ["film"] = "film", ["films"] = "film", ["flick"] = "film", ["flicks"] = "film",
        ["show"] = "show", ["shows"] = "show", ["series"] = "show", ["tv"] = "show", ["tv shows"] = "show", ["tv show"] = "show",
        ["tv series"] = "show", ["miniseries"] = "show", ["television"] = "show",
    };

    /// <summary>Words that order the answer, with what is shown for each order.</summary>
    private static readonly Dictionary<string, string> SortWords = new(StringComparer.Ordinal)
    {
        ["best"] = DiscoverSorts.Rating, ["top"] = DiscoverSorts.Rating, ["greatest"] = DiscoverSorts.Rating,
        ["top rated"] = DiscoverSorts.Rating, ["top-rated"] = DiscoverSorts.Rating, ["highest rated"] = DiscoverSorts.Rating,
        ["highest-rated"] = DiscoverSorts.Rating, ["best rated"] = DiscoverSorts.Rating, ["best-rated"] = DiscoverSorts.Rating,
        ["acclaimed"] = DiscoverSorts.Rating, ["critically acclaimed"] = DiscoverSorts.Rating,
        ["popular"] = DiscoverSorts.Popular, ["most popular"] = DiscoverSorts.Popular, ["trending"] = DiscoverSorts.Popular,
        ["newest"] = DiscoverSorts.Newest, ["latest"] = DiscoverSorts.Newest, ["new"] = DiscoverSorts.Newest, ["most recent"] = DiscoverSorts.Newest,
        ["oldest"] = DiscoverSorts.Oldest,
    };

    private static readonly Dictionary<string, string> SortText = new(StringComparer.Ordinal)
    {
        [DiscoverSorts.Rating] = "Best rated", [DiscoverSorts.Popular] = "Most popular",
        [DiscoverSorts.Newest] = "Newest", [DiscoverSorts.Oldest] = "Oldest",
    };

    private static readonly Dictionary<string, int> NumberWords = new(StringComparer.Ordinal)
    {
        ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8,
        ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["fifteen"] = 15, ["twenty"] = 20,
    };

    private static readonly Dictionary<string, int> DecadeWords = new(StringComparer.Ordinal)
    {
        ["twenties"] = 1920, ["thirties"] = 1930, ["forties"] = 1940, ["fifties"] = 1950,
        ["sixties"] = 1960, ["seventies"] = 1970, ["eighties"] = 1980, ["nineties"] = 1990,
    };

    /// <summary>Words that ask for something without saying what, read and passed over.</summary>
    private static readonly HashSet<string> Filler = new(
    [
        "the", "a", "an", "of", "all", "time", "all-time", "ever", "some", "any", "and", "show me", "give me", "find",
        "find me", "list", "i want", "to watch", "good", "great", "that are", "which are", "rated", "ranked", "made", "released",
    ], StringComparer.Ordinal);

    /// <summary>Words that make the words after them a name, and what that name is taken for.</summary>
    private static readonly Dictionary<string, string> Markers = new(StringComparer.Ordinal)
    {
        ["with"] = "with", ["starring"] = "person", ["featuring"] = "person", ["by"] = "person", ["directed by"] = "person",
        ["from director"] = "person", ["about"] = "about", ["involving"] = "about", ["like"] = "like", ["similar to"] = "like",
    };

    private static readonly string[] Exclusions = ["not", "no", "without", "except", "excluding", "but not"];

    /// <summary>The question the words ask, or null when they are no question.</summary>
    public static Question? Read(string text, DateOnly today, QuestionVocabulary vocabulary)
    {
        var words = Words(text);
        if (words.Count == 0) return null;
        var reading = new Reading(words, today, vocabulary);
        return reading.Run();
    }

    /// <summary>The words, in lower case, split at spaces and commas, without the marks that end a sentence.</summary>
    internal static List<string> Words(string text) =>
        [.. text.ToLowerInvariant().Replace('’', '\'').Replace('–', '-')
            .Split([' ', ',', '\t', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => w.Trim('?', '!', '.', '"', ':'))
            .Where(w => w.Length > 0)];

    /// <summary>
    /// The genres of <paramref name="list"/> a phrase names: by name, by its singular, or by a word of
    /// one name (<see cref="CatalogueSearch.MatchGenre"/>), else by the common words the lists spell
    /// otherwise. Empty when the list has none.
    /// </summary>
    private static List<CatalogueGenre> GenresNamed(IReadOnlyList<CatalogueGenre> list, string phrase)
    {
        foreach (var form in Singulars(phrase))
            if (CatalogueSearch.MatchGenre(list, form) is { } genre) return [genre];
        if (!GenreAliases.TryGetValue(phrase, out var alias)) return [];
        return [.. alias.Names.Select(n => CatalogueSearch.MatchGenre(list, n)).OfType<CatalogueGenre>().DistinctBy(g => g.Id)];
    }

    private static IEnumerable<string> Singulars(string word)
    {
        yield return word;
        if (word.EndsWith("ies") && word.Length > 4) yield return word[..^3] + "y";
        if (word.EndsWith('s') && word.Length > 3) yield return word[..^1];
    }

    private sealed class Reading(List<string> words, DateOnly today, QuestionVocabulary vocabulary)
    {
        private readonly Dictionary<string, LanguageName> _languageNames = vocabulary.Languages
            .Where(l => l.Code != "xx" && !string.IsNullOrWhiteSpace(l.Name))
            .GroupBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        /// <summary>The genre words typed, in order, matching the question's <c>genre</c> and <c>exclude</c> parts.</summary>
        private readonly List<string> _mentions = [];
        private readonly List<QuestionPart> _parts = [];
        private readonly List<string> _genres = [];
        private readonly List<string> _excluded = [];
        private readonly List<string> _people = [];
        private readonly List<string> _about = [];
        private readonly List<string> _withEither = [];
        private readonly List<int> _unread = [];
        private string? _kind;
        private string? _sort;
        private int? _count;
        private int? _from, _to;
        private bool _bareYear;
        private string? _language;
        private int? _minRuntime, _maxRuntime;
        private string? _like;
        private bool _anime;

        public Question? Run()
        {
            var at = 0;
            while (at < words.Count)
            {
                var read = Part(at, consume: true);
                if (read > 0) { at += read; continue; }
                if (Marker(at) is { } marker)
                {
                    at += Span(at + marker.Length, marker.Taken);
                    at += marker.Length;
                    continue;
                }
                _unread.Add(at);
                at++;
            }

            // Words no rule reads are a name, when they come first and something after them says what
            // of theirs is wanted.
            string? named = null;
            if (_unread.Count > 0)
            {
                var leading = _unread.Select((w, i) => w == i).All(b => b);
                if (!leading || (_kind is null && _genres.Count == 0 && !_anime)) return null;
                named = string.Join(' ', _unread.Select(i => words[i]));
            }

            var asks = _parts.Count(p => p.Part != "kind") + (named is null ? 0 : 1);
            if (asks == 0) return null;
            if (asks == 1 && _bareYear && _kind is null) return null;

            var kind = _kind ?? "film";
            var genres = Named(_genres, kind);
            var excluded = Named(_excluded, kind);
            ShowGenreParts(kind);
            var newest = _sort == DiscoverSorts.Newest;
            return new Question
            {
                Request = new DiscoverRequest
                {
                    Kind = _kind,
                    Genres = genres,
                    Exclude = excluded,
                    IncludeFamily = genres.Contains("Animation") || genres.Contains("Family"),
                    From = _from,
                    To = _to,
                    With = [.. _people],
                    About = [.. _about],
                    Language = _language,
                    Like = _like,
                    GoodOnly = !newest,
                    MinVotes = newest ? NewestVotes : null,
                    MinRuntime = _minRuntime,
                    MaxRuntime = _maxRuntime,
                    Sort = _sort,
                    Limit = _count ?? CatalogueSearch.MostListed,
                    Category = _anime ? "anime" : null,
                },
                Parts = _parts,
                Named = named,
                WithEither = _withEither,
            };
        }

        /// <summary>The genres the typed phrases name in the list of <paramref name="kind"/>; a phrase that list lacks keeps the other list's name, for the discovery to refuse.</summary>
        private List<string> Named(List<string> phrases, string kind) =>
            [.. phrases.SelectMany(p => Resolve(p, kind).Select(g => g.Name)).Distinct()];

        private List<CatalogueGenre> Resolve(string phrase, string kind)
        {
            var (own, other) = kind == "show" ? (vocabulary.ShowGenres, vocabulary.FilmGenres) : (vocabulary.FilmGenres, vocabulary.ShowGenres);
            return GenresNamed(own, phrase) is { Count: > 0 } named ? named : GenresNamed(other, phrase);
        }

        /// <summary>Gives each genre and exclusion part the name its kind's list has for the typed words.</summary>
        private void ShowGenreParts(string kind)
        {
            var mention = 0;
            for (var i = 0; i < _parts.Count; i++)
            {
                if (_parts[i].Part is not ("genre" or "exclude")) continue;
                var phrase = _mentions[mention++];
                var named = Resolve(phrase, kind);
                var text = GenreAliases.TryGetValue(phrase, out var alias) && alias.Label is { } label && named.Count == alias.Names.Length
                    ? label
                    : string.Join(", ", named.Select(g => g.Name));
                _parts[i] = _parts[i] with { Text = _parts[i].Part == "exclude" ? "Not " + text : text };
            }
        }

        /// <summary>
        /// How many words a part reads at <paramref name="at"/>, or 0 for none; with
        /// <paramref name="consume"/> false only whether one starts there, recording nothing.
        /// </summary>
        private int Part(int at, bool consume) =>
            Asked(at, consume) is > 0 and var n ? n
            : Phrase(at, 3, Filler.Contains);

        /// <summary>How many words a part that asks something reads at <paramref name="at"/>: any but a filler word.</summary>
        private int Asked(int at, bool consume) =>
            Count(at, consume) is > 0 and var c ? c
            : Exclusion(at, consume) is > 0 and var e ? e
            : Years(at, consume) is > 0 and var y ? y
            : Runtime(at, consume) is > 0 and var r ? r
            : Language(at, consume) is > 0 and var l ? l
            : Kind(at, consume) is > 0 and var k ? k
            : Genre(at, consume) is > 0 and var g ? g
            : Sort(at, consume) is > 0 and var s ? s
            : Word(at) == "anime" ? Anime(1, consume)
            : 0;

        /// <summary>The longest phrase of up to <paramref name="longest"/> words at <paramref name="at"/> that <paramref name="known"/> accepts.</summary>
        private int Phrase(int at, int longest, Func<string, bool> known)
        {
            for (var n = Math.Min(longest, words.Count - at); n >= 1; n--)
                if (known(string.Join(' ', words.Skip(at).Take(n)))) return n;
            return 0;
        }

        private string Joined(int at, int n) => string.Join(' ', words.Skip(at).Take(n));

        private string? Word(int at) => at < words.Count ? words[at] : null;

        private int Anime(int read, bool consume)
        {
            if (consume && !_anime)
            {
                _anime = true;
                _parts.Add(new QuestionPart("category", "Anime"));
            }
            return read;
        }

        private int Kind(int at, bool consume)
        {
            var n = Phrase(at, 2, KindWords.ContainsKey);
            if (n == 0 || !consume) return n;
            var kind = KindWords[Joined(at, n)];
            if (_kind is null)
            {
                _kind = kind;
                _parts.Add(new QuestionPart("kind", kind == "show" ? "Shows" : "Movies"));
            }
            return n;
        }

        /// <summary>Whether a phrase names a genre in either of the database's lists.</summary>
        private bool IsGenre(string phrase) =>
            GenresNamed(vocabulary.FilmGenres, phrase).Count > 0 || GenresNamed(vocabulary.ShowGenres, phrase).Count > 0;

        private int Genre(int at, bool consume)
        {
            var n = Phrase(at, 2, IsGenre);
            if (n == 0 || !consume) return n;
            var phrase = Joined(at, n);
            _genres.Add(phrase);
            _mentions.Add(phrase);
            _parts.Add(new QuestionPart("genre", phrase));
            return n;
        }

        private int Exclusion(int at, bool consume)
        {
            var lead = Phrase(at, 2, Exclusions.Contains);
            if (lead == 0) return 0;
            var n = Phrase(at + lead, 2, IsGenre);
            if (n == 0) return 0;
            if (consume)
            {
                var phrase = Joined(at + lead, n);
                _excluded.Add(phrase);
                _mentions.Add(phrase);
                _parts.Add(new QuestionPart("exclude", phrase));
            }
            return lead + n;
        }

        private int Sort(int at, bool consume)
        {
            var n = Phrase(at, 2, SortWords.ContainsKey);
            if (n == 0 || !consume) return n;
            var sort = SortWords[Joined(at, n)];
            if (_sort is null)
            {
                _sort = sort;
                if (_count is null || sort != DiscoverSorts.Rating) _parts.Add(new QuestionPart("sort", SortText[sort]));
            }
            return n;
        }

        private int Language(int at, bool consume)
        {
            var lead = Word(at) is "in" or "spoken" ? (Word(at) == "spoken" && Word(at + 1) == "in" ? 2 : 1) : 0;
            var n = Phrase(at + lead, 2, _languageNames.ContainsKey);
            if (n == 0) return 0;
            if (consume && _language is null)
            {
                var language = _languageNames[Joined(at + lead, n)];
                _language = language.Code;
                _parts.Add(new QuestionPart("language", language.Name));
            }
            return lead + n;
        }

        /// <summary>A count, which also orders by rating: <c>top 10</c>, <c>10 best</c>, <c>best ten</c>.</summary>
        private int Count(int at, bool consume)
        {
            int? number = null;
            var n = 0;
            if (Word(at) is "top" or "best" or "greatest" && Number(Word(at + 1)) is { } after)
            {
                number = after;
                n = 2;
                if (Word(at + 2) is "best" or "greatest" or "top") n = 3;
            }
            else if (Number(Word(at)) is { } before && Word(at + 1) is "best" or "greatest" or "top")
            {
                number = before;
                n = 2;
            }
            if (number is not { } count || !consume) return n;

            _count = Math.Clamp(count, 1, MostCounted);
            _sort ??= DiscoverSorts.Rating;
            _parts.RemoveAll(p => p.Part == "sort" && _sort == DiscoverSorts.Rating);
            _parts.Add(new QuestionPart("count", $"Top {_count}"));
            return n;
        }

        private static int? Number(string? word) =>
            word is null ? null
            : NumberWords.TryGetValue(word, out var named) ? named
            : word.Length is > 0 and <= 3 && word.All(char.IsAsciiDigit) && int.Parse(word) is > 0 and var n ? n
            : null;

        /// <summary>Years, a range of them, a decade or a bound: <c>2000</c>, <c>from 2000 to 2005</c>, <c>the 90s</c>, <c>before 1980</c>.</summary>
        private int Years(int at, bool consume)
        {
            var i = at;
            var lead = Word(i);
            string? bound = null;
            if (lead is "from" or "in" or "of" or "during")
                i++;
            else if (lead is "since" or "after" or "before" or "until" or "till")
            {
                bound = lead;
                i++;
            }
            else if (lead == "up" && Word(i + 1) == "to")
            {
                bound = "until";
                i += 2;
            }
            else if (lead == "between")
            {
                bound = "between";
                i++;
            }

            if (Word(i) == "the") i++;

            // Relative years.
            if (bound is null && Word(i) is "this" or "last" && Word(i + 1) == "year")
            {
                var year = Word(i) == "this" ? today.Year : today.Year - 1;
                return Record(at, i + 2, year, year, false, consume, year.ToString());
            }
            if (bound is null && Word(i) is "last" or "past" && Number(Word(i + 1)) is { } span && Word(i + 2) is "years" or "year")
                return Record(at, i + 3, today.Year - span, today.Year, false, consume, $"Since {today.Year - span}");
            if (bound is null && i == at && Word(i) is "recent")
                return Record(at, i + 1, today.Year - 2, today.Year, false, consume, $"Since {today.Year - 2}");

            if (Decade(i) is { } decade)
            {
                var end = i + decade.Words;
                return bound switch
                {
                    "since" => Record(at, end, decade.From, null, false, consume, $"Since {decade.From}"),
                    "after" => Record(at, end, decade.To + 1, null, false, consume, $"Since {decade.To + 1}"),
                    "before" => Record(at, end, null, decade.From - 1, false, consume, $"Before {decade.From}"),
                    "until" => Record(at, end, null, decade.To, false, consume, $"Until {decade.To}"),
                    "between" => 0,
                    _ => Record(at, end, decade.From, decade.To, false, consume, decade.Text),
                };
            }

            if (Range(Word(i)) is { } joined)
                return bound is null ? Record(at, i + 1, joined.From, joined.To, false, consume, $"{joined.From}–{joined.To}") : 0;

            if (Year(Word(i)) is not { } first) return 0;
            if (bound == "between")
            {
                if (Word(i + 1) != "and" || Year(Word(i + 2)) is not { } last || last < first) return 0;
                return Record(at, i + 3, first, last, false, consume, $"{first}–{last}");
            }
            if (bound is null && Word(i + 1) is "to" or "until" or "through" or "-" && Year(Word(i + 2)) is { } upTo && upTo >= first)
                return Record(at, i + 3, first, upTo, false, consume, $"{first}–{upTo}");
            return bound switch
            {
                "since" => Record(at, i + 1, first, null, false, consume, $"Since {first}"),
                "after" => Record(at, i + 1, first + 1, null, false, consume, $"Since {first + 1}"),
                "before" => Record(at, i + 1, null, first - 1, false, consume, $"Before {first}"),
                "until" => Record(at, i + 1, null, first, false, consume, $"Until {first}"),
                _ => Record(at, i + 1, first, first, i == at, consume, first.ToString()),
            };
        }

        private int Record(int at, int end, int? from, int? to, bool bare, bool consume, string text)
        {
            if (!consume) return end - at;
            if (_from is null && _to is null)
            {
                _from = from;
                _to = to;
                _bareYear = bare;
                _parts.Add(new QuestionPart("years", text));
            }
            return end - at;
        }

        private int? Year(string? word) =>
            word is { Length: 4 } && word.All(char.IsAsciiDigit) && int.Parse(word) is var year && year >= 1870 && year <= today.Year + 10
                ? year : null;

        private (int From, int To)? Range(string? word)
        {
            if (word is null || word.Split('-') is not [var a, var b]) return null;
            return Year(a) is { } from && Year(b) is { } to && to >= from ? (from, to) : null;
        }

        /// <summary>A decade, maybe its early, middle or late part: <c>1990s</c>, <c>90s</c>, <c>'90s</c>, <c>nineties</c>, <c>late 90s</c>.</summary>
        private (int From, int To, int Words, string Text)? Decade(int at)
        {
            var part = Word(at) is "early" or "mid" or "late" ? Word(at) : null;
            var i = part is null ? at : at + 1;
            if (Word(i) is not { } word) return null;

            int? start = DecadeWords.TryGetValue(word, out var named) ? named : null;
            var digits = word.TrimStart('\'');
            if (start is null && digits.EndsWith('s'))
            {
                digits = digits[..^1].TrimEnd('\'');
                if (digits.Length == 4 && digits.All(char.IsAsciiDigit) && digits.EndsWith('0')) start = int.Parse(digits);
                else if (digits.Length == 2 && digits.All(char.IsAsciiDigit) && digits.EndsWith('0'))
                {
                    var tens = int.Parse(digits);
                    start = tens <= 20 ? 2000 + tens : 1900 + tens;
                }
            }
            if (start is not { } from || from > today.Year) return null;

            var label = $"{from}s";
            return part switch
            {
                "early" => (from, from + 3, 2, "Early " + label),
                "mid" => (from + 3, from + 6, 2, "Mid " + label),
                "late" => (from + 6, from + 9, 2, "Late " + label),
                _ => (from, from + 9, 1, label),
            };
        }

        /// <summary>A bound on length: <c>under 90 minutes</c>, <c>over 2 hours</c>, <c>less than 100 min</c>.</summary>
        private int Runtime(int at, bool consume)
        {
            var lead = Phrase(at, 2, w => w is "under" or "over" or "less than" or "more than" or "shorter than" or "longer than" or "at most" or "at least");
            if (lead == 0) return 0;
            var longer = Joined(at, lead) is "over" or "more than" or "longer than" or "at least";

            var i = at + lead;
            if (Word(i) is not { } amount) return 0;
            int? minutes = null;
            var n = 0;
            if (Number(amount) is { } number && Word(i + 1) is { } unit)
            {
                minutes = unit is "minutes" or "minute" or "mins" or "min" ? number
                    : unit is "hours" or "hour" or "hrs" or "hr" or "h" ? number * 60 : null;
                n = 2;
            }
            else if (amount.EndsWith("min") && Number(amount[..^3]) is { } m) { minutes = m; n = 1; }
            else if (amount.EndsWith('h') && Number(amount[..^1]) is { } h) { minutes = h * 60; n = 1; }
            if (minutes is not { } bound) return 0;

            if (consume)
            {
                if (longer) _minRuntime = bound; else _maxRuntime = bound;
                _parts.Add(new QuestionPart("runtime", (longer ? "Over " : "Under ") + bound + " min"));
            }
            return lead + n;
        }

        /// <summary>Whether a name ends before the word at <paramref name="at"/>.</summary>
        private bool Ends(int at)
        {
            if (Marker(at) is not null || Asked(at, consume: false) > 0) return true;
            var i = at;
            while (i < words.Count && Phrase(i, 3, Filler.Contains) is > 0 and var n) i += n;
            return i > at && (i >= words.Count || Marker(i) is not null || Asked(i, consume: false) > 0);
        }

        private (int Length, string Taken)? Marker(int at)
        {
            var n = Phrase(at, 2, Markers.ContainsKey);
            return n == 0 || at + n >= words.Count ? null : (n, Markers[Joined(at, n)]);
        }

        /// <summary>
        /// Reads the name after a marker: its first word whatever it is, then every word up to where
        /// another part or marker starts. A subject's leading article is not part of it. Filler words
        /// end the name only when nothing but other parts, or the end, follows them (<c>keanu reeves of
        /// all time</c>), and belong to it otherwise (<c>the end of the world</c>). Names joined by
        /// <c>and</c> are each a name.
        /// </summary>
        private int Span(int at, string taken)
        {
            var start = at;
            if (taken == "about")
                while (start + 1 < words.Count && words[start] is "the" or "a" or "an") start++;
            var end = start + 1;
            while (end < words.Count && !Ends(end)) end++;

            var span = words.Skip(start).Take(end - start).ToList();
            var names = taken == "like"
                ? [string.Join(' ', span)]
                : string.Join(' ', span).Split(" and ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var name in names)
            {
                switch (taken)
                {
                    case "person":
                        _people.Add(name);
                        _parts.Add(new QuestionPart("person", name));
                        break;
                    case "about":
                        _about.Add(name);
                        _parts.Add(new QuestionPart("about", name));
                        break;
                    case "with":
                        _withEither.Add(name);
                        _parts.Add(new QuestionPart("person", name));
                        break;
                    case "like" when _like is null:
                        _like = name;
                        _parts.Add(new QuestionPart("like", "Like " + name));
                        break;
                }
            }
            return end - at;
        }
    }
}
