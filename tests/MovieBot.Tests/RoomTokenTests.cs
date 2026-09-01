using Microsoft.Extensions.Time.Testing;
using TheKrystalShip.MovieBot.Api.Auth;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Discord is the only door, and this is the lock. These are the cases that would let somebody
/// through it, so they are tested rather than reasoned about.
/// </summary>
public sealed class RoomTokenTests
{
    private static readonly AuthOptions Options = new()
    {
        SigningKey = "a-signing-key-of-at-least-thirty-two-characters",
        TokenLifetime = TimeSpan.FromHours(8)
    };

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-01T20:00:00Z");

    private static RoomTokenPayload Payload(long? expiresAt = null) => new()
    {
        UserId = "1544403597662621727",
        DisplayName = "Someone",
        RoomId = "385731524755980298",
        ExpiresAtUnix = expiresAt ?? Now.AddHours(8).ToUnixTimeSeconds()
    };

    [Fact]
    public void A_token_this_server_issued_is_accepted_and_says_who_it_is()
    {
        var token = RoomToken.Issue(Payload(), Options);
        var read = RoomToken.Validate(token, Options, Now);

        Assert.NotNull(read);
        Assert.Equal("1544403597662621727", read.UserId);
        Assert.Equal("Someone", read.DisplayName);
        Assert.Equal("385731524755980298", read.RoomId);
    }

    [Fact]
    public void A_token_signed_with_another_key_is_refused()
    {
        var forged = RoomToken.Issue(Payload(), new AuthOptions { SigningKey = new string('k', 40) });
        Assert.Null(RoomToken.Validate(forged, Options, Now));
    }

    [Fact]
    public void Editing_the_payload_invalidates_the_signature()
    {
        var token = RoomToken.Issue(Payload(), Options);
        var parts = token.Split('.');

        // Somebody granting themselves a longer stay, or another room, has to re-sign to be heard.
        var tampered = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(
                """{"sub":"1","name":"Someone","room":"any-room","exp":99999999999}"""))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.Null(RoomToken.Validate($"{tampered}.{parts[1]}", Options, Now));
    }

    [Fact]
    public void An_expired_token_is_refused_however_well_it_is_signed()
    {
        var token = RoomToken.Issue(Payload(Now.AddMinutes(-1).ToUnixTimeSeconds()), Options);
        Assert.Null(RoomToken.Validate(token, Options, Now));
    }

    [Fact]
    public void A_token_still_inside_its_life_is_accepted()
    {
        var token = RoomToken.Issue(Payload(Now.AddMinutes(1).ToUnixTimeSeconds()), Options);
        Assert.NotNull(RoomToken.Validate(token, Options, Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("only-one-part.")]
    [InlineData(".only-a-signature")]
    [InlineData("!!!not-base64!!!.!!!nor-this!!!")]
    public void Rubbish_is_refused_rather_than_thrown_over(string? offered)
    {
        Assert.Null(RoomToken.Validate(offered, Options, Now));
    }

    [Fact]
    public void Without_a_signing_key_nothing_validates()
    {
        // The API refuses to start in this state; if it ever did, it must not accept everybody.
        var token = RoomToken.Issue(Payload(), Options);
        Assert.Null(RoomToken.Validate(token, new AuthOptions { SigningKey = "" }, Now));
    }

    [Fact]
    public void Time_comes_from_the_clock_it_is_given()
    {
        var clock = new FakeTimeProvider(Now);
        var token = RoomToken.Issue(Payload(Now.AddHours(8).ToUnixTimeSeconds()), Options);

        Assert.NotNull(RoomToken.Validate(token, Options, clock.GetUtcNow()));
        clock.Advance(TimeSpan.FromHours(9));
        Assert.Null(RoomToken.Validate(token, Options, clock.GetUtcNow()));
    }
}
