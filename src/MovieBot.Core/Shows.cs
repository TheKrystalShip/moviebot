namespace TheKrystalShip.MovieBot.Core;

/// <summary>An episode of a show as the film database places it in time: where it falls and the date it airs.</summary>
public sealed record AiringView(int Season, int Episode, string? Name, string AirDate);

/// <summary>What the asking person's follow of a show comes to, on the show's page.</summary>
public sealed record ShowFollow(bool AutoDownload, DateTimeOffset Since);

/// <summary>One show a person follows, as the list of their follows shows it.</summary>
public sealed record FollowView
{
    public required string ImdbId { get; init; }
    public required string Title { get; init; }
    /// <summary>The show's card; absent for a show the catalogue does not list.</summary>
    public Catalogue.TitleCard? Card { get; init; }
    public required DateTimeOffset Since { get; init; }
    public bool AutoDownload { get; init; }

    /// <summary>The episode the film database expects next; absent when it names none.</summary>
    public AiringView? NextEpisode { get; init; }
}

/// <summary>Follow or change a follow: whether new episodes are downloaded for the person as well as announced.</summary>
public sealed record FollowRequest
{
    public bool AutoDownload { get; init; }
}

/// <summary>
/// What the asking person has of a show (<c>/api/me/shows/{imdbId}</c>): every season with its episodes,
/// numbered as the releases number them, each joined with whether the library holds it and how far
/// they got; where they pick up; their follow; and the episode expected next. What the show is, is its
/// catalogue page. Works for a show the library holds nothing of, which is where episodes are picked.
/// </summary>
public sealed record PersonShowView
{
    public required string ImdbId { get; init; }
    public required List<SeasonView> Seasons { get; init; }

    /// <summary>Where the asking person picks up: absent when they have nothing in progress here.</summary>
    public ResumePoint? Resume { get; init; }

    /// <summary>Whether the show can be followed: the catalogue does not say it has ended or been cancelled.</summary>
    public bool Followable { get; init; }

    /// <summary>The asking person's follow of the show; absent when they do not follow it.</summary>
    public ShowFollow? Follow { get; init; }

    /// <summary>The episode expected next; absent when none is named.</summary>
    public AiringView? NextEpisode { get; init; }
}

public sealed record SeasonView
{
    public required int Number { get; init; }
    public string? Name { get; init; }
    public string? AirDate { get; init; }
    public string? Overview { get; init; }
    public string? Poster { get; init; }
    public required List<EpisodeView> Episodes { get; init; }
}

public sealed record EpisodeView
{
    public required int Season { get; init; }
    public required int Number { get; init; }
    public string? Name { get; init; }
    public string? Overview { get; init; }
    public string? AirDate { get; init; }
    public int? Runtime { get; init; }

    /// <summary>The episode's still; kept for the shows the library holds.</summary>
    public string? Still { get; init; }

    /// <summary>The library's title for the episode; absent when the library does not hold it.</summary>
    public string? TitleId { get; init; }

    public TitleStatus? Status { get; init; }
    public double? DurationSeconds { get; init; }
    public TitleProgress? Progress { get; init; }
    public TitleLastPlayed? LastPlayed { get; init; }
}

/// <summary>An episode somebody is in the middle of, or the one after the last they finished.</summary>
public sealed record ResumePoint(string TitleId, int Season, int Episode, double PositionSeconds, double DurationSeconds);
