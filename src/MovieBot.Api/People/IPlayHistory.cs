namespace TheKrystalShip.MovieBot.Api.People;

/// <summary>When a film in the library was last played by anybody, which is what the library's clock runs on.</summary>
public interface IPlayHistory
{
    /// <summary>The last moment a room was playing the film, or null when no play is recorded.</summary>
    DateTimeOffset? LastPlayedAt(string titleId);

    /// <summary>
    /// When plays began to be recorded. Nothing before it is known, so no film's clock counts from
    /// earlier: a library that has just started recording does not read every film as long unwatched.
    /// </summary>
    DateTimeOffset RecordingSince { get; }
}
