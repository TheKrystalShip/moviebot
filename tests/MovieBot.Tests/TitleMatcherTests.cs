using Xunit;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Bot.Library;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// What somebody typed, turned into a film. Worth testing on its own because the failure mode is
/// the one people notice: the room gets the wrong film, or a request that plainly names the right
/// one is refused.
/// </summary>
public sealed class TitleMatcherTests
{
    private static readonly IReadOnlyList<LibraryTitle> Library =
    [
        Title("gladiator-2000-extended", "Gladiator 2000 Extended Cut"),
        Title("gladiator-2000-theatrical", "Gladiator 2000 Theatrical Cut"),
        Title("heat-1995", "Heat"),
        Title("the-gladiator-diaries", "The Gladiator Diaries")
    ];

    [Theory]
    [InlineData("heat-1995")]
    [InlineData("HEAT-1995")]
    public void An_id_resolves_exactly(string query)
    {
        var match = TitleMatcher.Resolve(Library, query);

        Assert.Equal(TitleMatchKind.Resolved, match.Kind);
        Assert.Equal("heat-1995", match.Title!.Id);
    }

    [Theory]
    [InlineData("Heat")]
    [InlineData("  heat ")]
    [InlineData("gladiator 2000 extended cut")]
    public void A_full_title_resolves_whatever_its_case_and_spacing(string query)
    {
        Assert.Equal(TitleMatchKind.Resolved, TitleMatcher.Resolve(Library, query).Kind);
    }

    [Theory]
    [InlineData("extended")]
    [InlineData("extended gladiator")]
    [InlineData("gladiator cut extended")]
    public void Every_word_has_to_appear_but_the_order_does_not(string query)
    {
        var match = TitleMatcher.Resolve(Library, query);

        Assert.Equal(TitleMatchKind.Resolved, match.Kind);
        Assert.Equal("gladiator-2000-extended", match.Title!.Id);
    }

    [Fact]
    public void A_query_that_fits_several_films_is_refused_rather_than_guessed()
    {
        var match = TitleMatcher.Resolve(Library, "gladiator 2000");

        Assert.Equal(TitleMatchKind.Ambiguous, match.Kind);
        Assert.Null(match.Title);
        Assert.Equal(2, match.Candidates.Count);
    }

    [Fact]
    public void A_film_the_query_actually_starts_beats_one_that_merely_contains_it()
    {
        // Both films contain the word, so this would otherwise be a refusal; one of them begins
        // with it, and that is the one a person asking for "alien" means.
        IReadOnlyList<LibraryTitle> shelf =
        [
            Title("alien-3", "Alien 3"),
            Title("the-alien-legacy", "The Alien Legacy")
        ];

        var match = TitleMatcher.Resolve(shelf, "alien");

        Assert.Equal(TitleMatchKind.Resolved, match.Kind);
        Assert.Equal("alien-3", match.Title!.Id);
    }

    [Fact]
    public void A_query_matching_nothing_comes_back_with_what_the_library_holds()
    {
        var match = TitleMatcher.Resolve(Library, "casablanca");

        Assert.Equal(TitleMatchKind.NotFound, match.Kind);
        Assert.Null(match.Title);
        Assert.Equal(Library.Count, match.Candidates.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_query_resolves_to_nothing(string query)
    {
        Assert.Equal(TitleMatchKind.NotFound, TitleMatcher.Resolve(Library, query).Kind);
    }

    [Fact]
    public void An_empty_library_resolves_to_nothing()
    {
        var match = TitleMatcher.Resolve([], "heat");

        Assert.Equal(TitleMatchKind.NotFound, match.Kind);
        Assert.Empty(match.Candidates);
    }

    [Fact]
    public void Suggestions_open_with_the_whole_library_and_narrow_as_you_type()
    {
        Assert.Equal(Library.Count, TitleMatcher.Suggest(Library, "").Count);
        Assert.Equal(3, TitleMatcher.Suggest(Library, "gladiator").Count);
        Assert.Equal(2, TitleMatcher.Suggest(Library, "glad 2000").Count);
        Assert.Single(TitleMatcher.Suggest(Library, "heat"));
        Assert.Empty(TitleMatcher.Suggest(Library, "casablanca"));
    }

    [Fact]
    public void A_word_has_to_start_a_word_rather_than_land_inside_one()
    {
        // "Theatrical" contains "heat". Matching it would make a request for Heat ambiguous
        // against a film with nothing to do with it.
        Assert.Single(TitleMatcher.Suggest(Library, "heat"));

        // A partly typed word still matches the word it begins.
        Assert.Equal(TitleMatchKind.Resolved, TitleMatcher.Resolve(Library, "gladiator theatr").Kind);
    }

    [Fact]
    public void Suggestions_stop_at_what_Discord_accepts()
    {
        var many = Enumerable.Range(0, 60).Select(i => Title($"film-{i}", $"Film {i}")).ToList();

        Assert.Equal(TitleMatcher.MaxChoices, TitleMatcher.Suggest(many, "film").Count);
    }

    private static LibraryTitle Title(string id, string title) => new()
    {
        Id = id,
        Title = title,
        DurationSeconds = 6000,
        Status = TitleStatus.Ready
    };
}
