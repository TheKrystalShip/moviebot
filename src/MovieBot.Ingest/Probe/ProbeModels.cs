using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheKrystalShip.MovieBot.Ingest.Probe;

/// <summary>
/// The shape of <c>ffprobe -print_format json -show_format -show_streams</c>. Only the fields
/// the pipeline reads are modelled; ffprobe emits many more and unknown ones are ignored.
/// </summary>
public sealed record ProbeResult
{
    [JsonPropertyName("format")] public ProbeFormat Format { get; init; } = new();
    [JsonPropertyName("streams")] public List<ProbeStream> Streams { get; init; } = [];
}

public sealed record ProbeFormat
{
    [JsonPropertyName("format_name")] public string? FormatName { get; init; }
    [JsonPropertyName("duration")] public string? Duration { get; init; }
    [JsonPropertyName("size")] public string? Size { get; init; }
    [JsonPropertyName("bit_rate")] public string? BitRate { get; init; }
    [JsonPropertyName("tags")] public Dictionary<string, string>? Tags { get; init; }

    public double DurationSeconds =>
        double.TryParse(Duration, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;

    /// <summary>The container's own title, which beats parsing the filename when present.</summary>
    public string? Title => Tag("title");

    public string? Tag(string key)
    {
        if (Tags is null) return null;
        foreach (var (k, v) in Tags)
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(v) ? null : v;
        return null;
    }
}

public sealed record ProbeStream
{
    [JsonPropertyName("index")] public int Index { get; init; }
    [JsonPropertyName("codec_type")] public string? CodecType { get; init; }
    [JsonPropertyName("codec_name")] public string? CodecName { get; init; }
    [JsonPropertyName("profile")] public string? Profile { get; init; }
    [JsonPropertyName("width")] public int? Width { get; init; }
    [JsonPropertyName("height")] public int? Height { get; init; }
    [JsonPropertyName("pix_fmt")] public string? PixFmt { get; init; }
    [JsonPropertyName("r_frame_rate")] public string? RFrameRate { get; init; }
    [JsonPropertyName("color_space")] public string? ColorSpace { get; init; }
    [JsonPropertyName("color_transfer")] public string? ColorTransfer { get; init; }
    [JsonPropertyName("color_primaries")] public string? ColorPrimaries { get; init; }
    [JsonPropertyName("channels")] public int? Channels { get; init; }
    [JsonPropertyName("channel_layout")] public string? ChannelLayout { get; init; }
    [JsonPropertyName("disposition")] public Dictionary<string, int>? Disposition { get; init; }
    [JsonPropertyName("tags")] public Dictionary<string, string>? Tags { get; init; }
    [JsonPropertyName("side_data_list")] public List<Dictionary<string, JsonElement>>? SideData { get; init; }

    public bool IsVideo => CodecType == "video";
    public bool IsAudio => CodecType == "audio";
    public bool IsSubtitle => CodecType == "subtitle";

    /// <summary>Embedded cover art rides as a video stream flagged <c>attached_pic</c>, not as a real track.</summary>
    public bool IsAttachedPicture => Flag("attached_pic");
    public bool IsCommentary => Flag("comment");
    public bool IsHearingImpaired => Flag("hearing_impaired");
    public bool IsForced => Flag("forced");
    public bool IsDefault => Flag("default");

    public string? Language => Tag("language") is { } l && l != "und" ? l : null;
    public string? Title => Tag("title");

    public bool Flag(string key) => Disposition is not null
        && Disposition.TryGetValue(key, out var v) && v != 0;

    /// <summary>ffprobe reports frame rate as a rational, e.g. "24000/1001" for 23.976.</summary>
    public double FrameRate()
    {
        if (string.IsNullOrWhiteSpace(RFrameRate)) return 0;
        var parts = RFrameRate.Split('/');
        if (parts.Length != 2) return 0;
        return double.TryParse(parts[0], out var n) && double.TryParse(parts[1], out var d) && d != 0
            ? n / d
            : 0;
    }

    public string? Tag(string key)
    {
        if (Tags is null) return null;
        foreach (var (k, v) in Tags)
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(v) ? null : v;
        return null;
    }

    /// <summary>
    /// The Dolby Vision configuration record, when the stream carries one. Profile 8.1 has an
    /// HDR10-compatible base layer, so tone-mapping from the base is correct and the RPU is ignored.
    /// </summary>
    public string? DolbyVisionProfile()
    {
        if (SideData is null) return null;
        foreach (var entry in SideData)
        {
            if (!entry.TryGetValue("side_data_type", out var type)) continue;
            if (type.ValueKind != JsonValueKind.String) continue;
            if (!type.GetString()!.Contains("DOVI", StringComparison.OrdinalIgnoreCase)) continue;

            var major = entry.TryGetValue("dv_profile", out var p) && p.TryGetInt32(out var pi) ? pi : 0;
            var compat = entry.TryGetValue("dv_bl_signal_compatibility_id", out var c) && c.TryGetInt32(out var ci) ? ci : 0;
            return $"dovi-p{major}.{compat}";
        }
        return null;
    }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ProbeResult))]
internal partial class ProbeJsonContext : JsonSerializerContext;
