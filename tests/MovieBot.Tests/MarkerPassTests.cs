using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.MovieBot.Handoff;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// What is written into a manifest and where the fingerprints are kept, against a temporary media
/// root. Fingerprints are seeded into the store, so nothing here runs chromaprint.
/// </summary>
public sealed class MarkerPassTests : IDisposable
{
    private const string Show = "tt5875444";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "moviebot-marker-tests", Guid.NewGuid().ToString("n"));

    private readonly MediaRoots _roots;
    private readonly FingerprintStore _store;
    private readonly MarkerPass _pass;

    public MarkerPassTests()
    {
        Directory.CreateDirectory(_root);
        _roots = new MediaRoots(_root);
        _store = new FingerprintStore(_roots);
        _pass = new MarkerPass(_roots, _store, new TitleLocks(), NullLogger<MarkerPass>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string PathOf(string id) => Path.Combine(_root, id, "manifest.json");

    private Manifest Write(
        int number, TitleStatus status = TitleStatus.Ready, IReadOnlyList<Chapter>? chapters = null,
        IReadOnlyList<Marker>? markers = null, bool episode = true, string? subtitles = null, bool forced = false)
    {
        var id = episode ? $"slow-horses-s05e0{number}" : "heat-1995";
        var manifest = new Manifest
        {
            Id = id,
            Title = id,
            DurationSeconds = 2400,
            Status = status,
            Kind = episode ? TitleKind.Show : TitleKind.Film,
            Episode = episode ? new EpisodeIdentity { Season = 5, Number = number } : null,
            Film = new FilmIdentity { ImdbId = episode ? Show : "tt0113277", Name = "Slow Horses" },
            Video = new VideoInfo { Width = 1280, Height = 640, SourceCodec = "h264", SourceHdr = "sdr", Renditions = [] },
            Audio = [new AudioTrack { Id = "a0", Kind = TrackKind.Feature, Language = "eng", Label = "English", Channels = 2, Uri = "a0/index.m3u8" }],
            Subtitles = subtitles is null ? [] :
            [
                new SubtitleTrack
                {
                    Id = "s3", Kind = TrackKind.Feature, Language = "eng", Label = "English", Forced = forced,
                    Source = SubtitleSource.EmbeddedText, Available = true, Uri = "s3.vtt"
                }
            ],
            Chapters = chapters ?? [],
            Markers = markers ?? []
        };

        Directory.CreateDirectory(Path.Combine(_root, id));
        if (subtitles is not null) File.WriteAllText(Path.Combine(_root, id, "s3.vtt"), subtitles);
        ManifestJson.WriteAtomic(PathOf(id), manifest);
        return manifest;
    }

    private Manifest Read(int number) =>
        ManifestJson.Deserialize(File.ReadAllText(PathOf($"slow-horses-s05e0{number}")))!;

    private void Keep(int number) =>
        _store.Save(Show, new StoredFingerprint(
            $"slow-horses-s05e0{number}", 5, number, AudioMarkerTests.Episode(number)));

    private static readonly Chapter[] ThirdEpisodeChapters =
    [
        new() { StartSeconds = 0, Title = "Chapter 1" },
        new() { StartSeconds = 196.125, Title = "Intro" },
        new() { StartSeconds = 230.583, Title = "Chapter 2" }
    ];

    [Fact]
    public void Markers_survive_a_round_trip_through_the_manifest()
    {
        var manifest = Write(2, markers:
        [
            new Marker { Kind = MarkerKind.Recap, StartSeconds = 0, EndSeconds = 61.5, From = MarkerSource.Audio },
            new Marker { Kind = MarkerKind.Intro, StartSeconds = 488.999, EndSeconds = 523.458, From = MarkerSource.Chapter }
        ]);

        var json = ManifestJson.Serialize(manifest);
        using var document = JsonDocument.Parse(json);
        var intro = document.RootElement.GetProperty("markers")[1];

        Assert.Equal("intro", intro.GetProperty("kind").GetString());
        Assert.Equal(488.999, intro.GetProperty("startSeconds").GetDouble());
        Assert.Equal(523.458, intro.GetProperty("endSeconds").GetDouble());
        Assert.Equal("chapter", intro.GetProperty("from").GetString());
        Assert.Equal("recap", document.RootElement.GetProperty("markers")[0].GetProperty("kind").GetString());
        Assert.Equal("audio", document.RootElement.GetProperty("markers")[0].GetProperty("from").GetString());

        Assert.Equal(manifest.Markers, ManifestJson.Deserialize(json)!.Markers);
    }

    [Fact]
    public void A_manifest_written_before_markers_existed_reads_with_none()
    {
        var json = ManifestJson.Serialize(Write(2));
        var without = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
        without.Remove("markers");

        Assert.Empty(ManifestJson.Deserialize(JsonSerializer.Serialize(without))!.Markers);
    }

    [Fact]
    public async Task A_chapter_that_names_the_intro_is_the_marker()
    {
        Write(3, chapters: ThirdEpisodeChapters);

        await _pass.RunAsync("slow-horses-s05e03", reuseStored: true, CancellationToken.None);

        var marker = Assert.Single(Read(3).Markers);
        Assert.Equal(MarkerKind.Intro, marker.Kind);
        Assert.Equal(196.125, marker.StartSeconds);
        Assert.Equal(230.583, marker.EndSeconds);
        Assert.Equal(MarkerSource.Chapter, marker.From);
    }

    [Fact]
    public async Task The_audio_finds_the_intro_and_gives_it_to_the_episode_it_was_found_from()
    {
        Write(1);
        Write(2);
        Keep(1);
        Keep(2);

        await _pass.RunAsync("slow-horses-s05e02", reuseStored: true, CancellationToken.None);

        var second = Assert.Single(Read(2).Markers);
        Assert.Equal(MarkerKind.Intro, second.Kind);
        Assert.Equal(MarkerSource.Audio, second.From);
        Assert.InRange(second.StartSeconds, 488.5, 489.5);
        Assert.InRange(second.EndSeconds, 533.1, 534.1);

        var first = Assert.Single(Read(1).Markers);
        Assert.Equal(MarkerKind.Intro, first.Kind);
        Assert.Equal(MarkerSource.Audio, first.From);
        Assert.InRange(first.StartSeconds, 238.5, 240);
    }

    [Fact]
    public async Task An_intro_already_marked_is_not_replaced_by_the_audio_of_another_episode()
    {
        Write(2);
        Write(3, chapters: ThirdEpisodeChapters);
        Keep(2);
        Keep(3);
        await _pass.RunAsync("slow-horses-s05e03", reuseStored: true, CancellationToken.None);

        await _pass.RunAsync("slow-horses-s05e02", reuseStored: true, CancellationToken.None);

        var third = Assert.Single(Read(3).Markers);
        Assert.Equal(MarkerSource.Chapter, third.From);
        Assert.Equal(196.125, third.StartSeconds);
    }

    [Fact]
    public async Task A_chapter_intro_is_not_searched_for_in_the_audio()
    {
        Write(2);
        Write(3, chapters: ThirdEpisodeChapters);
        Keep(2);
        Keep(3);

        await _pass.RunAsync("slow-horses-s05e03", reuseStored: true, CancellationToken.None);

        // The chapter is taken as it stands, and the audio is not consulted for a kind it names.
        Assert.Equal(230.583, Assert.Single(Read(3).Markers).EndSeconds);
    }

    private static string Vtt(params (string Start, string Text)[] cues) =>
        "WEBVTT\n\n" + string.Concat(cues.Select(c => $"{c.Start} --> {c.Start[..^1]}9\n{c.Text}\n\n"));

    [Fact]
    public async Task A_chapter_with_speech_in_it_is_cut_short_of_the_line()
    {
        Write(3, chapters: ThirdEpisodeChapters, subtitles: Vtt(("03:35.000", "Where were you?")));

        await _pass.RunAsync("slow-horses-s05e03", reuseStored: true, CancellationToken.None);

        var marker = Assert.Single(Read(3).Markers);
        Assert.Equal(196.125, marker.StartSeconds);
        Assert.Equal(214.5, marker.EndSeconds);
        Assert.Equal(MarkerSource.Chapter, marker.From);
    }

    [Fact]
    public async Task A_chapter_with_speech_leaving_too_little_is_dropped()
    {
        Write(3, chapters: ThirdEpisodeChapters, subtitles: Vtt(("03:20.000", "Where were you?")));

        await _pass.RunAsync("slow-horses-s05e03", reuseStored: true, CancellationToken.None);

        Assert.Empty(Read(3).Markers);
    }

    [Fact]
    public async Task Music_cues_and_a_forced_track_do_not_trim_a_sequence()
    {
        Write(3, chapters: ThirdEpisodeChapters, subtitles: Vtt(("03:30.000", "[\"Strange Game\" playing]"), ("03:40.000", "♪ strange game ♪")));
        await _pass.RunAsync("slow-horses-s05e03", reuseStored: true, CancellationToken.None);
        Assert.Equal(230.583, Assert.Single(Read(3).Markers).EndSeconds);

        Write(3, chapters: ThirdEpisodeChapters, subtitles: Vtt(("03:30.000", "Where were you?")), forced: true);
        await _pass.RunAsync("slow-horses-s05e03", reuseStored: true, CancellationToken.None);
        Assert.Equal(230.583, Assert.Single(Read(3).Markers).EndSeconds);
    }

    [Fact]
    public async Task The_audio_intro_is_cut_short_of_a_line_and_so_is_the_one_given_to_the_other_episode()
    {
        // 520 s is inside the second episode's sequence (489 to 534 s) and 250 s is inside the first's.
        Write(1, subtitles: Vtt(("04:10.000", "Where were you?")));
        Write(2, subtitles: Vtt(("08:40.000", "Where were you?")));
        Keep(1);
        Keep(2);

        await _pass.RunAsync("slow-horses-s05e02", reuseStored: true, CancellationToken.None);

        var second = Assert.Single(Read(2).Markers);
        Assert.Equal(519.5, second.EndSeconds);

        // 239 s to 249.5 s is under the shortest sequence, so the first episode is given nothing.
        Assert.Empty(Read(1).Markers);
    }

    [Fact]
    public async Task A_partner_is_given_the_sequence_cut_short_when_enough_is_left()
    {
        Write(1, subtitles: Vtt(("04:35.000", "Where were you?")));
        Write(2);
        Keep(1);
        Keep(2);

        await _pass.RunAsync("slow-horses-s05e02", reuseStored: true, CancellationToken.None);

        var first = Assert.Single(Read(1).Markers);
        Assert.Equal(274.5, first.EndSeconds);
    }

    [Fact]
    public async Task An_episode_alone_in_its_show_has_nothing_to_be_compared_with()
    {
        Write(2);
        Keep(2);

        await _pass.RunAsync("slow-horses-s05e02", reuseStored: true, CancellationToken.None);

        Assert.Empty(Read(2).Markers);
    }

    [Fact]
    public async Task Only_the_markers_of_the_manifest_are_changed()
    {
        Write(1);
        Write(2);
        Keep(1);
        Keep(2);
        var before = Read(2);

        await _pass.RunAsync("slow-horses-s05e02", reuseStored: true, CancellationToken.None);

        var after = Read(2);
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.Title, after.Title);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.DurationSeconds, after.DurationSeconds);
        Assert.Equal(before.Audio.Count, after.Audio.Count);
        Assert.Equal(before.Film, after.Film);
        Assert.Equal(before.Episode, after.Episode);
        Assert.False(File.Exists(PathOf("slow-horses-s05e02") + ".tmp"));
    }

    [Fact]
    public async Task What_an_earlier_pass_found_stays_when_this_one_finds_nothing()
    {
        var kept = new Marker { Kind = MarkerKind.Intro, StartSeconds = 100, EndSeconds = 140, From = MarkerSource.Audio };
        Write(2, markers: [kept]);
        Keep(2);

        await _pass.RunAsync("slow-horses-s05e02", reuseStored: true, CancellationToken.None);

        Assert.Equal([kept], Read(2).Markers);
    }

    [Fact]
    public async Task A_transcode_that_is_not_ready_is_left_alone()
    {
        Write(1);
        Write(2, status: TitleStatus.Transcoding);
        Keep(1);
        Keep(2);

        await _pass.RunAsync("slow-horses-s05e02", reuseStored: true, CancellationToken.None);

        Assert.Empty(Read(2).Markers);
        Assert.Empty(Read(1).Markers);
    }

    [Fact]
    public async Task A_film_has_no_markers()
    {
        Write(1, episode: false, chapters: [new Chapter { StartSeconds = 10, Title = "Intro" }, new Chapter { StartSeconds = 50, Title = "Chapter 2" }]);

        await _pass.RunAsync("heat-1995", reuseStored: true, CancellationToken.None);

        Assert.Empty(ManifestJson.Deserialize(File.ReadAllText(PathOf("heat-1995")))!.Markers);
    }

    [Fact]
    public async Task A_title_that_is_not_in_the_library_is_not_an_error()
    {
        await _pass.RunAsync("slow-horses-s05e09", reuseStored: false, CancellationToken.None);
        await _pass.RunAsync("../escape", reuseStored: false, CancellationToken.None);
    }

    [Fact]
    public void Fingerprints_are_kept_outside_the_titles_and_survive_the_title_going()
    {
        Write(2);
        Keep(2);

        Directory.Delete(Path.Combine(_root, "slow-horses-s05e02"), recursive: true);

        var kept = _store.Load(Show, 5, 2);
        Assert.NotNull(kept);
        Assert.Equal("slow-horses-s05e02", kept.TitleId);
        Assert.Equal(AudioMarkerTests.Episode(2), kept.Items);
        Assert.Equal([(5, 2)], _store.Episodes(Show));
    }

    [Fact]
    public void The_fingerprint_directory_is_not_a_title()
    {
        Write(2);
        Keep(2);

        Assert.True(Directory.Exists(Path.Combine(_root, FingerprintStore.DirectoryName)));
        Assert.Equal(["slow-horses-s05e02"], _roots.Ids());
    }

    [Fact]
    public void An_episode_ingested_again_replaces_its_fingerprint()
    {
        Keep(2);
        _store.Save(Show, new StoredFingerprint("slow-horses-s05e02", 5, 2, [1u, 2u, 3u]));

        Assert.Equal([1u, 2u, 3u], _store.Load(Show, 5, 2)!.Items);
        Assert.Single(_store.Episodes(Show));
    }

    [Fact]
    public void A_file_that_is_not_a_fingerprint_is_none()
    {
        Keep(2);
        File.WriteAllText(Path.Combine(_root, FingerprintStore.DirectoryName, Show, "s05e02.fp"), "not one");

        Assert.Null(_store.Load(Show, 5, 2));
    }

    [Fact]
    public void A_show_is_its_imdb_id_or_the_stem_of_its_title()
    {
        Assert.Equal(Show, FingerprintStore.ShowKey(Write(2)));

        var unidentified = Write(2);
        unidentified.Film = null;
        Assert.Equal("slow-horses", FingerprintStore.ShowKey(unidentified));

        Assert.Null(FingerprintStore.ShowKey(Write(1, episode: false)));
    }

    [Fact]
    public void A_show_key_cannot_name_somewhere_else()
    {
        Assert.Throws<ArgumentException>(() => _store.Load("../outside", 1, 1));
    }

    [Fact]
    public async Task The_backfill_compares_every_episode_with_all_the_others()
    {
        Write(1);
        Write(2);
        Write(3, chapters: ThirdEpisodeChapters);
        Keep(1);
        Keep(2);
        Keep(3);

        var backfill = new MarkerBackfill(_pass, NullLogger<MarkerBackfill>.Instance);
        Assert.Equal(0, await backfill.RunAsync(_roots, CancellationToken.None));

        Assert.All([1, 2, 3], n => Assert.Single(Read(n).Markers, m => m.Kind == MarkerKind.Intro));
        Assert.Equal(MarkerSource.Chapter, Read(3).Markers[0].From);
        Assert.Equal(MarkerSource.Audio, Read(1).Markers[0].From);
        Assert.Equal(MarkerSource.Audio, Read(2).Markers[0].From);
    }
}
