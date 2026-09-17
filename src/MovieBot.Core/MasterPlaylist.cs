using System.Globalization;
using System.Text;

namespace TheKrystalShip.MovieBot.Core;

/// <summary>
/// Composes the HLS master playlist that binds a title's audio renditions to its video.
///
/// Without one, a player handed the video media playlist alone plays the film silently and has
/// no audio track to switch to — the media playlists carry no reference to each other. The
/// master is what makes the output self-contained: it plays in VLC, in Safari and through
/// hls.js from the same file, rather than requiring every client to compose its own.
///
/// Subtitles stay out of it. They are standalone WebVTT files rather than segmented renditions,
/// and players attach them as text tracks from the manifest.
/// </summary>
public static class MasterPlaylist
{
    public const string FileName = "master.m3u8";

    /// <summary>
    /// Builds the playlist from a manifest. URIs are relative, so the file resolves the same way
    /// whether it is opened from disk or served from <c>/media/{id}/</c>.
    /// </summary>
    public static string Build(Manifest manifest)
    {
        const string audioGroup = "audio";

        var builder = new StringBuilder();
        builder.AppendLine("#EXTM3U");
        builder.AppendLine("#EXT-X-VERSION:7");
        builder.AppendLine("#EXT-X-INDEPENDENT-SEGMENTS");

        var defaultAudio = manifest.Audio.FirstOrDefault(a => a.Default) ?? manifest.Audio.FirstOrDefault();
        var usedNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var track in manifest.Audio)
        {
            // NAME identifies a rendition within its group, so a repeated label has to be broken
            // apart or a player cannot tell two tracks apart.
            var name = Escape(track.Label);
            while (!usedNames.Add(name))
                name = $"{name} ({track.Id})";

            var isDefault = ReferenceEquals(track, defaultAudio);
            builder.Append("#EXT-X-MEDIA:TYPE=AUDIO")
                .Append(",GROUP-ID=\"").Append(audioGroup).Append('"')
                .Append(",NAME=\"").Append(name).Append('"')
                .Append(",LANGUAGE=\"").Append(Escape(track.Language)).Append('"')
                .Append(",DEFAULT=").Append(isDefault ? "YES" : "NO")
                .Append(",AUTOSELECT=").Append(isDefault ? "YES" : "NO")
                .Append(",CHANNELS=\"").Append(track.Channels.ToString(CultureInfo.InvariantCulture)).Append('"')
                .Append(",URI=\"").Append(track.Uri).Append('"')
                .AppendLine();
        }

        var audioAttribute = manifest.Audio.Count > 0 ? $",AUDIO=\"{audioGroup}\"" : string.Empty;

        foreach (var rendition in manifest.Video.Renditions)
        {
            // CODECS is deliberately absent: a player reads the real codecs from the fMP4
            // initialisation segments, and a declared string that disagrees with them costs a
            // source buffer for no benefit.
            var (width, height) = rendition.SizeIn(manifest.Video);

            builder.Append("#EXT-X-STREAM-INF:BANDWIDTH=")
                .Append((rendition.BitrateKbps * 1000).ToString(CultureInfo.InvariantCulture))
                .Append(",RESOLUTION=")
                .Append(width.ToString(CultureInfo.InvariantCulture))
                .Append('x')
                .Append(height.ToString(CultureInfo.InvariantCulture))
                .Append(audioAttribute)
                .AppendLine();

            builder.AppendLine(rendition.Uri);
        }

        return builder.ToString();
    }

    public static void Write(string outputDirectory, Manifest manifest) =>
        File.WriteAllText(Path.Combine(outputDirectory, FileName), Build(manifest));

    /// <summary>
    /// Attribute values are double-quoted, so an embedded double quote would end the value early.
    /// Real titles carry them: a commentary track named with a quoted nickname is enough.
    /// </summary>
    private static string Escape(string value) => value.Replace('"', '\'');
}
