using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Ingest.Pipeline;
using TheKrystalShip.MovieBot.Ingest.Probe;

namespace TheKrystalShip.MovieBot.Handoff;

/// <summary>The disk a transcode is written to.</summary>
public enum Disk { Media, Cold }

/// <summary>
/// Where a transcode is to be written, or why it is not to start yet.
/// </summary>
/// <param name="Disk">The disk chosen, or null when neither has room.</param>
/// <param name="Reason">Why there is no disk; null when one was chosen.</param>
public sealed record Placement(Disk? Disk, string? Reason)
{
    public bool Placed => Disk is not null;
}

/// <summary>
/// How much disk a transcode will take, from the ladder and the running time.
///
/// The bitrates are what the encoder is held to (<c>-b:v</c> with a ceiling above it), so size is
/// bitrate times duration rather than a measurement of the source. An overhead factor covers what
/// is not video or audio payload: the container's framing in every two-second segment, playlists,
/// subtitles, the poster and the sheet of scrub previews.
/// </summary>
public static class TranscodeEstimate
{
    /// <summary>The share added over the payload for everything that is not the encoded streams.</summary>
    public const double Overhead = 1.10;

    /// <summary>
    /// The running time assumed when the probe reports none: three hours, which is longer than
    /// nearly any film. A reservation too large only costs a placement a disk it did not need.
    /// </summary>
    public static readonly double UnknownDurationSeconds = TimeSpan.FromHours(3).TotalSeconds;

    /// <summary>The bytes a transcode writes: every stream's bitrate times the running time, plus the overhead.</summary>
    public static long Bytes(double durationSeconds, IEnumerable<int> videoKbps, IEnumerable<int> audioKbps)
    {
        var seconds = durationSeconds > 0 ? durationSeconds : UnknownDurationSeconds;
        var kilobits = (videoKbps.Sum(k => (long)k) + audioKbps.Sum(k => (long)k)) * seconds;

        return (long)(kilobits * 1000 / 8 * Overhead);
    }

    /// <summary>
    /// The estimate for a probed source under the options the ingest will run with: the top rung
    /// and each step-down the source is wider than, each audio track at its own rate, and
    /// the dialogue boost's extra rendition of every track that is not commentary.
    /// </summary>
    public static long For(ProbeResult probe, IngestOptions options)
    {
        var video = probe.Streams.FirstOrDefault(s => s.IsVideo && !s.IsAttachedPicture);
        var ladder = VideoLadder.For(video?.Width ?? 0, video?.Height ?? 0, options);

        var primary = VideoLadder.ParseBitrate(options.PrimaryAudioBitrate);
        var commentary = VideoLadder.ParseBitrate(options.CommentaryAudioBitrate);

        var audio = new List<int>();
        foreach (var stream in probe.Streams.Where(s => s.IsAudio))
        {
            audio.Add(stream.IsCommentary ? commentary : primary);
            if (options.DialogueBoost && !stream.IsCommentary) audio.Add(primary);
        }

        return Bytes(probe.Format.DurationSeconds, ladder.Select(r => r.BitrateKbps), audio);
    }
}

/// <summary>
/// The rule for which disk a transcode is written to.
///
/// It is decided once, when the ingest starts, against a reservation: a transcode that began and
/// then ran out of room would stop partway with a manifest nobody can finish, in front of a room.
/// </summary>
public static class TranscodePlacement
{
    /// <summary>
    /// The preferred disk when the reservation fits what it has left after the downloads still
    /// arriving and the other transcodes still writing; otherwise the other disk, when it has the
    /// room; otherwise nowhere, and the film stays owed. The cold disk is a candidate only while
    /// its volume is present.
    /// </summary>
    /// <param name="reservationBytes">What the transcode is expected to take.</param>
    /// <param name="media">The media root's volume as it stands.</param>
    /// <param name="cold">The cold root's volume, or null when it is absent or not configured.</param>
    /// <param name="preferred">The disk tried first (<see cref="HandoffOptions.TranscodeDisk"/>).</param>
    public static Placement Decide(
        long reservationBytes, VolumeRoom media, VolumeRoom? cold, Disk preferred = Disk.Media)
    {
        var mediaFits = media.Fits(reservationBytes);
        var coldFits = cold is not null && cold.Fits(reservationBytes);

        if (preferred == Disk.Cold && coldFits) return new Placement(Disk.Cold, null);
        if (mediaFits) return new Placement(Disk.Media, null);
        if (coldFits) return new Placement(Disk.Cold, null);

        var needed = reservationBytes / (double)(1L << 30);
        var onMedia = media.AvailableBytes / (double)(1L << 30);

        return new Placement(null,
            $"It needs {needed:0.#} GiB; the media root has {onMedia:0.#} GiB to give and "
            + (cold is null
                ? "there is no cold storage to use."
                : $"cold storage has {cold.AvailableBytes / (double)(1L << 30):0.#} GiB."));
    }
}
