using TheKrystalShip.MovieBot.Acquire.Tmdb;
using TheKrystalShip.MovieBot.Api.Films;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Words typed into Discover, read as a question of the catalogue or as a title's name. The reader is
/// the grammar alone: what it reads and what it refuses to read, with no database behind it.
/// </summary>
public sealed class QuestionReaderTests
{
    private static readonly DateOnly Today = new(2026, 10, 24);

    /// <summary>The film database's genre lists for films and for shows, and a few of its languages.</summary>
    private static readonly QuestionVocabulary Vocabulary = new()
    {
        FilmGenres = Genres("Action", "Adventure", "Animation", "Comedy", "Crime", "Documentary", "Drama", "Family", "Fantasy", "History",
            "Horror", "Music", "Mystery", "Romance", "Science Fiction", "TV Movie", "Thriller", "War", "Western"),
        ShowGenres = Genres("Action & Adventure", "Animation", "Comedy", "Crime", "Documentary", "Drama", "Family", "Kids", "Mystery", "News",
            "Reality", "Sci-Fi & Fantasy", "Soap", "Talk", "War & Politics", "Western"),
        Languages =
        [
            new LanguageName { Code = "en", Name = "English" }, new LanguageName { Code = "ko", Name = "Korean" },
            new LanguageName { Code = "es", Name = "Spanish" }, new LanguageName { Code = "xx", Name = "No Language" },
        ],
        Providers =
        [
            new WatchProvider { ProviderId = 8, Name = "Netflix" }, new WatchProvider { ProviderId = 119, Name = "Amazon Prime Video" },
            new WatchProvider { ProviderId = 337, Name = "Disney Plus" }, new WatchProvider { ProviderId = 1899, Name = "HBO Max" },
            new WatchProvider { ProviderId = 350, Name = "Apple TV" }, new WatchProvider { ProviderId = 2, Name = "Apple TV Store" },
            new WatchProvider { ProviderId = 63, Name = "Filmin" }, new WatchProvider { ProviderId = 2241, Name = "Movistar Plus+" },
        ],
    };

    private static List<CatalogueGenre> Genres(params string[] names) =>
        [.. names.Select((name, i) => new CatalogueGenre { Id = i + 1, Name = name })];

    private static Question? ReadOrNone(string text) => QuestionReader.Read(text, Today, Vocabulary);

    private static Question Read(string text) => Assert.IsType<Question>(ReadOrNone(text));

    private static string Parts(Question question) => string.Join(" | ", question.Parts.Select(p => $"{p.Part}:{p.Text}"));

    [Fact]
    public void A_count_a_genre_a_kind_and_a_year_are_the_best_rated_of_that_genre_that_year()
    {
        var question = Read("Top 10 horror movies from 2000");

        Assert.Equal("film", question.Request.Kind);
        Assert.Equal(["Horror"], question.Request.Genres);
        Assert.Equal((2000, 2000), (question.Request.From, question.Request.To));
        Assert.Equal(DiscoverSorts.Rating, question.Request.Sort);
        Assert.Equal(10, question.Request.Limit);
        Assert.True(question.Request.GoodOnly);
        Assert.Equal("count:Top 10 | genre:Horror | kind:Movies | years:2000", Parts(question));
    }

    [Theory]
    [InlineData("horror Express")]
    [InlineData("1917")]
    [InlineData("2001 a space odyssey")]
    [InlineData("blade runner 2049")]
    [InlineData("the new world")]
    [InlineData("star wars")]
    [InlineData("keanu reeves")]
    [InlineData("")]
    public void Words_a_rule_leaves_unread_or_a_year_alone_are_a_name(string text) =>
        Assert.Null(ReadOrNone(text));

    [Theory]
    [InlineData("the 90s", 1990, 1999)]
    [InlineData("'90s", 1990, 1999)]
    [InlineData("1980s", 1980, 1989)]
    [InlineData("nineties", 1990, 1999)]
    [InlineData("the 2000s", 2000, 2009)]
    [InlineData("10s", 2010, 2019)]
    [InlineData("late 70s", 1976, 1979)]
    [InlineData("early 2010s", 2010, 2013)]
    [InlineData("from 2000 to 2005", 2000, 2005)]
    [InlineData("2000-2005", 2000, 2005)]
    [InlineData("between 1995 and 1999", 1995, 1999)]
    [InlineData("since 2010", 2010, null)]
    [InlineData("after 2010", 2011, null)]
    [InlineData("before 1980", null, 1979)]
    [InlineData("before the 80s", null, 1979)]
    [InlineData("this year", 2026, 2026)]
    [InlineData("last year", 2025, 2025)]
    [InlineData("of the last 5 years", 2021, 2026)]
    public void Years_are_read_as_a_year_a_range_a_decade_or_a_bound(string years, int? from, int? to)
    {
        var question = Read("thrillers " + years);

        Assert.Equal((from, to), (question.Request.From, question.Request.To));
        Assert.Equal(["Thriller"], question.Request.Genres);
    }

    [Theory]
    [InlineData("sci-fi", new[] { "Science Fiction" })]
    [InlineData("science fiction", new[] { "Science Fiction" })]
    [InlineData("scary", new[] { "Horror" })]
    [InlineData("animated", new[] { "Animation" })]
    [InlineData("romcoms", new[] { "Romance", "Comedy" })]
    [InlineData("romantic comedies", new[] { "Romance", "Comedy" })]
    [InlineData("action comedy", new[] { "Action", "Comedy" })]
    [InlineData("crime and mystery", new[] { "Crime", "Mystery" })]
    public void Genres_are_read_by_their_names_plurals_and_common_words_for_them(string text, string[] genres) =>
        Assert.Equal(genres, Read(text).Request.Genres);

    [Theory]
    [InlineData("sci-fi shows", "Sci-Fi & Fantasy")]
    [InlineData("shows science fiction", "Sci-Fi & Fantasy")]
    [InlineData("war series", "War & Politics")]
    [InlineData("action shows", "Action & Adventure")]
    [InlineData("kids shows", "Kids")]
    [InlineData("reality tv", "Reality")]
    public void A_show_s_genre_is_named_as_the_database_names_it_for_shows(string text, string genre)
    {
        var question = Read(text);

        Assert.Equal("show", question.Request.Kind);
        Assert.Equal([genre], question.Request.Genres);
        Assert.Equal(genre, question.Parts.Single(p => p.Part == "genre").Text);
    }

    [Fact]
    public void A_genre_the_other_list_alone_has_is_kept_for_the_discovery_to_refuse()
    {
        var question = Read("horror shows");

        Assert.Equal("show", question.Request.Kind);
        Assert.Equal(["Horror"], question.Request.Genres);
    }

    [Fact]
    public void The_words_for_a_kind_are_never_a_genre_of_the_database()
    {
        var question = Read("best tv movies");

        Assert.Empty(question.Request.Genres);
        Assert.Equal("film", question.Request.Kind);
    }

    [Fact]
    public void A_genre_the_vocabulary_lacks_is_not_read()
    {
        var horror = QuestionReader.Read("horror movies", Today, new QuestionVocabulary())!;
        Assert.Empty(horror.Request.Genres);
        Assert.Equal("horror", horror.Named);

        var korean = QuestionReader.Read("korean movies", Today, new QuestionVocabulary())!;
        Assert.Null(korean.Request.Language);
        Assert.Equal("korean", korean.Named);
    }

    [Fact]
    public void A_language_by_its_adjective_or_after_in_is_its_code()
    {
        Assert.Equal("ko", Read("korean thrillers of the 2010s").Request.Language);
        Assert.Equal("es", Read("horror in spanish").Request.Language);
        Assert.Equal("en", Read("english comedies").Request.Language);
        Assert.Equal("language:Korean", Read("korean thrillers").Parts[0].Part + ":" + Read("korean thrillers").Parts[0].Text);
    }

    [Fact]
    public void People_after_a_marker_are_names_each_ending_where_the_next_part_starts()
    {
        var question = Read("comedies starring tom hanks and meg ryan from the 90s");

        Assert.Equal(["tom hanks", "meg ryan"], question.Request.With);
        Assert.Equal((1990, 1999), (question.Request.From, question.Request.To));
    }

    [Fact]
    public void A_bare_with_is_a_person_or_a_subject_for_whoever_answers_to_decide()
    {
        var question = Read("sci-fi with keanu reeves");

        Assert.Equal(["keanu reeves"], question.WithEither);
        Assert.Empty(question.Request.With);
    }

    [Fact]
    public void A_name_ends_before_filler_that_only_parts_or_the_end_follow()
    {
        Assert.Equal(["christopher nolan"], Read("best films by christopher nolan of all time").Request.With);
        Assert.Equal(["end of the world"], Read("movies about the end of the world").Request.About);
    }

    [Fact]
    public void A_subject_is_read_after_about_without_its_article()
    {
        var question = Read("movies about a time loop");

        Assert.Equal(["time loop"], question.Request.About);
    }

    [Fact]
    public void Words_before_the_rest_that_no_rule_reads_are_a_name_when_something_after_says_what_of_theirs()
    {
        var question = Read("keanu reeves sci-fi movies");

        Assert.Equal("keanu reeves", question.Named);
        Assert.Equal(["Science Fiction"], question.Request.Genres);
        Assert.Null(ReadOrNone("keanu reeves from 1999"));
        Assert.Null(ReadOrNone("horror movies keanu reeves"));
    }

    [Fact]
    public void A_likeness_keeps_the_whole_title()
    {
        var question = Read("movies like the thing");

        Assert.Equal("the thing", question.Request.Like);
        Assert.Equal("like:Like the thing", Parts(question).Split(" | ")[1]);
    }

    [Fact]
    public void A_count_is_held_to_the_most_a_question_counts_and_none_is_the_whole_discovery()
    {
        Assert.Equal(QuestionReader.MostCounted, Read("top 500 westerns").Request.Limit);
        Assert.Equal(CatalogueSearch.MostListed, Read("horror shows").Request.Limit);
        Assert.Equal(5, Read("five best documentaries").Request.Limit);
        Assert.Equal(3, Read("3 best musicals").Request.Limit);
    }

    [Fact]
    public void The_newest_ask_for_titles_already_out_without_the_floor_of_the_well_rated()
    {
        var question = Read("new horror movies");

        Assert.Equal(DiscoverSorts.Newest, question.Request.Sort);
        Assert.False(question.Request.GoodOnly);
        Assert.Equal(QuestionReader.NewestVotes, question.Request.MinVotes);
    }

    [Fact]
    public void Runtimes_and_left_out_genres_are_bounds_on_the_answer()
    {
        var question = Read("comedies under 90 minutes not animated");

        Assert.Equal(90, question.Request.MaxRuntime);
        Assert.Equal(["Animation"], question.Request.Exclude);
        Assert.Equal(120, Read("dramas over 2 hours").Request.MinRuntime);
    }

    [Fact]
    public void Anime_is_its_category_and_a_bare_order_is_a_question()
    {
        Assert.Equal("anime", Read("best anime series").Request.Category);
        Assert.Equal("show", Read("best anime series").Request.Kind);
        Assert.Equal(DiscoverSorts.Popular, Read("most popular movies").Request.Sort);
    }

    [Fact]
    public void A_year_with_a_kind_is_a_question()
    {
        var question = Read("movies from 1999");

        Assert.Equal((1999, 1999), (question.Request.From, question.Request.To));
        Assert.Null(ReadOrNone("movies"));
    }

    // Wikidata's sets: awards and subgenres are closed phrases, an author or a place is a name after a marker.

    [Fact]
    public void An_award_is_a_closed_phrase_that_can_stand_anywhere_among_the_other_words()
    {
        var after = Read("oscar winners from the 90s");
        var before = Read("from the 90s oscar winners");

        Assert.Equal(new WikidataAsk(WikidataRelation.Won, "Q102427", "Best Picture winners"), after.Wikidata);
        Assert.Equal(after.Wikidata, before.Wikidata);
        Assert.Equal((1990, 1999), (after.Request.From, after.Request.To));
        Assert.Equal((1990, 1999), (before.Request.From, before.Request.To));
        Assert.Equal("award:Best Picture winners | years:1990s", Parts(after));
    }

    [Theory]
    [InlineData("best picture winners", WikidataRelation.Won, "Q102427")]
    [InlineData("oscar-winning movies", WikidataRelation.Won, "Q102427")]
    [InlineData("best picture nominees", WikidataRelation.Nominated, "Q102427")]
    [InlineData("oscar nominated films", WikidataRelation.Nominated, "Q102427")]
    [InlineData("palme d'or winners", WikidataRelation.Won, "Q179808")]
    public void The_awards_name_their_item_outright(string text, WikidataRelation relation, string item)
    {
        var ask = Read(text).Wikidata!;

        Assert.Equal((relation, item), (ask.Relation, ask.Item));
    }

    [Theory]
    [InlineData("stephen king adaptations")]
    [InlineData("best stephen king adaptations")]
    [InlineData("adaptations of stephen king")]
    [InlineData("based on stephen king")]
    [InlineData("adapted from stephen king")]
    [InlineData("stephen king horror adaptations")]
    public void An_author_is_a_name_to_resolve_whichever_way_the_adaptations_are_asked_for(string text)
    {
        var question = Read(text);

        Assert.Equal(new WikidataAsk(WikidataRelation.Adaptation, null, "stephen king"), question.Wikidata);
        Assert.Contains(question.Parts, p => p is { Part: "adaptation", Text: "Adaptations of Stephen King" });
        Assert.Null(question.Named);
    }

    [Fact]
    public void An_adaptation_question_keeps_the_genre_the_years_and_the_sort_it_was_given()
    {
        var question = Read("best stephen king horror adaptations from the 90s");

        Assert.Equal("stephen king", question.Wikidata!.Name);
        Assert.Equal(["Horror"], question.Request.Genres);
        Assert.Equal((1990, 1999), (question.Request.From, question.Request.To));
        Assert.Equal(DiscoverSorts.Rating, question.Request.Sort);
    }

    [Theory]
    [InlineData("set in paris", WikidataRelation.Set, "paris", "place:Set in Paris")]
    [InlineData("movies set in new york", WikidataRelation.Set, "new york", "kind:Movies | place:Set in New York")]
    [InlineData("filmed in barcelona", WikidataRelation.Filmed, "barcelona", "place:Filmed in Barcelona")]
    [InlineData("shot in barcelona", WikidataRelation.Filmed, "barcelona", "place:Filmed in Barcelona")]
    public void A_place_is_a_name_after_set_in_or_filmed_in(string text, WikidataRelation relation, string name, string parts)
    {
        var question = Read(text);

        Assert.Equal(new WikidataAsk(relation, null, name), question.Wikidata);
        Assert.Equal(parts, Parts(question));
    }

    [Fact]
    public void A_place_ends_where_the_next_part_starts()
    {
        var question = Read("top 10 movies set in paris from the 2000s");

        Assert.Equal("paris", question.Wikidata!.Name);
        Assert.Equal((2000, 2009), (question.Request.From, question.Request.To));
        Assert.Equal(10, question.Request.Limit);
        Assert.Equal(DiscoverSorts.Rating, question.Request.Sort);
    }

    [Theory]
    [InlineData("slasher movies", "Q853630")]
    [InlineData("heist films", "Q496523")]
    [InlineData("neo-noir", "Q2421031")]
    [InlineData("neo noir movies", "Q2421031")]
    [InlineData("found footage", "Q3272147")]
    [InlineData("cyberpunk movies", "Q174526")]
    [InlineData("film noir", "Q185867")]
    [InlineData("spaghetti westerns", "Q212781")]
    [InlineData("ww2 movies", "Q362")]
    [InlineData("second world war", "Q362")]
    public void A_subgenre_or_a_subject_names_its_item_outright(string text, string item)
    {
        Assert.Equal(item, Read(text).Wikidata!.Item);
    }

    [Fact]
    public void A_subgenre_and_a_genre_are_read_in_either_order()
    {
        var one = Read("horror found footage movies from the 2000s");
        var other = Read("2000s found footage horror movies");

        Assert.Equal(one.Wikidata, other.Wikidata);
        Assert.Equal(one.Request.Genres, other.Request.Genres);
        Assert.Equal((one.Request.From, one.Request.To), (other.Request.From, other.Request.To));
        Assert.Equal(["Horror"], other.Request.Genres);
    }

    [Theory]
    [InlineData("set in paris slasher")]
    [InlineData("oscar winners set in london")]
    [InlineData("stephen king adaptations heist")]
    [InlineData("adaptations")]
    [InlineData("horror adaptations")]
    public void Words_that_ask_wikidata_for_two_things_or_for_adaptations_of_nobody_are_no_question(string text)
    {
        Assert.Null(ReadOrNone(text));
    }

    [Fact]
    public void On_a_service_reads_it_in_any_order_as_the_service_the_database_lists()
    {
        var after = Read("horror on netflix");
        Assert.Equal(["Netflix"], after.Request.Providers);
        Assert.Equal(["Horror"], after.Request.Genres);
        Assert.Equal("genre:Horror | provider:Netflix", Parts(after));

        var before = Read("on netflix horror movies");
        Assert.Equal(["Netflix"], before.Request.Providers);
        Assert.Equal("provider:Netflix | genre:Horror | kind:Movies", Parts(before));

        var middle = Read("best comedies on FILMIN from the 90s");
        Assert.Equal(["Filmin"], middle.Request.Providers);
        Assert.Equal(DiscoverSorts.Rating, middle.Request.Sort);
        Assert.Equal("sort:Best rated | genre:Comedy | provider:Filmin | years:1990s", Parts(middle));
    }

    [Theory]
    [InlineData("horror on prime", "Amazon Prime Video")]
    [InlineData("horror on amazon", "Amazon Prime Video")]
    [InlineData("horror on prime video", "Amazon Prime Video")]
    [InlineData("horror on hbo", "HBO Max")]
    [InlineData("horror on disney", "Disney Plus")]
    [InlineData("horror on disney+", "Disney Plus")]
    [InlineData("horror on apple tv", "Apple TV")]
    [InlineData("horror on apple tv+", "Apple TV")]
    [InlineData("horror on movistar", "Movistar Plus+")]
    [InlineData("horror on movistar plus+", "Movistar Plus+")]
    [InlineData("horror streaming on netflix", "Netflix")]
    [InlineData("horror available on netflix", "Netflix")]
    public void A_name_people_say_for_a_service_is_the_service(string text, string service) =>
        Assert.Equal([service], Read(text).Request.Providers);

    [Fact]
    public void Several_services_are_each_asked_for_and_a_name_the_region_lacks_is_not_read()
    {
        Assert.Equal(["Netflix", "Filmin"], Read("horror on netflix on filmin").Request.Providers);
        Assert.Null(ReadOrNone("on hulu"));
        Assert.Null(ReadOrNone("horror on hulu"));
        Assert.Null(QuestionReader.Read("horror on netflix", Today, new QuestionVocabulary { FilmGenres = Vocabulary.FilmGenres }));
    }

    [Fact]
    public void Words_about_a_place_or_a_marker_with_on_in_them_are_not_a_service()
    {
        var based = Read("movies based on stephen king");
        Assert.Empty(based.Request.Providers);
        Assert.DoesNotContain(based.Parts, p => p.Part == "provider");
        Assert.Equal(["keanu reeves"], Read("movies with keanu reeves on netflix").WithEither);
    }

    [Theory]
    [InlineData("Disney+", "disney plus")]
    [InlineData("Movistar Plus+", "movistar plus plus")]
    [InlineData("  Apple   TV ", "apple tv")]
    [InlineData("Canal+ Ficción", "canal plus ficción")]
    public void A_service_is_compared_by_its_letters_digits_and_pluses(string name, string expected) =>
        Assert.Equal(expected, StreamingServices.Normalised(name));

    [Fact]
    public void A_service_is_matched_by_its_name_before_an_alias_and_an_alias_only_to_a_service_that_is_listed()
    {
        Assert.Equal(350, StreamingServices.Match(Vocabulary.Providers, "apple tv")!.ProviderId);
        Assert.Equal(2, StreamingServices.Match(Vocabulary.Providers, "Apple TV Store")!.ProviderId);
        Assert.Equal(119, StreamingServices.Match(Vocabulary.Providers, "Prime")!.ProviderId);
        Assert.Null(StreamingServices.Match([new WatchProvider { ProviderId = 8, Name = "Netflix" }], "prime"));
        Assert.Null(StreamingServices.Match(Vocabulary.Providers, "   "));
    }
}
