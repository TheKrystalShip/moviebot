using System.Globalization;
using System.Text;
using TheKrystalShip.MovieBot.Acquire.Tracker;
using TheKrystalShip.MovieBot.Acquire.Search;

namespace TheKrystalShip.MovieBot.Handoff;

/// <summary>
/// The name a film goes under in the library, derived from the release it arrived as.
/// </summary>
public static class LibraryId
{
    /// <summary>
    /// The library id for a release, from its name.
    ///
    /// The release name is parsed by the same parser the search uses rather than a second one
    /// written here: two spellings of the same idea do not disagree loudly, they just produce a
    /// different id for the same film and nobody notices until the library has both.
    /// </summary>
    public static string For(string releaseName)
    {
        var release = ReleaseParser.Parse(new TrackerTorrent { Name = releaseName });

        var basis = release.Year is { } year
            ? $"{release.Title} {year}"
            : release.Title;

        return Slug(string.IsNullOrWhiteSpace(basis) ? releaseName : basis);
    }

    private static string Slug(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasDash = false;

        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character)
                == UnicodeCategory.NonSpacingMark) continue;

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                lastWasDash = false;
            }
            else if (!lastWasDash && builder.Length > 0)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }
}
