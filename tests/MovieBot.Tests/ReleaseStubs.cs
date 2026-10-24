using System.Net;
using TheKrystalShip.MovieBot.Acquire.Imdb;
using TheKrystalShip.MovieBot.Acquire.Search;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The tracker and the title index answering from the test, for a host that stands the API up with
/// neither: a few releases and a few catalogue entries, found by whichever words or id name them.
/// </summary>
internal static class ReleaseStubs
{
    /// <summary>
    /// The tracker holds one release of Inception, as torrent 41, and one of Heat, which the library
    /// already has, as torrent 42. A search finds whichever the words or the id name.
    /// </summary>
    public sealed class OneRelease : IReleaseSearch
    {
        private static readonly Release Inception = new()
        {
            TorrentId = 41, ReleaseName = "Inception.2010.1080p.BluRay-GRP", Title = "Inception", Year = 2010,
            ImdbId = "tt1375666", SizeBytes = 8L << 30, Seeders = 12,
        };

        private static readonly Release Heat = new()
        {
            TorrentId = 42, ReleaseName = "Heat.1995.1080p.BluRay-GRP", Title = "Heat", Year = 1995,
            ImdbId = "tt0113277", SizeBytes = 9L << 30, Seeders = 20,
        };

        private static readonly Release DeadMansChest = new()
        {
            TorrentId = 43, ReleaseName = "Pirates.of.the.Caribbean.Dead.Mans.Chest.2006.1080p.BluRay-GRP",
            Title = "Pirates of the Caribbean Dead Mans Chest", Year = 2006,
            ImdbId = "tt0383574", SizeBytes = 12L << 30, Seeders = 30,
        };

        /// <summary>The films the title index names from words, one of which the tracker has no release of.</summary>
        private static readonly ImdbTitle[] Named =
        [
            new() { ImdbId = "tt1375666", Title = "Inception", Year = 2010 },
            new() { ImdbId = "tt0113277", Title = "Heat", Year = 1995 },
            new() { ImdbId = "tt0383574", Title = "Pirates of the Caribbean: Dead Man's Chest", Year = 2006 },
            new() { ImdbId = "tt0449088", Title = "Pirates of the Caribbean: At World's End", Year = 2007 },
        ];

        // The real search names the film in the title index before asking the tracker, so the
        // catalogue's spelling of the name finds it as surely as the id does.
        private static Task<RankedReleases> Find(string said) => Task.FromResult(new RankedReleases(
            [.. new[] { Inception, Heat, DeadMansChest }.Where(r =>
                said.Contains(r.Title, StringComparison.OrdinalIgnoreCase) || said == r.ImdbId
                || (r == DeadMansChest && said.Contains("Dead Man", StringComparison.OrdinalIgnoreCase)))], []));

        public async Task<RankedReleases> ByTextAsync(string typed, CancellationToken ct) =>
            await Find(typed) with
            {
                Film = Named.FirstOrDefault(f =>
                    typed.Contains(f.Title.Split(": ")[^1], StringComparison.OrdinalIgnoreCase)),
            };

        public Task<RankedReleases> ByTitleAsync(string query, CancellationToken ct) => Find(query);
        public Task<RankedReleases> ByImdbAsync(string imdbId, CancellationToken ct) => Find(imdbId);
    }

    /// <summary>
    /// The catalogue knows Inception, Heat and the Pirates of the Caribbean series, and finds whichever
    /// the words name. The series comes back as the index returns it: ordered by what people search
    /// for rather than by when the films came out, with people who share the words mixed in.
    /// </summary>
    internal sealed class CatalogueStub : HttpMessageHandler
    {
        private const string Pirates = """
            {"d":[
              {"id":"tt0325980","l":"Pirates of the Caribbean: The Curse of the Black Pearl","y":2003,"qid":"movie","s":"Johnny Depp, Geoffrey Rush","i":{"imageUrl":"https://m.media-amazon.com/images/M/pearl._V1_.jpg","width":1000,"height":1500}},
              {"id":"tt9900001","l":"Pirates of the Caribbean: The Series","y":2027,"qid":"tvSeries","s":"Keira Knightley"},
              {"id":"tt1790809","l":"Pirates of the Caribbean: Dead Men Tell No Tales","y":2017,"qid":"movie","s":"Johnny Depp, Geoffrey Rush","i":{"imageUrl":"https://m.media-amazon.com/images/M/tales._V1_.jpg","width":1000,"height":1500}},
              {"id":"tt1298650","l":"Pirates of the Caribbean: On Stranger Tides","y":2011,"qid":"movie","s":"Johnny Depp, Penélope Cruz"},
              {"id":"tt0383574","l":"Pirates of the Caribbean: Dead Man's Chest","y":2006,"qid":"movie","s":"Johnny Depp, Orlando Bloom"},
              {"id":"tt0449088","l":"Pirates of the Caribbean: At World's End","y":2007,"qid":"movie","s":"Johnny Depp, Orlando Bloom"},
              {"id":"nm0809688","l":"Rex Smith","s":"Actor, The Pirates of Penzance (1983)"}
            ],"q":"x","v":1}
            """;

        private const string Inception = """
            {"d":[{"id":"tt1375666","l":"Inception","y":2010,"qid":"movie","s":"Leonardo DiCaprio, Joseph Gordon-Levitt"}],"q":"x","v":1}
            """;

        // A studio's name: a project announced with no year, a title the film database holds nothing
        // for, and one film it knows.
        private const string Studio = """
            {"d":[
              {"id":"tt0009390","l":"Untitled Blumhouse Project","qid":"movie","s":""},
              {"id":"tt0009391","l":"Blumhouse Enhanced Cinema","y":2026,"qid":"movie","s":""},
              {"id":"tt0325980","l":"Pirates of the Caribbean: The Curse of the Black Pearl","y":2003,"qid":"movie","s":"Johnny Depp, Geoffrey Rush"}
            ],"q":"x","v":1}
            """;

        private const string Heat = """
            {"d":[{"id":"tt0113277","l":"Heat","y":1995,"qid":"movie","s":"Al Pacino, Robert De Niro"}],"q":"x","v":1}
            """;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;

            // An id is looked up under its own key, and the index answers with the film it names.
            var tag = path.Split('/')[^1].Replace(".json", "");
            if (tag.StartsWith("tt", StringComparison.Ordinal))
                path = new[] { Heat, Inception, Pirates }.FirstOrDefault(b => b.Contains($"\"{tag}\"")) switch
                {
                    Heat => "heat", Inception => "incep", Pirates => "pirates", _ => path,
                };

            var body = path.Contains("heat", StringComparison.OrdinalIgnoreCase) ? Heat
                : path.Contains("incep", StringComparison.OrdinalIgnoreCase) ? Inception
                : path.Contains("pirates", StringComparison.OrdinalIgnoreCase) ? Pirates
                : path.Contains("blumhouse", StringComparison.OrdinalIgnoreCase) ? Studio
                : null;

            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
                });
        }
    }
}
