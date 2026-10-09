using System.Globalization;
using System.Net;
using System.Text;
using TheKrystalShip.MovieBot.Api.Films;
using TheKrystalShip.MovieBot.Api.Library;

namespace TheKrystalShip.MovieBot.Api.Sessions;

/// <summary>
/// How a link to a room unfurls: the Open Graph and Twitter tags a chat client reads when somebody
/// pastes <c>/r/&lt;room&gt;</c>.
///
/// Whatever unfurls the link fetches it without a token, so everything here is public to anyone
/// holding the link. It therefore says what an open room is watching and how far in, and never who is
/// in the room or how many: the people in a room are the room's business. A private room says nothing
/// at all, not even its film. The tags are written by the server into the page itself, because a
/// crawler does not run the page's script.
/// </summary>
public static class RoomPreview
{
    private const string SiteName = "Krystal Cinema";

    /// <summary>The room a page path names, or null for a path that is not a room's.</summary>
    public static string? RoomOf(PathString path)
    {
        if (!path.StartsWithSegments("/r", out var rest) || rest.Value is not { Length: > 1 } value) return null;
        var room = Uri.UnescapeDataString(value[1..].TrimEnd('/'));
        return room.Length is > 0 and <= 200 && !room.Contains('/') ? room : null;
    }

    /// <summary>The code a guest link's page path carries, or null for a path that is not one.</summary>
    public static string? GuestCodeOf(PathString path) =>
        path.StartsWithSegments("/g", out var rest) && rest.Value is { Length: > 1 } value
                                                     && value[1..].TrimEnd('/') is { Length: > 0 } code && !code.Contains('/')
            ? code
            : null;

    /// <summary>
    /// The tags for a room. The film is the one the room holds; a room nobody has opened yet holds
    /// nothing, and the link that names a film (<c>?title=</c>) is what it will hold, so that is shown.
    /// A private room shows its film only on its guest link (<paramref name="invited"/>), whose holder
    /// was invited to watch it.
    /// </summary>
    public static string Tags(string room, string? linkTitle, SessionStore sessions, TitleLibrary library,
        FilmStore films, string baseUrl, string pageUrl, bool invited = false)
    {
        var summary = sessions.Summaries().FirstOrDefault(r => r.SessionId == room);
        var titleId = summary is { Open: false } && !invited ? null : summary?.TitleId ?? linkTitle;
        var manifest = titleId is not null && TitleLibrary.IsSafeId(titleId) ? library.Get(titleId) : null;

        var tags = new List<(string Property, string Content)>
        {
            ("og:site_name", SiteName),
            ("og:url", pageUrl),
        };

        if (manifest is null)
        {
            tags.Add(("og:type", "website"));
            tags.Add(("og:title", SiteName));
            tags.Add(("og:description", "A room on Krystal Cinema."));
            tags.Add(("twitter:card", "summary"));
            return Render(tags);
        }

        var name = manifest.Film?.Name ?? manifest.Title;
        var year = manifest.Film?.Year;
        var position = summary?.TitleId == manifest.Id ? summary.PositionSeconds : 0;
        var description = string.Join(" · ", new[] { year?.ToString(CultureInfo.InvariantCulture), HowFarIn(position) }
            .Where(s => !string.IsNullOrEmpty(s)));

        tags.Add(("og:type", "video.movie"));
        tags.Add(("og:title", $"Watch {name} on {SiteName}"));
        if (description.Length > 0) tags.Add(("og:description", description));

        if (Artwork(manifest, films, baseUrl) is { } image)
        {
            tags.Add(("og:image", image.Url));
            tags.Add(("og:image:width", image.Width.ToString(CultureInfo.InvariantCulture)));
            tags.Add(("og:image:height", image.Height.ToString(CultureInfo.InvariantCulture)));
            tags.Add(("og:image:alt", name));
            tags.Add(("twitter:card", image.Wide ? "summary_large_image" : "summary"));
            tags.Add(("twitter:image", image.Url));
        }
        else
        {
            tags.Add(("twitter:card", "summary"));
        }
        tags.Add(("twitter:title", $"Watch {name} on {SiteName}"));
        return Render(tags);
    }

    /// <summary>How far into the film the room is, as a chat message would say it; nothing at the start.</summary>
    public static string? HowFarIn(double seconds)
    {
        var minutes = (int)(seconds / 60);
        if (minutes < 1) return null;
        return minutes < 60 ? $"{minutes} min in" : $"{minutes / 60} h {minutes % 60:00} min in";
    }

    private sealed record Image(string Url, int Width, int Height, bool Wide);

    /// <summary>
    /// The backdrop kept from the film database, which is the wide still a large card wants, and the
    /// library's own poster otherwise. Both are open, which an unfurl needs: it carries no token.
    /// </summary>
    private static Image? Artwork(Core.Manifest manifest, FilmStore films, string baseUrl)
    {
        if (manifest.Film?.ImdbId is { } imdbId && FilmStore.IsImdbId(imdbId))
        {
            if (films.HasImage(imdbId, FilmStore.Backdrop))
                return new Image(baseUrl + FilmViews.ImageUrl(imdbId, FilmStore.Backdrop), 1280, 720, true);
            if (films.HasImage(imdbId, FilmStore.Poster))
                return new Image(baseUrl + FilmViews.ImageUrl(imdbId, FilmStore.Poster), 500, 750, false);
        }
        return manifest.Poster is { } poster
            ? new Image($"{baseUrl}/media/{Uri.EscapeDataString(manifest.Id)}/{Uri.EscapeDataString(poster)}", 500, 750, false)
            : null;
    }

    private static string Render(List<(string Property, string Content)> tags)
    {
        var html = new StringBuilder();
        foreach (var (property, content) in tags)
        {
            var attribute = property.StartsWith("twitter:", StringComparison.Ordinal) ? "name" : "property";
            html.Append("    <meta ").Append(attribute).Append("=\"").Append(property).Append("\" content=\"")
                .Append(WebUtility.HtmlEncode(content)).Append("\" />\n");
        }
        return html.ToString();
    }
}
