using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.MovieBot.Handoff;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The two roots a film can be under, and the move that takes it from one to the other.
///
/// The marker file stands in for the volume being mounted. A cold root that is really an empty
/// directory on the hot disk is the failure worth guarding: films would be copied onto the disk
/// they were being moved off and the originals deleted, and nothing about that throws.
/// </summary>
public sealed class ColdStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "moviebot-cold-tests", Guid.NewGuid().ToString("n"));

    private string Hot => Path.Combine(_root, "media");
    private string Cold => Path.Combine(_root, "cold");

    public ColdStorageTests()
    {
        Directory.CreateDirectory(Hot);
        Directory.CreateDirectory(Cold);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private void Mount() => File.WriteAllText(Path.Combine(Cold, MediaRoots.ColdMarker), "");

    private readonly FakeTimeProvider _clock = new();

    private MediaRoots Roots() => new(Hot, Cold, _clock);

    private ColdStore Store(MediaRoots roots) => new(roots, NullLogger<ColdStore>.Instance);

    private static void WriteTitle(string root, string id, string segment)
    {
        Directory.CreateDirectory(Path.Combine(root, id, "v0"));
        File.WriteAllText(Path.Combine(root, id, "manifest.json"), $$"""{"id":"{{id}}"}""");
        File.WriteAllText(Path.Combine(root, id, "v0", "0.m4s"), segment);
    }

    [Fact]
    public void An_unmounted_cold_root_is_not_used()
    {
        var roots = Roots();

        Assert.False(roots.ColdReady);
        Assert.Contains(MediaRoots.ColdMarker, roots.ColdState.Problem);
    }

    [Fact]
    public void A_cold_root_that_names_the_media_root_is_refused()
    {
        var roots = new MediaRoots(Hot, Hot);

        Assert.False(roots.ColdReady);
        Assert.Contains("the media root itself", roots.ColdState.Problem);
    }

    [Fact]
    public void No_cold_root_is_not_a_fault()
    {
        var roots = new MediaRoots(Hot);

        Assert.False(roots.ColdReady);
        Assert.Null(roots.ColdState.Problem);
        Assert.Null(roots.Cold);
    }

    [Fact]
    public void A_settled_film_is_resolved_before_the_copy_being_made()
    {
        Mount();
        WriteTitle(Hot, "a-film", "being made");
        WriteTitle(Cold, "a-film", "settled");

        var directory = Roots().DirectoryOf("a-film");

        Assert.Equal(Path.Combine(Cold, "a-film"), directory);
    }

    [Fact]
    public void A_film_on_a_volume_that_is_gone_is_not_in_the_library()
    {
        WriteTitle(Cold, "kept-film", "settled");

        var roots = Roots();

        Assert.Null(roots.DirectoryOf("kept-film"));
        Assert.Empty(roots.Ids());
    }

    [Fact]
    public void Films_on_either_root_are_listed_once()
    {
        Mount();
        WriteTitle(Hot, "being-made", "x");
        WriteTitle(Hot, "overlapping", "hot");
        WriteTitle(Cold, "overlapping", "cold");
        WriteTitle(Cold, "settled", "y");
        Directory.CreateDirectory(Path.Combine(Cold, MediaRoots.IncomingDirectory, "half-copied"));

        var ids = Roots().Ids();

        Assert.Equal(
            ["being-made", "overlapping", "settled"],
            ids.Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// ext4 makes <c>lost+found</c> at the root of every volume, fsck needs it, and only root can
    /// read it. It is not a dotted name, so nothing about it says "not a film" but this.
    /// </summary>
    [Fact]
    public void A_volumes_own_directories_are_not_films()
    {
        Mount();
        Directory.CreateDirectory(Path.Combine(Cold, "lost+found"));
        WriteTitle(Cold, "a-film", "settled");

        Assert.Equal(["a-film"], Roots().Ids().ToArray());
    }

    [Fact]
    public async Task Settling_moves_the_film_and_leaves_nothing_behind()
    {
        Mount();
        WriteTitle(Hot, "a-film", "the picture");

        var stamped = new DateTime(2020, 5, 4, 3, 2, 1, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(Hot, "a-film", "v0", "0.m4s"), stamped);

        var roots = Roots();
        Assert.True(await Store(roots).SettleAsync("a-film", CancellationToken.None));

        Assert.False(Directory.Exists(Path.Combine(Hot, "a-film")));
        Assert.Equal(Path.Combine(Cold, "a-film"), roots.DirectoryOf("a-film"));
        Assert.Equal("the picture", File.ReadAllText(Path.Combine(Cold, "a-film", "v0", "0.m4s")));

        // Artwork is served asking to be revalidated, so a film stamped with the moment it moved
        // would have every viewer fetch all of it again for nothing.
        Assert.Equal(stamped, File.GetLastWriteTimeUtc(Path.Combine(Cold, "a-film", "v0", "0.m4s")));

        Assert.Empty(Directory.EnumerateFileSystemEntries(
            Path.Combine(Cold, MediaRoots.IncomingDirectory)));
    }

    [Fact]
    public async Task Settling_onto_a_volume_that_is_gone_leaves_the_film_alone()
    {
        WriteTitle(Hot, "a-film", "the picture");

        var roots = Roots();
        Assert.False(await Store(roots).SettleAsync("a-film", CancellationToken.None));

        Assert.True(Directory.Exists(Path.Combine(Hot, "a-film")));
        Assert.False(Directory.Exists(Path.Combine(Cold, "a-film")));
        Assert.Equal(Path.Combine(Hot, "a-film"), roots.DirectoryOf("a-film"));
    }

    [Fact]
    public async Task A_film_being_made_again_takes_the_settled_copy_with_it()
    {
        Mount();
        WriteTitle(Cold, "a-film", "the old release");
        WriteTitle(Hot, "a-film", "the new one");

        var roots = Roots();
        Store(roots).ClearSettled("a-film");

        Assert.Equal(Path.Combine(Hot, "a-film"), roots.DirectoryOf("a-film"));
        Assert.Equal(
            "the new one",
            File.ReadAllText(Path.Combine(roots.DirectoryOf("a-film")!, "v0", "0.m4s")));

        // And it settles again over nothing.
        Assert.True(await Store(roots).SettleAsync("a-film", CancellationToken.None));
        Assert.Equal("the new one", File.ReadAllText(Path.Combine(Cold, "a-film", "v0", "0.m4s")));
    }

    [Fact]
    public void What_a_settle_left_half_copied_is_cleared()
    {
        Mount();
        Directory.CreateDirectory(Path.Combine(Cold, MediaRoots.IncomingDirectory, "half-copied", "v0"));
        File.WriteAllText(
            Path.Combine(Cold, MediaRoots.IncomingDirectory, "half-copied", "v0", "0.m4s"), "part");

        Store(Roots()).SweepIncoming();

        Assert.Empty(Directory.EnumerateFileSystemEntries(
            Path.Combine(Cold, MediaRoots.IncomingDirectory)));
    }

    [Fact]
    public async Task A_volume_that_comes_back_is_picked_up_without_a_restart()
    {
        WriteTitle(Hot, "a-film", "the picture");

        var roots = Roots();
        Assert.False(roots.ColdReady);

        Mount();

        // The reading of the volume stands for a few seconds before it is taken again, which is
        // what keeps it off the path of every lookup.
        _clock.Advance(MediaRoots.Recheck);

        Assert.True(roots.ColdReady);
        Assert.True(await Store(roots).SettleAsync("a-film", CancellationToken.None));
    }
}
