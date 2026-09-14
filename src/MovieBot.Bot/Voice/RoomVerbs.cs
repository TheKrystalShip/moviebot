using System.Text;
using System.Text.RegularExpressions;

namespace TheKrystalShip.MovieBot.Bot.Voice;

/// <summary>Whether something said out loud was one of the room's own verbs.</summary>
public enum RoomVerbMatch
{
    /// <summary>Not a room verb at all: a question, a remark, a request for something else.</summary>
    NoMatch,

    /// <summary>
    /// Shaped like a room verb and not readable as one — no amount, a vague one, or more than one act.
    /// Distinct from <see cref="NoMatch"/> because the difference is worth reading in a log: this is a
    /// phrasing the gate nearly handled.
    /// </summary>
    Ambiguous,

    /// <summary>Unmistakably one of the room's verbs, with everything needed to carry it out.</summary>
    Match,
}

/// <summary>Which act a matched verb is.</summary>
public enum RoomVerbKind
{
    Play,
    Pause,

    /// <summary>Move the film by an amount from where it is. Negative goes back.</summary>
    Nudge,
}

/// <summary>What the gate made of one utterance.</summary>
/// <param name="Seconds">For a nudge, how far to move — negative goes back. Zero otherwise.</param>
public readonly record struct RoomVerbReading(
    RoomVerbMatch Match, RoomVerbKind Kind = default, double Seconds = 0)
{
    public static readonly RoomVerbReading NotAVerb = new(RoomVerbMatch.NoMatch);
    public static readonly RoomVerbReading Unreadable = new(RoomVerbMatch.Ambiguous);
}

/// <summary>
/// Reads the handful of things said to a room that need no judgement: pause, play, back fifteen.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately no model.</b> The common verbs are a closed set with no ambiguity in them, and
/// spending a model round trip — and a small model's judgement — on "pause" buys nothing and risks
/// something. A component that moves a room for everybody in it should be readable, testable and
/// identical every time it runs.
/// </para>
/// <para>
/// <b>The whole utterance, never a part of it.</b> "Pause the movie" matches. "Should we pause?" and
/// "don't pause it" contain the word and are not the verb, and a substring match would act on both.
/// What is left unmatched goes to whatever understands language, which is where a question belongs.
/// </para>
/// <para>
/// <b>Three answers, because the gate's mistakes are not symmetric.</b> Missing a verb costs the room
/// a slower answer. Acting on a misreading moves the film for everybody. So a phrasing this nearly
/// reads — "go back" with no amount, "rewind a bit" — is <see cref="RoomVerbMatch.Ambiguous"/> and is
/// not guessed at.
/// </para>
/// <para>
/// <b>Numbers are read from the words before they are flattened.</b> Recognition writes "1:30" and
/// "1.5", and stripping punctuation turns those into "130" and "15" — a confident, wrong seek. Any
/// digits joined by a colon, point or comma make the utterance ambiguous rather than being fused.
/// </para>
/// </remarks>
public static partial class RoomVerbs
{
    /// <summary>The furthest a spoken nudge may move the film. Past it the amount was probably misheard.</summary>
    public const double LongestNudgeSeconds = 3 * 60 * 60;

    private static readonly HashSet<string> Fillers = new(StringComparer.Ordinal)
    {
        "please", "now", "okay", "ok", "thanks", "just",
    };

    private static readonly string[][] PausePhrases =
    [
        ["pause"], ["pause", "it"], ["pause", "the", "film"], ["pause", "the", "movie"],
        ["stop"], ["stop", "it"], ["stop", "the", "film"], ["stop", "the", "movie"],
    ];

    private static readonly string[][] PlayPhrases =
    [
        ["play"], ["play", "it"], ["play", "the", "film"], ["play", "the", "movie"],
        ["resume"], ["resume", "it"], ["resume", "the", "film"], ["resume", "the", "movie"],
        ["unpause"], ["continue"],
    ];

    /// <summary>
    /// Words that open a play or a pause. An utterance starting with one that is not an exact phrase is
    /// somebody asking for something near a room verb — "stop talking", "play something else" — and is
    /// ambiguous rather than unrelated.
    /// </summary>
    private static readonly HashSet<string> PlaybackOpeners = new(StringComparer.Ordinal)
    {
        "pause", "stop", "play", "resume", "unpause", "continue",
    };

    // Longest first, so "skip back" is not read as "skip" followed by "back".
    private static readonly string[][] BackPhrases =
    [
        ["go", "backwards"], ["go", "back"], ["skip", "back"], ["jump", "back"],
        ["rewind"], ["backwards"], ["back"],
    ];

    // "go ahead" is absent on purpose: it means "carry on", and read as a direction it would skip the
    // film forward the first time somebody said it to agree with something.
    private static readonly string[][] ForwardPhrases =
    [
        ["fast", "forward"], ["skip", "forward"], ["skip", "ahead"], ["jump", "forward"],
        ["jump", "ahead"], ["go", "forward"], ["forward"], ["skip"],
    ];

    private static readonly Dictionary<string, int> Units = new(StringComparer.Ordinal)
    {
        ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5,
        ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9,
    };

    private static readonly Dictionary<string, int> Teens = new(StringComparer.Ordinal)
    {
        ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13, ["fourteen"] = 14,
        ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19,
    };

    private static readonly Dictionary<string, int> Tens = new(StringComparer.Ordinal)
    {
        ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50,
        ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90,
    };

    private static readonly Dictionary<string, int> TimeUnits = new(StringComparer.Ordinal)
    {
        ["s"] = 1, ["sec"] = 1, ["secs"] = 1, ["second"] = 1, ["seconds"] = 1,
        ["min"] = 60, ["mins"] = 60, ["minute"] = 60, ["minutes"] = 60,
    };

    /// <summary>Reads one utterance, with the trigger already removed.</summary>
    public static RoomVerbReading Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return RoomVerbReading.NotAVerb;

        // Before anything is flattened: digits joined by punctuation are a time or a decimal, and the
        // tokeniser would fuse them into a different number.
        if (JoinedDigits().IsMatch(text)) return Refused(Tokens(text));

        string[] words = Trim(Tokens(text));
        if (words.Length == 0) return RoomVerbReading.NotAVerb;

        if (IsAny(words, PausePhrases)) return new(RoomVerbMatch.Match, RoomVerbKind.Pause);
        if (IsAny(words, PlayPhrases)) return new(RoomVerbMatch.Match, RoomVerbKind.Play);

        return ReadNudge(words);
    }

    private static RoomVerbReading ReadNudge(string[] words)
    {
        // DIRECTION AMOUNT [UNIT] — "back fifteen", "skip forward thirty seconds"
        if (Leading(words, BackPhrases, out int back))
            return Amount(words[back..], sign: -1);
        if (Leading(words, ForwardPhrases, out int forward))
            return Amount(words[forward..], sign: +1);

        // AMOUNT [UNIT] DIRECTION — "fifteen seconds back"
        if (Trailing(words, BackPhrases, out int backAt))
            return Amount(words[..backAt], sign: -1);
        if (Trailing(words, ForwardPhrases, out int forwardAt))
            return Amount(words[..forwardAt], sign: +1);

        return PlaybackOpeners.Contains(words[0]) ? RoomVerbReading.Unreadable : RoomVerbReading.NotAVerb;
    }

    /// <summary>A direction was said; the rest has to be an amount and nothing else.</summary>
    private static RoomVerbReading Amount(string[] words, int sign)
    {
        if (!TryReadDuration(words, out double seconds)) return RoomVerbReading.Unreadable;
        if (seconds <= 0 || seconds > LongestNudgeSeconds) return RoomVerbReading.Unreadable;

        return new(RoomVerbMatch.Match, RoomVerbKind.Nudge, sign * seconds);
    }

    /// <summary>
    /// Reads "fifteen", "15 seconds", "a minute", "two minutes", "forty five seconds", "half a minute".
    /// Every word must be spent: a trailing "and pause" is a second act, and one act is all this reads.
    /// </summary>
    private static bool TryReadDuration(string[] words, out double seconds)
    {
        seconds = 0;
        if (words.Length == 0) return false;

        if (words is ["half", "a", "minute"])
        {
            seconds = 30;
            return true;
        }

        int i = 0;
        int? count = null;
        bool needsUnit = false;

        if (words[i] is "a" or "an")
        {
            count = 1;
            needsUnit = true;
            i++;
        }
        else if (words[i].All(char.IsAsciiDigit))
        {
            if (!int.TryParse(words[i], out int digits)) return false;
            count = digits;
            i++;
        }
        else if (Teens.TryGetValue(words[i], out int teen))
        {
            count = teen;
            i++;
        }
        else if (Tens.TryGetValue(words[i], out int tens))
        {
            count = tens;
            i++;
            if (i < words.Length && Units.TryGetValue(words[i], out int unit))
            {
                count += unit;
                i++;
            }
        }
        else if (Units.TryGetValue(words[i], out int single))
        {
            count = single;
            i++;
        }

        if (count is null) return false;

        int multiplier = 1;
        if (i < words.Length && TimeUnits.TryGetValue(words[i], out int perUnit))
        {
            multiplier = perUnit;
            i++;
        }
        else if (needsUnit)
        {
            return false;
        }

        if (i != words.Length) return false;

        seconds = count.Value * multiplier;
        return true;
    }

    /// <summary>
    /// An utterance whose numbers cannot be trusted. Ambiguous only when it was shaped like a verb: a
    /// time mentioned in passing — "the meeting is at 1:30" — was never a request to move the film.
    /// </summary>
    private static RoomVerbReading Refused(string[] words)
    {
        string[] trimmed = Trim(words);
        if (trimmed.Length == 0) return RoomVerbReading.NotAVerb;

        bool shaped = Leading(trimmed, BackPhrases, out _) || Leading(trimmed, ForwardPhrases, out _)
            || Trailing(trimmed, BackPhrases, out _) || Trailing(trimmed, ForwardPhrases, out _)
            || PlaybackOpeners.Contains(trimmed[0]);

        return shaped ? RoomVerbReading.Unreadable : RoomVerbReading.NotAVerb;
    }

    /// <summary>
    /// Lower case, letters and digits only, hyphens as spaces, and a number fused to its unit split
    /// apart — recognition writes "30s" and "forty-five" as readily as "30 s" and "forty five".
    /// </summary>
    private static string[] Tokens(string text)
    {
        var builder = new StringBuilder(text.Length + 8);
        char previous = ' ';

        foreach (char raw in text)
        {
            char c = char.ToLowerInvariant(raw);
            if (char.IsLetterOrDigit(c))
            {
                if (builder.Length > 0 && previous != ' '
                    && char.IsAsciiDigit(previous) != char.IsAsciiDigit(c))
                    builder.Append(' ');

                builder.Append(c);
                previous = c;
            }
            else if (previous != ' ')
            {
                // Apostrophes join a word rather than splitting it: "don't" is one word, and it is the
                // word that makes an utterance not a pause.
                if (c is '\'' or '’') continue;

                builder.Append(' ');
                previous = ' ';
            }
        }

        return builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Politeness at either end changes nothing about what was asked.</summary>
    private static string[] Trim(string[] words)
    {
        int start = 0, end = words.Length;

        while (start < end && Fillers.Contains(words[start])) start++;
        while (end > start)
        {
            if (Fillers.Contains(words[end - 1])) end--;
            else if (end - start >= 2 && words[end - 2] == "thank" && words[end - 1] == "you") end -= 2;
            else break;
        }

        return words[start..end];
    }

    private static bool IsAny(string[] words, string[][] phrases)
    {
        foreach (string[] phrase in phrases)
            if (words.AsSpan().SequenceEqual(phrase)) return true;
        return false;
    }

    private static bool Leading(string[] words, string[][] phrases, out int length)
    {
        foreach (string[] phrase in phrases)
        {
            if (words.Length >= phrase.Length && words.AsSpan(0, phrase.Length).SequenceEqual(phrase))
            {
                length = phrase.Length;
                return true;
            }
        }

        length = 0;
        return false;
    }

    private static bool Trailing(string[] words, string[][] phrases, out int at)
    {
        foreach (string[] phrase in phrases)
        {
            int from = words.Length - phrase.Length;
            if (from > 0 && words.AsSpan(from).SequenceEqual(phrase))
            {
                at = from;
                return true;
            }
        }

        at = 0;
        return false;
    }

    [GeneratedRegex(@"\d\s*[:.,]\s*\d")]
    private static partial Regex JoinedDigits();
}
