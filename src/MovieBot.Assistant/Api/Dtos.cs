using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheKrystalShip.MovieBot.Assistant.Api;

// What the API answers, as far as the tools read it. The shapes are docs/api-contract.md's (the catalogue's
// are MovieBot.Core's CatalogueViews); a field a tool never reads is not declared here. A field the API
// leaves out is null or empty, never a default that reads as a fact.

/// <summary>The rating a card shows: IMDb's where IMDb rates the title, the film database's otherwise; no value under 25 votes.</summary>
public sealed record CardRating(double? Value, int Votes, string By);

/// <summary>A person's part in a title, on a list filtered by that person: the department and the part or the job.</summary>
public sealed record CardCredit(string Department, string Role);

/// <summary>What the asking person has to do with a title, joined by the API.</summary>
public sealed record Viewer(bool InLibrary, string? LibraryId, bool Watched, bool Wanted);

/// <summary>A title wherever the catalogue lists one, named by its IMDb id, which is its page.</summary>
public record CatalogueCard
{
    public string Id { get; init; } = "";
    public string Kind { get; init; } = "film";
    public string Title { get; init; } = "";
    public string? OriginalTitle { get; init; }
    public int? Year { get; init; }
    public int? EndYear { get; init; }

    /// <summary>The release date, <c>yyyy-MM-dd</c>; absent when the catalogue dates it no day.</summary>
    public string? Released { get; init; }

    public int? Runtime { get; init; }

    /// <summary><c>anime</c> or <c>general</c>: the shelf the site puts it on beyond its kind.</summary>
    public string Category { get; init; } = "general";

    public List<string> Genres { get; init; } = [];

    /// <summary>The original language, as an ISO 639 code.</summary>
    public string? Language { get; init; }

    public CardRating? Rating { get; init; }
    public string? Poster { get; init; }

    /// <summary>The overview's first sentence.</summary>
    public string? Plot { get; init; }

    /// <summary>A film's directors or a show's creators.</summary>
    public List<string> MadeBy { get; init; } = [];

    public List<string> Starring { get; init; } = [];

    /// <summary>On a list filtered by one person, their part in it.</summary>
    public CardCredit? Credit { get; init; }

    public Viewer? You { get; init; }

    public bool InLibrary => You?.InLibrary == true;
    public bool Watched => You?.Watched == true;
}

/// <summary>A page of cards: <see cref="Total"/> across every page.</summary>
public sealed record CardPage
{
    public List<CatalogueCard> Titles { get; init; } = [];
    public int Total { get; init; }
}

/// <summary>Something a title names by id and name: a keyword, a company, a Wikidata item.</summary>
public sealed record Named(string Id, string Name);

public sealed record CastMember(long PersonId, string Name, string? Character);

public sealed record CrewMember(long PersonId, string Name, string Job);

/// <summary>A work a title is based on, and its authors where Wikidata names them.</summary>
public sealed record Source(Named Work, List<Named> Authors);

public sealed record Facts
{
    public List<Named> SetIn { get; init; } = [];
    public List<Source> BasedOn { get; init; } = [];
}

public sealed record FranchiseRef(string Id, string Name, string? Ordinal);

public sealed record SeasonSummary(int Number, string? Name, string? AirDate, int Episodes);

public sealed record NextEpisode(int Season, int Number, string? Name, string AirDate);

/// <summary>A title's page: its card, and what the catalogue holds about it.</summary>
public sealed record TitlePage : CatalogueCard
{
    public string? Overview { get; init; }
    public string? Tagline { get; init; }
    public string? Status { get; init; }
    public Dictionary<string, string> Certificates { get; init; } = [];
    public List<string> Countries { get; init; } = [];
    public List<Named> Networks { get; init; } = [];
    public List<Named> Keywords { get; init; } = [];
    public List<CastMember> Cast { get; init; } = [];
    public List<CrewMember> Crew { get; init; } = [];
    public Facts Facts { get; init; } = new();
    public List<FranchiseRef> Franchises { get; init; } = [];
    public List<SeasonSummary> Seasons { get; init; } = [];
    public NextEpisode? NextEpisode { get; init; }
}

/// <summary>
/// A word resolved to what the list query takes: <see cref="Filter"/> is <c>genre</c>, <c>person</c>,
/// <c>keyword</c>, <c>company</c>, <c>fact</c> or <c>franchise</c>, and <see cref="Value"/> what that
/// filter is given. For a person <see cref="Detail"/> is the department they are known for.
/// </summary>
public sealed record Term(string Filter, string Value, string Name, string? Detail);

public sealed record FacetOption(string Value, string? Label, int Count);

public sealed record FacetSection(string Id, List<FacetOption> Options);

/// <summary>Every facet section's options over the catalogue: the genres and the original languages it holds, among others.</summary>
public sealed record Facets
{
    public List<FacetSection> Sections { get; init; } = [];

    public List<FacetOption> Section(string id) => Sections.FirstOrDefault(s => s.Id == id)?.Options ?? [];
}

/// <summary>A film people are waiting on: how many, and whether the asking person is one of them.</summary>
public sealed record AnticipatedTitle
{
    public string ImdbId { get; init; } = "";
    public string Kind { get; init; } = "film";
    public string Title { get; init; } = "";
    public int? Year { get; init; }
    public string? PosterUrl { get; init; }
    public int Waiters { get; init; }
    public bool Waiting { get; init; }
}

public sealed record Airing(int Season, int Episode, string? Name, string AirDate);

public sealed record ShowFollow(bool AutoDownload, DateTimeOffset Since);

/// <summary>An episode the asking person is in the middle of, or the one after the last they finished.</summary>
public sealed record ResumePoint(string TitleId, int Season, int Episode, double PositionSeconds, double DurationSeconds);

/// <summary>What the asking person has of a show: where they pick up and their follow.</summary>
public sealed record PersonShow
{
    public string ImdbId { get; init; } = "";
    public ResumePoint? Resume { get; init; }
    public ShowFollow? Follow { get; init; }
}

public sealed record EpisodeRef(int Season, int Number);

public sealed record HistoryEntry
{
    public string TitleId { get; init; } = "";
    public string Name { get; init; } = "";
    public bool Finished { get; init; }
    public double PositionSeconds { get; init; }
    public double DurationSeconds { get; init; }
    public DateTimeOffset LastWatchedAt { get; init; }
    public EpisodeRef? Episode { get; init; }
    public string? ShowImdbId { get; init; }
}

public sealed record Want
{
    public string ImdbId { get; init; } = "";
    public string Kind { get; init; } = "film";
    public string Title { get; init; } = "";

    /// <summary>The title's card; absent for a title the catalogue does not list.</summary>
    public CatalogueCard? Card { get; init; }

    public DateTimeOffset Since { get; init; }
    public string? State { get; init; }
    public double? Progress { get; init; }
    public bool InLibrary { get; init; }
    public bool Watched { get; init; }
    public WantNext? NextEpisode { get; init; }
}

public sealed record WantNext(int Season, int Episode, string? Name = null);

public sealed record Follow
{
    public string ImdbId { get; init; } = "";
    public string Title { get; init; } = "";

    /// <summary>The show's card; absent for a show the catalogue does not list.</summary>
    public CatalogueCard? Card { get; init; }

    public DateTimeOffset Since { get; init; }
    public bool AutoDownload { get; init; }
    public Airing? NextEpisode { get; init; }
}

/// <summary>
/// What one list of titles asks the catalogue for (<c>/api/catalogue/titles</c>). A filter left null or
/// empty is not sent; the list filters (<see cref="Genres"/>, <see cref="Keywords"/>, <see cref="People"/>)
/// match all of their values, <see cref="NotGenres"/> none of them.
/// </summary>
public sealed record TitleFilter
{
    public string? Words { get; init; }
    public string? Kind { get; init; }
    public string? Category { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];
    public IReadOnlyList<string> NotGenres { get; init; } = [];
    public int? From { get; init; }
    public int? To { get; init; }

    /// <summary>Released on or after this day, <c>yyyy-MM-dd</c>.</summary>
    public string? Released { get; init; }

    /// <summary>Released before this day, <c>yyyy-MM-dd</c>.</summary>
    public string? ReleasedBefore { get; init; }

    /// <summary>ISO 639 codes.</summary>
    public IReadOnlyList<string> Languages { get; init; } = [];

    /// <summary>Keyword ids, as <c>k123</c>.</summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>The catalogue's person ids.</summary>
    public IReadOnlyList<string> People { get; init; } = [];

    /// <summary><c>any</c>, <c>cast</c>, <c>directing</c> or <c>writing</c>: which of the people's credits count.</summary>
    public string? Role { get; init; }

    public double? MinRating { get; init; }
    public int? MinVotes { get; init; }
    public string? Ranking { get; init; }

    /// <summary>An IMDb id: the titles the film database counts as similar to it, in its order unless a sort is given.</summary>
    public string? SimilarTo { get; init; }

    public string? Sort { get; init; }
    public int? Page { get; init; }
    public int? Size { get; init; }

    /// <summary><c>ready</c>: only what the library holds ready to play, the newest added first.</summary>
    public string? Here { get; init; }

    /// <summary>The query parameters, each list filter repeated once a value.</summary>
    public IEnumerable<(string Name, object? Value)> Parameters()
    {
        yield return ("q", Words);
        yield return ("kind", Kind);
        yield return ("category", Category);
        foreach (string genre in Genres) yield return ("genre", genre);
        foreach (string genre in NotGenres) yield return ("notGenre", genre);
        yield return ("from", From);
        yield return ("to", To);
        yield return ("released", Released);
        yield return ("releasedBefore", ReleasedBefore);
        foreach (string language in Languages) yield return ("language", language);
        foreach (string keyword in Keywords) yield return ("keyword", keyword);
        foreach (string person in People) yield return ("person", person);
        yield return ("role", Role);
        yield return ("minRating", MinRating);
        yield return ("minVotes", MinVotes);
        yield return ("ranking", Ranking);
        yield return ("similarTo", SimilarTo);
        yield return ("sort", Sort);
        yield return ("page", Page);
        yield return ("size", Size);
        yield return ("here", Here);
    }
}

/// <summary>The API's answers as JSON, in the camel case it writes, read without reflection.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(CardPage))]
[JsonSerializable(typeof(TitlePage))]
[JsonSerializable(typeof(List<Term>))]
[JsonSerializable(typeof(Facets))]
[JsonSerializable(typeof(PersonShow))]
[JsonSerializable(typeof(List<AnticipatedTitle>))]
[JsonSerializable(typeof(List<HistoryEntry>))]
[JsonSerializable(typeof(List<Want>))]
[JsonSerializable(typeof(List<Follow>))]
public sealed partial class ApiJson : JsonSerializerContext;
