using TheKrystalShip.MovieBot.Api.Notifications;
using TheKrystalShip.MovieBot.Api.Wants;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>Which wants are together, and what the together digest says.</summary>
public sealed class WantMatchTests
{
    private static readonly DateTimeOffset Day = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private static StoredWant Want(string user, string imdb, string kind = WantKinds.Film, bool visible = true) => new()
    {
        UserId = user, ImdbId = imdb, Kind = kind, Visible = visible, MayFetch = true, MayWish = true, Since = Day,
    };

    private static (int, int)? Nothing(StoredWant _) => null;

    [Fact]
    public void A_film_matches_everybody_else_who_visibly_wants_it()
    {
        var matches = WantMatcher.Match([Want("a", "tt1")], [Want("b", "tt1"), Want("c", "tt1"), Want("d", "tt2")], Nothing);

        var match = Assert.Single(matches);
        Assert.Equal(["b", "c"], match.With.Select(w => w.UserId));
    }

    [Fact]
    public void A_want_kept_to_its_person_matches_nobody()
    {
        var matches = WantMatcher.Match([Want("a", "tt1")], [Want("b", "tt1", visible: false)], Nothing);

        Assert.Empty(matches);
    }

    [Fact]
    public void A_title_nobody_else_wants_is_no_match()
    {
        Assert.Empty(WantMatcher.Match([Want("a", "tt1")], [Want("b", "tt2")], Nothing));
    }

    [Fact]
    public void Two_wanters_of_a_show_are_together_only_at_the_same_next_episode()
    {
        var next = new Dictionary<string, (int, int)> { ["a"] = (1, 3), ["b"] = (1, 3), ["c"] = (2, 1) };

        var matches = WantMatcher.Match(
            [Want("a", "tt1", WantKinds.Show)],
            [Want("b", "tt1", WantKinds.Show), Want("c", "tt1", WantKinds.Show)],
            w => next.TryGetValue(w.UserId, out var at) ? at : null);

        Assert.Equal(["b"], Assert.Single(matches).With.Select(w => w.UserId));
    }

    [Fact]
    public void A_show_with_no_next_episode_matches_nobody()
    {
        var matches = WantMatcher.Match([Want("a", "tt1", WantKinds.Show)], [Want("b", "tt1", WantKinds.Show)], Nothing);

        Assert.Empty(matches);
    }

    [Fact]
    public void The_digest_names_the_titles_and_the_people()
    {
        Assert.Equal("Dune is ready to watch together with Ana.", TogetherDigest.Text(["Dune"], ["Ana"]));
        Assert.Equal("Dune and Heat are ready to watch together with Ana and Ben.",
            TogetherDigest.Text(["Dune", "Heat"], ["Ana", "Ben"]));
        Assert.Equal("Dune, Heat and 2 more are ready to watch together with Ana, Ben and 1 more.",
            TogetherDigest.Text(["Dune", "Heat", "Alien", "Se7en"], ["Ana", "Ben", "Cai"]));
    }

    [Fact]
    public void Together_is_told_only_when_switched_on()
    {
        Assert.False(NotificationSetup.Kinds.Single(k => k.Id == NotificationKinds.Together).EnabledByDefault);
    }
}
