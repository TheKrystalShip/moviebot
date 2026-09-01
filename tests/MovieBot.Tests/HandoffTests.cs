using TheKrystalShip.MovieBot.Handoff;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Both of these fail quietly rather than loudly. A wrong id puts the same film in the library
/// twice under different names; a wrong file transcodes ninety seconds of sample and calls it the
/// feature. Neither throws.
/// </summary>
public class LibraryIdTests
{
    [Theory]
    [InlineData("Heat.1995.BDRip.x264.AC3.RoSubbed-playSD", "heat-1995")]
    [InlineData("Heat.1995.1080p.BluRay.DD5.1.x264-EbP", "heat-1995")]
    [InlineData("Blade.Runner.2049.2017.1080p.BluRay.DD5.1.x264-playHD", "blade-runner-2049-2017")]
    [InlineData("The.Thing.1982.1080p.BluRay.x264-GROUP", "the-thing-1982")]
    [InlineData("The.Devil.Wears.Prada.2006.720p.BluRay.DD5.1.x264-playHD",
        "the-devil-wears-prada-2006")]
    public void Names_a_film_by_what_it_is_rather_than_how_it_was_encoded(
        string release, string expected) =>
        Assert.Equal(expected, LibraryId.For(release));

    [Fact]
    public void Two_encodes_of_one_film_land_on_the_same_id()
    {
        // Otherwise the library carries the same film twice and the second never replaces the
        // first, which is only ever discovered by looking at it.
        Assert.Equal(
            LibraryId.For("Heat.1995.720p.BluRay.DD5.1.x264-ZQ"),
            LibraryId.For("Heat.1995.1080p.BluRay.DD+5.1.x264-playHD"));
    }

    [Theory]
    [InlineData("The.Devil.Wears.Prada.2006.720p.BluRay.DD5.1.x264-playHD",
        "The Devil Wears Prada (2006)")]
    [InlineData("Heat.1995.1080p.BluRay.DD5.1.x264-EbP", "Heat (1995)")]
    [InlineData("Blade.Runner.2049.2017.1080p.BluRay.x264-playHD", "Blade Runner 2049 (2017)")]
    public void Names_a_film_for_a_person_rather_than_for_a_muxer(string release, string expected)
    {
        // The container's own title tag is routinely the release name again, so the library shows
        // resolution, source, codec and group in a list somebody is choosing a film from.
        Assert.Equal(expected, LibraryId.TitleFor(release));
    }

    [Fact]
    public void Falls_back_to_the_release_name_when_there_is_nothing_else()
    {
        Assert.False(string.IsNullOrWhiteSpace(LibraryId.For("something-unparseable")));
    }
}

public class FilmFileTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "moviebot-filmfile-" + Guid.NewGuid().ToString("N")[..8]);

    public FilmFileTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string Write(string name, long megabytes)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var file = File.Create(path);
        file.SetLength(megabytes * (1 << 20));
        return path;
    }

    [Fact]
    public void Picks_the_feature_over_the_sample()
    {
        // A sample is a real video file, sits beside the feature, and is what "the first video
        // file" or "any video file" would find.
        Write("Heat.1995.sample.mkv", 300);
        var feature = Write("Heat.1995.mkv", 4000);

        Assert.Equal(feature, FilmFile.Locate(_root, 200L << 20));
    }

    [Fact]
    public void Ignores_files_that_are_not_video()
    {
        Write("Heat.1995.nfo", 300);
        Write("Heat.1995.srt", 300);
        var feature = Write("Heat.1995.mkv", 4000);

        Assert.Equal(feature, FilmFile.Locate(_root, 200L << 20));
    }

    [Fact]
    public void Finds_a_film_nested_in_the_torrents_own_folder()
    {
        var feature = Write(Path.Combine("Heat.1995.BluRay", "Heat.1995.mkv"), 4000);

        Assert.Equal(feature, FilmFile.Locate(_root, 200L << 20));
    }

    [Fact]
    public void Takes_a_single_file_torrent_as_it_is()
    {
        var feature = Write("Heat.1995.mkv", 4000);

        Assert.Equal(feature, FilmFile.Locate(feature, 200L << 20));
    }

    [Fact]
    public void Finds_nothing_when_everything_is_below_the_floor()
    {
        // A torrent holding only a sample must yield nothing rather than the sample, or the room
        // watches ninety seconds of a film.
        Write("Heat.1995.sample.mkv", 50);
        Write("trailer.mkv", 40);

        Assert.Null(FilmFile.Locate(_root, 200L << 20));
    }
}
