namespace TheKrystalShip.MovieBot.Core;

/// <summary>Whether the cold root can be used, and what is wrong with it when it cannot.</summary>
public sealed record ColdStatus(bool Ready, string? Problem);

/// <summary>
/// Where the library's films are: the disk they are made on and the disk they are kept on.
///
/// A transcode writes a film in four-second pieces for as long as it runs, which is the part that
/// wants a fast disk. Reading one back afterwards is a single playhead at a little over a
/// megabyte a second, which any disk serves. So a film is made on the hot root and moved to the
/// cold one once it is finished, and the cold root is where the library's capacity comes from.
///
/// A title is in exactly one root. The cold root is resolved first, so during the moment a
/// settling film is in both, every reader is already on the copy that is staying.
///
/// The cold root is an upgrade rather than a requirement: with none configured, or with its
/// volume gone, everything on the hot root is served exactly as it is with one root. What has
/// settled is missing from the library until the volume comes back, which is the honest answer
/// and the reason the fault is reported rather than absorbed.
/// </summary>
public sealed class MediaRoots
{
    /// <summary>
    /// The file that proves the cold volume is mounted.
    ///
    /// A mount point is an ordinary directory when nothing is mounted on it, and a cold root that
    /// is really a directory on the hot disk would be written to happily: films would be copied
    /// onto the disk they were being moved off, and the hot copy deleted afterwards. Nothing
    /// about that fails, which is why it is checked rather than assumed. The marker is made once,
    /// by hand, on the volume itself, so it is present only when the volume is.
    /// </summary>
    public const string ColdMarker = ".moviebot-cold";

    /// <summary>
    /// Where a film is assembled while it is being moved. It is under the cold root so that the
    /// last step is a rename within one filesystem, which either happened or did not.
    /// </summary>
    public const string IncomingDirectory = ".incoming";

    /// <summary>
    /// How long a reading of the cold volume stands before it is taken again. The check is two
    /// stats, and it is on the path of every title lookup, so it is not taken per request; a
    /// volume that came back is picked up within this and needs no restart.
    /// </summary>
    public static readonly TimeSpan Recheck = TimeSpan.FromSeconds(5);

    private readonly TimeProvider _clock;

    private ColdStatus _cold = new(false, null);
    private DateTimeOffset _standsUntil = DateTimeOffset.MinValue;

    public MediaRoots(string hot, string? cold = null, TimeProvider? clock = null)
    {
        if (string.IsNullOrWhiteSpace(hot))
            throw new ArgumentException("The media root must be a path.", nameof(hot));

        Hot = Path.GetFullPath(hot);
        Cold = string.IsNullOrWhiteSpace(cold) ? null : Path.GetFullPath(cold);
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>Where films are made, and where one lives until it has settled.</summary>
    public string Hot { get; }

    /// <summary>Where films are kept, or null when the whole library lives on the hot root.</summary>
    public string? Cold { get; }

    /// <summary>Where a film being moved is assembled, or null when there is no cold root.</summary>
    public string? Incoming => Cold is null ? null : Path.Combine(Cold, IncomingDirectory);

    /// <summary>
    /// The state of the cold root, taken from the volume rather than remembered from startup.
    /// </summary>
    public ColdStatus ColdState
    {
        get
        {
            var now = _clock.GetUtcNow();
            if (now < _standsUntil) return _cold;

            var state = Examine();

            // Both are written after the reading is complete, and a racing caller either sees the
            // previous reading or this one. Two callers taking it at once compute the same answer.
            _cold = state;
            _standsUntil = now + Recheck;

            return state;
        }
    }

    /// <summary>Whether films may be read from and written to the cold root right now.</summary>
    public bool ColdReady => ColdState.Ready;

    private ColdStatus Examine()
    {
        if (Cold is null) return new ColdStatus(false, null);

        if (string.Equals(Cold, Hot, StringComparison.Ordinal))
            return new ColdStatus(false, $"{Cold} is the media root itself.");

        if (!Directory.Exists(Cold))
            return new ColdStatus(false, $"{Cold} does not exist.");

        if (!File.Exists(Path.Combine(Cold, ColdMarker)))
        {
            return new ColdStatus(false,
                $"{Cold} holds no {ColdMarker}, so the volume is not mounted there. "
                + $"If it is where it should be, make the marker with: touch {Path.Combine(Cold, ColdMarker)}");
        }

        return new ColdStatus(true, null);
    }

    /// <summary>
    /// Where a title's files are, or null when the library does not hold it.
    ///
    /// Cold first: a film that has settled is the copy that is staying, and during the moment a
    /// film is in both roots the hot one is about to be deleted.
    /// </summary>
    public string? DirectoryOf(string id)
    {
        if (Cold is not null && ColdReady)
        {
            var settled = Path.Combine(Cold, id);
            if (Directory.Exists(settled)) return settled;
        }

        var making = Path.Combine(Hot, id);
        return Directory.Exists(making) ? making : null;
    }

    /// <summary>Where a title is written while it is being made, which is always the hot root.</summary>
    public string WorkingDirectoryOf(string id) => Path.Combine(Hot, id);

    /// <summary>Where a title's files are once it has settled, or null when there is no cold root.</summary>
    public string? SettledDirectoryOf(string id) => Cold is null ? null : Path.Combine(Cold, id);

    /// <summary>
    /// Every title the library holds, in resolution order and without repeats.
    ///
    /// Directories whose names begin with a dot are the roots' own working space rather than
    /// films, which is why the one a settling film is assembled in is named that way. A volume's
    /// own root carries <c>lost+found</c>, which ext4 makes at mkfs, fsck needs, and only root
    /// can read.
    /// </summary>
    public IReadOnlyList<string> Ids()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ids = new List<string>();

        foreach (var root in InOrder())
        {
            if (!Directory.Exists(root)) continue;

            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                var id = Path.GetFileName(directory);
                if (!IsTitleDirectory(id)) continue;
                if (seen.Add(id)) ids.Add(id);
            }
        }

        return ids;
    }

    /// <summary>Whether a directory sitting in a root is a film rather than the volume's own.</summary>
    public static bool IsTitleDirectory(string name) =>
        name.Length > 0 && name[0] != '.' && name != "lost+found";

    /// <summary>The roots a title is looked for in, in the order it is looked for in them.</summary>
    public IEnumerable<string> InOrder()
    {
        if (Cold is not null && ColdReady) yield return Cold;
        yield return Hot;
    }
}
