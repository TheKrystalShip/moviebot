# Transcode scheduling, backfill and placement

A plan, not canon. When a phase lands, what it settles moves into the `CLAUDE.md` beside the code
it governs (`src/MovieBot.Ingest/`, `src/MovieBot.Handoff/`, `src/MovieBot.Api/`,
`src/MovieBot.Core/`, `web/site/src/player/`) and its section here is struck.

## 1. What this is for

The card is a fixed resource shared by every transcode, and today it is divided without regard to
who is watching what. This plan makes the GPU's time follow demand:

- a quality somebody is watching is kept comfortably ahead of the room's playhead before anything
  else runs;
- a film a room has just asked for starts at once, never behind a film nobody is watching;
- spare capacity goes first to the unwatched qualities of watched films, then to films nobody holds;
- when no room is playing, the card backfills what the library is missing;
- a transcode is written to the SSD when it fits and to the cold disk when it does not, and a film
  a room is watching is moved only once nobody is.

**Out of scope: the dialogue boost.** It runs on the CPU and pins all four cores for minutes, and
how it is made is being re-evaluated separately. It keeps exactly its current behaviour: made after
the main pass, before the status turns ready. It is not scheduled, not paused and not backfilled by
anything here.

## 2. What was measured

On hotbox (Quadro P2000, one NVENC and one NVDEC engine, no session cap; AMD Athlon X4 860K), with
the hand-off's own ffmpeg (`/opt/ffmpeg-p2000`), 180 s of an 8-bit H.264 1920x1040 source, video
only, `h264_nvenc -preset p5 -cq 19`:

| Configuration | Speed per output | NVENC | NVDEC | CPU (of 4 cores) |
|---|---|---|---|---|
| 1080 alone | 12.2x | 93% | 53% | 73% |
| 720 alone, CPU scale | 11.4x | 42% | 50% | 85% |
| 1080 + 720, today's command (two decodes, CPU scale) | 6.3x | 73% | 57% | 82% |
| 1080 + 720, one decode, `split` | 8.4x | 94% | 36% | 81% |
| 1080 + 720 + 480, one decode, `split` | 6.3x | 82% | 29% | 86% |
| 720 alone, `h264_cuvid -resize` | 20.1x | 56% | 89% | 61% |
| 1080 + 720, separate processes, decoder resize | 9.0x | 95% | 83% | — |
| 1080 + 720 + 480, separate processes, decoder resize | 7.0x | 82% | 96% | — |
| two separate 1080 jobs | 6.3x each | — | — | — |
| 1080, `-preset p3` | 13.1x | 93% | 58% | 51% |

What follows from it:

- **NVENC is the ceiling**, at about 12x realtime of 1080p pixel rate, and concurrent processes
  divide it evenly. Pausing reorders GPU time; it never creates more of it.
- **Today's main pass leaves about a third on the table**: two decodes copying frames to system
  memory and a CPU scale on a four-core machine hold it to 6.3x where the encoder could carry 9x.
- **With decoder-side resizing, NVDEC becomes the second limit** once three processes each decode
  the full source.
- **Production runs slower than these numbers**: 2.7x to 4x for two-rung 1080p films in the
  hand-off's log. Audio encoding shares the CPU, a second ingest usually runs alongside, and a source
  still arriving holds the reader back. Those were not separated, and phase 1 re-measures with audio.
- **Not measured yet**: decoder resize ahead of `tonemap_opencl` on an HDR source; the VRAM and RAM a
  stopped ffmpeg holds; the cold disk under concurrent transcode writes, a settle copy and playback.
  The resize heights in the table were slightly under the source's aspect, so those rows flatter a
  little.
- The ffmpeg build has no `scale_cuda` or `scale_npp`. Decoder resize (`-resize` on the `*_cuvid`
  decoders) is what keeps scaling off the CPU, and it exists only where NVDEC decodes the source.
  H.264 Hi10P is not one of those on Pascal and decodes in software.
- `llama-server` (the assistant's model) shares the card: 1.4 GiB of its 5 GiB VRAM resident. Its
  compute contention with NVENC is small, but its memory is part of the budget a set of paused
  transcodes has to fit in.

## 3. Roles

| Piece | Owns | Never does |
|---|---|---|
| `MovieBot.Api` | **The planner.** Demand (rooms, playheads, what viewers want), what each title is owed, the priority order, placement, when a film may settle. Publishes a desired state. | Run ffmpeg, write the media roots, touch the GPU. |
| `MovieBot.Handoff` | **The executor.** Starts, pauses, resumes and stops work units to match the desired state; reports what is running, at what speed, and how far each output has got. Measures disks and owns every write to the media roots. | Decide priority from rooms. |
| `MovieBot.Ingest` | **A stateless library** performing one unit of work it is handed, with explicit inputs and outputs. | Know about rooms, other units, or the schedule. |

The split follows the library clock already in place: the API decides what is due
(`GET /api/library/due`) and the hand-off acts on it, being the only process allowed to write the
media roots. The API does not run transcodes because it is redeployed more often than anything else,
and a restart would kill every transcode in flight; its unit also has neither the media roots nor
the GPU.

## 4. Desired state, not commands

The API publishes **what should be true**; the hand-off reconciles toward it. A restart of either
side loses nothing, because the next reconcile computes the same answer from the same facts.

- **`GET /api/transcode/plan`**: an ordered list of work units, each with its id, its kind and
  inputs (section 5), a disposition (`run`, `hold`, `park`) and a placement for new output. Order
  is priority; the executor runs from the top within its measured budget.
- **`PUT /api/transcode/status`**: the executor's report, sent each reconcile: every unit it knows,
  its state (`running`, `held`, `parked`, `done`, `failed` with the reason), its live speed from
  ffmpeg's progress, each output's head in seconds, the disk it writes to, and the disks' readings.
  The planner reads heads and speeds from here, not from guesses.
- **Cadence.** The hand-off reconciles every 2 to 5 s while anything is running or any room is
  playing, and on its existing poll otherwise. A room starting a film is the case latency matters
  for; if polling proves too slow there, the API pushes a nudge and the plan is still fetched, never
  carried in the nudge.
- **When the API cannot answer, the hand-off falls back to today's behaviour**: new downloads are
  ingested up to a fixed ceiling, everything running keeps running, nothing new is held. This is the
  opposite of the rule for deletions, deliberately: doing work that turns out unneeded is
  recoverable, and a library that stops ingesting because the planner is down is not.
- **One owner of SIGSTOP and SIGCONT per process.** `ReadAheadGuard` already stops and resumes the
  transcode of an arriving source. A process runs only when neither the guard nor the plan wants it
  stopped; both feed one gate in the executor, and neither sends a signal directly.

## 5. Work units

Each unit is something `MovieBot.Ingest` can do alone and repeat safely.

- **`video-rung`**: one quality of one title, from a given segment number to the end, into a given
  directory. Its own ffmpeg process, decoding on NVDEC with `-resize` to the rung's size where the
  decoder supports it, falling back to a CPU scale where it does not. Keyframes are forced on the
  same timestamp expression as today, so a rung started later, or resumed, opens segment *n* on the
  same frame as every other rung.
- **`audio`**: every audio rendition of a title, in one process, from the same demux rule as today
  (each rendition through its own demuxer).
- **`rung-add`**: a backfilled quality published into a finished title (section 8).

**Resuming from a segment** is the new capability every unit needs: `-ss` at the boundary of the
first missing segment, `-hls_flags append_list`, `-start_number`, and the output timestamps offset
to continue the existing ones. The playlist's last complete segment is the resume point; a partial
segment is deleted first.

**Two kinds of pause**:

- **Hold**: SIGSTOP. Instant, keeps the process's state, costs the memory it holds. For contention
  measured in minutes: a room starting while a third rung runs.
- **Park**: stop the process and resume from the last whole segment later. Costs a few seconds of
  restart and one partial segment. For anything held long: backfill when a room starts playing, a
  hold that has lasted past a threshold, or more held processes than the VRAM and RAM budget allows.

**What "head" means changes.** The manifest carries a head per rung and for audio, read from each
playlist's `#EXTINF` sums as today. A title's playable head for a viewer is the minimum of their
rung's head and the audio head.

## 6. The scheduling policy

Each reconcile the planner ranks every unit and marks as many `run` as the budget allows, from the
top:

1. **Watched and short of lead.** A rung somebody in a playing room wants, whose head is less than
   the lead target (initially 120 s) past the room's playhead. Audio for the same title alongside.
2. **A film a room has just asked for**, its top rung first. A room never waits behind a film nobody
   holds; the work that makes room for it is held or parked.
3. **Watched and comfortably ahead.**
4. **Unwatched rungs of films in rooms**, so a switch later has somewhere to go.
5. **Films nobody holds** that are still owed their first pass.
6. **Backfill** (section 8), only while no room is playing.

- **Budget.** Two per-host settings measured on the card, the way `MaxConcurrentIngests` is now: an
  NVENC budget in 1080p-equivalents of realtime, and an NVDEC budget in source decodes of realtime.
  A unit costs its pixel area against the first and one decode against the second. The executor's
  reported speeds are compared against the model and a persistent gap is logged, never silently
  absorbed.
- **Hysteresis.** A unit is not held within a minimum run time of being started, and the lead target
  has a band, so two units of similar rank do not swap every reconcile.
- **"Playing"** is a room with somebody in it, playing or paused. A room merely holding a title
  (`OccupiedRooms`' sense) is not demand: rooms are kept idle for a while and would pin work for
  nobody.
- **"Wants"** is the rung the viewer's player would choose, not the one it plays (section 7). A viewer
  capped at 720p because 1080p is behind wants 1080p, and that is the demand that must be served.
- **Hi10P and anything else NVDEC refuses** is costed as a software decode: its CPU cost, not an
  NVDEC slot. Phase 1 measures what it actually costs on this CPU.

## 7. The player: per-rung heads and what a viewer wants

Pausing any single rung is unsafe until both of these land: hls.js switching to a rung that is behind
the playhead stalls the viewer until it catches up.

- **Per-rung heads on the room.** The room state carries a head per rung instead of one
  `TranscodeHead`. The player caps its levels to the rungs whose head covers the playhead plus a
  margin (`hls.autoLevelCapping` or the level list it allows), and lifts the cap as a rung catches up.
  The seek clamp uses the head of the best rung that covers the target, so one lagging rung never
  clamps a whole room.
- **What each viewer would choose, reported up.** The player sends the API the rung it plays and the
  rung its ABR would pick unconstrained (from hls.js's bandwidth estimate against the advertised
  bitrates), on change and at a slow heartbeat. It goes up only: never into the shared room state,
  never broadcast. This is a relaxation of the player's rule that quality never reaches the hub; the
  rule becomes that a viewer's quality is never shared with the room. Where the browser plays HLS
  itself (Safari on iOS), nothing is known and the planner assumes the top rung.
- Segment requests cannot stand in for this report: hotrod's nginx caches segments with
  `proxy_cache_lock`, so one fetch per segment reaches hotbox however many viewers there are.

## 8. Backfill

**What is owed is computed, never recorded.** Each pass, the planner compares every manifest with the
ladder a title of that source should have (`VideoLadder.For`) and lists what is missing. A separate
ledger of owed work would drift from the disk; the manifest is what players are served and is
therefore the truth. The hand-off already works out what an episode is owed this way.

- **The source decides what can be made.**
  - With the source on disk: anything the first pass would have made.
  - Without it (the tracker's clock removed it): a lower rung only, transcoded from the top rung. A
    second generation of lossy H.264 is acceptable going down and never going up. A missing top
    rung without its source is not backfillable and is reported, not attempted.
  - Owed backfill does not hold a source past the tracker's clock.
- **Publishing into a finished title.** A ready title is today treated as a directory nothing will
  add to; backfill makes that false, so the add is made atomic:
  1. the rung is written into a staging directory on the SSD when it fits, else beside the title on
     its own disk;
  2. under the title's lock (`TitleLocks`, the one settling and the marker pass take), the staging
     directory is moved into the title, the manifest and master playlist are rewritten through a
     temp file and an atomic move;
  3. a title on the cold disk whose rung was staged on the SSD is copied across under the same
     ordering settling uses, and the staging copy deleted last.
  A player holding the old master simply does not see the new rung until it next loads the film.
  The ready master playlist is served with `max-age=300`, so that is the longest a viewer waits to
  see it.
- **A cold volume that is out** skips every title on it, as every other pass does.
- **Backfill runs only while no room is playing** and parks the moment one starts.

## 9. Placement: where a transcode is written

**The torrents stay on the SSD** until the tracker's clock removes them, as they do now.

**A transcode chooses its disk when it starts, against a reservation.** Its size is predictable from
the ladder's bitrates and the duration: the top two rungs and their audio come to about 6 GB an hour.
The rule:

1. The SSD, when the reservation fits within what the download budget leaves after its own
   reservations.
2. Otherwise the cold disk, when its volume is present and the reservation fits.
3. Otherwise the transcode waits. It does not start and fail partway, which is how
   `stardust-2007` ended with `No space left on device` writing its manifest.

The reservation is accounted in the same place as the downloads' (`DiskBudget` in moviebot-acquire),
not counted twice by two owners each believing the free space is theirs. It replaces the static
`Download.ReservedGiB` held for "the transcode written beside" a download, which is a guess at one
film where the reservation is the sum of the actual ones.

**Writing a transcode to the cold disk changes a rule** that `src/MovieBot.Core/CLAUDE.md` states
flat: nothing seek-heavy and no scratch space on the platter. A transcode writes two-second segments
of about two megabytes, sequentially, at a few megabytes a second, which is within what the disk
streams. The rule is rewritten to what is measured once phase 4 measures the worst case: two
transcodes writing, a settle copy, and a viewer reading. While any transcode writes to the cold
disk, nothing settles onto it.

**A film settles when it is ready and no room is playing it.** Settling is already built so that it
never breaks a read in progress (copy, check, one rename, delete the original last, every request
resolving the title's disk afresh), but its copy saturates the disk the viewer is about to read
from. Waiting costs nothing: the film is finished and no longer grows. Whether a film may settle
becomes a disposition the planner publishes, and the hand-off's settle worker acts on it.

**When the SSD is short and watched films are being kept on it**, downloads win: a download refused
for space stops new films arriving at all, while a held film only waits. New transcodes stop choosing
the SSD while it is under pressure, and nothing is moved off under a playing room to make room.

## 10. Failure modes to keep in view

- The API unreachable: the executor's fallback (section 4). Nothing held stays held forever; a held
  unit with no plan behind it is resumed.
- The executor restarting: every unit it ran is either complete (its playlist has `#EXT-X-ENDLIST`)
  or resumable from its last whole segment; the next reconcile resumes it.
- The cold volume vanishing under a transcode writing to it: the unit fails, its partial output is
  unreadable anyway, and the next reconcile places it again from scratch.
- A unit failing repeatedly: reported in the status with its reason, backed off, and not retried in a
  loop. One that fails on the first pass of a new download is surfaced the same way the hand-off
  reports a failed ingest now.
- Stopped processes exhausting VRAM or RAM (hotbox has 7.7 GiB and no swap): the park threshold is a
  count and a memory budget, measured in phase 1.

## 11. Phases

Each phase is shippable alone and leaves the system working.

1. **One process per rung and one for audio, decoder resize.** Ingest only. Same outputs, same
   manifest, same single head (the minimum, as now). Measure: real films with audio, two concurrent
   ingests, an HDR source through decoder resize and `tonemap_opencl`, a Hi10P source, the memory of
   a stopped process. Acceptance: two-rung throughput at or near the encoder ceiling, no regression
   in quality or alignment (segment *n* of each rung opens on the same frame).
2. **Resume from a segment, and hold and park in the executor.** Ingest gains the resume input;
   hand-off gains the single gate over SIGSTOP and SIGCONT shared with `ReadAheadGuard`. Still driven
   by today's local rules.
3. **The planner, whole films only.** `GET /api/transcode/plan` and `PUT /api/transcode/status`, the
   ranking at film granularity (every rung of a film runs or holds together), the fallback. This
   alone serves the "a new room never waits" case and replaces `MaxConcurrentIngests` with the
   measured budget.
4. **Placement.** The reservation in the shared disk budget, the SSD-then-cold rule, waiting when
   neither fits, settling gated on no room playing, the cold-disk measurement and the
   `MovieBot.Core` rule rewritten to it.
5. **Per-rung heads and the viewer's report.** Room state, player capping, the seek clamp, the
   report up to the API. Only after this does the planner rank and hold individual rungs.
6. **Backfill.** Owed work computed from manifests, `rung-add`, publishing into a finished title,
   lower rungs from the top rung where the source is gone.
7. **A third rung, if it earns it.** The reports from phase 5 say whether viewers fall below 720p.
   480p costs about 17% more disk per film; making it only while somebody needs it and dropping it
   at settle is the option that costs no disk at all.

## 12. Open questions

- The lead target and its band: 120 s is a starting point. The real constraint is how long a rung
  takes to recover from a hold at the speed it gets back.
- Whether the plan should be pushed rather than polled, once phase 3 shows the latency.
- Whether a viewer's reported quality is kept beyond the session, for the planner to anticipate a
  person who always watches at 720p. Kept nowhere until there is a reason.
