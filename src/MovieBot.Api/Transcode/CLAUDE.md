# The planner: what the card should be doing (`Transcode/`)

The API decides which transcode work runs, and the hand-off carries it out. The API never runs
ffmpeg, writes a media root or touches the GPU: it is redeployed more often than anything else, and
a restart of it must lose no transcode in flight. `docs/api-contract.md` is the authority for the
wire; `src/MovieBot.Core/Transcode.cs` holds the shapes, so the hand-off reads the types the API
writes.

- **A plan is desired state, computed and never stored.** `TranscodePlanner.Plan` is a function of
  its `PlanInput`: the rooms somebody is in, the library's titles, the executor's last report and
  the options. A restart of either side computes the same answer from the same facts, so there is no
  queue to lose and nothing to reconcile against.
- **Demand is a room with a person in it and a film set, playing or paused.** A room merely holding
  a film is not demand: rooms are kept idle for a while and would pin work for nobody
  (`SessionStore.Viewing`).
- **What is wanted is the rung a viewer's player would choose, not the one it plays.** A viewer
  capped at 720p because 1080p is behind wants 1080p, and one who pinned 720p wants 720p.
  `IViewerQuality` answers per room with one height per connected viewer, from what the players
  report to the hub (`Sessions/ViewerQuality`); a player that says nothing wants the best rung the
  film has. A wanted height is served by the tallest rung not above it, or the lowest rung when
  every one is taller.
- **The tiers rank every unit; the budgets decide how far down the list the card reaches.**
  1. watched and short of the lead target;
  2. a film a room has just asked for, which is a wanted output with no head yet, its top rung first;
  3. watched and comfortably ahead;
  4. an unwatched rung of a film a room holds;
  5. a film nobody holds that is owed its first pass;
  6. backfill, a rung a finished film is owed, and only while no room is playing: while one is, it is
     parked.

  Audio is wanted by every room holding the title.
- **Short of lead has a band.** A unit that is running stays short of lead until it is the lead plus
  the band ahead; one that is held is short only below the lead minus the band. Two units near the
  lead do not trade places on every plan.
- **A unit that has only just started is not displaced by anything below tier 3.** The minimum run
  time lifts it to tier 3 while it lasts. A watched film short of lead, or a film a room has just
  asked for, still displaces it, because a viewer waiting is worse than a restart. Backfill is never
  lifted.
- **Two budgets, both measured on the card.** NVENC is spent in 1080p-equivalents of realtime, a
  unit costing its pixel area against 1920x1080; NVDEC is spent in source decodes, a video unit
  costing one. Audio costs neither. The budget is walked from the top of the ranking and a group
  that does not fit is skipped for one that does. The first group is admitted whatever it costs, so
  a film dearer than the whole budget still runs rather than never being made.
- **A unit that is not to run is held or parked.** Held (stopped in place) keeps the process's
  memory and is for a wait of minutes: only a unit that is already running or held, not held past
  `ParkAfterSeconds`, and within `MaxHeld` stopped processes. Everything else is parked and resumes
  from its last whole segment. A unit the executor reports failed is parked and spends nothing; the
  executor owns its back-off.
- **Whole films by default.** With `PerRung` off, a film's rungs and its audio are one group:
  ranked at its most urgent unit, costed together, and run or held together. `PerRung` switches
  every unit to its own rank. A rung is only safe to hold alone once the player caps its levels to
  the rungs that have caught up.
- **A unit's id is what it is, and progress is not part of it.** `video-rung/{title}/{directory}`,
  `audio/{title}` and `rung-add/{title}/{directory}`; `fromSegment` carries how far it has got. The
  id is therefore the same on every plan for as long as the work is owed.
- **The executor's report is a fact for `StatusStaleSeconds`.** Heads and speeds are read from it
  in preference to the manifest, since the manifest is rewritten every couple of seconds at best.
  Once it is stale, or before any report has come, the plan is made from manifests alone and says so
  with `executorReporting: false`; every unit is then treated as not started.
- **What a finished title is owed is computed on every plan and never recorded.** Each finished
  title's manifest is compared with `Ladder.For` of its source's dimensions, and each rung it lacks
  is a `rung-add` unit; a title still being made contributes its unfinished rungs and audio as
  `video-rung` and `audio`. The ladder rule and its bitrates, step-down widths and segment length are
  `MovieBot.Core`'s `Ladder`, which the ingest's defaults are taken from, so the planner compares
  against what a first pass would have made and nothing has to be kept in step by hand. Whether the source is
  still on disk is the executor's to find out; the plan names the rung.
- **A film may settle when it is ready and no room is playing it.** Playing is the one predicate
  demand uses, `PlanRoom.IsPlaying`: a person connected and a film set, paused or playing. A room that
  still holds the film after everyone has left does not block settling. `titles[].maySettle` is the
  disposition the hand-off's settle worker acts on.
- **The library is read from a cache.** `TranscodeCatalog` rebuilds the list of titles every
  `CatalogRefreshSeconds`, because asking the filesystem about every title costs a second on the cold
  disk; a title still being made or held by a room is read fresh on every plan.
- **A head is read from the manifest through `IManifestHeads`.** Each rung and the audio carry
  their own head while a title is made; an output whose manifest records none of its own is at the
  overall head, and a finished title's outputs are whole.
- **Both routes answer the service key alone.** A plan names every film and what rooms are watching.
