using TheKrystalShip.Agent.Plugin;

namespace TheKrystalShip.MovieBot.Assistant;

/// <summary>What the harness checks a reply against before the person reads it.</summary>
internal static class CinemaGuards
{
    // A title named in a reply as "Title (Year)" has to be in a result a tool returned this turn, or in what
    // the person or the conversation already said. A title is a run of capitalised words (with the small
    // words between them) that starts a line, follows a list marker, or follows a lowercase word or a comma,
    // so "You might be thinking of The Lighthouse (2019)" claims The Lighthouse and not the words before it.
    private const string TitleClaim =
        @"(?:(?:^|\n)[ \t*_>\-\d.]*|(?<=[a-z,:;).!?] ))(?<title>(?!\d+s\b)[\p{Lu}\d][\w'’&\-]*(?::?[ ]+(?:(?:of|the|a|an|and|in|on|to|for|at|vs|with|la|le|de)[ ]+)*[\p{Lu}\d][\w'’&\-]*)*)[*_]* \((?<year>\d{4})\)";

    public static void Declare(GuardBuilder guards) => guards
        // A reply is held to the figures the turn's tools returned: a rating, a year, a runtime or a vote
        // count nothing the turn read contains is sent back.
        .FabricatedFigures()

        // The words an act is claimed in. Wanting a title and changing a plan are proposals the person
        // confirms; watching, getting a film and follows are buttons on a title's page. A reply that says one
        // was done, or that a confirmation waits, when no tool proposed it is sent back once and then
        // corrected. "I can add that" is an offer, never a claim.
        .ActionClaims(claims => claims
            .Completed(
                "added", "saved", "downloaded", "fetched", "queued", "started", "played", "put on", "followed",
                "unfollowed", "removed", "set up", "reminded", "scheduled", "subscribed", "paused", "resumed",
                "planned", "invited", "cancelled", "picked", "answered")
            .Offered(
                "add", "save", "download", "fetch", "queue", "start", "play", "put on", "follow", "unfollow",
                "remove", "remind", "schedule", "subscribe", "pause", "resume", "plan", "invite", "cancel")
            .Phrases("waiting for your confirmation", "waiting for you to confirm", "awaiting your confirmation", "pending your confirmation")
            .Correction("\n\nCorrection: nothing was proposed or changed, and no confirmation is waiting. The card for the title opens its page. Ask me again and I will propose it properly.")
            .RetryNotice("\n\n(Correction: I described something I had not done. Doing it properly now.)\n\n"))

        .Claims("titles", TitleClaim, group: "title", against: "toolResults", ignoreCase: false,
            advice: "Name only titles a tool returned, or say you could not find it.")

        // Calls of a class one user message may make. The reads are unlimited; a hunt for one film makes
        // several. Proposals, the harness's web search and page fetch are bounded.
        .PerMessageLimit(ToolClass.Staged, 4)
        .PerMessageLimit(ToolClass.Search, 5)
        .PerMessageLimit(ToolClass.Fetch, 4);
}
