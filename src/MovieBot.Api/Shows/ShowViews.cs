using TheKrystalShip.MovieBot.Acquire.Tmdb;
using TheKrystalShip.MovieBot.Api.Films;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Shows;

/// <summary>
/// A show as its page shows it: the description, and every season with its episodes, each episode
/// joined with whether the library holds it and the asking person's progress in it. Works for a show
/// the library does not hold, which is the page somebody picks episodes on.
/// </summary>
public sealed record ShowView
{
    public required string ImdbId { get; init; }
    public int? TmdbId { get; init; }
    public required string Title { get; init; }
    public string? OriginalTitle { get; init; }
    public int? FirstYear { get; init; }
    public int? LastYear { get; init; }
    public string? Status { get; init; }
    public bool InProduction { get; init; }
    public string? Certification { get; init; }
    public required List<string> Genres { get; init; }
    public string? Overview { get; init; }
    public string? Tagline { get; init; }

    /// <summary>The database's keywords for the show; empty while the description is being asked for them.</summary>
    public required List<KeywordView> Keywords { get; init; }

    public double? Rating { get; init; }
    public int Votes { get; init; }
    /// <summary>Whose <see cref="Rating"/> is, <c>imdb</c> or <c>tmdb</c>; absent with no rating (<see cref="TitleScores.Serve"/>).</summary>
    public string? RatedBy { get; init; }

    public required List<string> Creators { get; init; }

    /// <summary>Each creator's id in the film database, by the name they are listed under.</summary>
    public required Dictionary<string, int> PersonIds { get; init; }

    public required List<CastView> Cast { get; init; }
    public required List<string> Languages { get; init; }
    public required List<string> Countries { get; init; }
    public required List<string> Networks { get; init; }
    public string? PosterUrl { get; init; }
    public string? BackdropUrl { get; init; }

    /// <summary>The title set as a logo, a transparent PNG served from here; absent when the database has none.</summary>
    public string? LogoUrl { get; init; }

    /// <summary>The official trailers and teasers on YouTube, the best first; empty while the description is being asked for them.</summary>
    public List<TrailerView> Trailers { get; init; } = [];

    /// <summary>The titles the database recommends after it, joined with the library like any list of titles.</summary>
    public List<TitleHitView> Recommendations { get; init; } = [];

    /// <summary>The first reviews the database holds.</summary>
    public List<ReviewView> Reviews { get; init; } = [];

    /// <summary>How many reviews the database holds.</summary>
    public int ReviewCount { get; init; }

    public required List<SeasonView> Seasons { get; init; }

    /// <summary>Where the asking person picks up: absent when they have nothing in progress here.</summary>
    public ResumePoint? Resume { get; init; }

    /// <summary>Whether the show can be followed: the film database does not say it has ended or been cancelled.</summary>
    public bool Followable { get; init; }

    /// <summary>The asking person's follow of the show; absent when they do not follow it.</summary>
    public ShowFollow? Follow { get; init; }

    /// <summary>The episode the film database expects next; absent when it names none.</summary>
    public AiringView? NextEpisode { get; init; }

    /// <summary>The shelf it is on beyond its kind, <c>anime</c> or <c>general</c>; absent while nothing says.</summary>
    public Library.TitleCategory? Category { get; init; }
}

public sealed record SeasonView
{
    public required int Number { get; init; }
    public string? Name { get; init; }
    public string? AirDate { get; init; }
    public string? Overview { get; init; }
    public required List<EpisodeView> Episodes { get; init; }
}

public sealed record EpisodeView
{
    public required int Season { get; init; }
    public required int Number { get; init; }
    public string? Name { get; init; }
    public string? Overview { get; init; }
    public string? AirDate { get; init; }
    public int? RuntimeMinutes { get; init; }
    public double? Rating { get; init; }
    public int Votes { get; init; }
    public string? StillUrl { get; init; }

    /// <summary>The library's title for the episode; absent when the library does not hold it.</summary>
    public string? TitleId { get; init; }

    public TitleStatus? Status { get; init; }
    public double? DurationSeconds { get; init; }
    public TitleProgress? Progress { get; init; }
    public TitleLastPlayed? LastPlayed { get; init; }
}

/// <summary>An episode somebody is in the middle of, or the one after the last they finished.</summary>
public sealed record ResumePoint(string TitleId, int Season, int Episode, double PositionSeconds, double DurationSeconds);

/// <summary>One season of a show in the library: the episodes held of it, and those the asking person watched through.</summary>
public sealed record SeasonProgress(int Season, int Episodes, int Watched);

/// <summary>One show in the library, as a shelf, a hero and the library page list it.</summary>
public sealed record ShowSummary
{
    /// <summary>The show's IMDb id, which its page is at; absent for a show the library could not name.</summary>
    public string? ImdbId { get; init; }
    public required string Title { get; init; }
    public int? FirstYear { get; init; }
    public int? LastYear { get; init; }
    public string? Status { get; init; }
    public FilmFacts? Details { get; init; }
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

    /// <summary>The shelf it is on beyond its kind, <c>anime</c> or <c>general</c>; absent while nothing says.</summary>
    public Library.TitleCategory? Category { get; init; }
}

public static class ShowViews
{
    public static string ImageUrl(string imdbId, string file) => $"/shows/{imdbId}/{file}";

    /// <summary>The part of a show's description a listing shows, in the shape a film's takes.</summary>
    public static FilmFacts? Facts(this ShowStore store, string? imdbId)
    {
        if (imdbId is null || store.Held(imdbId)?.Details is not { } d) return null;
        var (rating, votes, ratedBy) = store.Scores.Serve(d.ImdbId, d.TmdbId, "show", d.Rating, d.Votes);
        return new FilmFacts
        {
            Rating = rating,
            Votes = votes,
            RatedBy = ratedBy,
            Certification = d.Certification,
            Genres = [.. d.Genres],
            Overview = d.Overview,
            Directors = [.. d.Creators],
            PosterUrl = store.HasImage(d.ImdbId, ShowStore.Poster) ? ImageUrl(d.ImdbId, ShowStore.Poster) : null,
            BackdropUrl = store.HasImage(d.ImdbId, ShowStore.Backdrop) ? ImageUrl(d.ImdbId, ShowStore.Backdrop) : null,
        };
    }

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

    /// <summary>The page of one show, from what is kept and what the library holds of it.</summary>
    public static ShowView View(
        this ShowStore store, string imdbId, StoredShow? stored,
        IReadOnlyList<TitleSummary> episodes, StoredFollow? follow = null)
    {
        var d = stored?.Details;
        string? Image(string file) => store.HasImage(imdbId, file) ? ImageUrl(imdbId, file) : null;
        var (rating, votes, ratedBy) = store.Scores.Serve(imdbId, d?.TmdbId, "show", d?.Rating, d?.Votes ?? 0);

        var held = episodes
            .Where(e => e.Episode is not null)
            .ToDictionary(e => (e.Episode!.Season, e.Episode.Number));

        // Every season the database lists with an episode in it, and any the library holds that it
        // does not. An announced season with no episodes yet has nothing to show or pick.
        var described = (stored?.Seasons ?? []).Where(s => s.Episodes.Count > 0).ToList();
        var numbers = described.Select(s => s.Number)
            .Concat(held.Keys.Select(k => k.Season))
            .Distinct().Order();

        var seasons = numbers.Select(n =>
        {
            var season = described.FirstOrDefault(s => s.Number == n);
            var known = season?.Episodes.Select(e => e.Number) ?? [];
            var episodeNumbers = known.Concat(held.Keys.Where(k => k.Season == n).Select(k => k.Number)).Distinct().Order();

            var rows = episodeNumbers.Select(number =>
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
                    RuntimeMinutes = e?.RuntimeMinutes,
                    Rating = e?.Rating,
                    Votes = e?.Votes ?? 0,
                    StillUrl = Image(ShowStore.Still(n, number)),
                    TitleId = title?.Id,
                    Status = title?.Status,
                    DurationSeconds = title?.DurationSeconds,
                    Progress = title?.Progress,
                    LastPlayed = title?.LastPlayed,
                };
            }).ToList();

            return new SeasonView
            {
                Number = n,
                Name = season?.Name,
                AirDate = season?.AirDate,
                Overview = season?.Overview,
                Episodes = rows,
            };
        }).ToList();

        var name = d?.Title ?? episodes.FirstOrDefault()?.Film?.Name ?? imdbId;
        return new ShowView
        {
            ImdbId = imdbId,
            TmdbId = d?.TmdbId,
            Title = name,
            OriginalTitle = d?.OriginalTitle,
            FirstYear = d?.FirstYear ?? episodes.FirstOrDefault()?.Film?.Year,
            LastYear = d?.LastYear,
            Status = d?.Status,
            InProduction = d?.InProduction ?? false,
            Certification = d?.Certification,
            Genres = [.. d?.Genres ?? []],
            Overview = d?.Overview,
            Tagline = d?.Tagline,
            Keywords = TitleExtrasViews.Of(d?.KeywordTags),
            Rating = rating,
            Votes = votes,
            RatedBy = ratedBy,
            Creators = [.. d?.Creators ?? []],
            PersonIds = new Dictionary<string, int>(d?.PersonIds ?? new Dictionary<string, int>()),
            Cast = (d?.Cast ?? []).Select((c, i) => new CastView
            {
                PersonId = c.TmdbId,
                Name = c.Name,
                Character = c.Character,
                PortraitUrl = Image(ShowStore.Portrait(i)),
            }).ToList(),
            Languages = [.. d?.Languages ?? []],
            Countries = [.. d?.Countries ?? []],
            Networks = [.. d?.Networks ?? []],
            PosterUrl = Image(ShowStore.Poster),
            BackdropUrl = Image(ShowStore.Backdrop),
            LogoUrl = Image(ShowStore.Logo),
            Trailers = TitleExtrasViews.Of(d?.Trailers),
            Reviews = TitleExtrasViews.Of(d?.Reviews ?? []),
            ReviewCount = d?.ReviewCount ?? 0,
            Seasons = seasons,
            Resume = ResumeIn(Ordered(episodes)),
            Followable = FollowViews.Followable(d),
            Follow = follow is null ? null : new ShowFollow(follow.AutoDownload, follow.Since),
            NextEpisode = FollowViews.Airing(d?.NextEpisodeToAir),
        };
    }

    /// <summary>A show's episodes in the library, grouped one entry per show for a listing.</summary>
    public static List<ShowSummary> Summaries(this ShowStore store, IReadOnlyList<TitleSummary> titles)
    {
        return titles
            .Where(t => t.Kind == TitleKind.Show && t.Episode is not null)
            .GroupBy(t => t.Film?.ImdbId ?? t.Unit[..t.Unit.LastIndexOf(':')])
            .Select(g =>
            {
                var episodes = Ordered([.. g]);
                var imdbId = episodes[0].Film?.ImdbId;
                var d = imdbId is null ? null : store.Held(imdbId)?.Details;
                return new ShowSummary
                {
                    ImdbId = imdbId,
                    Title = d?.Title ?? episodes[0].Film?.Name ?? g.Key,
                    FirstYear = d?.FirstYear ?? episodes[0].Film?.Year,
                    LastYear = d?.LastYear,
                    Status = d?.Status,
                    Details = store.Facts(imdbId),
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

    /// <summary>An episode's own name as the kept description of its show has it, or null.</summary>
    public static string? EpisodeName(this ShowStore store, string? showImdbId, EpisodeIdentity? episode) =>
        showImdbId is null || episode is null ? null
        : store.Held(showImdbId)?.Seasons.FirstOrDefault(s => s.Number == episode.Season)?
            .Episodes.FirstOrDefault(e => e.Number == episode.Number)?.Name;

    /// <summary>
    /// The episode after one, as the film database lists the show: the next of its season, else the
    /// first of the next season. Null at the end, and for a show the database has not described.
    /// </summary>
    public static EpisodeIdentity? EpisodeAfter(this ShowStore store, string showImdbId, EpisodeIdentity episode) =>
        store.Held(showImdbId)?.Seasons
            .Where(s => s.Number >= 1)
            .SelectMany(s => s.Episodes.Select(e => new EpisodeIdentity { Season = s.Number, Number = e.Number }))
            .Where(e => e.Season > episode.Season || (e.Season == episode.Season && e.Number > episode.Number))
            .OrderBy(e => e.Season).ThenBy(e => e.Number)
            .FirstOrDefault();

    /// <summary>
    /// The episode before one, as the film database lists the show: the one before it in its season,
    /// else the last of the season before. Null at the start, and for a show the database has not described.
    /// </summary>
    public static EpisodeIdentity? EpisodeBefore(this ShowStore store, string showImdbId, EpisodeIdentity episode) =>
        store.Held(showImdbId)?.Seasons
            .Where(s => s.Number >= 1)
            .SelectMany(s => s.Episodes.Select(e => new EpisodeIdentity { Season = s.Number, Number = e.Number }))
            .Where(e => e.Season < episode.Season || (e.Season == episode.Season && e.Number < episode.Number))
            .OrderBy(e => e.Season).ThenBy(e => e.Number)
            .LastOrDefault();

    public static List<TitleSummary> Ordered(IReadOnlyList<TitleSummary> episodes) =>
        [.. episodes.Where(e => e.Episode is not null)
            .OrderBy(e => e.Episode!.Season).ThenBy(e => e.Episode!.Number)];
}
