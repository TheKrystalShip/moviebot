using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheKrystalShip.MovieBot.Api.People;

/// <summary>
/// How one person watches, whichever door they come in by. The site and the Activity sign the same
/// Discord account in, so these follow the person rather than the browser.
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

    /// <summary>A stored value that no longer makes sense is the setting left alone.</summary>
    public ViewerPreferences Normalised() => this with
    {
        SubtitleLanguage = string.IsNullOrWhiteSpace(SubtitleLanguage) ? null : SubtitleLanguage.Trim(),
        AudioLanguage = string.IsNullOrWhiteSpace(AudioLanguage) ? null : AudioLanguage.Trim(),
        QualityHeight = QualityHeight is > 0 ? QualityHeight : null,
    };
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, ViewerPreferences>))]
internal partial class PreferencesJson : JsonSerializerContext;

/// <summary>
/// Everybody's preferences, in one file in the state directory, keyed by Discord user id.
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
