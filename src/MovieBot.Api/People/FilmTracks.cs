using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.People;

/// <summary>
/// The tracks one person watches one title with. A subtitle is chosen by its track, never by its
/// language: <see cref="SubtitleId"/> names it, <see cref="SubtitleOff"/> is a chosen "no subtitles",
/// and neither is no choice, so the film starts on the person's language. The same goes for the audio
/// track.
/// </summary>
public sealed record FilmTracks
{
    public required string TitleId { get; init; }
    public string? SubtitleId { get; init; }
    public bool SubtitleOff { get; init; }
    public string? AudioId { get; init; }

    /// <summary>
    /// Where this record stands in the order the person's writes to this film were kept, on what a write
    /// answers and what a push carries; absent on a read.
    /// </summary>
    public long? Revision { get; init; }
}

/// <summary>
/// What a request sets: a field it names is set and one it leaves out is kept. Naming a subtitle
/// track or <see cref="SubtitleOff"/> replaces the subtitle choice either way.
/// </summary>
public sealed record TrackChoice
{
    public string? SubtitleId { get; init; }
    public bool? SubtitleOff { get; init; }
    public string? AudioId { get; init; }

    /// <summary>Why this request says nothing the store can keep, or null.</summary>
    public string? Malformed() =>
        SubtitleId is null && SubtitleOff is not true && AudioId is null
            ? "Name a subtitle track, set subtitleOff, or name an audio track."
            : SubtitleId is not null && SubtitleOff is true
                ? "Subtitles are either off or one track."
                : null;

    /// <summary>
    /// Why the film does not have what this chooses, or null. A subtitle is a track the manifest lists
    /// and can show; an audio track is one the manifest lists.
    /// </summary>
    public string? RefusedBy(Manifest manifest)
    {
        if (SubtitleId is { } subtitle
            && !manifest.Subtitles.Any(t => t.Id == subtitle && t.Available && t.Uri is not null))
            return "This film has no subtitle track with that id.";
        if (AudioId is { } audio && !manifest.Audio.Any(t => t.Id == audio))
            return "This film has no audio track with that id.";
        return null;
    }
}

/// <summary>
/// Everybody's track choices, one row per person and title. Somebody who chose nothing for a title has
/// no row.
/// </summary>
public sealed class TrackStore(CinemaDatabase database)
{
    public FilmTracks For(string userId, string titleId) =>
        database.Read(c => Read(c, userId, titleId)) ?? new FilmTracks { TitleId = titleId };

    /// <summary>Keeps what <paramref name="choice"/> sets, leaves the rest, and answers the film's tracks as they now stand.</summary>
    public FilmTracks Set(string userId, string titleId, TrackChoice choice) =>
        database.Write(c =>
        {
            var before = Read(c, userId, titleId) ?? new FilmTracks { TitleId = titleId };
            var after = before with
            {
                SubtitleId = choice.SubtitleId is not null ? choice.SubtitleId : choice.SubtitleOff is true ? null : before.SubtitleId,
                SubtitleOff = choice.SubtitleId is not null ? false : choice.SubtitleOff is true || before.SubtitleOff,
                AudioId = choice.AudioId ?? before.AudioId,
            };
            c.Execute(
                "INSERT OR REPLACE INTO viewer_tracks (user_id, title_id, subtitle_id, subtitle_off, audio_id) "
                + "VALUES ($user, $title, $subtitle, $off, $audio)",
                ("$user", userId), ("$title", titleId), ("$subtitle", after.SubtitleId), ("$off", after.SubtitleOff),
                ("$audio", after.AudioId));
            return after with { Revision = Revisions.Next($"t:{userId}:{titleId}") };
        });

    private static FilmTracks? Read(Microsoft.Data.Sqlite.SqliteConnection c, string userId, string titleId) =>
        c.First("SELECT subtitle_id, subtitle_off, audio_id FROM viewer_tracks WHERE user_id = $user AND title_id = $title",
            r => new FilmTracks
            {
                TitleId = titleId,
                SubtitleId = r.GetStringOrNull(0),
                SubtitleOff = r.GetBool(1),
                AudioId = r.GetStringOrNull(2),
            },
            ("$user", userId), ("$title", titleId));
}
