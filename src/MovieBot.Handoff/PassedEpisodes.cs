using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Acquire.Search;

namespace TheKrystalShip.MovieBot.Handoff;

/// <summary>
/// Whether the file of an episode a room moved past (<see cref="TorrentTags.Passed"/>) is let go of.
///
/// It is decided by the hand-off, which knows whether the episode's transcode has begun: a
/// transcode reading a file that stops arriving is held behind it for good.
/// </summary>
public static class PassedEpisodes
{
    /// <summary>
    /// True when nothing else wants the episode's file. It is kept on a torrent whose picks did not
    /// all say why (<see cref="TorrentTags.PicksNamed"/>), when the torrent names no file of it or
    /// its file holds another episode too, when another room waits on it or something kept it
    /// (<see cref="TorrentTags.Keep"/>), and when its transcode has begun or its title exists
    /// (<paramref name="started"/>).
    /// </summary>
    public static bool LetGo(IReadOnlyCollection<string> tags, EpisodeNumber episode, EpisodeFile? file, bool started) =>
        tags.Contains(TorrentTags.PicksNamed)
        && file is { Episodes.Count: 1 }
        && !tags.Any(t => TorrentTags.Keeps(t, episode) || TorrentTags.ReadWatch(t)?.Episode == episode)
        && !started;
}
