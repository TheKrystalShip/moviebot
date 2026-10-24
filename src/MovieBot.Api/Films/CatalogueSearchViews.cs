namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>
/// A film or a show a search, a discovery or a list of the week names, joined with the library: the
/// card a person picks from. Its page is at the IMDb id, and everything else about it is asked there.
/// </summary>
public sealed record TitleHitView
{
    /// <summary>The page's address. Absent for a title the database holds no IMDb id for, as an announced one often is: it has no page and no poster here.</summary>
    public string? ImdbId { get; init; }

    /// <summary>The database's own id, which names a title that has no IMDb id.</summary>
    public required int TmdbId { get; init; }

    /// <summary><c>film</c> or <c>show</c>.</summary>
    public required string Kind { get; init; }

    public required string Title { get; init; }
    public int? Year { get; init; }

    /// <summary>The day it was released or first aired, <c>2025-01-16</c>; absent while the database has none.</summary>
    public string? ReleaseDate { get; init; }

    public required List<string> Genres { get; init; }
    public double? Rating { get; init; }
    public int Votes { get; init; }

    /// <summary>IMDb's rating out of ten, to one decimal; absent when the IMDb import does not hold the title.</summary>
    public double? ImdbRating { get; init; }

    /// <summary>The votes behind <see cref="ImdbRating"/>.</summary>
    public int? ImdbVotes { get; init; }

    /// <summary>The first sentence of what it is about, cut at 200 characters; the title's page holds the rest.</summary>
    public string? Plot { get; init; }

    /// <summary>A film's directors, a show's creators.</summary>
    public required List<string> MadeBy { get; init; }

    /// <summary>The two top-billed actors.</summary>
    public required List<string> Starring { get; init; }

    /// <summary>The language it was made in, an ISO 639-1 code.</summary>
    public string? OriginalLanguage { get; init; }

    /// <summary>The poster its page shows, served from here; absent when the database has none.</summary>
    public string? PosterUrl { get; init; }

    /// <summary>Whether the library holds the film, or any episode of the show.</summary>
    public bool InLibrary { get; init; }

    /// <summary>For a film the library holds, the title that plays it.</summary>
    public string? LibraryId { get; init; }

    /// <summary>Whether the asking person watched the film through; never set for a show, or for the service key.</summary>
    public bool Watched { get; init; }

    /// <summary>The shelf it is on beyond its kind, <c>anime</c> or <c>general</c>; absent while nothing says.</summary>
    public Library.TitleCategory? Category { get; init; }
}

/// <summary>What <c>/api/catalogue/search</c> answers.</summary>
public sealed record CatalogueSearchView
{
    public required string Query { get; init; }
    public required List<TitleHitView> Titles { get; init; }
}

/// <summary>A person a name search found.</summary>
public sealed record PersonMatchView
{
    public required int PersonId { get; init; }
    public required string Name { get; init; }

    /// <summary>The department the database knows them for: <c>Acting</c>, <c>Directing</c>…</summary>
    public string? KnownFor { get; init; }

    /// <summary>Their portrait, served from here once their credits have been asked for.</summary>
    public string? PortraitUrl { get; init; }

    /// <summary>Up to three titles they are best known for, each <c>Name (year)</c>.</summary>
    public required List<string> KnownForTitles { get; init; }
}

/// <summary>What <c>/api/catalogue/people</c> answers: the people a name could mean, the best match first.</summary>
public sealed record CataloguePeopleView
{
    public required string Query { get; init; }
    public required List<PersonMatchView> People { get; init; }
}

/// <summary>The release statuses of a credit.</summary>
public static class ReleaseStatuses
{
    public const string Released = "released";
    public const string Upcoming = "upcoming";
    public const string Announced = "announced";
}

/// <summary>One title a person is credited on.</summary>
public sealed record CreditView
{
    /// <summary>The database's own id, for a credit with no IMDb id to name it by.</summary>
    public required int TmdbId { get; init; }

    /// <summary>Absent for a title the database holds no IMDb id for, as an announcement often is; it has no page here.</summary>
    public string? ImdbId { get; init; }

    public required string Kind { get; init; }
    public required string Title { get; init; }
    public int? Year { get; init; }

    /// <summary>The day it was released or first aired, <c>2025-01-16</c>; absent for an announcement without a date.</summary>
    public string? ReleaseDate { get; init; }

    /// <summary><c>released</c>, <c>upcoming</c> (dated in the future) or <c>announced</c> (no date, or only planned or rumoured).</summary>
    public required string Status { get; init; }

    /// <summary>The parts played and jobs done on it, joined by a middle dot.</summary>
    public string? Role { get; init; }

    public double? Rating { get; init; }
    public int Votes { get; init; }

    /// <summary>IMDb's rating out of ten, to one decimal; absent when the IMDb import does not hold the title.</summary>
    public double? ImdbRating { get; init; }

    /// <summary>The votes behind <see cref="ImdbRating"/>.</summary>
    public int? ImdbVotes { get; init; }
    public string? PosterUrl { get; init; }
    public bool InLibrary { get; init; }
    public string? LibraryId { get; init; }
    public bool Watched { get; init; }

    /// <summary>The shelf it is on beyond its kind, <c>anime</c> or <c>general</c>; absent while nothing says.</summary>
    public Library.TitleCategory? Category { get; init; }
}

/// <summary>What <c>/api/catalogue/people/{personId}/credits</c> answers: the credits asked for, oldest first.</summary>
public sealed record PersonCreditsView
{
    public required int PersonId { get; init; }
    public required string Name { get; init; }
    public string? KnownFor { get; init; }
    public string? PortraitUrl { get; init; }

    /// <summary>The day the statuses are read against, <c>2025-01-16</c>.</summary>
    public required string AsOf { get; init; }

    /// <summary>How many credits match, which is more than <c>credits</c> holds when it was cut to the newest.</summary>
    public int Total { get; init; }

    public required List<CreditView> Credits { get; init; }
}

/// <summary>What a discovery matched each of its words to.</summary>
public sealed record DiscoverResolved
{
    public required List<string> Genres { get; init; }

    /// <summary>The genres left out of the results, named or by default.</summary>
    public List<string> Excluded { get; init; } = [];

    public required List<ResolvedPerson> People { get; init; }
    public required List<string> Keywords { get; init; }

    /// <summary>The streaming services the results are on, by the names the film database lists them under.</summary>
    public List<string> Providers { get; init; } = [];
    public string? Language { get; init; }

    /// <summary>The title the results are similar to, as <c>Name (year)</c>.</summary>
    public string? Like { get; init; }

    public double? MinRating { get; init; }
    public int? MinVotes { get; init; }
    public required string Sort { get; init; }
}

public sealed record ResolvedPerson(int PersonId, string Name, string? KnownFor);

/// <summary>
/// What <c>/api/catalogue/ask</c> answers: whether the words are a question, the parts they were read
/// as, and the titles the question found, or why it found none.
/// </summary>
public sealed record CatalogueQuestionView
{
    public required string Query { get; init; }
    public required bool Question { get; init; }
    public required List<QuestionPart> Parts { get; init; }

    /// <summary><c>film</c> or <c>show</c>; absent when nothing was discovered.</summary>
    public string? Kind { get; init; }

    public DiscoverResolved? Resolved { get; init; }
    public required List<TitleHitView> Titles { get; init; }

    /// <summary>The page of titles that follows this one; absent on the last.</summary>
    public int? Next { get; init; }

    /// <summary>Why a question found nothing: the film database's refusal of a word in it.</summary>
    public string? Message { get; init; }
}

/// <summary>What <c>/api/catalogue/discover</c> answers.</summary>
public sealed record CatalogueDiscoverView
{
    public required string Kind { get; init; }
    public required DiscoverResolved Resolved { get; init; }
    public required List<TitleHitView> Titles { get; init; }

    /// <summary>The page of titles that follows this one; absent on the last.</summary>
    public int? Next { get; init; }
}

/// <summary>What <c>/api/catalogue/released</c> and <c>/api/catalogue/upcoming</c> answer.</summary>
public sealed record CatalogueListView
{
    public required string Kind { get; init; }

    /// <summary>The first and last day of the window the list is of, <c>2025-01-16</c>.</summary>
    public required string From { get; init; }
    public required string To { get; init; }

    public required List<TitleHitView> Titles { get; init; }
}

/// <summary>A film or a show the library gained.</summary>
public sealed record LibraryAdditionView
{
    /// <summary>Absent for a title the library could not name.</summary>
    public string? ImdbId { get; init; }

    public required string Kind { get; init; }
    public required string Title { get; init; }
    public int? Year { get; init; }
    public required List<string> Genres { get; init; }
    public double? Rating { get; init; }
    public string? Plot { get; init; }
    public string? PosterUrl { get; init; }

    /// <summary>The title that plays a film; a show's first episode.</summary>
    public required string LibraryId { get; init; }

    /// <summary>When the film came in, or the show's latest episode did.</summary>
    public DateTimeOffset AddedAt { get; init; }

    public bool Watched { get; init; }

    /// <summary>The shelf it is on beyond its kind, <c>anime</c> or <c>general</c>; absent while nothing says.</summary>
    public Library.TitleCategory? Category { get; init; }
}

/// <summary>What <c>/api/catalogue/added</c> answers: the library's newest films and shows, newest first.</summary>
public sealed record LibraryAdditionsView
{
    public required List<LibraryAdditionView> Titles { get; init; }
}
