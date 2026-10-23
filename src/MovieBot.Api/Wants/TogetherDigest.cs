using System.Security.Cryptography;
using System.Text;
using TheKrystalShip.MovieBot.Api.Notifications;
using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.Notifications;

namespace TheKrystalShip.MovieBot.Api.Wants;

/// <summary>
/// Tells each person, at most once a day, which of the titles they want are ready and wanted by
/// somebody else too. The digest is told only to those who switched <c>together</c> on. It is keyed by
/// what it says, so a title that stays ready is not announced again every day.
/// </summary>
public sealed class TogetherDigest(
    WantStore wants,
    WantPresenter present,
    INotifier notifier,
    TimeProvider clock,
    ILogger<TogetherDigest> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    // The people told, or found to need no telling, today: a day's digest is one, however often it is asked.
    private readonly HashSet<string> _settled = new(StringComparer.Ordinal);
    private string? _day;

    /// <summary>How many titles or people a digest names before it counts the rest.</summary>
    private const int Named = 2;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await ReadyNotices.WaitAsync(timer, stoppingToken))
        {
            try
            {
                await PassAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "A pass for titles to watch together failed.");
            }
        }
    }

    public async Task PassAsync(CancellationToken ct)
    {
        var day = clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        if (_day != day)
        {
            _day = day;
            _settled.Clear();
        }

        foreach (var userId in wants.People())
        {
            if (_settled.Contains(userId)) continue;

            var ready = (await present.TogetherAsync(userId, ct)).Where(t => t.Want.State == WantStates.Ready).ToList();
            if (ready.Count == 0) continue;

            var text = Text(
                [.. ready.Select(t => t.Want.Title)],
                [.. ready.SelectMany(t => t.With).Select(w => w.Name ?? "Someone").Distinct(StringComparer.Ordinal)]);

            // Keyed by what it says: a digest saying what one already held is not told again.
            await notifier.TellAsync(userId, new NotificationDraft
            {
                Kind = NotificationKinds.Together,
                Key = "together:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16],
                Text = text,
                Url = ready.Count == 1 ? Link(ready[0].Want) : "/",
                Action = ready.Count == 1 ? "Open" : null,
            }, ct);
            _settled.Add(userId);
        }
    }

    /// <summary>One sentence naming the titles and the people, and counting the rest past <see cref="Named"/>.</summary>
    public static string Text(IReadOnlyList<string> titles, IReadOnlyList<string> names) =>
        $"{Say(titles)} {(titles.Count == 1 ? "is" : "are")} ready to watch together with {Say(names)}.";

    private static string Say(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        <= Named => $"{items[0]} and {items[1]}",
        _ => $"{string.Join(", ", items.Take(Named))} and {items.Count - Named} more",
    };

    private static string Link(WantView want) => want.Kind == WantKinds.Show
        ? SitePaths.Show(want.ImdbId)
        : want.LibraryId is { } id ? SitePaths.Title(id) : SitePaths.Film(want.ImdbId);
}
