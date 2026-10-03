using System.Buffers.Binary;
using TheKrystalShip.MovieBot.Ingest.Markers;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Fingerprints stand in for audio: two episodes that share a stretch share it to within the few
/// bits two encodes of one sound differ by, and everything else is unrelated.
/// </summary>
public class AudioMarkerTests
{
    private const int EpisodeItems = 22000;

    private static uint[] Random(int count, int seed)
    {
        var random = new System.Random(seed);
        var items = new uint[count];
        for (var i = 0; i < count; i++) items[i] = random.NextUInt();
        return items;
    }

    /// <summary>The same sound as another encode hears it: a few bits of each value flipped.</summary>
    private static uint[] Heard(uint[] sound, int seed, int bitsPerItem = 2)
    {
        var random = new System.Random(seed);
        var items = (uint[])sound.Clone();
        for (var i = 0; i < items.Length; i++)
            for (var bit = 0; bit < bitsPerItem; bit++)
                items[i] ^= 1u << random.Next(32);
        return items;
    }

    private static void Place(uint[] episode, int at, uint[] sound) => sound.CopyTo(episode, at);

    private static int Items(double seconds) => (int)Math.Round(seconds / Fpcalc.ItemSeconds);

    [Fact]
    public void A_title_sequence_is_found_wherever_each_episode_puts_it()
    {
        var theme = Random(Items(35), seed: 1);
        var a = Random(EpisodeItems, seed: 2);
        var b = Random(EpisodeItems, seed: 3);
        Place(a, Items(300), Heard(theme, 10));
        Place(b, Items(90), Heard(theme, 11));

        var match = IntroFinder.Find(a, b);

        Assert.NotNull(match);
        Assert.InRange(match.Value.StartA, 299, 301);
        Assert.InRange(match.Value.StartB, 89, 91);
        Assert.InRange(match.Value.EndA - match.Value.StartA, 33, 36);
        Assert.InRange(match.Value.EndB - match.Value.StartB, 33, 36);
    }

    [Fact]
    public void The_same_sequence_is_found_in_either_order()
    {
        var theme = Random(Items(35), seed: 1);
        var a = Random(EpisodeItems, seed: 2);
        var b = Random(EpisodeItems, seed: 3);
        Place(a, Items(300), Heard(theme, 10));
        Place(b, Items(90), Heard(theme, 11));

        var forward = IntroFinder.Find(a, b)!.Value;
        var back = IntroFinder.Find(b, a)!.Value;

        Assert.Equal(forward.StartA, back.StartB, 0.01);
        Assert.Equal(forward.EndA, back.EndB, 0.01);
    }

    [Fact]
    public void A_brief_disagreement_inside_the_sequence_does_not_split_it()
    {
        var theme = Random(Items(35), seed: 1);
        var a = Random(EpisodeItems, seed: 2);
        var b = Random(EpisodeItems, seed: 3);
        var heard = Heard(theme, 11);
        for (var i = 120; i < 123; i++) heard[i] = Random(1, i)[0];

        Place(a, Items(300), Heard(theme, 10));
        Place(b, Items(90), heard);

        var match = IntroFinder.Find(a, b)!.Value;
        Assert.InRange(match.EndA - match.StartA, 33, 36);
    }

    [Fact]
    public void A_noisier_encode_is_still_the_same_sound()
    {
        var theme = Random(Items(35), seed: 1);
        var a = Random(EpisodeItems, seed: 2);
        var b = Random(EpisodeItems, seed: 3);
        Place(a, Items(300), Heard(theme, 10, bitsPerItem: 4));
        Place(b, Items(90), Heard(theme, 11, bitsPerItem: 4));

        Assert.NotNull(IntroFinder.Find(a, b));
    }

    [Fact]
    public void Episodes_that_share_nothing_share_no_sequence()
    {
        Assert.Null(IntroFinder.Find(Random(EpisodeItems, 2), Random(EpisodeItems, 3)));
    }

    [Fact]
    public void A_shared_stretch_too_short_to_be_a_sequence_is_a_sting()
    {
        var sting = Random(Items(10), seed: 1);
        var a = Random(EpisodeItems, seed: 2);
        var b = Random(EpisodeItems, seed: 3);
        Place(a, Items(300), Heard(sting, 10));
        Place(b, Items(90), Heard(sting, 11));

        Assert.Null(IntroFinder.Find(a, b));
    }

    [Fact]
    public void A_shared_stretch_too_long_to_be_a_sequence_is_a_scene()
    {
        var scene = Random(Items(150), seed: 1);
        var a = Random(EpisodeItems, seed: 2);
        var b = Random(EpisodeItems, seed: 3);
        Place(a, Items(100), Heard(scene, 10));
        Place(b, Items(60), Heard(scene, 11));

        Assert.Null(IntroFinder.Find(a, b));
    }

    [Fact]
    public void Silence_in_both_is_not_a_shared_sequence()
    {
        var a = Random(EpisodeItems, seed: 2);
        var b = Random(EpisodeItems, seed: 3);
        Array.Fill(a, 627964279u, Items(100), Items(40));
        Array.Fill(b, 627964279u, Items(200), Items(40));

        Assert.Null(IntroFinder.Find(a, b));
    }

    [Fact]
    public void A_sequence_after_the_opening_of_both_is_not_looked_for()
    {
        var theme = Random(Items(35), seed: 1);
        var a = Random(EpisodeItems, seed: 2);
        var b = Random(EpisodeItems, seed: 3);
        Place(a, Items(1500), Heard(theme, 10));
        Place(b, Items(1400), Heard(theme, 11));

        Assert.Null(IntroFinder.Find(a, b));
    }

    // ---- recap ----

    /// <summary>An episode of the show, and an opening made of clips taken from its body.</summary>
    private static (uint[] Earlier, uint[] Episode) Montage(
        IEnumerable<(double From, double Source, double Length)> clips)
    {
        var earlier = Random(EpisodeItems, seed: 20);
        var episode = Random(EpisodeItems, seed: 21);

        foreach (var (from, source, length) in clips)
        {
            var clip = earlier.AsSpan(Items(source), Items(length)).ToArray();
            Place(episode, Items(from), Heard(clip, seed: (int)from));
        }

        return (earlier, episode);
    }

    [Fact]
    public void A_montage_of_clips_from_an_earlier_episode_is_a_recap()
    {
        var (earlier, episode) = Montage([(8, 1400, 8), (18, 1900, 10), (30, 2300, 6), (38, 1500, 12)]);

        var recap = RecapFinder.Find(episode, [earlier], RecapFinder.DefaultSearchSeconds);

        Assert.NotNull(recap);
        Assert.Equal(0, recap.Value.StartSeconds);
        Assert.InRange(recap.Value.EndSeconds, 49, 53);
        Assert.InRange(recap.Value.MatchedSeconds, 34, 37);
    }

    [Fact]
    public void A_recap_that_begins_later_keeps_its_own_start()
    {
        var (earlier, episode) = Montage([(25, 1400, 8), (36, 1900, 10), (50, 2300, 6)]);

        var recap = RecapFinder.Find(episode, [earlier], RecapFinder.DefaultSearchSeconds);

        Assert.NotNull(recap);
        Assert.InRange(recap.Value.StartSeconds, 24, 26);
    }

    [Fact]
    public void A_recap_ends_where_the_title_sequence_begins_when_it_is_next()
    {
        var (earlier, episode) = Montage([(12, 1400, 8), (22, 1900, 10), (34, 2300, 6)]);

        var recap = RecapFinder.Find(episode, [earlier], searchSeconds: 45, introStart: 45);

        Assert.NotNull(recap);
        Assert.Equal(45, recap.Value.EndSeconds, 0.01);
    }

    [Fact]
    public void Clips_beyond_where_the_titles_begin_are_not_a_recap()
    {
        var (earlier, episode) = Montage([(90, 1400, 8), (100, 1900, 10), (112, 2300, 6)]);

        Assert.Null(RecapFinder.Find(episode, [earlier], searchSeconds: 60, introStart: 60));
    }

    [Fact]
    public void One_long_match_is_a_scene_replayed_and_not_a_recap()
    {
        var (earlier, episode) = Montage([(12, 1400, 25)]);

        Assert.Null(RecapFinder.Find(episode, [earlier], RecapFinder.DefaultSearchSeconds));
    }

    [Fact]
    public void Clips_adding_up_to_too_little_are_not_a_recap()
    {
        var (earlier, episode) = Montage([(12, 1400, 4), (22, 1900, 4), (34, 2300, 4)]);

        Assert.Null(RecapFinder.Find(episode, [earlier], RecapFinder.DefaultSearchSeconds));
    }

    [Fact]
    public void Clips_far_apart_are_not_one_recap()
    {
        var (earlier, episode) = Montage([(12, 1400, 8), (120, 1900, 8), (200, 2300, 8)]);

        Assert.Null(RecapFinder.Find(episode, [earlier], RecapFinder.DefaultSearchSeconds));
    }

    [Fact]
    public void A_montage_that_starts_far_into_the_episode_is_not_a_recap()
    {
        var (earlier, episode) = Montage([(100, 1400, 8), (110, 1900, 10), (122, 2300, 6), (130, 1500, 12)]);

        Assert.Null(RecapFinder.Find(episode, [earlier], RecapFinder.DefaultSearchSeconds));
    }

    [Fact]
    public void An_earlier_episodes_own_opening_is_not_a_source_of_clips()
    {
        // The earlier episode's first minutes hold its title sequence, which every episode shares.
        var (earlier, episode) = Montage([(12, 40, 8), (22, 100, 10), (34, 160, 6), (42, 200, 12)]);

        Assert.Null(RecapFinder.Find(episode, [earlier], RecapFinder.DefaultSearchSeconds));
    }

    [Fact]
    public void An_opening_with_nothing_of_the_earlier_episodes_has_no_recap()
    {
        Assert.Null(RecapFinder.Find(
            Random(EpisodeItems, 21), [Random(EpisodeItems, 20)], RecapFinder.DefaultSearchSeconds));
    }

    [Fact]
    public void Clips_may_come_from_several_earlier_episodes()
    {
        var first = Random(EpisodeItems, seed: 30);
        var second = Random(EpisodeItems, seed: 31);
        var episode = Random(EpisodeItems, seed: 32);
        Place(episode, Items(10), Heard(first.AsSpan(Items(1400), Items(9)).ToArray(), 1));
        Place(episode, Items(22), Heard(second.AsSpan(Items(1800), Items(10)).ToArray(), 2));
        Place(episode, Items(35), Heard(first.AsSpan(Items(2400), Items(9)).ToArray(), 3));

        Assert.NotNull(RecapFinder.Find(episode, [first, second], RecapFinder.DefaultSearchSeconds));
    }

    // ---- the fingerprint a tool prints ----

    [Fact]
    public void Reads_the_fingerprint_line_of_fpcalc()
    {
        var items = Fpcalc.Parse("DURATION=12\nFINGERPRINT=627964279,4287838852,0\n");

        Assert.Equal([627964279u, 4287838852u, 0u], items);
    }

    [Fact]
    public void An_empty_fingerprint_is_no_values()
    {
        Assert.Empty(Fpcalc.Parse("DURATION=0\nFINGERPRINT=\n"));
    }

    [Fact]
    public void Output_with_no_fingerprint_is_refused()
    {
        Assert.Throws<FormatException>(() => Fpcalc.Parse("DURATION=12\n"));
    }

    // ---- real episodes ----

    /// <summary>
    /// The first ten minutes of three real episodes' fingerprints, padded with unrelated values to a
    /// realistic length so the share of an episode that counts as its opening is the real one.
    /// </summary>
    internal static uint[] Episode(int number)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fingerprints", $"slow-horses-s05e0{number}.opening.u32");
        var bytes = File.ReadAllBytes(path);
        var opening = new uint[bytes.Length / 4];
        for (var i = 0; i < opening.Length; i++)
            opening[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i * 4));

        var episode = Random(21000, seed: 100 + number);
        opening.CopyTo(episode, 0);
        return episode;
    }

    [Theory]
    [InlineData(2, 3, 489.0, 534.1, 196.1, 241.1)]
    [InlineData(3, 2, 196.1, 241.1, 489.0, 534.1)]
    public void Finds_the_title_sequence_where_the_frames_of_both_episodes_say_it_is(
        int a, int b, double startA, double endA, double startB, double endB)
    {
        var match = IntroFinder.Find(Episode(a), Episode(b));

        Assert.NotNull(match);

        // The start is the chapter's. The end is where the frames of the two episodes stop being the
        // same, which is the show's name card ending; it is eleven seconds after the chapter's end,
        // and the match is within a second of it and never past it.
        Assert.InRange(match.Value.StartA, startA - 0.5, startA + 0.5);
        Assert.InRange(match.Value.StartB, startB - 0.5, startB + 0.5);
        Assert.InRange(match.Value.EndA, endA - 1.0, endA);
        Assert.InRange(match.Value.EndB, endB - 1.0, endB);
    }

    [Fact]
    public void Finds_the_title_sequence_of_an_episode_that_names_none()
    {
        var one = Episode(1);

        var withTwo = IntroFinder.Find(one, Episode(2));
        var withThree = IntroFinder.Find(one, Episode(3));

        Assert.NotNull(withTwo);
        Assert.NotNull(withThree);
        Assert.Equal(withTwo.Value.StartA, withThree.Value.StartA, 1.0);
        Assert.Equal(withTwo.Value.EndA, withThree.Value.EndA, 1.0);
    }

    [Fact]
    public void A_show_with_no_recap_is_given_none()
    {
        Assert.Null(RecapFinder.Find(Episode(3), [Episode(1), Episode(2)], RecapFinder.DefaultSearchSeconds));
        Assert.Null(RecapFinder.Find(Episode(2), [Episode(1)], RecapFinder.DefaultSearchSeconds));
    }
}

internal static class RandomExtensions
{
    public static uint NextUInt(this System.Random random) => (uint)random.NextInt64(0, 1L << 32);
}
