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
    /// The smallest file worth treating as the film, in mebibytes. A torrent carries samples,
    /// trailers and extras, and picking the largest file is only right if there is a floor under
    /// what counts at all.
    /// </summary>
    public int MinimumFilmSizeMiB { get; set; } = 200;
}
