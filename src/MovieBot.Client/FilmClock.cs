using System.Globalization;

namespace TheKrystalShip.MovieBot.Client;

/// <summary>A position in a film, the way a person reads one.</summary>
public static class FilmClock
{
    public static string Format(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds)));
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes}:{span.Seconds:00}";
    }

    /// <summary>
    /// Reads "1:02:03", "62:03", "90" or "90.5" as seconds. Null for anything else, which is refused
    /// rather than guessed at: a position read wrongly moves the film for everybody.
    /// </summary>
    public static double? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim();

        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var plain))
            return plain >= 0 ? plain : null;

        var parts = trimmed.Split(':');
        if (parts.Length is < 2 or > 3) return null;

        double total = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var value)) return null;
            if (i > 0 && value >= 60) return null;
            total = total * 60 + value;
        }

        return total;
    }
}
