using System.Text;
using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Subtitles;

/// <summary>
/// Subtitles fetched from outside, kept beside the library rather than inside it: the WebVTT as a file
/// under its own root, and what it is as a row.
///
/// The files live under their own root, away from the media directory, for three reasons that all
/// point the same way. Everything under the media root is regenerable from the source file and
/// these are not (each one cost a download from a limited daily allowance), so a re-ingest that
/// replaces a title's directory must not be able to take them with it. The transcode owns that
/// directory and rewrites the manifest inside it every few seconds, so anything written there by
/// another process is racing. And the service that serves the library holds the media root
/// read-only, which is a protection worth keeping rather than widening.
/// </summary>
public sealed class SubtitleStore(string root, CinemaDatabase database)
{
    public string Root { get; } = Path.GetFullPath(root);

    /// <summary>The path segment a subtitle is served under, within a title.</summary>
    public const string UriPrefix = "subs/";

    private const string Columns =
        "id, language, label, hearing_impaired, release, added_by, added_at, applied_shift_seconds, aligned_fraction";

    /// <summary>
    /// Everything fetched for one title, the earliest first. A subtitle is kept only once its file is
    /// on disk, so one whose file is missing is one somebody removed by hand, and is left out.
    /// </summary>
    public IReadOnlyList<SidecarSubtitle> For(string titleId)
    {
        var directory = DirectoryFor(titleId);
        if (directory is null) return [];

        return database.Read(c => c.Query(
                $"SELECT {Columns} FROM fetched_subtitles WHERE title_id = $title ORDER BY added_at, rowid",
                r => new SidecarSubtitle
                {
                    Id = r.GetString(0),
                    Language = r.GetString(1),
                    Label = r.GetString(2),
                    HearingImpaired = r.GetBool(3),
                    Release = r.GetStringOrNull(4),
                    AddedBy = r.GetStringOrNull(5),
                    AddedAt = r.GetInstant(6),
                    AppliedShiftSeconds = r.GetDoubleOrNull(7),
                    AlignedFraction = r.GetDoubleOrNull(8),
                },
                ("$title", titleId)))
            .Where(s => File.Exists(Path.Combine(directory, s.Id + ".vtt")))
            .ToList();
    }

    /// <summary>
    /// The file behind a <c>subs/</c> URI, or null when it is not one this holds. Resolved first
    /// and checked for containment afterwards, because a path that merely looks safe can still
    /// land outside the root through an encoded traversal or a symlink.
    /// </summary>
    public string? Resolve(string titleId, string fileName)
    {
        var directory = DirectoryFor(titleId);
        if (directory is null) return null;

        var requested = Path.GetFullPath(Path.Combine(directory, fileName));

        return requested.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal)
               && File.Exists(requested)
            ? requested
            : null;
    }

    /// <summary>
    /// Keeps a subtitle: the file first, through a temporary file and an atomic move, and the row
    /// second. That order is the whole guarantee: a reader that sees a row can always open the file it
    /// names, and a write interrupted halfway leaves a file nothing points at rather than a menu row
    /// that fails to load. Fetching the same subtitle again replaces it.
    /// </summary>
    public void Save(string titleId, SidecarSubtitle subtitle, string webVtt)
    {
        var directory = DirectoryFor(titleId)
            ?? throw new ArgumentException($"'{titleId}' is not a title id.", nameof(titleId));

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, subtitle.Id + ".vtt");
        File.WriteAllText(path + ".tmp", webVtt, new UTF8Encoding(false));
        File.Move(path + ".tmp", path, overwrite: true);

        database.Write(c => c.Execute(
            $"INSERT OR REPLACE INTO fetched_subtitles (title_id, {Columns}) VALUES "
            + "($title, $id, $language, $label, $hi, $release, $by, $at, $shift, $aligned)",
            ("$title", titleId), ("$id", subtitle.Id), ("$language", subtitle.Language), ("$label", subtitle.Label),
            ("$hi", subtitle.HearingImpaired), ("$release", subtitle.Release), ("$by", subtitle.AddedBy),
            ("$at", subtitle.AddedAt), ("$shift", subtitle.AppliedShiftSeconds), ("$aligned", subtitle.AlignedFraction)));
    }

    private string? DirectoryFor(string titleId) =>
        Library.TitleLibrary.IsSafeId(titleId) ? Path.Combine(Root, titleId) : null;

    /// <summary>
    /// What the player sees: one more entry in the title's subtitle list, marked as having come
    /// from outside the file rather than out of it.
    /// </summary>
    public static SubtitleTrack AsTrack(SidecarSubtitle subtitle) => new()
    {
        Id = subtitle.Id,
        Kind = TrackKind.Feature,
        Language = subtitle.Language,
        Label = subtitle.Label,
        HearingImpaired = subtitle.HearingImpaired,
        Source = SubtitleSource.Sidecar,
        Available = true,
        Uri = UriPrefix + subtitle.Id + ".vtt",
        AppliedShiftSeconds = subtitle.AppliedShiftSeconds,
        AlignedFraction = subtitle.AlignedFraction,
        AddedBy = subtitle.AddedBy
    };
}
