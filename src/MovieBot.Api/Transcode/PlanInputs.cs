using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Transcode;

/// <summary>
/// What the people in a room would choose to watch at, as their players report it: the rung each
/// player's own adaptation would pick unconstrained, which is not always the one it is playing.
/// </summary>
public interface IViewerQuality
{
    /// <summary>A want with no ceiling: the best rung the film has.</summary>
    public const int Top = int.MaxValue;

    /// <summary>
    /// The heights the room's viewers want, one per connected viewer. A player that says nothing,
    /// Safari playing HLS itself among them, wants the best rung the film has. Empty for a room
    /// nobody is in.
    /// </summary>
    IReadOnlyList<int> WantedHeights(string sessionId);
}

/// <summary>
/// How far each output of a title has got, read from its manifest. The manifest is the one thing
/// every title has, and the ingest writes a head per rung and for the audio into it as it goes.
/// </summary>
public interface IManifestHeads
{
    TitleHeads Of(Manifest manifest);
}

/// <summary>
/// Reads each rung's and the audio's own head, and the overall head for any output whose manifest
/// carries none of its own. A finished title's outputs are all whole.
/// </summary>
public sealed class ManifestHeads : IManifestHeads
{
    public TitleHeads Of(Manifest manifest)
    {
        var ready = manifest.Status == TitleStatus.Ready;
        var overall = ready ? manifest.DurationSeconds : manifest.HeadSeconds ?? 0;

        var rungs = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var rendition in manifest.Video.Renditions)
            rungs[TranscodeCatalog.DirectoryOf(rendition)] = ready ? overall : rendition.HeadSeconds ?? overall;

        return new TitleHeads(rungs, ready ? overall : manifest.AudioHeadSeconds ?? overall);
    }
}
