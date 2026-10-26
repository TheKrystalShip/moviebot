using TheKrystalShip.MovieBot.Api.Catalogue;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Catalogue;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Shows;

/// <summary>One season of a show in the library: the episodes held of it, and those the asking person watched through.</summary>
public sealed record SeasonProgress(int Season, int Episodes, int Watched);

/// <summary>One show in the library, as a shelf, a hero and the library page list it: its card, and what the library holds of it.</summary>
public sealed record ShowSummary
{
    /// <summary>The show's IMDb id, which its page is at; absent for a show the library could not name.</summary>
    public string? ImdbId { get; init; }
    public required string Title { get; init; }

    /// <summary>The show's card; absent for a show the catalogue does not list.</summary>
    public TitleCard? Card { get; init; }

    public required List<int> Seasons { get; init; }
    public required int Episodes { get; init; }

    /// <summary>Each season the library holds, in order: how many of its episodes it holds and how many of those the asking person watched through.</summary>
    public required List<SeasonProgress> SeasonProgress { get; init; }

    public DateTimeOffset? AddedAt { get; init; }
    public long SizeBytes { get; init; }
    public TitleLastPlayed? LastPlayed { get; init; }
    public ResumePoint? Resume { get; init; }

    /// <summary>The first episode the library holds, for a surface that only wants to start it.</summary>
    public required string FirstTitleId { get; init; }
}

public static class ShowViews
{
    /// <summary>
    /// The person's place in a show, from their progress in its episodes: the latest one they
    /// watched if they did not finish it, else the one after it, if the library holds it.
    /// </summary>
    public static ResumePoint? ResumeIn(IReadOnlyList<TitleSummary> episodes)
    {
        var latest = episodes
            .Where(e => e.Progress is not null)
            .MaxBy(e => e.Progress!.LastWatchedAt);
        if (latest is null) return null;

        var resume = latest.Progress!.Finished
            ? episodes.SkipWhile(e => e.Id != latest.Id).Skip(1).FirstOrDefault()
            : latest;
        if (resume?.Episode is not { } place) return null;

        var position = resume == latest ? latest.Progress.PositionSeconds : 0;
        return new ResumePoint(resume.Id, place.Season, place.Number, position, resume.DurationSeconds);
    }

    /// <summary>What the person has of one show, from the catalogue's seasons and what the library holds of it.</summary>
    public static PersonShowView View(this CatalogueTitles titles, string imdbId, IReadOnlyList<TitleSummary> episodes, StoredFollow? follow = null)
    {
        var stored = titles.Show(imdbId);
        var held = episodes
            .Where(e => e.Episode is not null)
            .ToDictionary(e => (e.Episode!.Season, e.Episode.Number));

        // Every season the catalogue lists with an episode in it, and any the library holds that it does
        // not. An announced season with no episodes yet has nothing to show or pick.
        var described = (stored?.Seasons ?? []).Where(s => s.Episodes.Count > 0).ToList();
        var numbers = described.Select(s => s.Number).Concat(held.Keys.Select(k => k.Season)).Distinct().Order();

        var seasons = numbers.Select(n =>
        {
            var season = described.FirstOrDefault(s => s.Number == n);
            var known = season?.Episodes.Select(e => e.Number) ?? [];
            var rows = known.Concat(held.Keys.Where(k => k.Season == n).Select(k => k.Number)).Distinct().Order().Select(number =>
            {
                var e = season?.Episodes.FirstOrDefault(x => x.Number == number);
                held.TryGetValue((n, number), out var title);
                return new EpisodeView
                {
                    Season = n,
                    Number = number,
                    Name = e?.Name,
                    Overview = e?.Overview,
                    AirDate = e?.AirDate,
                    Runtime = e?.RuntimeMinutes,
                    Still = e?.StillPath,
                    TitleId = title?.Id,
                    Status = title?.Status,
                    DurationSeconds = title?.DurationSeconds,
                    Progress = title?.Progress,
                    LastPlayed = title?.LastPlayed,
                };
            }).ToList();
            return new SeasonView { Number = n, Name = season?.Name, AirDate = season?.AirDate, Overview = season?.Overview, Poster = season?.PosterPath, Episodes = rows };
        }).ToList();

        return new PersonShowView
        {
            ImdbId = imdbId,
            Seasons = seasons,
            Resume = ResumeIn(Ordered(episodes)),
            Followable = FollowViews.Followable(stored?.Details),
            Follow = follow is null ? null : new ShowFollow(follow.AutoDownload, follow.Since),
            NextEpisode = FollowViews.Airing(stored?.Details?.NextEpisodeToAir),
        };
    }

    /// <summary>A show's episodes in the library, grouped one entry per show for a listing.</summary>
    public static List<ShowSummary> Summaries(this CatalogueTitles catalogue, IReadOnlyList<TitleSummary> titles)
    {
        var groups = titles
            .Where(t => t.Kind == TitleKind.Show && t.Episode is not null)
            .GroupBy(t => t.Film?.ImdbId ?? t.Unit[..t.Unit.LastIndexOf(':')])
            .Select(g => Ordered([.. g]))
            .ToList();
        var cards = catalogue.Cards(groups.Select(e => e[0].Film?.ImdbId));
        return groups
            .Select(episodes =>
            {
                var imdbId = episodes[0].Film?.ImdbId;
                var card = imdbId is null ? null : cards.GetValueOrDefault(imdbId);
                return new ShowSummary
                {
                    ImdbId = imdbId,
                    Title = card?.Title ?? episodes[0].Film?.Name ?? episodes[0].Title,
                    Card = card,
                    Seasons = [.. episodes.Select(e => e.Episode!.Season).Distinct()],
                    Episodes = episodes.Count,
                    SeasonProgress = [.. episodes
                        .GroupBy(e => e.Episode!.Season)
                        .Select(s => new SeasonProgress(s.Key, s.Count(), s.Count(e => e.Progress is { Finished: true })))],
                    AddedAt = episodes.Max(e => e.AddedAt),
                    SizeBytes = episodes.Sum(e => e.SizeBytes ?? 0),
                    LastPlayed = episodes.Select(e => e.LastPlayed).OfType<TitleLastPlayed>().MaxBy(p => p.At),
                    Resume = ResumeIn(episodes),
                    FirstTitleId = episodes[0].Id,
                };
            })
            .OrderBy(s => s.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<TitleSummary> Ordered(IReadOnlyList<TitleSummary> episodes) =>
        [.. episodes.Where(e => e.Episode is not null)
            .OrderBy(e => e.Episode!.Season).ThenBy(e => e.Episode!.Number)];
}
