using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Offline;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Save offline: a track is a rendition's files joined, measured exactly and ranged so a transfer
/// resumes; a save holds its title while it is made; a device's progress comes back newest first.
/// </summary>
public sealed class OfflineTests(OfflineTests.Fixture fixture) : IClassFixture<OfflineTests.Fixture>
{
    public const string Film = "saved-film";

    /// <summary>The API over a library holding one finished film whose files are on disk.</summary>
    public sealed class Fixture : SessionFixture
    {
        public Fixture()
        {
            var directory = Path.Combine(MediaRoot, Film);
            Rendition(Path.Combine(directory, "v0"), "q23-", [2.002, 2.002, 0.5], seed: 1);
            Rendition(Path.Combine(directory, "v1"), "", [2.002, 2.002, 0.5], seed: 2);
            Rendition(Path.Combine(directory, "a0"), "", [4.0, 0.5], seed: 3);
            File.WriteAllText(Path.Combine(directory, "s3.vtt"), "WEBVTT\n\n00:00.000 --> 00:01.000\nHello\n");

            ManifestJson.WriteAtomic(Path.Combine(directory, "manifest.json"), new Manifest
            {
                Id = Film,
                Title = "Saved Film",
                DurationSeconds = 4.504,
                Status = TitleStatus.Ready,
                Video = new VideoInfo
                {
                    Width = 1920, Height = 800, SourceCodec = "h264", SourceHdr = "sdr",
                    Renditions =
                    [
                        new Rendition { Name = "800p", BitrateKbps = 9000, Width = 1920, Height = 800, Uri = "v0/index.m3u8" },
                        new Rendition { Name = "534p", BitrateKbps = 4000, Width = 1280, Height = 534, Uri = "v1/index.m3u8" },
                    ],
                },
                Audio =
                [
                    new AudioTrack { Id = "a0", Kind = TrackKind.Feature, Language = "eng", Label = "English", Channels = 2, Default = true, Uri = "a0/index.m3u8" },
                ],
                Subtitles =
                [
                    new SubtitleTrack { Id = "s3", Kind = TrackKind.Feature, Language = "eng", Label = "English", Source = SubtitleSource.EmbeddedText, Available = true, Uri = "s3.vtt" },
                    new SubtitleTrack { Id = "s4", Kind = TrackKind.Feature, Language = "spa", Label = "Spanish", Source = SubtitleSource.Bitmap, Available = false, Reason = "needs-ocr" },
                ],
            });
        }

        protected override Dictionary<string, string?> MoreSettings() => new()
        {
            ["Offline:Path"] = Path.Combine(StateRoot, "offline.json"),
            ["Offline:MaxTransfersPerPerson"] = "1",
        };

        /// <summary>
        /// A rendition's playlist and files, each segment of a length of its own so an offset error shows.
        /// One not <paramref name="ended"/> is still being encoded.
        /// </summary>
        public static void Rendition(string directory, string prefix, double[] seconds, int seed, bool ended = true)
        {
            Directory.CreateDirectory(directory);
            var random = new Random(seed);
            File.WriteAllBytes(Path.Combine(directory, prefix + "init.mp4"), Bytes(random, 700 + seed));

            var playlist = new System.Text.StringBuilder(
                $"#EXTM3U\n#EXT-X-VERSION:7\n#EXT-X-TARGETDURATION:4\n#EXT-X-PLAYLIST-TYPE:EVENT\n#EXT-X-MAP:URI=\"{prefix}init.mp4\"\n");
            for (var i = 0; i < seconds.Length; i++)
            {
                var name = $"{prefix}seg{i:00000}.m4s";
                File.WriteAllBytes(Path.Combine(directory, name), Bytes(random, 5000 + 1000 * i + seed));
                playlist.Append(System.Globalization.CultureInfo.InvariantCulture, $"#EXTINF:{seconds[i]:0.000000},\n{name}\n");
            }
            if (ended) playlist.Append("#EXT-X-ENDLIST\n");
            File.WriteAllText(Path.Combine(directory, "index.m3u8"), playlist.ToString());
        }

        /// <summary>
        /// A film the hand-off is still encoding, by <paramref name="imdbId"/>: two segments of its one
        /// rung and one of its audio made so far.
        /// </summary>
        public void Growing(string id, string imdbId)
        {
            var directory = Path.Combine(MediaRoot, id);
            Rendition(Path.Combine(directory, "v0"), "", [2.002, 2.002], seed: 4, ended: false);
            Rendition(Path.Combine(directory, "a0"), "", [4.0], seed: 5, ended: false);
            ManifestJson.WriteAtomic(Path.Combine(directory, "manifest.json"), new Manifest
            {
                Id = id,
                Title = "Growing Film",
                DurationSeconds = 600,
                Status = TitleStatus.Transcoding,
                HeadSeconds = 4,
                Film = new FilmIdentity { ImdbId = imdbId, Name = "Growing Film", Year = 2026 },
                Video = new VideoInfo
                {
                    Width = 854, Height = 356, SourceCodec = "h264", SourceHdr = "sdr",
                    Renditions = [new Rendition { Name = "356p", BitrateKbps = 1600, Width = 854, Height = 356, Uri = "v0/index.m3u8" }],
                },
                Audio = [new AudioTrack { Id = "a0", Kind = TrackKind.Feature, Language = "eng", Label = "English", Channels = 2, Default = true, Uri = "a0/index.m3u8" }],
                Subtitles = [],
            });
        }

        /// <summary>One more segment of a growing rendition, written and then listed, as the encoder does.</summary>
        public void Grow(string id, string rendition, int seed)
        {
            var directory = Path.Combine(MediaRoot, id, rendition);
            var playlist = Path.Combine(directory, "index.m3u8");
            var count = File.ReadAllLines(playlist).Count(l => l.StartsWith("seg", StringComparison.Ordinal));
            var name = $"seg{count:00000}.m4s";
            File.WriteAllBytes(Path.Combine(directory, name), Bytes(new Random(seed), 4321 + seed));
            File.AppendAllText(playlist, $"#EXTINF:2.002000,\n{name}\n");
        }

        private static byte[] Bytes(Random random, int length)
        {
            var bytes = new byte[length];
            random.NextBytes(bytes);
            return bytes;
        }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>What the files of a rendition joined are, read straight off the disk.</summary>
    private byte[] Joined(string rendition, string title = Film)
    {
        var directory = Path.Combine(fixture.MediaRoot, title, rendition);
        var lines = File.ReadAllLines(Path.Combine(directory, "index.m3u8"));
        var init = lines.First(l => l.StartsWith("#EXT-X-MAP")).Split('"')[1];
        return [.. new[] { init }.Concat(lines.Where(l => l.Length > 0 && l[0] != '#'))
            .SelectMany(f => File.ReadAllBytes(Path.Combine(directory, f)))];
    }

    private async Task<OfflinePackage> SaveAsync(HttpClient client, string device = "Pixel", string rung = "v1")
    {
        using var response = await client.PostAsJsonAsync("/api/me/offline",
            new { titleId = Film, rung, audio = new[] { "a0" }, subtitles = new[] { "s3" }, device });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OfflinePackage>(Json))!;
    }

    [Fact]
    public async Task The_offer_lists_every_rung_and_track_with_its_size_on_disk()
    {
        var offer = await fixture.CreatePersonClient("o1").GetFromJsonAsync<OfflineOffer>($"/api/titles/{Film}/offline", Json);

        Assert.Equal(["v0", "v1"], offer!.Rungs.Select(r => r.Rendition));
        Assert.Equal(Joined("v0").LongLength, offer.Rungs[0].Bytes);
        Assert.Equal(Joined("v1").LongLength, offer.Rungs[1].Bytes);
        Assert.Equal(Joined("a0").LongLength, offer.Audio.Single().Bytes);
        // The bitmap track cannot be shown from a text file, so it is not offered.
        Assert.Equal(["s3"], offer.Subtitles.Select(s => s.Id));
    }

    [Fact]
    public async Task A_title_that_failed_is_a_conflict_and_saving_needs_the_action()
    {
        var person = fixture.CreatePersonClient("o2");
        var failed = Path.Combine(fixture.MediaRoot, "failed-film");
        Directory.CreateDirectory(failed);
        ManifestJson.WriteAtomic(Path.Combine(failed, "manifest.json"), new Manifest
        {
            Id = "failed-film", Title = "Failed Film", Status = TitleStatus.Failed, DurationSeconds = 60,
            Video = new VideoInfo { Width = 854, Height = 356, SourceCodec = "h264", SourceHdr = "sdr", Renditions = [] },
            Audio = [], Subtitles = [],
        });
        using var conflict = await person.GetAsync("/api/titles/failed-film/offline");
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        var watcher = fixture.CreatePersonClient("o2", actions: [CinemaActions.Watch, CinemaActions.ProfileRead, CinemaActions.ProfileWrite]);
        using var refused = await watcher.PostAsJsonAsync("/api/me/offline",
            new { titleId = Film, rung = "v1", audio = new[] { "a0" }, device = "Pixel" });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task The_package_describes_each_track_as_the_files_joined()
    {
        var package = await SaveAsync(fixture.CreatePersonClient("o3"));

        Assert.Equal(["v1", "a0"], package.Tracks.Select(t => t.Rendition));
        var video = package.Tracks[0];
        Assert.Equal("video", video.Kind);
        Assert.Equal($"/media/{Film}/v1/track.mp4", video.Url);
        Assert.Equal(Joined("v1").LongLength, video.Bytes);
        Assert.Equal(video.Bytes, video.InitBytes + video.SegmentBytes.Sum());
        Assert.Equal([2.002, 2.002, 0.5], video.SegmentSeconds);

        // The manifest the device keeps is the film as it has it.
        Assert.Equal(["v1/index.m3u8"], package.Manifest.Video.Renditions.Select(r => r.Uri));
        Assert.Equal(["s3"], package.Manifest.Subtitles.Select(s => s.Id));
        Assert.Equal($"/media/{Film}/s3.vtt", package.Subtitles.Single().Url);
        Assert.Equal(package.Tracks.Sum(t => t.Bytes) + package.Subtitles.Sum(s => s.Bytes), package.Bytes);
        Assert.Null(package.Save.CompletedAt);
        Assert.NotNull(package.Save.HoldsUntil);
    }

    [Fact]
    public async Task A_track_is_the_files_joined_and_a_ranged_resume_reassembles_it()
    {
        var client = fixture.CreatePersonClient("o4");
        var package = await SaveAsync(client, rung: "v0");
        var expected = Joined("v0");
        var track = package.Tracks[0];

        using var whole = await client.GetAsync(track.Url);
        Assert.Equal(HttpStatusCode.OK, whole.StatusCode);
        Assert.Equal(expected.LongLength, whole.Content.Headers.ContentLength);
        Assert.Equal("video/mp4", whole.Content.Headers.ContentType?.MediaType);
        Assert.Equal(track.Etag, whole.Headers.ETag?.ToString());
        Assert.Equal(expected, await whole.Content.ReadAsByteArrayAsync());

        // A transfer broken partway, inside a segment, resumes from what the device holds.
        var cut = track.InitBytes + track.SegmentBytes[0] + 123;
        using var resume = new HttpRequestMessage(HttpMethod.Get, track.Url);
        resume.Headers.Range = new RangeHeaderValue(cut, null);
        resume.Headers.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue(track.Etag));
        using var rest = await client.SendAsync(resume);
        Assert.Equal(HttpStatusCode.PartialContent, rest.StatusCode);
        Assert.Equal($"bytes {cut}-{expected.Length - 1}/{expected.Length}", rest.Content.Headers.ContentRange?.ToString());
        var joined = expected[..(int)cut].Concat(await rest.Content.ReadAsByteArrayAsync()).ToArray();
        Assert.Equal(expected, joined);
    }

    [Fact]
    public async Task A_resume_against_another_version_gets_the_whole_track_and_past_the_end_is_416()
    {
        var client = fixture.CreatePersonClient("o5");
        var track = (await SaveAsync(client)).Tracks[1];

        using var stale = new HttpRequestMessage(HttpMethod.Get, track.Url);
        stale.Headers.Range = new RangeHeaderValue(10, null);
        stale.Headers.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue("\"another\""));
        using var whole = await client.SendAsync(stale);
        Assert.Equal(HttpStatusCode.OK, whole.StatusCode);
        Assert.Equal(track.Bytes, (await whole.Content.ReadAsByteArrayAsync()).LongLength);

        using var past = new HttpRequestMessage(HttpMethod.Get, track.Url);
        past.Headers.Range = new RangeHeaderValue(track.Bytes, null);
        using var refused = await client.SendAsync(past);
        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, refused.StatusCode);
        Assert.Equal($"bytes */{track.Bytes}", refused.Content.Headers.ContentRange?.ToString());

        using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, track.Url));
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(track.Bytes, head.Content.Headers.ContentLength);
    }

    [Fact]
    public async Task A_track_opens_only_to_somebody_who_saved_the_title_and_never_to_a_ticket()
    {
        using var unsaved = await fixture.CreatePersonClient("o6").GetAsync($"/media/{Film}/v1/track.mp4");
        Assert.Equal(HttpStatusCode.Conflict, unsaved.StatusCode);

        var ticket = MediaTicket.Issue(Film, new AuthOptions { MediaTicketKey = SessionFixture.MediaTicketKey }, DateTimeOffset.UtcNow);
        using var ticketed = await fixture.CreateClient().GetAsync($"/media/{Film}/v1/track.mp4?mt={Uri.EscapeDataString(ticket)}");
        Assert.Equal(HttpStatusCode.Unauthorized, ticketed.StatusCode);

        var saver = fixture.CreatePersonClient("o6b");
        await SaveAsync(saver);
        using var missing = await saver.GetAsync($"/media/{Film}/v9/track.mp4");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task A_save_s_grant_opens_its_tracks_and_its_completion_with_no_token()
    {
        var package = await SaveAsync(fixture.CreatePersonClient("o6c"));
        var grant = package.Grant!;
        Assert.Equal(SaveGrant.Header, grant.Header);
        Assert.True(grant.ExpiresAt > DateTimeOffset.UtcNow);

        var device = fixture.CreateClient();
        device.DefaultRequestHeaders.Add(grant.Header, grant.Value);
        var track = package.Tracks[0];
        var bytes = await device.GetByteArrayAsync(track.Url);
        Assert.Equal(Joined(track.Rendition), bytes);

        using var listing = await device.GetAsync("/api/me/offline");
        Assert.Equal(HttpStatusCode.Unauthorized, listing.StatusCode);

        using var done = await device.PostAsync($"/api/me/offline/{package.Save.Id}/complete", null);
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        Assert.NotNull((await done.Content.ReadFromJsonAsync<OfflineSave>(Json))!.CompletedAt);
    }

    [Fact]
    public async Task A_grant_opens_nothing_once_its_save_is_forgotten_or_once_tampered_with()
    {
        var owner = fixture.CreatePersonClient("o6d");
        var package = await SaveAsync(owner);
        var url = package.Tracks[0].Url;

        var forged = package.Grant!.Value[..^2] + (package.Grant.Value[^2..] == "AA" ? "BB" : "AA");
        var tampered = fixture.CreateClient();
        tampered.DefaultRequestHeaders.Add(SaveGrant.Header, forged);
        using var refusedForgery = await tampered.GetAsync(url);
        Assert.Equal(HttpStatusCode.Unauthorized, refusedForgery.StatusCode);

        // Another person's completion is not this grant's to make.
        var other = await SaveAsync(fixture.CreatePersonClient("o6e"));
        var device = fixture.CreateClient();
        device.DefaultRequestHeaders.Add(SaveGrant.Header, package.Grant.Value);
        using var elsewhere = await device.PostAsync($"/api/me/offline/{other.Save.Id}/complete", null);
        Assert.Equal(HttpStatusCode.Forbidden, elsewhere.StatusCode);

        using var forget = await owner.DeleteAsync($"/api/me/offline/{package.Save.Id}");
        Assert.Equal(HttpStatusCode.NoContent, forget.StatusCode);
        using var gone = await device.GetAsync(url);
        Assert.Equal(HttpStatusCode.Unauthorized, gone.StatusCode);
    }

    [Fact]
    public async Task A_person_past_their_transfers_is_asked_to_wait()
    {
        var client = fixture.CreatePersonClient("o7");
        var track = (await SaveAsync(client)).Tracks[0];
        var transfers = (Transfers)fixture.Services.GetService(typeof(Transfers))!;

        // A transfer of theirs under way holds the one place a person has here.
        using (var running = transfers.TryBegin("o7"))
        {
            Assert.NotNull(running);
            Assert.Null(transfers.TryBegin("o7"));

            using var refused = await client.GetAsync(track.Url);
            Assert.Equal((HttpStatusCode)429, refused.StatusCode);
            Assert.NotNull(refused.Headers.RetryAfter);

            // A probe moves nothing, so it is never refused.
            using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, track.Url));
            Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        }

        using var admitted = await client.GetAsync(track.Url);
        Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
    }

    [Fact]
    public async Task A_save_holds_its_title_until_it_is_complete_or_forgotten()
    {
        var client = fixture.CreatePersonClient("o8");
        var service = fixture.CreateServiceClient();
        var package = await SaveAsync(client, device: "Laptop");

        Assert.Contains(Film, (await service.GetFromJsonAsync<string[]>("/api/library/saving"))!);

        using var done = await client.PostAsync($"/api/me/offline/{package.Save.Id}/complete", null);
        var completed = await done.Content.ReadFromJsonAsync<OfflineSave>(Json);
        Assert.NotNull(completed!.CompletedAt);
        Assert.Null(completed.HoldsUntil);

        var mine = await client.GetFromJsonAsync<List<OfflineSave>>("/api/me/offline", Json);
        Assert.Equal(package.Save.Id, mine!.Single().Id);

        // Saved again on the same device it is the same save, being made again.
        var again = await SaveAsync(client, device: "Laptop");
        Assert.Equal(package.Save.Id, again.Save.Id);
        Assert.Null(again.Save.CompletedAt);

        using var forgotten = await client.DeleteAsync($"/api/me/offline/{package.Save.Id}");
        Assert.Equal(HttpStatusCode.NoContent, forgotten.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<OfflineSave>>("/api/me/offline", Json))!);
        using var twice = await client.DeleteAsync($"/api/me/offline/{package.Save.Id}");
        Assert.Equal(HttpStatusCode.NoContent, twice.StatusCode);
    }

    [Fact]
    public void A_save_stops_holding_once_its_hold_runs_out()
    {
        var path = Path.Combine(fixture.StateRoot, $"hold-{Guid.NewGuid():n}.db");
        var saves = new OfflineSaves(new CinemaDatabase(path), new Monitor(new OfflineOptions { HoldHours = 48 }));
        var made = DateTimeOffset.UtcNow;
        saves.Save("p", "heat-1995", "v1", ["a0"], [], "Pixel", made);

        Assert.Contains("heat-1995", saves.Holding(made.AddHours(47)));
        Assert.Empty(saves.Holding(made.AddHours(48)));

        // Read back from what was kept, it holds the same.
        var reread = new OfflineSaves(new CinemaDatabase(path), new Monitor(new OfflineOptions { HoldHours = 48 }));
        Assert.Contains("heat-1995", reread.Holding(made.AddHours(1)));
    }

    [Fact]
    public async Task Progress_from_a_device_is_recorded_where_it_is_newer()
    {
        var client = fixture.CreatePersonClient("o9");
        var earlier = DateTimeOffset.UtcNow.AddHours(-2);

        using var first = await client.PostAsJsonAsync("/api/me/progress",
            new[] { new { titleId = SessionFixture.ReadyTitle, positionSeconds = 1200.0, watchedAt = earlier } });
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        // An older report, and one for a title the library does not hold, change nothing.
        using var older = await client.PostAsJsonAsync("/api/me/progress", new[]
        {
            new { titleId = SessionFixture.ReadyTitle, positionSeconds = 60.0, watchedAt = earlier.AddHours(-1) },
            new { titleId = "never-here", positionSeconds = 60.0, watchedAt = earlier },
        });
        Assert.Equal(HttpStatusCode.NoContent, older.StatusCode);

        var progress = await client.GetFromJsonAsync<List<Core.ProgressEntry>>("/api/me/progress", Json);
        Assert.Equal(1200, progress!.Single(p => p.TitleId == SessionFixture.ReadyTitle).PositionSeconds);

        using var newer = await client.PostAsJsonAsync("/api/me/progress",
            new[] { new { titleId = SessionFixture.ReadyTitle, positionSeconds = 2400.0, watchedAt = earlier.AddHours(1) } });
        progress = await client.GetFromJsonAsync<List<Core.ProgressEntry>>("/api/me/progress", Json);
        Assert.Equal(2400, progress!.Single(p => p.TitleId == SessionFixture.ReadyTitle).PositionSeconds);

        using var negative = await client.PostAsJsonAsync("/api/me/progress",
            new[] { new { titleId = SessionFixture.ReadyTitle, positionSeconds = -1.0, watchedAt = earlier } });
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
    }

    [Fact]
    public async Task A_title_still_being_made_is_saved_as_it_grows()
    {
        fixture.Growing("growing-a", "tt9000001");
        var client = fixture.CreatePersonClient("o10");

        var offer = await client.GetFromJsonAsync<OfflineOffer>("/api/titles/growing-a/offline", Json);
        Assert.True(offer!.Growing);
        Assert.Equal(Joined("v0", "growing-a").LongLength, offer.Rungs.Single().Bytes);

        using var asked = await client.PostAsJsonAsync("/api/me/offline",
            new { titleId = "growing-a", rung = "v0", audio = new[] { "a0" }, device = "Pixel" });
        asked.EnsureSuccessStatusCode();
        var package = (await asked.Content.ReadFromJsonAsync<OfflinePackage>(Json))!;
        Assert.True(package.Growing);
        var track = package.Tracks[0];
        Assert.Equal([2.002, 2.002], track.SegmentSeconds);

        using var first = await client.GetAsync(track.Url);
        var held = await first.Content.ReadAsByteArrayAsync();
        Assert.Equal(track.Bytes, held.LongLength);

        fixture.Grow("growing-a", "v0", seed: 9);

        // What the device holds is how the track begins now, so it carries on from there.
        using var more = new HttpRequestMessage(HttpMethod.Get, track.Url);
        more.Headers.Range = new RangeHeaderValue(held.Length, null);
        more.Headers.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue(track.Etag));
        using var rest = await client.SendAsync(more);
        Assert.Equal(HttpStatusCode.PartialContent, rest.StatusCode);
        Assert.EndsWith("/*", rest.Content.Headers.ContentRange?.ToString());
        Assert.NotEqual(track.Etag, rest.Headers.ETag?.ToString());
        Assert.Equal(Joined("v0", "growing-a"), held.Concat(await rest.Content.ReadAsByteArrayAsync()).ToArray());

        // A stretch of something else is answered with the whole track.
        using var other = new HttpRequestMessage(HttpMethod.Get, track.Url);
        other.Headers.Range = new RangeHeaderValue(held.Length, null);
        other.Headers.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue("\"0123456789abcdef0123456789abcdef-2\""));
        using var whole = await client.SendAsync(other);
        Assert.Equal(HttpStatusCode.OK, whole.StatusCode);
    }

    [Fact]
    public async Task A_film_the_library_holds_is_ready_to_download_at_once_and_the_want_goes_once_the_device_saves_it()
    {
        fixture.Growing("growing-b", "tt9000002");
        var client = fixture.CreatePersonClient("o11");

        using var asked = await client.PostAsJsonAsync("/api/me/offline/wants", new { imdbId = "tt9000002", height = 480, device = "Pixel" });
        asked.EnsureSuccessStatusCode();
        var want = Assert.Single((await asked.Content.ReadFromJsonAsync<List<OfflineWant>>(Json))!);
        Assert.Equal(WantStages.Ready, want.Stage);
        Assert.Equal("growing-b", want.TitleId);
        Assert.Equal(480, want.Height);

        // Another device saving it leaves this device waiting; this device saving it ends the wait.
        using var elsewhere = await client.PostAsJsonAsync("/api/me/offline",
            new { titleId = "growing-b", rung = "v0", audio = new[] { "a0" }, device = "Laptop" });
        elsewhere.EnsureSuccessStatusCode();
        Assert.Single((await client.GetFromJsonAsync<List<OfflineWant>>("/api/me/offline/wants", Json))!);

        using var here = await client.PostAsJsonAsync("/api/me/offline",
            new { titleId = "growing-b", rung = "v0", audio = new[] { "a0" }, device = "Pixel" });
        here.EnsureSuccessStatusCode();
        Assert.Empty((await client.GetFromJsonAsync<List<OfflineWant>>("/api/me/offline/wants", Json))!);
    }

    [Fact]
    public async Task A_download_not_in_the_library_is_estimated_from_its_runtime_and_what_the_library_takes()
    {
        var client = fixture.CreatePersonClient("o16");
        var estimates = fixture.Services.GetRequiredService<OfflineEstimates>();

        var before = (await client.GetFromJsonAsync<OfflineEstimate>("/api/offline/estimate?imdbId=tt0113277", Json))!;
        Assert.Equal("en", before.OriginalLanguage);
        Assert.Equal([1080, 720, 480], before.Sizes.Select(s => s.Height));

        estimates.Measure(shortest: 1);
        var after = (await client.GetFromJsonAsync<OfflineEstimate>("/api/offline/estimate?imdbId=tt0113277", Json))!;
        // The saved film's 1920 rung stands for 1080p and its 1280 rung for 720p and 480p, with its
        // default audio, a second of it scaled to Heat's 170 minutes.
        var indexes = fixture.Services.GetRequiredService<TrackIndexes>();
        var directory = Path.Combine(fixture.MediaRoot, Film);
        long Track(string uri) => indexes.Of(directory, uri)!.Length;
        long Expected(string rung) => (long)((Track(rung) + Track("a0/index.m3u8")) / 4.504 * 170 * 60);
        Assert.Equal(Expected("v0/index.m3u8"), after.Sizes[0].Bytes);
        Assert.Equal(Expected("v1/index.m3u8"), after.Sizes[1].Bytes);
        Assert.Equal(Expected("v1/index.m3u8"), after.Sizes[2].Bytes);

        using var bad = await client.GetAsync("/api/offline/estimate?imdbId=tt0113277&season=1&episodes=x");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task A_save_asked_with_a_subtitle_language_takes_the_title_s_subtitles_in_it()
    {
        var client = fixture.CreatePersonClient("o17");
        using var saved = await client.PostAsJsonAsync("/api/me/offline",
            new { titleId = Film, rung = "v1", audio = new[] { "a0" }, subtitles = Array.Empty<string>(), device = "Pixel", subtitleLanguage = "en" });
        saved.EnsureSuccessStatusCode();
        var package = (await saved.Content.ReadFromJsonAsync<OfflinePackage>(Json))!;
        Assert.Equal(["s3"], package.Subtitles.Select(s => s.Id));
        Assert.Equal("en", package.Save.SubtitleLanguage);

        using var bad = await client.PostAsJsonAsync("/api/me/offline",
            new { titleId = Film, rung = "v1", audio = new[] { "a0" }, device = "Pixel", subtitleLanguage = "en; drop" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task A_want_carries_the_name_of_the_film_as_described_here()
    {
        var client = fixture.CreatePersonClient("o15");

        using var asked = await client.PostAsJsonAsync("/api/me/offline/wants",
            new { imdbId = "tt0113277", device = "Pixel", audioLanguage = "es", subtitleLanguage = "en" });
        asked.EnsureSuccessStatusCode();
        var want = Assert.Single((await asked.Content.ReadFromJsonAsync<List<OfflineWant>>(Json))!);
        Assert.Equal("Heat", want.Name);
        Assert.Equal("es", want.AudioLanguage);
        Assert.Equal("en", want.SubtitleLanguage);

        using var gone = await client.DeleteAsync($"/api/me/offline/wants/{want.Id}");
        Assert.Equal(HttpStatusCode.NoContent, gone.StatusCode);
    }

    [Fact]
    public async Task A_want_takes_downloading_and_is_the_person_s_own_to_let_go()
    {
        fixture.Growing("growing-c", "tt9000003");
        var saver = fixture.CreatePersonClient("o12",
            actions: [CinemaActions.Watch, CinemaActions.Offline, CinemaActions.ProfileRead, CinemaActions.ProfileWrite]);
        using var refused = await saver.PostAsJsonAsync("/api/me/offline/wants", new { imdbId = "tt9000003", device = "Pixel" });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        var client = fixture.CreatePersonClient("o13");
        using var bad = await client.PostAsJsonAsync("/api/me/offline/wants", new { imdbId = "nope", device = "Pixel" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        using var asked = await client.PostAsJsonAsync("/api/me/offline/wants", new { imdbId = "tt9000003", device = "Pixel" });
        var want = Assert.Single((await asked.Content.ReadFromJsonAsync<List<OfflineWant>>(Json))!);
        Assert.Empty((await fixture.CreatePersonClient("o14").GetFromJsonAsync<List<OfflineWant>>("/api/me/offline/wants", Json))!);

        using var gone = await client.DeleteAsync($"/api/me/offline/wants/{want.Id}");
        Assert.Equal(HttpStatusCode.NoContent, gone.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<OfflineWant>>("/api/me/offline/wants", Json))!);
    }

    [Fact]
    public void A_want_is_waited_on_for_its_days_and_one_that_failed_is_asked_for_afresh()
    {
        var path = Path.Combine(fixture.StateRoot, $"wants-{Guid.NewGuid():n}.db");
        var saves = new OfflineSaves(new CinemaDatabase(path), new Monitor(new OfflineOptions { WantDays = 14 }));
        var made = DateTimeOffset.UtcNow;
        var season = new List<EpisodePick> { new(1, 1), new(1, 2) };

        var wants = saves.Want("p", "tt0903747", season, "Pixel", new WantPick(null, null, "en"), made);
        Assert.Equal(2, wants.Count);
        saves.Fetched("p", wants[0].Id, null, WantStages.NotOffered);
        saves.Fetched("p", wants[1].Id, "abc");

        var again = saves.Want("p", "tt0903747", season, "Pixel", new WantPick(480, "es", "en"), made.AddHours(1));
        Assert.Equal(wants.Select(w => w.Id), again.Select(w => w.Id));
        Assert.Null(again[0].Problem);
        Assert.Equal("abc", again[1].Hash);
        Assert.All(again, w => Assert.Equal(480, w.Height));
        Assert.All(again, w => Assert.Equal("es", w.AudioLanguage));
        Assert.All(again, w => Assert.Equal("en", w.SubtitleLanguage));

        var reread = new OfflineSaves(new CinemaDatabase(path), new Monitor(new OfflineOptions { WantDays = 14 }));
        Assert.Equal(2, reread.WantsOf("p", made.AddDays(13)).Count);
        // The episode asked for afresh counts its days from then.
        Assert.Single(reread.WantsOf("p", made.AddDays(14).AddMinutes(30)));
    }

    [Theory]
    [InlineData(null, 100, null, null, true)]
    [InlineData("bytes=0-", 100, 0L, 99L, true)]
    [InlineData("bytes=10-19", 100, 10L, 19L, true)]
    [InlineData("bytes=90-500", 100, 90L, 99L, true)]
    [InlineData("bytes=-30", 100, 70L, 99L, true)]
    [InlineData("bytes=-500", 100, 0L, 99L, true)]
    [InlineData("bytes=100-", 100, null, null, false)]
    [InlineData("bytes=0-1,5-6", 100, null, null, true)]
    [InlineData("items=0-1", 100, null, null, true)]
    [InlineData("bytes=20-10", 100, null, null, true)]
    public void A_range_is_read_against_the_track_s_length(string? header, long length, long? start, long? end, bool satisfiable)
    {
        var range = ByteRange.Parse(header, length, out var ok);

        Assert.Equal(satisfiable, ok);
        Assert.Equal(start, range?.Start);
        Assert.Equal(end, range?.End);
    }

    [Fact]
    public void An_offset_is_found_in_the_file_that_holds_it()
    {
        var index = new TrackIndex("v0", ["init", "s0", "s1"], [10, 100, 50], [0, 10, 110], [2, 2], 160, "\"x\"");

        Assert.Equal(0, index.FileAt(0));
        Assert.Equal(0, index.FileAt(9));
        Assert.Equal(1, index.FileAt(10));
        Assert.Equal(1, index.FileAt(109));
        Assert.Equal(2, index.FileAt(110));
        Assert.Equal(2, index.FileAt(159));
    }

    [Fact]
    public void A_playlist_that_does_not_end_or_leaves_its_directory_is_no_track()
    {
        var directory = Path.Combine(fixture.MediaRoot, Film, "v1");
        var playlist = File.ReadAllText(Path.Combine(directory, "index.m3u8"));

        Assert.NotNull(TrackIndex.Read(directory, "v1", playlist));
        Assert.Null(TrackIndex.Read(directory, "v1", playlist.Replace("#EXT-X-ENDLIST", "")));
        // A title still being made reads the playlist as far as it has got.
        Assert.False(TrackIndex.Read(directory, "v1", playlist.Replace("#EXT-X-ENDLIST", ""), growing: true)!.Ended);
        Assert.Null(TrackIndex.Read(directory, "v1", playlist.Replace("seg00001.m4s", "../a0/seg00001.m4s")));
    }

    [Theory]
    [InlineData(0, 0, 1000, 0)]
    [InlineData(2_500_000, 500, 2_500_000, 500)]
    [InlineData(2_500_000, 1500, 2_500_000, 0)]
    [InlineData(5_000_000, 0, 0, 0)]
    public void A_transfer_waits_for_the_time_its_bytes_were_owed(long sent, int elapsedMs, long rate, int waitMs)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(waitMs), Transfers.Wait(sent, TimeSpan.FromMilliseconds(elapsedMs), rate));
    }

    private sealed class Monitor(OfflineOptions value) : IOptionsMonitor<OfflineOptions>
    {
        public OfflineOptions CurrentValue => value;
        public OfflineOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<OfflineOptions, string?> listener) => null;
    }
}
