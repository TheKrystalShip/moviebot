using TheKrystalShip.Agent.Replies;

namespace TheKrystalShip.MovieBot.Bot.Assistant;

/// <summary>
/// The claim-of-acting check in a film room's words: the verbs a room, a download and the library are
/// acted on with.
/// </summary>
/// <remarks>
/// Asked only of a turn that moved no room, launched no film and proposed nothing, where "I have loaded
/// Heat" is false by construction — the reply this model gave to a refusal that mentioned putting a
/// film on. A report of what somebody else did ("Haru paused it") has no first-person subject and is
/// never a claim.
/// </remarks>
public static class RoomActionClaim
{
    public static readonly UnbackedActionClaim Check = new(new ActionClaimWords
    {
        CompletedVerbs =
            @"staged|queued|proposed|paused|played|resumed|started|stopped|restarted|skipped|moved|jumped|"
            + @"rewound|seeked|loaded|switched|changed|downloaded|fetched|added|kept|deleted|removed|"
            + @"put (?:it |that |the film |[A-Z][\w']* )?on|let (?:it |that )?go",
        OfferedVerbs = "stage|propose|pause|play|resume|start|stop|skip|move|jump|rewind|seek|load|switch|"
                       + "change|download|fetch|add|keep|delete|remove|put|let",
        Correction =
            "\n\n**Correction:** nothing was actually done. The room was not moved, and nothing was put on, "
            + "proposed or downloaded. Ask again.",
        RetryNotice = "\n\n*(Nothing was done there. Doing it properly now.)*\n\n",
    });
}
