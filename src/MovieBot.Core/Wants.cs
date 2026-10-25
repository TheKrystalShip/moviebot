namespace TheKrystalShip.MovieBot.Core;

/// <summary>What is measured of a title somebody wants (<see cref="WantView.State"/>).</summary>
public static class WantStates
{
    /// <summary>The library holds it.</summary>
    public const string Ready = "ready";

    /// <summary>The torrent client has it and it is arriving.</summary>
    public const string Fetching = "fetching";

    /// <summary>The tracker offers a release at the source floor.</summary>
    public const string Available = "available";

    /// <summary>A wish is kept for the film and the tracker has nothing at the source floor.</summary>
    public const string Waiting = "waiting";
}

/// <summary>How making a want went (<see cref="SetWantReply.Outcome"/>).</summary>
public static class WantOutcomes
{
    /// <summary>The person already had the want.</summary>
    public const string AlreadyWanted = "alreadyWanted";

    /// <summary>The want is made and nothing more is known: a show, a film whose checks could not be made, or an account that may not wish.</summary>
    public const string Wanted = "wanted";

    /// <summary>The want is made, nothing has the film yet and a wish is kept for it.</summary>
    public const string Waiting = "waiting";

    /// <summary>The want is made and the tracker offers the film now.</summary>
    public const string Available = "available";

    /// <summary>The want is made and the library already holds the film.</summary>
    public const string InLibrary = "inLibrary";

    /// <summary>The want is made and the film is already on its way.</summary>
    public const string Downloading = "downloading";
}

/// <summary>A show's next episode for one person.</summary>
public sealed record WantEpisode
{
    public required int Season { get; init; }

    public required int Episode { get; init; }

    public string? Name { get; init; }
}

/// <summary>The wish kept for a film nothing has yet.</summary>
public sealed record WantWish
{
    /// <summary>The release found for it, once the tracker offers one at the source floor.</summary>
    public ReleaseOffer? Release { get; init; }
}

/// <summary>One title a person wants to watch: its catalogue card, and what the library and the watch record say about it.</summary>
public sealed record WantView
{
    public required string ImdbId { get; init; }

    /// <summary><c>film</c> or <c>show</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>The title's name: its card's, else its IMDb id.</summary>
    public required string Title { get; init; }

    /// <summary>The title's card; absent for a title the catalogue does not list.</summary>
    public Catalogue.TitleCard? Card { get; init; }

    /// <summary>Whether the want is named to everybody signed in.</summary>
    public required bool Visible { get; init; }

    public required DateTimeOffset Since { get; init; }

    /// <summary>One of <see cref="WantStates"/>; absent when none of them is known.</summary>
    public string? State { get; init; }

    /// <summary>How much of the title has arrived, from 0 to 1, while it is <see cref="WantStates.Fetching"/>.</summary>
    public double? Progress { get; init; }

    /// <summary>For a film the library holds, the title that plays it.</summary>
    public string? LibraryId { get; init; }

    /// <summary>Whether the library holds the film, or any episode of the show.</summary>
    public bool InLibrary { get; init; }

    /// <summary>Whether the asking person watched the film through; never set for a show.</summary>
    public bool Watched { get; init; }

    /// <summary>For a show, the asking person's next episode.</summary>
    public WantEpisode? NextEpisode { get; init; }

    /// <summary>How many other people have a visible want on the title.</summary>
    public int Others { get; init; }

    /// <summary>Present while a wish is kept for the film.</summary>
    public WantWish? Wish { get; init; }
}

/// <summary>Somebody with a visible want on a title.</summary>
public sealed record WanterView
{
    public required string UserId { get; init; }

    public string? Name { get; init; }

    public required DateTimeOffset Since { get; init; }

    /// <summary>For a show, that person's next episode.</summary>
    public WantEpisode? NextEpisode { get; init; }
}

/// <summary>A title the asking person wants that others visibly want too, with those others.</summary>
public sealed record TogetherView
{
    public required WantView Want { get; init; }

    /// <summary>The other wanters who are at the same point: for a show, the same next episode.</summary>
    public required List<WanterView> With { get; init; }
}

/// <summary>Want a title: what kind it is, and whether the want is named to everybody. An absent <see cref="Visible"/> is visible on create and leaves an existing want as it is.</summary>
public sealed record SetWantRequest
{
    public string? Kind { get; init; }

    public bool? Visible { get; init; }
}

/// <summary>How a want went: an outcome from <see cref="WantOutcomes"/> with the facts behind it, and the want as it stands.</summary>
public sealed record SetWantReply
{
    public required string Outcome { get; init; }

    public WantView? Want { get; init; }

    /// <summary>The release the tracker offers, for <see cref="WantOutcomes.Available"/>.</summary>
    public ReleaseOffer? Release { get; init; }

    public string? LibraryId { get; init; }

    public string? LibraryName { get; init; }

    public string? DownloadName { get; init; }
}
