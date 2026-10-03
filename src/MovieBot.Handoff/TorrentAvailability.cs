using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Ingest.Pipeline;

namespace TheKrystalShip.MovieBot.Handoff;

/// <summary>
/// Answers the ingest's question about a still-arriving source by asking the torrent client.
///
/// This is the only thing in the hand-off that knows both halves: the ingest asks how much of a
/// file it may read, and the torrent client is what knows. Neither has to learn about the other.
/// </summary>
/// <param name="torrentPath">
/// The file's path inside the torrent, as the client names it, when the file is one of several
/// that are ingested apart — an episode of a pack. Its own completeness is then what counts, since
/// the torrent as a whole finishes only with its last episode.
/// </param>
public sealed class TorrentAvailability(
    AcquisitionService acquisition,
    string hash,
    string filePath,
    string? torrentPath = null) : ISourceAvailability
{
    public Task<long> ReadableBytesAsync(CancellationToken ct) =>
        acquisition.ReadableBytesAsync(hash, filePath, ct);

    public async Task<bool> IsCompleteAsync(CancellationToken ct)
    {
        if (torrentPath is not null)
        {
            var files = await acquisition.FilesAsync(hash, ct);
            var file = files.FirstOrDefault(f => f.Name == torrentPath);

            // Forgotten, as below: the file it was reading is still on disk.
            return file is null || file.Progress >= 1;
        }

        var status = await acquisition.StatusAsync(hash, ct);

        // A torrent the client has forgotten is treated as complete rather than as forever
        // incomplete: the alternative holds the transcode back permanently over a torrent that
        // somebody removed, and the file it was reading is still on disk.
        return status is null || status.IsFinished;
    }
}
