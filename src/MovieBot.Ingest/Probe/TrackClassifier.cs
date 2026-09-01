using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Ingest.Probe;

/// <summary>
/// Turns raw ffprobe streams into the tracks a person picks from a menu.
/// </summary>
public static partial class TrackClassifier
{
    /// <summary>Subtitle codecs that are text and convert straight to WebVTT.</summary>
    private static readonly HashSet<string> TextSubtitleCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "subrip", "srt", "ass", "ssa", "mov_text", "webvtt", "text", "microdvd", "subviewer"
    };

    /// <summary>
    /// Subtitle codecs that are images of text. There is no conversion to WebVTT: they need an
    /// OCR pass, and burning them in would remove the per-viewer selection that is the point.
    /// </summary>
    private static readonly HashSet<string> BitmapSubtitleCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "hdmv_pgs_subtitle", "pgssub", "dvd_subtitle", "dvdsub", "dvb_subtitle", "dvbsub", "xsub"
    };

    public static bool IsTextSubtitle(ProbeStream s) =>
        s.IsSubtitle && s.CodecName is { } c && TextSubtitleCodecs.Contains(c);

    public static bool IsBitmapSubtitle(ProbeStream s) =>
        s.IsSubtitle && s.CodecName is { } c && BitmapSubtitleCodecs.Contains(c);

    /// <summary>
    /// Detects the source's dynamic range from its transfer characteristics. A PQ or HLG transfer
    /// means the pixel values are HDR and must be tone-mapped to BT.709 for a browser; anything
    /// else is already SDR and must be left alone.
    /// </summary>
    public static string DescribeDynamicRange(ProbeStream video)
    {
        var baseRange = video.ColorTransfer switch
        {
            "smpte2084" => "hdr10",
            "arib-std-b67" => "hlg",
            _ => "sdr"
        };

        var dolbyVision = video.DolbyVisionProfile();
        return dolbyVision is null ? baseRange : $"{baseRange}+{dolbyVision}";
    }

    public static bool NeedsToneMapping(string dynamicRange) =>
        dynamicRange.StartsWith("hdr10", StringComparison.Ordinal)
        || dynamicRange.StartsWith("hlg", StringComparison.Ordinal);

    /// <summary>
    /// Builds the label a viewer reads. The language code alone is not enough: one real film
    /// carries three tracks tagged <c>chi</c> (Cantonese Traditional, Simplified, Traditional)
    /// and two tagged <c>spa</c>, so a code-only menu shows identical rows.
    /// </summary>
    public static string BuildLabel(ProbeStream stream)
    {
        var language = LanguageName(stream.Language);
        var title = stream.Title;

        if (stream.IsCommentary)
            return CommentaryLabel(language, title);

        if (title is null)
            return language;

        if (stream.IsHearingImpaired
            && (title.Equals("SDH", StringComparison.OrdinalIgnoreCase)
                || title.Equals("CC", StringComparison.OrdinalIgnoreCase)))
        {
            return $"{language} {title.ToUpperInvariant()}";
        }

        // The title already names the language, so wrapping it would read "French (French)".
        if (title.Contains(language, StringComparison.OrdinalIgnoreCase))
            return title;

        return $"{language} ({title})";
    }

    /// <summary>
    /// Inside a menu already headed "Commentary", a track titled "Commentary" says nothing — and a
    /// real film ships thirteen of them, one per language, every one tagged with that same word. So
    /// the word is stripped and what remains qualifies the language: "Commentary" becomes "French",
    /// "Canadian - Commentary" becomes "French (Canadian)". A title that carries real information —
    /// who is speaking — is longer than a qualifier and is kept whole.
    /// </summary>
    private static string CommentaryLabel(string language, string? title)
    {
        if (title is null) return language;

        var cleaned = CommentaryWord()
            .Replace(title, " ")
            .Trim(' ', '-', '–', '—', ':', ',');

        if (cleaned.Length == 0) return language;
        if (cleaned.Length > 24) return title;

        return cleaned.Contains(language, StringComparison.OrdinalIgnoreCase)
            ? cleaned
            : $"{language} ({cleaned})";
    }

    [System.Text.RegularExpressions.GeneratedRegex(
        @"\s*[-–—]?\s*\bcommentary\b\s*",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex CommentaryWord();

    public static TrackKind KindOf(ProbeStream stream) =>
        stream.IsCommentary ? TrackKind.Commentary : TrackKind.Feature;

    public static string LanguageName(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "Unknown";
        return Languages.TryGetValue(code, out var name) ? name : code.ToUpperInvariant();
    }

    /// <summary>
    /// ISO 639-2/B codes as ffmpeg writes them, which is why this is a table and not
    /// <c>CultureInfo</c>: ffmpeg emits "fre" and "ger" where .NET expects "fra" and "deu".
    /// Unknown codes fall through to the raw code rather than being guessed at.
    /// </summary>
    private static readonly Dictionary<string, string> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["eng"] = "English", ["spa"] = "Spanish", ["fre"] = "French", ["fra"] = "French",
        ["ger"] = "German", ["deu"] = "German", ["ita"] = "Italian", ["por"] = "Portuguese",
        ["dut"] = "Dutch", ["nld"] = "Dutch", ["swe"] = "Swedish", ["nor"] = "Norwegian",
        ["dan"] = "Danish", ["fin"] = "Finnish", ["ice"] = "Icelandic", ["isl"] = "Icelandic",
        ["pol"] = "Polish", ["rus"] = "Russian", ["ukr"] = "Ukrainian", ["cze"] = "Czech",
        ["ces"] = "Czech", ["slo"] = "Slovak", ["slk"] = "Slovak", ["slv"] = "Slovenian",
        ["hrv"] = "Croatian", ["srp"] = "Serbian", ["bul"] = "Bulgarian", ["rum"] = "Romanian",
        ["ron"] = "Romanian", ["hun"] = "Hungarian", ["gre"] = "Greek", ["ell"] = "Greek",
        ["tur"] = "Turkish", ["heb"] = "Hebrew", ["ara"] = "Arabic", ["per"] = "Persian",
        ["fas"] = "Persian", ["hin"] = "Hindi", ["ben"] = "Bengali", ["tam"] = "Tamil",
        ["tel"] = "Telugu", ["tha"] = "Thai", ["vie"] = "Vietnamese", ["ind"] = "Indonesian",
        ["may"] = "Malay", ["msa"] = "Malay", ["fil"] = "Filipino", ["tgl"] = "Tagalog",
        ["jpn"] = "Japanese", ["kor"] = "Korean", ["chi"] = "Chinese", ["zho"] = "Chinese",
        ["cat"] = "Catalan", ["baq"] = "Basque", ["eus"] = "Basque", ["glg"] = "Galician",
        ["est"] = "Estonian", ["lav"] = "Latvian", ["lit"] = "Lithuanian"
    };
}
