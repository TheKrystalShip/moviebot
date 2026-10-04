using Microsoft.Extensions.Logging.Abstractions;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.MovieBot.Handoff;
using TheKrystalShip.MovieBot.Ingest.Pipeline;
using TheKrystalShip.MovieBot.Ingest.Probe;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Where a transcode is written: what it is expected to take, which disk has the room, and what
/// stops a finished film being moved under a viewer or onto a disk being written to.
/// </summary>
public sealed class TranscodePlacementTests : IDisposable
{
    private const long GiB = 1L << 30;

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "moviebot-placement-tests", Guid.NewGuid().ToString("n"));

    private string Hot => Path.Combine(_root, "media");
    private string Cold => Path.Combine(_root, "cold");

    public TranscodePlacementTests()
    {
        Directory.CreateDirectory(Hot);
        Directory.CreateDirectory(Cold);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static VolumeRoom Room(double freeGiB, double transcodeGiB = 0, double downloadGiB = 0) =>
        new((long)(freeGiB * GiB), (long)(transcodeGiB * GiB), (long)(downloadGiB * GiB));

    // ── the estimate ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_transcode_is_every_streams_bitrate_times_the_running_time_plus_the_overhead()
    {
        // 9 + 4 Mbit/s of video and 256 kbit/s of audio for an hour.
        var bytes = TranscodeEstimate.Bytes(3600, [9000, 4000], [256]);

        Assert.Equal((long)((9000 + 4000 + 256) * 3600L * 1000 / 8 * TranscodeEstimate.Overhead), bytes);
        Assert.InRange(bytes / (double)GiB, 6.0, 6.3);
    }

    [Fact]
    public void A_source_with_no_running_time_is_reserved_for_a_long_film()
    {
        var unknown = TranscodeEstimate.Bytes(0, [9000], [256]);

        Assert.Equal(TranscodeEstimate.Bytes(TimeSpan.FromHours(3).TotalSeconds, [9000], [256]), unknown);
    }

    private static ProbeResult Probe(int width, int height, double seconds, params ProbeStream[] audio) => new()
    {
        Format = new ProbeFormat { Duration = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        Streams =
        [
            new ProbeStream { Index = 0, CodecType = "video", Width = width, Height = height },
            .. audio
        ]
    };

    private static ProbeStream Audio(int index, bool commentary = false) => new()
    {
        Index = index,
        CodecType = "audio",
        Disposition = commentary ? new Dictionary<string, int> { ["comment"] = 1 } : null
    };

    private static IngestOptions Options(bool boost = false) => new()
    {
        SourcePath = "film.mkv",
        OutputRoot = "/media",
        DialogueBoost = boost
    };

    [Fact]
    public void A_source_wider_than_the_step_down_is_reserved_for_both_rungs()
    {
        var wide = TranscodeEstimate.For(Probe(1920, 1040, 3600, Audio(1)), Options());
        var narrow = TranscodeEstimate.For(Probe(1280, 720, 3600, Audio(1)), Options());

        Assert.Equal(TranscodeEstimate.Bytes(3600, [9000, 4000], [256]), wide);
        Assert.Equal(TranscodeEstimate.Bytes(3600, [9000], [256]), narrow);
    }

    [Fact]
    public void Commentary_costs_its_own_rate_and_the_dialogue_boost_adds_a_rendition_of_every_other_track()
    {
        var probe = Probe(1280, 720, 3600, Audio(1), Audio(2, commentary: true));

        Assert.Equal(TranscodeEstimate.Bytes(3600, [9000], [256, 128]), TranscodeEstimate.For(probe, Options()));
        Assert.Equal(
            TranscodeEstimate.Bytes(3600, [9000], [256, 256, 128]),
            TranscodeEstimate.For(probe, Options(boost: true)));
    }

    // ── the decision ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_transcode_that_fits_the_media_root_is_written_there()
    {
        var placement = TranscodePlacement.Decide(6 * GiB, Room(freeGiB: 40), Room(freeGiB: 500));

        Assert.Equal(Disk.Media, placement.Disk);
    }

    [Fact]
    public void The_margin_is_kept_free_past_the_reservation()
    {
        Assert.Equal(Disk.Cold, TranscodePlacement.Decide(6 * GiB, Room(freeGiB: 6.5), Room(freeGiB: 500)).Disk);
        Assert.Equal(Disk.Media, TranscodePlacement.Decide(6 * GiB, Room(freeGiB: 7.5), Room(freeGiB: 500)).Disk);
    }

    [Fact]
    public void Downloads_still_arriving_take_the_media_roots_room_before_a_transcode_does()
    {
        // 40 GiB free looks like plenty until a 35 GiB download has still to land.
        var placement = TranscodePlacement.Decide(
            6 * GiB, Room(freeGiB: 40, downloadGiB: 35), Room(freeGiB: 500));

        Assert.Equal(Disk.Cold, placement.Disk);
    }

    [Fact]
    public void Another_transcodes_unwritten_remainder_is_not_given_away_twice()
    {
        var first = TranscodePlacement.Decide(6 * GiB, Room(freeGiB: 10), null);
        Assert.Equal(Disk.Media, first.Disk);

        // With the first one's claim recorded, the second no longer fits the same 10 GiB.
        var second = TranscodePlacement.Decide(6 * GiB, Room(freeGiB: 10, transcodeGiB: 6), null);
        Assert.Null(second.Disk);
    }

    [Fact]
    public void A_film_with_no_room_anywhere_waits_and_says_why()
    {
        var placement = TranscodePlacement.Decide(6 * GiB, Room(freeGiB: 3), Room(freeGiB: 2));

        Assert.False(placement.Placed);
        Assert.Contains("needs 6 GiB", placement.Reason);
        Assert.Contains("cold storage has", placement.Reason);
    }

    [Fact]
    public void Without_a_cold_volume_a_full_media_root_means_waiting()
    {
        var placement = TranscodePlacement.Decide(6 * GiB, Room(freeGiB: 3), cold: null);

        Assert.False(placement.Placed);
        Assert.Contains("no cold storage", placement.Reason);
    }

    // ── the settle gate ─────────────────────────────────────────────────────────────────────────

    private static RoomSummary Room(string titleId, int participants, bool paused = false) => new()
    {
        SessionId = Guid.NewGuid().ToString("n"),
        TitleId = titleId,
        Participants = participants,
        Paused = paused
    };

    [Fact]
    public void A_room_with_somebody_in_it_is_playing_its_film_whether_or_not_it_is_paused()
    {
        var playing = OccupiedRooms.Playing([
            Room("heat-1995", participants: 2),
            Room("alien-1979", participants: 1, paused: true),
            Room("collateral-2004", participants: 0),
            new RoomSummary { SessionId = "empty", Participants = 3 }
        ]);

        Assert.Equal(["alien-1979", "heat-1995"], playing.Order().ToArray());
    }

    [Fact]
    public void A_room_nobody_is_in_holds_its_film_without_playing_it()
    {
        var rooms = new[] { Room("heat-1995", participants: 0) };

        Assert.Contains("heat-1995", OccupiedRooms.Held(rooms));
        Assert.Empty(OccupiedRooms.Playing(rooms));
    }

    [Fact]
    public void A_finished_film_nobody_is_playing_may_settle_and_one_somebody_is_playing_may_not()
    {
        var owed = SettleGate.Owed(["heat-1995", "alien-1979"], new HashSet<string> { "heat-1995" });

        Assert.Equal(["alien-1979"], owed);
    }

    [Fact]
    public void Nothing_settles_when_the_rooms_could_not_be_read()
    {
        Assert.Empty(SettleGate.Owed(["heat-1995", "alien-1979"], playing: null));
    }

    [Fact]
    public void A_film_held_back_settles_once_nobody_is_playing_it()
    {
        string[] finished = ["heat-1995"];

        Assert.Empty(SettleGate.Owed(finished, new HashSet<string> { "heat-1995" }));
        Assert.Equal(finished, SettleGate.Owed(finished, new HashSet<string>()));
    }

    // ── a title is under one root ───────────────────────────────────────────────────────────────

    private void Mount() => File.WriteAllText(Path.Combine(Cold, MediaRoots.ColdMarker), "");

    private ColdStore Store(MediaRoots roots) => new(roots, NullLogger<ColdStore>.Instance);

    [Fact]
    public void Making_a_film_on_the_cold_root_clears_its_copy_on_the_media_root_first()
    {
        Mount();
        var roots = new MediaRoots(Hot, Cold);
        Directory.CreateDirectory(Path.Combine(Hot, "heat-1995", "v0"));
        File.WriteAllText(Path.Combine(Hot, "heat-1995", "v0", "0.m4s"), "old");

        Assert.True(Store(roots).ClearWorking("heat-1995"));

        Assert.False(Directory.Exists(Path.Combine(Hot, "heat-1995")));
    }

    [Fact]
    public void Nothing_is_removed_from_the_media_root_while_the_cold_volume_is_out()
    {
        // No marker: the volume is not mounted.
        var roots = new MediaRoots(Hot, Cold);
        Directory.CreateDirectory(Path.Combine(Hot, "heat-1995"));

        Assert.False(Store(roots).ClearWorking("heat-1995"));

        Assert.True(Directory.Exists(Path.Combine(Hot, "heat-1995")));
    }

    [Fact]
    public void The_ledger_directory_under_the_media_root_is_not_a_title()
    {
        var ledger = new TranscodeLedger(
            Microsoft.Extensions.Options.Options.Create(new TranscodeLedgerOptions { MediaRoot = Hot }),
            NullLogger<TranscodeLedger>.Instance);
        Directory.CreateDirectory(Path.Combine(Hot, "heat-1995"));

        Assert.True(ledger.Reserve(new TranscodeReservation
        {
            Key = "k", Root = Hot, TitleDirectory = Path.Combine(Hot, "heat-1995"), Bytes = GiB
        }));

        Assert.True(Directory.Exists(Path.Combine(Hot, TranscodeLedger.FolderName)));
        Assert.Equal(["heat-1995"], new MediaRoots(Hot).Ids());
    }

    [Fact]
    public void A_title_being_written_to_the_cold_root_is_found_there_and_listed_once()
    {
        Mount();
        var roots = new MediaRoots(Hot, Cold);
        Directory.CreateDirectory(Path.Combine(Cold, "heat-1995", "v0"));
        File.WriteAllText(Path.Combine(Cold, "heat-1995", "manifest.json"), """{ "status": "transcoding" }""");

        Assert.Equal(Path.Combine(Cold, "heat-1995"), roots.DirectoryOf("heat-1995"));
        Assert.Equal(["heat-1995"], roots.Ids());
    }
}
