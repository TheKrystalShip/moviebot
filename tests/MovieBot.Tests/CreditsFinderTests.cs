using TheKrystalShip.MovieBot.Ingest.Markers;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Fingerprints stand in for audio, as in <see cref="AudioMarkerTests"/>: the credits are the same
/// sound at the close of each episode, heard a few bits apart, and the rest is unrelated.
/// </summary>
public class CreditsFinderTests
{
    private static uint[] Random(int count, int seed)
    {
        var random = new System.Random(seed);
        var items = new uint[count];
        for (var i = 0; i < count; i++) items[i] = random.NextUInt();
        return items;
    }

    private static uint[] Heard(uint[] sound, int seed)
    {
        var random = new System.Random(seed);
        var items = (uint[])sound.Clone();
        for (var i = 0; i < items.Length; i++)
            for (var bit = 0; bit < 2; bit++)
                items[i] ^= 1u << random.Next(32);
        return items;
    }

    private static int Items(double seconds) => (int)Math.Round(seconds / Fpcalc.ItemSeconds);

    /// <summary>An episode of the given length with the theme placed to end the given seconds before its end.</summary>
    private static uint[] Episode(double seconds, uint[] theme, double after, int seed)
    {
        var episode = Random(Items(seconds), seed);
        Heard(theme, seed + 100).CopyTo(episode, episode.Length - Items(after) - theme.Length);
        return episode;
    }

    [Fact]
    public void Credits_that_close_both_episodes_run_to_the_end_of_each()
    {
        var theme = Random(Items(36), seed: 1);
        var a = Episode(2600, theme, after: 0, seed: 2);
        var b = Episode(2480, theme, after: 0, seed: 3);

        var match = CreditsFinder.Find(a, b);

        Assert.NotNull(match);
        Assert.InRange(match.Value.StartA, 2563, 2565);
        Assert.Equal(Fpcalc.TimeOf(a.Length), match.Value.EndA);
        Assert.InRange(match.Value.StartB, 2443, 2445);
        Assert.Equal(Fpcalc.TimeOf(b.Length), match.Value.EndB);
    }

    [Fact]
    public void A_preview_after_the_credits_is_left_after_them()
    {
        var theme = Random(Items(36), seed: 1);
        var a = Episode(2650, theme, after: 60, seed: 2);
        var b = Episode(2600, theme, after: 0, seed: 3);

        var match = CreditsFinder.Find(a, b)!.Value;

        Assert.InRange(match.EndA, 2588, 2591);
        Assert.Equal(Fpcalc.TimeOf(b.Length), match.EndB);
    }

    [Fact]
    public void A_shared_cue_far_from_the_end_is_not_the_credits()
    {
        var cue = Random(Items(40), seed: 1);
        var a = Episode(2600, cue, after: 200, seed: 2);
        var b = Episode(2600, cue, after: 200, seed: 3);

        Assert.Null(CreditsFinder.Find(a, b));
    }

    [Fact]
    public void A_sting_is_not_the_credits()
    {
        var logo = Random(Items(6), seed: 1);

        Assert.Null(CreditsFinder.Find(Episode(1290, logo, 0, seed: 2), Episode(1288, logo, 0, seed: 3)));
    }

    [Fact]
    public void Credits_broken_by_a_moment_the_encodes_disagree_on_are_one_stretch()
    {
        var theme = Random(Items(150), seed: 1);
        var a = Episode(3800, theme, after: 0, seed: 2);
        var b = Episode(3900, theme, after: 0, seed: 3);
        var gap = a.Length - Items(80);
        Random(Items(4), seed: 9).CopyTo(a, gap);

        var match = CreditsFinder.Find(a, b)!.Value;

        Assert.InRange(match.StartA, 3649, 3651);
        Assert.Equal(Fpcalc.TimeOf(a.Length), match.EndA);
    }

    [Fact]
    public void One_partner_is_believed_alone()
    {
        var found = CreditsFinder.Agree([new CreditsMatch(2560, 2595, 2440, 2475)]);

        Assert.Equal((2560, 2595), (found!.Value.Start, found.Value.End));
    }

    [Fact]
    public void With_several_partners_two_have_to_agree()
    {
        Assert.Null(CreditsFinder.Agree([new CreditsMatch(1399, 1462, 0, 0), null, new CreditsMatch(1529, 1557, 0, 0)]));
        Assert.Null(CreditsFinder.Agree([new CreditsMatch(1399, 1462, 0, 0), null, null]));
    }

    [Fact]
    public void Partners_that_agree_give_what_they_have_in_common()
    {
        var found = CreditsFinder.Agree(
        [
            new CreditsMatch(3609, 3768, 0, 0),
            new CreditsMatch(3650, 3763, 0, 0),
            new CreditsMatch(3000, 3100, 0, 0)
        ])!.Value;

        Assert.Equal(3650, found.Start);
        Assert.Equal(3763, found.End);
        Assert.Equal([0, 1], found.Agreeing);
    }
}
