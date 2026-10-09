namespace TheKrystalShip.MovieBot.Assistant.Tools;

/// <summary>Whether a tool lists films or shows.</summary>
public enum TitleKind { Film, Show }

/// <summary>Which of a person's credits <c>person_credits</c> lists.</summary>
public enum CreditRole { Acting, Directing, Writing, Any }

/// <summary>The order <c>discover</c> lists in.</summary>
public enum DiscoverSort { Popular, Rating, Newest, Oldest }

/// <summary>What <c>whats_new</c> lists.</summary>
public enum Window { Trending, Released, Upcoming, Library }

/// <summary>Whether a half-remembered title is out yet.</summary>
public enum ReleaseState { Released, Upcoming, Announced, Any }

internal static class Choices
{
    /// <summary>The word the API and the tools' text use for a choice: its name, lower case.</summary>
    public static string Wire<T>(this T choice) where T : struct, Enum => choice.ToString().ToLowerInvariant();

    public static string? Wire<T>(this T? choice) where T : struct, Enum => choice?.ToString().ToLowerInvariant();
}
