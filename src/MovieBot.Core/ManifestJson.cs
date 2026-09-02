using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheKrystalShip.MovieBot.Core;

/// <summary>
/// The serializer for everything that crosses the wire, shipped beside the shapes so a consumer
/// cannot hold the right types under the wrong naming policy. Every root is registered here; a
/// type reached only by reflection would throw at runtime.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(Manifest))]
[JsonSerializable(typeof(SidecarSubtitle))]
[JsonSerializable(typeof(SubtitlePin))]
[JsonSerializable(typeof(Chapter))]
[JsonSerializable(typeof(ThumbnailStrip))]
[JsonSerializable(typeof(SessionState))]
[JsonSerializable(typeof(SessionStatePush))]
[JsonSerializable(typeof(SeekClamped))]
[JsonSerializable(typeof(SetTitleRequest))]
[JsonSerializable(typeof(IReadOnlyList<Participant>))]
public partial class ManifestJsonContext : JsonSerializerContext;

public static class ManifestJson
{
    public static string Serialize(Manifest manifest) =>
        JsonSerializer.Serialize(manifest, ManifestJsonContext.Default.Manifest);

    public static Manifest? Deserialize(string json) =>
        JsonSerializer.Deserialize(json, ManifestJsonContext.Default.Manifest);

    /// <summary>
    /// Writes the manifest through a temporary file and an atomic move. The API polls this file
    /// while the transcode is running, so a reader must never observe a half-written document.
    /// </summary>
    public static void WriteAtomic(string path, Manifest manifest)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temporary = path + ".tmp";
        File.WriteAllText(temporary, Serialize(manifest));
        File.Move(temporary, path, overwrite: true);
    }
}
