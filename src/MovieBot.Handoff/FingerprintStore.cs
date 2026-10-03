using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Handoff;

/// <summary>One episode's audio fingerprint, and the title it was taken from.</summary>
public sealed record StoredFingerprint(string TitleId, int Season, int Number, uint[] Items);

/// <summary>
/// The audio fingerprint of every episode that has been through the library, kept apart from the
/// titles.
///
/// A recap is recognised by finding its clips in the episodes that came before, and those episodes
/// may have left the library by then: the library's clock removes a title that nobody has played,
/// and a fingerprint is about a hundred kilobytes where the title is a gigabyte. So the fingerprints
/// live in a dotted directory of the media root, which every listing of titles skips, and are
/// neither moved by settling nor removed with the title. They are written by this process alone
/// and are regenerable from the audio, so a lost one costs a recap that is not found, never a
/// wrong one.
///
/// A show is the IMDb id its episodes carry, and an episode is its place in the show. An episode
/// ingested again replaces its own file.
/// </summary>
public sealed partial class FingerprintStore(MediaRoots roots)
{
    public const string DirectoryName = ".fingerprints";

    private static readonly byte[] Magic = "MBFP"u8.ToArray();

    private string Root => Path.Combine(roots.Hot, DirectoryName);

    /// <summary>
    /// What groups the episodes of one show: its IMDb id, or the id's own stem — the part before
    /// <c>-s02e04</c> — when nothing identified the show. Null for a title that is not an episode,
    /// or whose name could not be a directory.
    /// </summary>
    public static string? ShowKey(Manifest manifest)
    {
        if (manifest.Episode is null) return null;

        if (manifest.Film?.ImdbId is { } imdb && ImdbId().IsMatch(imdb)) return imdb;

        var match = EpisodeSuffix().Match(manifest.Id);
        var stem = match.Success ? manifest.Id[..match.Index] : manifest.Id;
        return MediaRoots.IsSafeId(stem) ? stem : null;
    }

    public void Save(string showKey, StoredFingerprint fingerprint)
    {
        var path = PathOf(showKey, fingerprint.Season, fingerprint.Number);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var id = Encoding.UTF8.GetBytes(fingerprint.TitleId);
        var buffer = new byte[Magic.Length + 4 + id.Length + 4 + fingerprint.Items.Length * 4];
        var span = buffer.AsSpan();

        Magic.CopyTo(span);
        span = span[Magic.Length..];
        BinaryPrimitives.WriteInt32LittleEndian(span, id.Length);
        id.CopyTo(span[4..]);
        span = span[(4 + id.Length)..];
        BinaryPrimitives.WriteInt32LittleEndian(span, fingerprint.Items.Length);
        for (var i = 0; i < fingerprint.Items.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(span[(4 + i * 4)..], fingerprint.Items[i]);

        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, buffer);
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>The fingerprint of an episode, or null when none is kept or the file is not one.</summary>
    public StoredFingerprint? Load(string showKey, int season, int number)
    {
        var path = PathOf(showKey, season, number);
        if (!File.Exists(path)) return null;

        try
        {
            var bytes = File.ReadAllBytes(path);
            var span = bytes.AsSpan();
            if (span.Length < Magic.Length + 8 || !span[..Magic.Length].SequenceEqual(Magic)) return null;

            span = span[Magic.Length..];
            var idLength = BinaryPrimitives.ReadInt32LittleEndian(span);
            if (idLength < 0 || span.Length < 4 + idLength + 4) return null;

            var id = Encoding.UTF8.GetString(span.Slice(4, idLength));
            span = span[(4 + idLength)..];

            var count = BinaryPrimitives.ReadInt32LittleEndian(span);
            if (count < 0 || span.Length != 4 + count * 4) return null;

            var items = new uint[count];
            for (var i = 0; i < count; i++)
                items[i] = BinaryPrimitives.ReadUInt32LittleEndian(span[(4 + i * 4)..]);

            return new StoredFingerprint(id, season, number, items);
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Every episode of a show that has a fingerprint, as (season, number).</summary>
    public IReadOnlyList<(int Season, int Number)> Episodes(string showKey)
    {
        var directory = Path.Combine(Root, showKey);
        if (!Directory.Exists(directory)) return [];

        var found = new List<(int, int)>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.fp"))
        {
            var match = FileName().Match(Path.GetFileName(file));
            if (!match.Success) continue;

            found.Add((
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)));
        }

        return found;
    }

    private string PathOf(string showKey, int season, int number)
    {
        if (!MediaRoots.IsSafeId(showKey))
            throw new ArgumentException("A show key names a directory under the fingerprint root.", nameof(showKey));

        return Path.Combine(Root, showKey, string.Create(
            CultureInfo.InvariantCulture, $"s{season:00}e{number:00}.fp"));
    }

    [GeneratedRegex(@"^tt\d+$")]
    private static partial Regex ImdbId();

    [GeneratedRegex(@"-s\d+e\d+$")]
    private static partial Regex EpisodeSuffix();

    [GeneratedRegex(@"^s(\d+)e(\d+)\.fp$")]
    private static partial Regex FileName();
}
