namespace TheKrystalShip.MovieBot.Handoff;

/// <summary>Where finished downloads are turned into something the player can open.</summary>
public sealed class HandoffOptions
{
    public const string Section = "Handoff";

    /// <summary>
    /// Where the ingest writes. The same directory the API serves from: a film is in the library
    /// because it is here, so writing anywhere else produces a transcode nobody can watch.
    /// </summary>
    public string MediaRoot { get; set; } = "";

    /// <summary>
    /// How often finished downloads are looked for. A transcode takes minutes and the download
    /// before it takes longer, so there is nothing to gain from asking more often.
    /// </summary>
    public int PollSeconds { get; set; } = 30;

    /// <summary>
    /// How much of a download must have arrived before transcoding starts, as a fraction.
    ///
    /// Transcoding alongside the download is what removes the wait for it to finish, and the
    /// head start is only there so the first pieces and the container's own index are present
    /// before anything tries to read them. The reader is held behind the arrived bytes from then
    /// on, so this being too small costs a pause rather than a broken film.
    ///
    /// Zero waits for the whole download, which is the older behaviour and the safer one if a
    /// transcode ever has to be reasoned about in isolation.
    /// </summary>
    public double StartAtProgress { get; set; } = 0.05;

    /// <summary>
    /// The smallest file worth treating as the film, in mebibytes. A torrent carries samples,
    /// trailers and extras, and picking the largest file is only right if there is a floor under
    /// what counts at all.
    /// </summary>
    public int MinimumFilmSizeMiB { get; set; } = 200;

    /// <summary>
    /// Which subtitle languages to keep from a download. Empty keeps every one of them.
    ///
    /// A disc carries thirty and a room reads one. They cost nothing to extract — they come out of
    /// a single pass either way — but a menu forty-eight rows long is one in which the three rows
    /// anybody wants cannot be found.
    ///
    /// Widening this later is recoverable: the source file stays on disk for seeding, so a
    /// language left behind can be extracted again from it.
    /// </summary>
    public string[] SubtitleLanguages { get; set; } = ["eng"];
}
