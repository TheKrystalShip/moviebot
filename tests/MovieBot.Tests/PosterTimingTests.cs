using TheKrystalShip.MovieBot.Ingest.Pipeline;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A manifest is read the moment it exists. A film is announced within seconds of the first
/// segments landing and is offered by name for the hour that follows, so a manifest naming a
/// poster that is not on disk yet is a message with a hole in it — and a surface that fetches
/// artwork on its own servers caches the 404 rather than trying again later.
///
/// The catalogue's poster is already a file on this disk and is put down before the manifest names
/// it. A source's own cover art has to be demuxed out of the source, which for a film still
/// downloading means waiting for the download.
/// </summary>
public sealed class PosterTimingTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void Cover_art_is_only_ready_early_when_the_source_is_already_whole(
        bool hasCoverArt, bool sourceIsWhole, bool ready)
    {
        Assert.Equal(ready, IngestPipeline.EmbeddedPosterIsReady(hasCoverArt, sourceIsWhole));
    }
}
