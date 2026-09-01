namespace TheKrystalShip.MovieBot.Ingest.Pipeline;

/// <summary>
/// How much of the source file can be read right now.
///
/// A source that is still arriving is readable only up to the point it has arrived at, and the
/// ingest has no way of knowing where that is — the file's length says nothing, since space for
/// it is claimed up front and the bytes past the end of what has arrived are zeros rather than
/// the end of the file.
///
/// Implemented by whoever is fetching the file. The ingest stays ignorant of how it is arriving.
/// </summary>
public interface ISourceAvailability
{
    /// <summary>
    /// Bytes readable from the start of the file, without a gap.
    ///
    /// Contiguous, because ffmpeg reads forwards: bytes beyond a hole are not reachable, and an
    /// offset with a hole behind it would be read straight through.
    /// </summary>
    Task<long> ReadableBytesAsync(CancellationToken ct);

    /// <summary>Whether the whole file has arrived and nothing needs guarding any more.</summary>
    Task<bool> IsCompleteAsync(CancellationToken ct);
}
