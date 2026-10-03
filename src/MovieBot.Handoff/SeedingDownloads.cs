using TheKrystalShip.MovieBot.Acquire.Download;

namespace TheKrystalShip.MovieBot.Handoff;

/// <summary>
/// The downloads the torrent client holds and the one thing done to them here: letting go of one.
/// The seam is what lets the tracker's clock be exercised without a torrent client.
/// </summary>
public interface ISeedingDownloads
{
    Task<IReadOnlyList<DownloadStatus>> ListAsync(CancellationToken ct);

    /// <summary>Removes the torrent and the files it downloaded, through the client.</summary>
    Task RemoveAsync(string hash, CancellationToken ct);
}

public sealed class ClientDownloads(AcquisitionService acquisition) : ISeedingDownloads
{
    public Task<IReadOnlyList<DownloadStatus>> ListAsync(CancellationToken ct) => acquisition.ListAsync(ct);

    public Task RemoveAsync(string hash, CancellationToken ct) => acquisition.RemoveAsync(hash, ct);
}
