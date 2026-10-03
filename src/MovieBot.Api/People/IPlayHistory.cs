namespace TheKrystalShip.MovieBot.Api.People;

/// <summary>When a film in the library was last played by anybody, which is what the library's clock runs on.</summary>
public interface IPlayHistory
{
    /// <summary>The last moment a room was playing the film, or null when no play is recorded.</summary>
    DateTimeOffset? LastPlayedAt(string titleId);
}

/// <summary>No plays recorded: every film counts as never played.</summary>
public sealed class NoPlayHistory : IPlayHistory
{
    public DateTimeOffset? LastPlayedAt(string titleId) => null;
}
