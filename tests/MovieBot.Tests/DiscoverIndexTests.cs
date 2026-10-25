using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TheKrystalShip.MovieBot.Api.Catalogue;
using TheKrystalShip.MovieBot.Catalogue;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>The Discover index's background pass, which every Discover request waits on until it has been read once.</summary>
public sealed class DiscoverIndexTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "moviebot-tests", "discover-" + Guid.NewGuid().ToString("n"));
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task A_catalogue_unreadable_when_the_service_starts_is_read_within_a_minute_of_becoming_readable()
    {
        var reader = new CatalogueReader(Path.Combine(_dir, "catalogue.db"));
        using var index = new DiscoverIndex(new CatalogueQueries(reader), new CatalogueState(reader, _clock, NullLogger<CatalogueState>.Instance),
            _clock, NullLogger<DiscoverIndex>.Instance);
        await index.StartAsync(CancellationToken.None);
        Assert.False(index.FirstRead.IsCompleted);

        TestCatalogue.Write(_dir, _clock);
        for (var waited = 0; waited < 60 && !index.FirstRead.IsCompleted; waited++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            await Task.WhenAny(index.FirstRead, Task.Delay(50));
        }

        Assert.True(index.FirstRead.IsCompleted);
        await index.StopAsync(CancellationToken.None);
    }
}
