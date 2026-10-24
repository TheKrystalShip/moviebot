using TheKrystalShip.MovieBot.Acquire.Tmdb;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>A trailer or teaser on YouTube, embedded from <c>youtube-nocookie.com/embed/{key}</c>.</summary>
public sealed record TrailerView(string Key, string Name, string Kind, bool Official, string? Language);

/// <summary>One review, cut to its beginning, with the page that holds all of it.</summary>
public sealed record ReviewView(string Author, double? Rating, string Excerpt, string? Url);

/// <summary>
/// A keyword of the film database. It leads to the titles tagged with it at
/// <c>/api/catalogue/discover?about={name}</c>.
/// </summary>
public sealed record KeywordView(int Id, string Name);

public static class TitleExtrasViews
{
    public static List<TrailerView> Of(IReadOnlyList<Trailer>? trailers) =>
        [.. (trailers ?? []).Select(t => new TrailerView(t.Key, t.Name, t.Kind, t.Official, t.Language))];

    public static List<ReviewView> Of(IReadOnlyList<Review> reviews) =>
        [.. reviews.Select(r => new ReviewView(r.Author, r.Rating, r.Excerpt, r.Url))];

    public static List<KeywordView> Of(IReadOnlyList<KeywordTag>? tags) =>
        [.. (tags ?? []).Select(k => new KeywordView(k.Id, k.Name))];
}
