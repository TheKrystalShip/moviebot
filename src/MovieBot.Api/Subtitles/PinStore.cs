using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Subtitles;

/// <summary>
/// Which subtitle tracks somebody has watched a film with and confirmed.
///
/// Kept apart from the media root, as the fetched subtitles are: a pin is a person's judgement and
/// nothing can regenerate it, where everything under the media root can be rebuilt from the source
/// file. One row per track, so two people pinning at the same moment cannot overwrite each other.
/// </summary>
public sealed class PinStore(CinemaDatabase database)
{
    /// <summary>
    /// Every pin on a title, by the track it belongs to.
    ///
    /// A pin naming a track the film no longer has is dropped by the reader rather than honoured. Ids
    /// come from stream indices, and a re-ingest can hand the same number to a different track:
    /// pointing an old judgement at whatever now holds that number would be worse than losing it.
    /// </summary>
    public IReadOnlyDictionary<string, SubtitlePin> For(string titleId) =>
        database.Read(c => c.Query(
                "SELECT track_id, pinned_by, pinned_at, watched_fraction FROM subtitle_pins WHERE title_id = $title",
                r => KeyValuePair.Create(r.GetString(0),
                    new SubtitlePin { PinnedBy = r.GetString(1), PinnedAt = r.GetInstant(2), WatchedFraction = r.GetDoubleOrNull(3) }),
                ("$title", titleId)))
            .ToDictionary(StringComparer.Ordinal);

    public void Pin(string titleId, string trackId, SubtitlePin pin)
    {
        if (!Library.TitleLibrary.IsSafeId(titleId) || !IsSafeTrackId(trackId))
            throw new ArgumentException("Not a title and track this can hold.", nameof(trackId));

        database.Write(c => c.Execute(
            "INSERT OR REPLACE INTO subtitle_pins (title_id, track_id, pinned_by, pinned_at, watched_fraction) "
            + "VALUES ($title, $track, $by, $at, $fraction)",
            ("$title", titleId), ("$track", trackId), ("$by", pin.PinnedBy), ("$at", pin.PinnedAt), ("$fraction", pin.WatchedFraction)));
    }

    /// <summary>
    /// Removes a pin. Anyone may, deliberately: a wrong pin is fixed by one click from whoever
    /// notices, which is a better answer for a room of friends than deciding who is allowed.
    /// </summary>
    public void Unpin(string titleId, string trackId) =>
        database.Write(c => c.Execute("DELETE FROM subtitle_pins WHERE title_id = $title AND track_id = $track",
            ("$title", titleId), ("$track", trackId)));

    /// <summary>
    /// What a track id is made of where a manifest names one: letters, digits, <c>-</c> and <c>_</c>, at
    /// most a hundred of them. Anything else names no track and is refused.
    /// </summary>
    public static bool IsSafeTrackId(string id) =>
        !string.IsNullOrWhiteSpace(id)
        && id.Length <= 100
        && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
