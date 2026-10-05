using System.Text.Json;
using System.Text.Json.Serialization;

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
    /// Whether an intro or a recap is skipped on its own when this person is the only one in the room.
    /// With anybody else there the player offers the skip and leaves it to a press.
    /// </summary>
    public bool SkipIntros { get; init; }

    /// <summary>How subtitles are drawn for this person, or null for the player's own look.</summary>
    public SubtitleStyle? SubtitleStyle { get; init; }

    /// <summary>A stored value that no longer makes sense is the setting left alone.</summary>
    public ViewerPreferences Normalised() => this with
    {
        SubtitleLanguage = string.IsNullOrWhiteSpace(SubtitleLanguage) ? null : SubtitleLanguage.Trim(),
        AudioLanguage = string.IsNullOrWhiteSpace(AudioLanguage) ? null : AudioLanguage.Trim(),
        QualityHeight = QualityHeight is > 0 ? QualityHeight : null,
        SubtitleStyle = SubtitleStyle?.Normalised(),
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

    /// <summary>
    /// Anything out of range or unknown is that field left to the default, and a style with nothing
    /// left in it is no style at all.
    /// </summary>
    public SubtitleStyle? Normalised()
    {
        var style = new SubtitleStyle
        {
            Size = Size is { } size ? Math.Clamp(size, 50, 200) : null,
            Font = Font is { } font && Fonts.Contains(font) ? font : null,
            Bold = Bold,
            TextColor = Colour(TextColor),
            Edge = Edge is { } edge && Edges.Contains(edge) ? edge : null,
            EdgeColor = Colour(EdgeColor),
            BackgroundColor = Colour(BackgroundColor),
            BackgroundOpacity = BackgroundOpacity is { } opacity && double.IsFinite(opacity) ? Math.Clamp(opacity, 0, 1) : null,
            Position = Position is { } position && double.IsFinite(position) ? Math.Clamp(position, 0, 30) : null,
        };
        return style == new SubtitleStyle() ? null : style;
    }

    private static string? Colour(string? value) =>
        value is { Length: 7 } && value[0] == '#' && value.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF") is false
            ? value.ToLowerInvariant()
            : null;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, ViewerPreferences>))]
internal partial class PreferencesJson : JsonSerializerContext;

/// <summary>
/// Everybody's preferences, in one file in the state directory, keyed by account.
///
/// Written whole on every change: it is a few hundred bytes per person and changes when somebody
/// opens a menu, so a write is cheap and rare, and a file written whole through a rename is never
/// found half-written.
/// </summary>
public sealed class PreferenceStore
{
    private readonly string _path;
    private readonly ILogger<PreferenceStore> _logger;
    private readonly Lock _gate = new();
    private Dictionary<string, ViewerPreferences> _byUser;

    public PreferenceStore(string path, ILogger<PreferenceStore> logger)
    {
        _path = path;
        _logger = logger;
        _byUser = Read();
    }

    /// <summary>Somebody who never set anything has the defaults, which are all nulls.</summary>
    public ViewerPreferences For(string userId)
    {
        lock (_gate) return _byUser.GetValueOrDefault(userId) ?? new ViewerPreferences();
    }

    public ViewerPreferences Set(string userId, ViewerPreferences preferences)
    {
        var stored = preferences.Normalised();
        lock (_gate)
        {
            _byUser = new Dictionary<string, ViewerPreferences>(_byUser) { [userId] = stored };
            Write(_byUser);
        }
        return stored;
    }

    private Dictionary<string, ViewerPreferences> Read()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize(stream, PreferencesJson.Default.DictionaryStringViewerPreferences) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Unreadable preferences cost everybody their settings and nothing else. Starting
            // anyway beats refusing to serve films over them; the file is left for somebody to
            // look at, and the next change writes a good one beside it.
            _logger.LogError(ex, "Preferences at {Path} could not be read; everybody starts from the defaults", _path);
            return [];
        }
    }

    private void Write(Dictionary<string, ViewerPreferences> all)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(all, PreferencesJson.Default.DictionaryStringViewerPreferences));
        File.Move(temporary, _path, overwrite: true);
    }
}
