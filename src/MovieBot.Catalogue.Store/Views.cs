namespace TheKrystalShip.MovieBot.Catalogue;

/// <summary>
/// The catalogue's shapes as they go on the wire (authority: <c>docs/api-contract.md</c>, Catalogue).
/// One card wherever a title is listed, one page for a title. Every title is named by its IMDb id, and
/// every image by its URL under <see cref="ImageUrls"/>.
/// </summary>
public static class ImageUrls
{
    /// <summary>An image's URL: the route serves TMDB's file at a size, and a file never changes under its name.</summary>
    public static string? Of(string? path, string size) => path is { Length: > 1 } ? $"/images/{size}{path}" : null;

    public const string Poster = "w500";
    public const string Backdrop = "w1280";
    public const string Logo = "w500";
    public const string Portrait = "w185";
    public const string Still = "w300";
}

/// <summary>The rating a card shows: IMDb's where IMDb rates the title, the film database's otherwise; no value under 25 votes.</summary>
public sealed record Rating(double? Value, int Votes, string By);

/// <summary>Both sources' ratings, side by side.</summary>
public sealed record SourceRatings(Rating? Imdb, Rating? Tmdb);

/// <summary>A person's part in a title, on a list filtered by that person.</summary>
public sealed record Credit(string Department, string Role);

/// <summary>What the asking person has to do with a title, joined by the API.</summary>
public sealed record Viewer(bool InLibrary, string? LibraryId, bool Watched, bool Wanted);

/// <summary>A title wherever one is listed.</summary>
public record TitleCard
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required string Title { get; init; }
    public string? OriginalTitle { get; init; }
    public int? Year { get; init; }
    public int? EndYear { get; init; }
    public string? Released { get; init; }
    public int? Runtime { get; init; }
    public required string Category { get; init; }
    public List<string> Genres { get; init; } = [];
    public string? Language { get; init; }
    public Rating? Rating { get; init; }
    public string? Poster { get; init; }
    public string? Backdrop { get; init; }
    public string? Plot { get; init; }
    public List<string> MadeBy { get; init; } = [];
    public List<string> Starring { get; init; } = [];
    public DateTimeOffset? ChangedAt { get; init; }
    public Credit? Credit { get; init; }
    public Viewer? You { get; init; }
}

/// <summary>Something a title names by id and name: a keyword, a company, a Wikidata item, a franchise.</summary>
public sealed record Named(string Id, string Name);

public sealed record CastMember(long PersonId, string Name, string? Character, string? Portrait);

public sealed record CrewMember(long PersonId, string Name, string Job);

public sealed record Trailer(string Key, string Name, string Kind);

/// <summary>A work a title is based on, and its author where Wikidata names one.</summary>
public sealed record Source(Named Work, List<Named> Authors);

/// <summary>What Wikidata holds about a title that a page shows.</summary>
public sealed record Facts
{
    public List<Named> SetIn { get; init; } = [];
    public List<Named> FilmedIn { get; init; } = [];
    public List<Source> BasedOn { get; init; } = [];
    public List<Named> Awards { get; init; } = [];
    public int Nominations { get; init; }
    public List<Named> Subjects { get; init; } = [];
}

/// <summary>A franchise a title is in, and its place there where its series numbers it.</summary>
public sealed record FranchiseRef(string Id, string Name, string? Ordinal);

public sealed record SeasonSummary(int Number, string? Name, string? AirDate, string? Poster, int Episodes);

public sealed record NextEpisode(int Season, int Number, string? Name, string AirDate);

/// <summary>A title's page: its card, and everything the catalogue holds about it.</summary>
public sealed record TitlePage : TitleCard
{
    public string? Overview { get; init; }
    public string? Tagline { get; init; }
    public string? Status { get; init; }
    public string? Logo { get; init; }
    public required SourceRatings Ratings { get; init; }
    public Dictionary<string, string> Certificates { get; init; } = [];
    public List<string> Countries { get; init; } = [];
    public List<string> Languages { get; init; } = [];
    public List<Named> Companies { get; init; } = [];
    public List<Named> Networks { get; init; } = [];
    public List<Named> Keywords { get; init; } = [];
    public List<CastMember> Cast { get; init; } = [];
    public List<CrewMember> Crew { get; init; } = [];
    public List<Trailer> Trailers { get; init; } = [];
    public required Facts Facts { get; init; }
    public List<FranchiseRef> Franchises { get; init; } = [];
    public List<TitleCard> Follows { get; init; } = [];
    public List<TitleCard> FollowedBy { get; init; } = [];
    public long? Budget { get; init; }
    public long? Revenue { get; init; }
    public List<SeasonSummary> Seasons { get; init; } = [];
    public NextEpisode? NextEpisode { get; init; }
    public List<TitleCard> Similar { get; init; } = [];
}

public sealed record Episode(int Number, string? Name, string? Overview, string? AirDate, int? Runtime, string? Still);

public sealed record Season(int Number, string? Name, string? Overview, string? AirDate, string? Poster, List<Episode> Episodes);

/// <summary>A page of cards: <see cref="Total"/> across every page, <see cref="Next"/> the next page's number while there is one.</summary>
public sealed record CardPage(List<TitleCard> Titles, int Total, int? Next);

public sealed record FacetOption(string Value, string? Label, int Count);

public sealed record FacetSection(string Id, List<FacetOption> Options);

/// <summary>Every section's options over the titles a query names, each counted with the query's other sections' picks applied.</summary>
public sealed record Facets(int Total, List<FacetSection> Sections);

public sealed record Person(long Id, string Name, string? Portrait, string? Department, int Titles);

/// <summary>A franchise: a film database collection (<c>c…</c>) or a Wikidata universe (<c>Q…</c>).</summary>
public sealed record FranchiseCard(string Id, string Kind, string Name, string? Poster, string? Backdrop, int Titles, int? FirstYear, int? LastYear);

public sealed record Franchise(FranchiseCard Card, List<FranchiseCard> Parts);

public sealed record FranchisePage(List<FranchiseCard> Franchises, int Total, int? Next);

/// <summary>A word resolved to what a list query takes: its filter, the value it takes, and a name.</summary>
public sealed record Term(string Filter, string Value, string Name, string? Detail);
