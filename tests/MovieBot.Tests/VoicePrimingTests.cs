using Microsoft.Extensions.Configuration;
using TheKrystalShip.Discord.Voice;
using TheKrystalShip.MovieBot.Bot.Voice;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// What the recogniser is primed with, held against the phrases that address the bot.
/// </summary>
/// <remarks>
/// Whisper given noise hands back the sentence it was primed with, so a priming that contains a
/// trigger turns a breath into somebody addressing the bot: a tone, a door held open, and whatever is
/// heard next put to the assistant.
/// </remarks>
public sealed class VoicePrimingTests
{
    [Fact]
    public void The_priming_echoed_back_does_not_address_the_bot()
    {
        var shipped = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            .Build()["Voice:Triggers"];
        Assert.False(string.IsNullOrWhiteSpace(shipped));

        var wake = new WakeWordDetector(shipped!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        Assert.Null(wake.Match(MovieBotSpeechToText.Priming));
    }
}
