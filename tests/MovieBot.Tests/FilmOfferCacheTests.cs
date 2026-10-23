using Microsoft.Extensions.Time.Testing;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Api.Acquisition;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The tracker is asked about a film once per few minutes, however many pages and wants ask, and an
/// answer the wish sweep heard stands for the pages after it.
/// </summary>
public sealed class FilmOfferCacheTests
{
    private const string Film = "tt0113277";

    private sealed class CountingSearch : IReleaseSearch
    {
        public int Asked;

        public Task<RankedReleases> ByTextAsync(string typed, CancellationToken ct) => throw new NotSupportedException();

        public Task<RankedReleases> ByTitleAsync(string query, CancellationToken ct) => throw new NotSupportedException();

        public Task<RankedReleases> ByImdbAsync(string imdbId, CancellationToken ct)
        {
            Asked++;
            return Task.FromResult(new RankedReleases([], []));
        }
    }

    [Fact]
    public async Task A_film_is_asked_about_once_while_its_answer_is_fresh()
    {
        var search = new CountingSearch();
        var clock = new FakeTimeProvider();
        var cache = new FilmOfferCache(search, clock);

        await cache.GetAsync(Film, CancellationToken.None);
        clock.Advance(FilmOfferCache.KeptFor);
        await cache.GetAsync(Film.ToUpperInvariant(), CancellationToken.None);
        Assert.Equal(1, search.Asked);

        clock.Advance(TimeSpan.FromSeconds(1));
        await cache.GetAsync(Film, CancellationToken.None);
        Assert.Equal(2, search.Asked);
    }

    [Fact]
    public async Task What_the_sweep_heard_answers_the_next_page()
    {
        var search = new CountingSearch();
        var cache = new FilmOfferCache(search, new FakeTimeProvider());
        var heard = new RankedReleases([], []);

        cache.Keep(Film, heard);
        Assert.Same(heard, await cache.GetAsync(Film, CancellationToken.None));
        Assert.Equal(0, search.Asked);
    }
}
