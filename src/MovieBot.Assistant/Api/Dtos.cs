namespace TheKrystalShip.MovieBot.Assistant.Api;

// What the API answers, as far as the tools read it. The shapes are docs/api-contract.md's; a field a
// tool never reads is not declared here. A field the API leaves out is null or empty, never a default
// that reads as a fact.

/// <summary>A film or show a search, a discovery or a list of the week names, joined with the library and the asking person.</summary>
public sealed record TitleHit
{
    public string? ImdbId { get; init; }
    public int TmdbId { get; init; }
    public string Kind { get; init; } = "film";
    public string Title { get; init; } = "";
    public int? Year { get; init; }
    public string? ReleaseDate { get; init; }
    public List<string> Genres { get; init; } = [];
    public double? Rating { get; init; }
    public int Votes { get; init; }
    public string? Plot { get; init; }
    public List<string> MadeBy { get; init; } = [];
    public List<string> Starring { get; init; } = [];
    public string? OriginalLanguage { get; init; }
    public string? PosterUrl { get; init; }
    public bool InLibrary { get; init; }
    public string? LibraryId { get; init; }
    public bool Watched { get; init; }
}

public sealed record SearchResult
{
    public string Query { get; init; } = "";
    public List<TitleHit> Titles { get; init; } = [];
}

public sealed record PersonMatch
{
    public int PersonId { get; init; }
    public string Name { get; init; } = "";
    public string? KnownFor { get; init; }
    public List<string> KnownForTitles { get; init; } = [];
}

public sealed record PeopleResult
{
    public string Query { get; init; } = "";
    public List<PersonMatch> People { get; init; } = [];
}

/// <summary>One title a person is credited on.</summary>
public sealed record Credit
{
    public int TmdbId { get; init; }
    public string? ImdbId { get; init; }
    public string Kind { get; init; } = "film";
    public string Title { get; init; } = "";
    public int? Year { get; init; }
    public string? ReleaseDate { get; init; }

    /// <summary><c>released</c>, <c>upcoming</c> or <c>announced</c>.</summary>
    public string Status { get; init; } = "";

    public string? Role { get; init; }
    public double? Rating { get; init; }
    public int Votes { get; init; }
    public string? PosterUrl { get; init; }
    public bool InLibrary { get; init; }
    public string? LibraryId { get; init; }
    public bool Watched { get; init; }
}

public sealed record PersonCredits
{
    public int PersonId { get; init; }
    public string Name { get; init; } = "";
    public string? KnownFor { get; init; }
    public string AsOf { get; init; } = "";
    public int Total { get; init; }
    public List<Credit> Credits { get; init; } = [];
}

public sealed record ResolvedPerson(int PersonId, string Name, string? KnownFor);

public sealed record DiscoverResolved
{
    public List<string> Genres { get; init; } = [];
    public List<ResolvedPerson> People { get; init; } = [];
    public List<string> Keywords { get; init; } = [];
    public string? Language { get; init; }
    public string? Like { get; init; }
    public double? MinRating { get; init; }
    public int? MinVotes { get; init; }
    public string Sort { get; init; } = "";
}

public sealed record DiscoverResult
{
    public string Kind { get; init; } = "film";
    public DiscoverResolved Resolved { get; init; } = new();
    public List<TitleHit> Titles { get; init; } = [];
}

/// <summary>What <c>released</c> and <c>upcoming</c> answer.</summary>
public sealed record TitleList
{
    public string Kind { get; init; } = "film";
    public string From { get; init; } = "";
    public string To { get; init; } = "";
    public List<TitleHit> Titles { get; init; } = [];
}

public sealed record PopularTitle
{
    public string ImdbId { get; init; } = "";
    public string Title { get; init; } = "";
    public int? Year { get; init; }
    public double? Rating { get; init; }
    public int Votes { get; init; }
    public string? PosterUrl { get; init; }
}

public sealed record Popular
{
    public List<PopularTitle> Films { get; init; } = [];
    public List<PopularTitle> Shows { get; init; } = [];

    /// <summary>When the film database was last asked; absent until it first is.</summary>
    public DateTimeOffset? FetchedAt { get; init; }
}

public sealed record LibraryAddition
{
    public string? ImdbId { get; init; }
    public string Kind { get; init; } = "film";
    public string Title { get; init; } = "";
    public int? Year { get; init; }
    public List<string> Genres { get; init; } = [];
    public double? Rating { get; init; }
    public string? Plot { get; init; }
    public string? PosterUrl { get; init; }
    public string LibraryId { get; init; } = "";
    public DateTimeOffset AddedAt { get; init; }
    public bool Watched { get; init; }
}

public sealed record LibraryAdditions
{
    public List<LibraryAddition> Titles { get; init; } = [];
}

public sealed record CastMember
{
    public string Name { get; init; } = "";
    public string? Character { get; init; }
}

/// <summary>A film as its page shows it.</summary>
public sealed record FilmDetails
{
    public string ImdbId { get; init; } = "";
    public string Title { get; init; } = "";
    public string? OriginalTitle { get; init; }
    public int? Year { get; init; }
    public string? ReleaseDate { get; init; }
    public int? RuntimeMinutes { get; init; }
    public string? Certification { get; init; }
    public List<string> Genres { get; init; } = [];
    public string? Overview { get; init; }
    public string? Tagline { get; init; }
    public List<string> Keywords { get; init; } = [];

    /// <summary>True when the database tags the film black and white. False says only that it is not tagged.</summary>
    public bool BlackAndWhite { get; init; }

    public double? Rating { get; init; }
    public int Votes { get; init; }
    public List<string> Directors { get; init; } = [];
    public List<string> Writers { get; init; } = [];
    public List<CastMember> Cast { get; init; } = [];
    public string? OriginalLanguage { get; init; }
    public List<string> Countries { get; init; } = [];
    public string? PosterUrl { get; init; }
    public string? LibraryId { get; init; }
    public bool Watched { get; init; }
}

public sealed record EpisodeEntry
{
    public int Number { get; init; }

    /// <summary>The library's title for the episode; absent when the library does not hold it.</summary>
    public string? TitleId { get; init; }
}

public sealed record SeasonDetails
{
    public int Number { get; init; }
    public string? Name { get; init; }
    public string? AirDate { get; init; }
    public List<EpisodeEntry> Episodes { get; init; } = [];
}

public sealed record Airing(int Season, int Episode, string? Name, string AirDate);

/// <summary>A show as its page shows it.</summary>
public sealed record ShowDetails
{
    public string ImdbId { get; init; } = "";
    public string Title { get; init; } = "";
    public string? OriginalTitle { get; init; }
    public int? FirstYear { get; init; }
    public int? LastYear { get; init; }
    public string? Status { get; init; }
    public string? Certification { get; init; }
    public List<string> Genres { get; init; } = [];
    public string? Overview { get; init; }
    public string? Tagline { get; init; }
    public List<string> Keywords { get; init; } = [];
    public bool BlackAndWhite { get; init; }
    public double? Rating { get; init; }
    public int Votes { get; init; }
    public List<string> Creators { get; init; } = [];
    public List<CastMember> Cast { get; init; } = [];
    public List<string> Countries { get; init; } = [];
    public List<string> Networks { get; init; } = [];
    public string? PosterUrl { get; init; }
    public List<SeasonDetails> Seasons { get; init; } = [];
    public Airing? NextEpisode { get; init; }

    /// <summary>The asking person's follow of the show; absent when they do not follow it.</summary>
    public FollowDetails? Follow { get; init; }
}

public sealed record FollowDetails
{
    public bool AutoDownload { get; init; }
    public DateTimeOffset Since { get; init; }
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

public sealed record ListSummary
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool WatchLater { get; init; }
    public int Count { get; init; }
}

public sealed record ListEntry
{
    public string ImdbId { get; init; } = "";
    public string Kind { get; init; } = "film";
    public string Title { get; init; } = "";
    public int? Year { get; init; }
    public double? Rating { get; init; }
    public List<string> Genres { get; init; } = [];
    public DateTimeOffset AddedAt { get; init; }
    public string? Note { get; init; }
    public string? PosterUrl { get; init; }
    public bool InLibrary { get; init; }
    public bool Watched { get; init; }
}

public sealed record ListContents
{
    public ListSummary List { get; init; } = new();
    public List<ListEntry> Entries { get; init; } = [];
}

public sealed record Follow
{
    public string ImdbId { get; init; } = "";
    public string Title { get; init; } = "";
    public DateTimeOffset Since { get; init; }
    public bool AutoDownload { get; init; }
    public Airing? NextEpisode { get; init; }
}

/// <summary>What a discovery asks for. A null filter is not sent.</summary>
public sealed record DiscoverQuery
{
    public string Kind { get; init; } = "film";
    public IReadOnlyList<string> Genre { get; init; } = [];
    public int? From { get; init; }
    public int? To { get; init; }
    public IReadOnlyList<string> With { get; init; } = [];
    public IReadOnlyList<string> About { get; init; } = [];
    public string? Language { get; init; }
    public string? Like { get; init; }
    public bool GoodOnly { get; init; } = true;
    public string? Sort { get; init; }
}
