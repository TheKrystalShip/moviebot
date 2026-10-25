using Microsoft.Data.Sqlite;
using TheKrystalShip.MovieBot.Api.Data;

namespace TheKrystalShip.MovieBot.Api.People;

/// <summary>
/// How one person watches, whichever door they come in by. The site and the Activity both arrive
/// with a token naming the same account, so these follow the person rather than the browser.
/// </summary>
public sealed record ViewerPreferences
{
    /// <summary>The subtitle language a film starts with when it carries one, or null for none.</summary>
    public string? SubtitleLanguage { get; init; }

    /// <summary>The audio language a film starts with when it carries one, or null for the film's own.</summary>
    public string? AudioLanguage { get; init; }

    /// <summary>The height a film is held at, or null to let the connection decide.</summary>
    public int? QualityHeight { get; init; }

    /// <summary>
    /// The quality Save offline picks by default, as a height at 16:9, or null for the device's own
    /// default. The site matches it to the widest rung no wider than that height's 16:9 width.
    /// </summary>
    public int? OfflineQualityHeight { get; init; }

    /// <summary>
    /// Whether an intro or a recap is skipped on its own when this person is the only one in the room.
    /// With anybody else there the player offers the skip and leaves it to a press.
    /// </summary>
    public bool SkipIntros { get; init; }

    /// <summary>How subtitles are drawn for this person, or null for the player's own look.</summary>
    public SubtitleStyle? SubtitleStyle { get; init; }

    /// <summary>
    /// The four pages the person keeps in their phone's tab bar, left to right around the locked middle
    /// one, as the site's page ids, or null for the site's own choice. The API holds the shape of an id,
    /// not the site's pages: an id the site does not route is the site's to read as its default.
    /// </summary>
    public IReadOnlyList<string>? TabBar { get; init; }

    /// <summary>
    /// Where this record stands in the order the person's writes were kept, on what a write answers and
    /// what a push carries; absent on a read. Never stored, and never taken from a request.
    /// </summary>
    public long? Revision { get; init; }

    /// <summary>The tallest picture a height may name: 8K.</summary>
    public const int MaxHeight = 4320;

    /// <summary>How many pages the tab bar's free slots hold.</summary>
    public const int TabBarSlots = 4;

    /// <summary>The longest a page id is.</summary>
    public const int MaxTabIdLength = 64;

    /// <summary>
    /// Why these cannot be kept, or null. A quality is Auto (absent) or a height; it is the person's
    /// default for every film, so it is held to what a picture can be and not to one film's rungs, and a
    /// film with no rung at that height plays the tallest below it. A style is held to the ranges
    /// <see cref="SubtitleStyle"/> keeps.
    /// </summary>
    public string? Refusal() =>
        HeightRefusal("qualityHeight", QualityHeight) ?? HeightRefusal("offlineQualityHeight", OfflineQualityHeight)
        ?? SubtitleStyle?.Refusal() ?? TabBarRefusal();

    private string? TabBarRefusal() =>
        TabBar is null || (TabBar.Count == TabBarSlots && TabBar.Distinct(StringComparer.Ordinal).Count() == TabBarSlots && TabBar.All(IsTabId))
            ? null
            : $"tabBar is absent for the site's own, or {TabBarSlots} distinct ids of 1 to {MaxTabIdLength} characters of a-z, 0-9 and -.";

    private static bool IsTabId(string? id) =>
        id is { Length: >= 1 and <= MaxTabIdLength } && id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    private static string? HeightRefusal(string name, int? height) =>
        height is { } value && value is <= 0 or > MaxHeight
            ? $"{name} is absent for Auto, or a height from 1 to {MaxHeight}."
            : null;

    /// <summary>Whitespace around a language is not part of it, and a style with nothing in it is no style.</summary>
    public ViewerPreferences Normalised() => this with
    {
        SubtitleLanguage = string.IsNullOrWhiteSpace(SubtitleLanguage) ? null : SubtitleLanguage.Trim(),
        AudioLanguage = string.IsNullOrWhiteSpace(AudioLanguage) ? null : AudioLanguage.Trim(),
        SubtitleStyle = SubtitleStyle?.Normalised(),
        TabBar = TabBar is null ? null : [.. TabBar],
        Revision = null,
    };
}

/// <summary>
/// How one person's subtitles look: the choices a television's caption settings offer. Every field
/// is optional and a missing one is the player's default, so the defaults can be retuned for everybody
/// who never changed them. The player draws from these; the API only keeps them in range.
/// </summary>
public sealed record SubtitleStyle
{
    /// <summary>The fonts the player names; each is a stack of fonts a device already has.</summary>
    public static readonly IReadOnlySet<string> Fonts = new HashSet<string>(StringComparer.Ordinal)
        { "sans", "serif", "mono", "casual" };

    /// <summary>What is drawn around the letters, the edge styles caption settings name.</summary>
    public static readonly IReadOnlySet<string> Edges = new HashSet<string>(StringComparer.Ordinal)
        { "none", "outline", "shadow", "raised", "depressed" };

    /// <summary>The text's size as a percentage of the player's own, from 50 to 200.</summary>
    public int? Size { get; init; }

    /// <summary>One of <see cref="Fonts"/>.</summary>
    public string? Font { get; init; }

    public bool? Bold { get; init; }

    /// <summary>A colour as <c>#rrggbb</c>.</summary>
    public string? TextColor { get; init; }

    /// <summary>One of <see cref="Edges"/>.</summary>
    public string? Edge { get; init; }

    public string? EdgeColor { get; init; }

    public string? BackgroundColor { get; init; }

    /// <summary>How solid the box behind each line is, from 0, none at all, to 1.</summary>
    public double? BackgroundOpacity { get; init; }

    /// <summary>How far above the bottom of the frame dialogue sits, as a percentage of its height, 0 to 30.</summary>
    public double? Position { get; init; }

    /// <summary>Why this style cannot be kept, or null: a field outside the ranges and sets above is refused, not bent.</summary>
    public string? Refusal() =>
        Size is { } size && size is < 50 or > 200 ? "size is from 50 to 200."
        : Font is { } font && !Fonts.Contains(font) ? $"font is one of {string.Join(", ", Fonts)}."
        : Edge is { } edge && !Edges.Contains(edge) ? $"edge is one of {string.Join(", ", Edges)}."
        : TextColor is not null && Colour(TextColor) is null ? "textColor is #rrggbb."
        : EdgeColor is not null && Colour(EdgeColor) is null ? "edgeColor is #rrggbb."
        : BackgroundColor is not null && Colour(BackgroundColor) is null ? "backgroundColor is #rrggbb."
        : BackgroundOpacity is { } opacity && !(double.IsFinite(opacity) && opacity is >= 0 and <= 1) ? "backgroundOpacity is from 0 to 1."
        : Position is { } position && !(double.IsFinite(position) && position is >= 0 and <= 30) ? "position is from 0 to 30."
        : null;

    /// <summary>Colours are kept lowercase, and a style with nothing left in it is no style at all.</summary>
    public SubtitleStyle? Normalised()
    {
        var style = this with
        {
            TextColor = Colour(TextColor),
            EdgeColor = Colour(EdgeColor),
            BackgroundColor = Colour(BackgroundColor),
        };
        return style == new SubtitleStyle() ? null : style;
    }

    private static string? Colour(string? value) =>
        value is { Length: 7 } && value[0] == '#' && value.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF") is false
            ? value.ToLowerInvariant()
            : null;
}

/// <summary>
/// Everybody's preferences, one row per account. Somebody who never set anything has no row and has
/// the defaults.
/// </summary>
public sealed class PreferenceStore(CinemaDatabase database)
{
    private const string Columns =
        "subtitle_language, audio_language, quality_height, offline_quality_height, skip_intros, style_size, "
        + "style_font, style_bold, style_text_color, style_edge, style_edge_color, style_background_color, "
        + "style_background_opacity, style_position, tab_bar";

    /// <summary>Somebody who never set anything has the defaults, which are all nulls.</summary>
    public ViewerPreferences For(string userId) =>
        database.Read(c => c.First($"SELECT {Columns} FROM viewer_preferences WHERE user_id = $user", Row, ("$user", userId)))
        ?? new ViewerPreferences();

    public ViewerPreferences Set(string userId, ViewerPreferences preferences)
    {
        var stored = preferences.Normalised();
        var style = stored.SubtitleStyle;
        var revision = database.Write(c =>
        {
            c.Execute(
                $"INSERT OR REPLACE INTO viewer_preferences (user_id, {Columns}) VALUES "
                + "($user, $subtitle, $audio, $quality, $offline, $skip, $size, $font, $bold, $text, $edge, $edgeColor, "
                + "$background, $opacity, $position, $tabBar)",
                ("$user", userId), ("$subtitle", stored.SubtitleLanguage), ("$audio", stored.AudioLanguage),
                ("$quality", stored.QualityHeight), ("$offline", stored.OfflineQualityHeight), ("$skip", stored.SkipIntros),
                ("$size", style?.Size), ("$font", style?.Font), ("$bold", style?.Bold), ("$text", style?.TextColor),
                ("$edge", style?.Edge), ("$edgeColor", style?.EdgeColor), ("$background", style?.BackgroundColor),
                ("$opacity", style?.BackgroundOpacity), ("$position", style?.Position),
                ("$tabBar", stored.TabBar is null ? null : string.Join(',', stored.TabBar)));
            return Revisions.Next("p:" + userId);
        });
        return stored with { Revision = revision };
    }

    private static ViewerPreferences Row(SqliteDataReader r)
    {
        var style = new SubtitleStyle
        {
            Size = r.GetIntOrNull(5),
            Font = r.GetStringOrNull(6),
            Bold = r.GetBoolOrNull(7),
            TextColor = r.GetStringOrNull(8),
            Edge = r.GetStringOrNull(9),
            EdgeColor = r.GetStringOrNull(10),
            BackgroundColor = r.GetStringOrNull(11),
            BackgroundOpacity = r.GetDoubleOrNull(12),
            Position = r.GetDoubleOrNull(13),
        };
        return new ViewerPreferences
        {
            SubtitleLanguage = r.GetStringOrNull(0),
            AudioLanguage = r.GetStringOrNull(1),
            QualityHeight = r.GetIntOrNull(2),
            OfflineQualityHeight = r.GetIntOrNull(3),
            SkipIntros = r.GetBool(4),
            SubtitleStyle = style == new SubtitleStyle() ? null : style,
            TabBar = r.GetStringOrNull(14)?.Split(','),
        };
    }
}
