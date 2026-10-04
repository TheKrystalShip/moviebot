# Transcode scheduling, backfill and placement

A plan, not canon. What it settles moves into the `CLAUDE.md` beside the code it governs
(`src/MovieBot.Ingest/`, `src/MovieBot.Handoff/`, `src/MovieBot.Api/Transcode/`,
`src/MovieBot.Core/`, `web/site/src/player/`) when it lands, and its section here is struck. When
the last phase lands, this file is deleted.

## 1. What this is for

The card is a fixed resource shared by every transcode. This plan makes the GPU's time follow
demand:

- a quality somebody is watching is kept comfortably ahead of the room's playhead before anything
  else runs;
- a film a room has just asked for starts at once, never behind a film nobody is watching;
- spare capacity goes first to the unwatched qualities of watched films, then to films nobody holds;
- when no room is playing, the card backfills what the library is owed: rungs it lacks, and rungs
  made at a quality the library has moved past.

**Out of scope: the dialogue boost.** It runs on the CPU, pins all four cores for minutes, and how
it is made is being re-evaluated separately. It keeps its current place: made after the video and
audio, before the title turns ready. Nothing here schedules, pauses or backfills it.

## 2. What stands

These are canon and are not restated here:

| Piece | Authority |
|---|---|
| One ffmpeg per video rung, audio across two processes, decoder resize, `-surfaces 32`, CQ 25 | `src/MovieBot.Ingest/CLAUDE.md` |
| The planner: tiers, two budgets, hysteresis, hold vs park, whole films vs `PerRung`, owed work computed from manifests, `maySettle` | `src/MovieBot.Api/Transcode/CLAUDE.md` |
| `GET /api/transcode/plan`, `PUT /api/transcode/status` | `docs/api-contract.md`, shapes in `src/MovieBot.Core/Transcode.cs` |
| Per-rung heads in the manifest and room state, player level capping, per-rung seek clamp, viewer quality reported up the hub | `web/site/src/player/CLAUDE.md`, `src/MovieBot.Api/Sessions/CLAUDE.md` |
| Placement: the reservation in `TranscodeLedger`, media root then cold root then wait | `src/MovieBot.Handoff/CLAUDE.md` |
| `--reencode`: CQ 23 from the top rung, staged and swapped by rename on the title's disk | `src/MovieBot.Handoff/CLAUDE.md` |

What does not yet exist: the hand-off neither fetches the plan nor reports status. It starts first
passes on its own rules under `MaxConcurrentIngests`, a unit cannot resume from a segment, and
nothing executes a `rung-add`.

## 3. Roles

| Piece | Owns | Never does |
|---|---|---|
| `MovieBot.Api` | **The planner.** Demand (rooms, playheads, what viewers want), what each title is owed, the priority order, when a film may settle. Publishes a desired state. | Run ffmpeg, write the media roots, touch the GPU, choose a disk. |
| `MovieBot.Handoff` | **The executor.** Starts, holds, parks, resumes and stops units to match the desired state; reports what is running, at what speed, how far each output has got, and what is owed that has not begun. Resolves each unit's input, chooses its disk, finishes titles, owns every write to the media roots. | Decide priority from rooms. |
| `MovieBot.Ingest` | **A stateless library** performing one unit it is handed, with explicit inputs and outputs. | Know about rooms, other units, or the schedule. |

**Placement is the executor's.** It measures the disks and holds the ledger lock that makes placing
and claiming one step. The plan's `placement` field is removed from the wire, from
`TranscodePlanUnit` and from `docs/api-contract.md`.

## 4. The executor's loop

- **Reconcile.** The hand-off fetches the plan and reconciles toward it every 2 to 5 s while any unit
  is running, held or parked, or any room is playing; on its existing `PollSeconds` otherwise.
  Polling is the mechanism. A pushed nudge is added only if a room starting a film measurably waits
  on the poll, and even then the plan is fetched, never carried in the nudge.
- **Report.** Each reconcile ends with `PUT /api/transcode/status`: every unit it knows, the disks'
  readings, and the owed entries (section 5).
- **The key is the unit id.** The in-flight set keyed by download hash (and hash and file for an
  episode) becomes a map from unit id to the process running it. Ids are stable for as long as the
  work is owed (`src/MovieBot.Api/Transcode/CLAUDE.md`), so a unit is never started twice.
- **Input is resolved here.** A unit names a title and a rung; the hand-off maps it to the source:
  the download's feature file (or the episode's file) while the download holds it, else the title's
  top rung for a lower rung or a re-encode. A missing top rung with no source is reported `failed`
  with that reason, not attempted.
- **Disk is chosen here**, at the unit's start, by `TranscodePlacement` as today. A unit with no room
  stays parked and is reported so, with the reason; the plan keeps ranking it.
- **Finishing a title is the hand-off's.** When every unit of a title reports `done`, the hand-off
  runs what follows the main pass today (subtitles, preview sheet, dialogue boost, the ready status,
  markers for an episode) exactly as it does now. Finishing is not a plan unit and spends nothing from
  the GPU budgets.
- **One gate over SIGSTOP and SIGCONT per process.** `ReadAheadGuard` and the plan both feed it; a
  process runs only when neither wants it stopped, and neither sends a signal itself.
- **When the API cannot answer, the executor falls back**: new downloads are ingested on the local
  rules up to `MaxConcurrentIngests`, everything running keeps running, a held unit with no plan
  behind it is resumed, nothing new is held, no backfill runs and nothing settles. Doing work that
  turns out unneeded is recoverable; a library that stops ingesting because the planner is down is
  not. `MaxConcurrentIngests` survives only as this ceiling.
- **Failures back off.** A unit that fails is reported with its reason, retried after a back-off that
  doubles from one minute to an hour, and never in a loop. A first pass that fails is surfaced the way
  a failed ingest is now.
- **Restart.** Every unit the executor ran is either complete (`#EXT-X-ENDLIST`) or resumable from
  its last whole segment, and the next reconcile resumes it. Ledger claims are cleared at start, as
  now, and a resumed unit claims again.

## 5. Films the planner cannot see yet

The planner reads titles from manifests, and a film still downloading has none until its transcode
begins. The executor's report gains an **owed** list: every download or episode file it would
ingest and has not started, each with its title id, kind (film or episode), the show and its season
and episode number for an episode, and what a probe gives once enough has arrived to read the
container (duration, width, height, HDR, decodable on NVDEC). The planner ranks these as first-pass
work: tier 2 when a room has asked for it, tier 5 otherwise. An owed entry not yet probed is ranked
but not admitted.

- **Episodes are titles like films, in order.** Among first passes of one show, the earlier episode
  ranks first. An episode a room is watching makes the next episode of the same show, when it is
  owed, rank as tier 4: wanted soon.
- This is a contract change on both sides: `TranscodeStatus` gains `owed`, `docs/api-contract.md`
  says so, and both deploy together.

## 6. Resuming and pausing

**Resuming from a segment** is the capability every unit needs: `-ss` at the boundary of the first
missing segment, `-hls_flags append_list`, `-start_number`, and output timestamps offset to continue
the existing ones. The playlist's last complete segment is the resume point; a partial segment is
deleted first. Keyframes are forced on the same timestamp expression as now, so a rung resumed
opens segment *n* on the same frame as every other rung. Resuming has to hold for:

- an SDR source on NVDEC with decoder resize;
- an HDR source through `tonemap_opencl`;
- a Hi10P source decoded in software;
- an audio process carrying several renditions, each resumed from its own playlist's last whole
  segment;
- a source still arriving, behind `ReadAheadGuard`.

**A resume keeps the quality the rung began at.** The manifest records `cq` per rendition when a
rung starts, and a resumed rung is encoded at that value, so one playlist never mixes two qualities.
A rung left at an outdated quality is owed again by backfill (section 7) and redone whole.

**Two kinds of pause.** Hold is SIGSTOP: instant, keeps the process's state, costs its memory. Park
stops the process; it resumes from the last whole segment. **Hold ships disabled** (`MaxHeld: 0`,
every pause a park) until these are measured on hotbox and the settings set from them:

- the VRAM and RAM a stopped ffmpeg holds, for SDR with decoder resize, HDR through tonemapping, and
  Hi10P, against hotbox's 7.7 GiB, no swap, and `llama-server`'s 1.4 GiB resident on the card;
- the NVDEC ceiling in concurrent source decodes, which replaces the `NvdecBudget` of 20;
- the NVENC budget checked against production throughput with audio and two concurrent titles.

## 7. Backfill

**Owed is computed, never recorded.** Each plan compares every finished title's manifest with what
it should hold, and each difference is a tier-6 `rung-add` unit:

- **a missing rung**: one `Ladder.For` gives the source's dimensions and the manifest lacks;
- **an outdated rung**: one whose rendition records no `cq`, on a settled title.

`--reencode` is folded into this and removed: the planner owes what the tool selected, and the
executor carries it out with the tool's method.

- **The source decides what can be made.** With the source on disk, anything the first pass would
  make, at the ingest's quality. Without it, from the title's top rung at CQ 23 (`Reencoder`'s
  measured choice): a lower rung, or the top rung itself when it is outdated. A second generation is
  acceptable going down and in place, never going up. Owed backfill does not hold a source past the
  tracker's clock.
- **Made on the title's own disk and swapped by rename.** A unit writes into a dotted staging
  directory on the title's root (`.reencode/<id>`); under the title's lock (`TitleLocks`) the staged
  rung is moved in, and the master playlist and manifest are rewritten through a temp file and a
  rename. A replaced rung's old directory is moved out first and deleted last; a pass stopped between
  the moves is repaired by the next, as `--reencode` repairs now.
- **New segments are named apart** (`q23-seg00000.m4s`) where a rung replaces one already served,
  because segments are served immutable.
- **Adding a rung needs no room to be idle**; replacing one needs no room playing that title, since a
  player holding the old initialisation segment cannot decode the new one. A replacement due while
  the title is played stays owed. A player holding the old master sees a new rung when it next loads
  the film, at most `max-age=300` later.
- **Backfill runs only while no room is playing** and is parked the moment one starts. A cold volume
  that is out skips every title on it.

## 8. Settling

**`maySettle` replaces `SettleGate`.** The settle worker reads the title's disposition from the
current plan; without a plan nothing settles, as now when the API cannot say. Nothing settles onto
the cold root while a transcode writes to it, as now.

**The cold disk is not measured further.** A transcode writes two-second segments of about two
megabytes sequentially, settling stands aside while one writes to the cold root, and that is taken
as sufficient. `src/MovieBot.Core/CLAUDE.md` is rewritten to state the rule as it stands: transcodes
may be written to the platter, settle copies wait for them.

## 9. Viewer quality, counted

The API keeps a running tally of the heights viewers want and play: per height, the seconds watched
and the number of sessions, with no person, room or connection kept. It persists across restarts in
MovieBot's state directory and is readable over the service key. Its one consumer is the decision in
phase 4.

## 10. Phases

Each phase is shippable alone and leaves the system working.

1. **Resume, the gate, and the measurements.** Ingest gains the resume input for every case in
   section 6; the hand-off gains the one SIGSTOP/SIGCONT gate shared with `ReadAheadGuard`, still on
   its local rules. Measure what section 6 lists and set `MaxHeld`, `NvdecBudget` and `NvencBudget`
   from it. Acceptance: a unit parked and resumed mid-film is frame-aligned with its sibling rungs and
   plays through the join; a Hi10P and an HDR film each survive a park.
2. **The executor follows the plan.** Reconcile and report (section 4), the owed list and episodes
   in order (section 5), input resolution, finishing on all-done, the fallback, back-off, `placement`
   removed from the wire, `maySettle` replacing `SettleGate`, and `PerRung` turned on. API and
   hand-off deploy together. Acceptance: a room asking for a new film while two unwatched films
   transcode gets its film started within one reconcile; the API stopped mid-transcode leaves every
   unit running; the reported speeds against the model are logged and any persistent gap named.
3. **Backfill.** Owed rungs and outdated rungs (section 7), the swap by rename, `--reencode` removed.
   Acceptance: a settled CQ 19 film with only its top rung comes out with both rungs at CQ 23 with
   nobody playing, and is left alone while a room plays it.
4. **A third rung, if it earns it.** The tally (section 9) is built with phase 2 and read here: it
   says whether viewers want below 720p. 480p costs about 17% more disk per film; making it only while
   somebody needs it and dropping it at settle costs no disk at all.

## 11. Open questions

- The lead target and its band: 120 s and 30 s are starting points. The real constraint is how long
  a rung takes to recover from a pause at the speed it gets back, which phase 2 shows.
- Whether the plan should be pushed rather than polled, once phase 2 shows the latency.
