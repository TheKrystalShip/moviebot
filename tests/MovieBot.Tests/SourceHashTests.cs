using TheKrystalShip.MovieBot.Ingest.Probe;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The expected values come from the reference implementation of the algorithm rather than from
/// this one, so the test says the hash is right rather than that it is unchanged. All three films
/// in the library hash identically under both.
/// </summary>
public class SourceHashTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "moviebot-oshash-" + Guid.NewGuid().ToString("N")[..8]);

    public SourceHashTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Deterministic bytes, so the expected hashes below mean something.</summary>
    private static byte[] Pattern(int length)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++) bytes[i] = (byte)((i * 31 + 7) & 0xFF);
        return bytes;
    }

    private string Write(string name, byte[] content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    [Fact]
    public void MatchesTheReferenceImplementation()
    {
        Assert.Equal("5f9fe02060a309f0", SourceHash.Compute(Write("film.mkv", Pattern(150_000))));
    }

    [Fact]
    public void ReadsTheEndOfTheFileAsWellAsTheStart()
    {
        // Folding only the head would hash these two identically, and every release of a film would
        // then look like every other release that happened to share an opening.
        var content = Pattern(150_000);
        var head = SourceHash.Compute(Write("head.mkv", content));

        content[^1]++;

        Assert.NotEqual(head, SourceHash.Compute(Write("tail.mkv", content)));
        Assert.Equal("609fe02060a309f0", SourceHash.Compute(Path.Combine(_directory, "tail.mkv")));
    }

    [Fact]
    public void HashesASourceThatIsStillArrivingToSomethingElseEntirely()
    {
        // The space for a download is reserved before the bytes land, so the tail reads as zeroes
        // and the hash comes back confident and wrong. Nothing about the file says so, which is why
        // the pipeline waits for the source to be whole rather than checking it here.
        var complete = Pattern(150_000);
        var arriving = Pattern(150_000);
        Array.Clear(arriving, 100_000, 50_000);

        Assert.Equal("5f9fe02060a309f0", SourceHash.Compute(Write("complete.mkv", complete)));
        Assert.Equal("cfcac8c7c6c7fa32", SourceHash.Compute(Write("arriving.mkv", arriving)));
    }

    [Fact]
    public void HashesAFileOfExactlyTheMinimumSize()
    {
        var path = Write("minimum.mkv", Pattern((int)SourceHash.MinimumSizeBytes));

        Assert.Equal("5f9fe02060a2c000", SourceHash.Compute(path));
    }

    [Fact]
    public void RefusesAFileTooShortToHash()
    {
        var path = Write("sample.mkv", Pattern((int)SourceHash.MinimumSizeBytes - 1));

        Assert.Null(SourceHash.Compute(path));
    }

    [Fact]
    public void RefusesAFileThatIsNotThere()
    {
        Assert.Null(SourceHash.Compute(Path.Combine(_directory, "gone.mkv")));
    }
}
