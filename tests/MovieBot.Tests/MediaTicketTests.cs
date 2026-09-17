using TheKrystalShip.MovieBot.Api.Auth;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A ticket opens a film's bytes without saying who is watching. These are the cases that decide
/// whether it opens the right film, for the right length of time, and whether two people watching
/// together are handed the one string that makes their requests cacheable.
/// </summary>
public sealed class MediaTicketTests
{
    private static readonly AuthOptions Options = new()
    {
        SigningKey = "a-signing-key-of-at-least-thirty-two-characters"
    };

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-17T20:05:00Z");

    [Fact]
    public void A_ticket_this_server_issued_opens_the_film_it_names()
    {
        var ticket = MediaTicket.Issue("evil-dead-burn-2026", Options, Now);

        Assert.True(MediaTicket.IsValid(ticket, "evil-dead-burn-2026", Options, Now));
    }

    /// <summary>
    /// The whole reason this exists. Two viewers starting the same film minutes apart have to
    /// produce byte-identical URLs, or a cache holds a copy of the film per person and the
    /// origin serves every one of them.
    /// </summary>
    [Fact]
    public void Everybody_watching_the_same_film_gets_the_same_ticket()
    {
        var first = MediaTicket.Issue("evil-dead-burn-2026", Options, Now);
        var second = MediaTicket.Issue("evil-dead-burn-2026", Options, Now.AddMinutes(17));
        var third = MediaTicket.Issue("evil-dead-burn-2026", Options, Now.AddSeconds(3));

        Assert.Equal(first, second);
        Assert.Equal(first, third);
    }

    [Fact]
    public void A_ticket_for_one_film_does_not_open_another()
    {
        var ticket = MediaTicket.Issue("evil-dead-burn-2026", Options, Now);

        Assert.False(MediaTicket.IsValid(ticket, "the-matrix-1999", Options, Now));
    }

    [Fact]
    public void A_ticket_stops_working_once_it_expires()
    {
        var ticket = MediaTicket.Issue("evil-dead-burn-2026", Options, Now);
        var expires = MediaTicket.ExpiresAt(Now);

        Assert.True(MediaTicket.IsValid(ticket, "evil-dead-burn-2026", Options, expires.AddSeconds(-1)));
        Assert.False(MediaTicket.IsValid(ticket, "evil-dead-burn-2026", Options, expires));
        Assert.False(MediaTicket.IsValid(ticket, "evil-dead-burn-2026", Options, expires.AddHours(1)));
    }

    /// <summary>A film runs long. A ticket that expired mid-showing would stall it at the credits.</summary>
    [Fact]
    public void A_ticket_outlasts_a_long_film_started_just_before_it_was_issued()
    {
        var ticket = MediaTicket.Issue("evil-dead-burn-2026", Options, Now);

        Assert.True(MediaTicket.IsValid(ticket, "evil-dead-burn-2026", Options, Now.AddHours(11)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("nodot")]
    [InlineData(".")]
    [InlineData("99999999999.")]
    [InlineData("notanumber.c2lnbmF0dXJl")]
    public void A_malformed_ticket_is_refused(string? ticket)
    {
        Assert.False(MediaTicket.IsValid(ticket, "evil-dead-burn-2026", Options, Now));
    }

    [Fact]
    public void A_ticket_signed_with_another_key_is_refused()
    {
        var other = new AuthOptions { SigningKey = "a-different-key-of-at-least-thirty-two-chars" };
        var ticket = MediaTicket.Issue("evil-dead-burn-2026", other, Now);

        Assert.False(MediaTicket.IsValid(ticket, "evil-dead-burn-2026", Options, Now));
    }

    /// <summary>
    /// The expiry travels in the clear, so it is the obvious thing to edit. It is signed with the
    /// title for exactly that reason.
    /// </summary>
    [Fact]
    public void An_expiry_moved_forward_by_hand_is_refused()
    {
        var ticket = MediaTicket.Issue("evil-dead-burn-2026", Options, Now);
        var signature = ticket[(ticket.IndexOf('.') + 1)..];
        var forged = $"{Now.AddYears(1).ToUnixTimeSeconds()}.{signature}";

        Assert.False(MediaTicket.IsValid(forged, "evil-dead-burn-2026", Options, Now));
    }

    [Fact]
    public void Nothing_is_valid_when_the_host_has_no_signing_key()
    {
        var unkeyed = new AuthOptions { SigningKey = "" };
        var ticket = MediaTicket.Issue("evil-dead-burn-2026", Options, Now);

        Assert.False(MediaTicket.IsValid(ticket, "evil-dead-burn-2026", unkeyed, Now));
    }
}
