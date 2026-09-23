using TheKrystalShip.MovieBot.Ingest.Pipeline;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The boosted mix was chosen by ear, on a 5.1 feature, against the room's own playback. The
/// weights for that layout are therefore a fixed point, and every other layout has to keep the
/// same balance between dialogue, fronts and surrounds rather than drift toward whichever role
/// happens to fill more speakers.
/// </summary>
public sealed class DialogueBoostTests
{
    [Fact]
    public void A_side_surround_source_gets_the_mix_that_was_chosen()
    {
        Assert.Equal(
            "pan=stereo|FL=0.9*FC+0.6*FL+0.45*SL+0.15*LFE|FR=0.9*FC+0.6*FR+0.45*SR+0.15*LFE",
            DialogueBoost.Downmix("5.1(side)"));
    }

    [Fact]
    public void A_back_surround_source_takes_its_surrounds_from_the_back_pair()
    {
        Assert.Equal(
            "pan=stereo|FL=0.9*FC+0.6*FL+0.45*BL+0.15*LFE|FR=0.9*FC+0.6*FR+0.45*BR+0.15*LFE",
            DialogueBoost.Downmix("5.1"));
    }

    /// <summary>
    /// Two surrounds a side at the full weight each would put three decibels more surround into
    /// the mix than the layout the weights were chosen on.
    /// </summary>
    [Fact]
    public void Seven_one_shares_the_surround_weight_between_side_and_back_at_equal_power()
    {
        Assert.Equal(
            "pan=stereo|FL=0.9*FC+0.6*FL+0.318*SL+0.318*BL+0.15*LFE"
            + "|FR=0.9*FC+0.6*FR+0.318*SR+0.318*BR+0.15*LFE",
            DialogueBoost.Downmix("7.1"));
    }

    [Fact]
    public void A_back_centre_is_a_surround_on_both_sides()
    {
        Assert.Equal(
            "pan=stereo|FL=0.9*FC+0.6*FL+0.318*SL+0.318*BC+0.15*LFE"
            + "|FR=0.9*FC+0.6*FR+0.318*SR+0.318*BC+0.15*LFE",
            DialogueBoost.Downmix("6.1"));
    }

    [Theory]
    [InlineData("stereo")]
    [InlineData("mono")]
    [InlineData("quad")]
    [InlineData("6 channels")]
    [InlineData(null)]
    public void A_source_without_a_known_centre_is_folded_down_the_ordinary_way(string? layout)
    {
        Assert.Null(DialogueBoost.Downmix(layout));
        Assert.StartsWith("aformat=channel_layouts=stereo,", DialogueBoost.FilterFor(layout));
    }

    [Theory]
    [InlineData("5.1(side)")]
    [InlineData("stereo")]
    public void Every_mix_ends_in_the_same_dynamics_at_the_output_rate(string layout)
    {
        var chain = DialogueBoost.FilterFor(layout);

        Assert.Contains(",acompressor=threshold=-24dB:ratio=3:attack=15:release=250:knee=6,", chain);
        Assert.EndsWith(",loudnorm=I=-18:LRA=9:TP=-2,aresample=48000", chain);
    }
}
