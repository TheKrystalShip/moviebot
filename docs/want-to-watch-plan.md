# Want to watch, prefetch, matching and plans

A plan, not canon. What it settles moves into the `CLAUDE.md` beside the code it governs
(`src/MovieBot.Api/`, `Acquisition/`, `Notifications/`, `Sessions/`, and the site's README) and into
`docs/api-contract.md` when each phase lands, and that phase's section here is struck. When the last
phase lands, this file is deleted.

## 1. What this is for

Krystal Cinema's point is the shared room, but today people agree on what to watch and when
somewhere else, then one of them opens a room. This plan lets a person say "I want to watch this",
and the server acts on it:

- **it fetches the title ahead of time**, as soon as the host is quiet and has the disk for it, so
  it is ready when they sit down;
- **it shows people who want the same title to each other** and suggests watching it together;
- **it lets them schedule that**: a *plan* is a title, a few proposed times and the people invited;
  answering settles a time, and at that time the room is waiting with them already let in.

"Want to watch" replaces both personal lists and wishes. Watch later was already "I want to watch
this", kept private and acted on by nothing; a wish is the same intent for a film that cannot be
fetched yet. One signal covers every state a title can be in, and the server decides what to do:

| The title is | The server |
|---|---|
| here and ready | shows who else wants it, offers a plan |
| fetchable from the tracker | prefetches it when the conditions in `Acquisition/CLAUDE.md` "Prefetch" hold |
| not on the tracker at the source floor | sweeps for it hourly (what a wish does today) and tells the wanters when it turns up |
| wanted by somebody who has not watched it | keeps it in the library, within a limit (`Acquisition/CLAUDE.md` "The library's clock") |

## 2. Decisions taken (by the owner, not to be re-opened)

- **Wants are visible to everybody by default**, with a per-title "just me" switch.
- **Prefetch runs as soon as its conditions hold**, at any hour, not in a nightly window.
- **No Discord channel for plans yet.** Plans live on the site, in the bell and in Web Push.
- **Plans are in scope**: proposing times, answering, fixing a time, the room made ahead of time,
  reminders.

## 3. Cold context: what exists today

These were mapped on 2026-10-20 and are the pieces this plan builds on. Read the `CLAUDE.md` named in
each row before changing that area.

| Piece | Where | Rules |
|---|---|---|
| Wants, keyed by person and IMDb id, `kind` film/show, inLibrary/watched joined at read; the wish for a film nothing has yet is kept while a want on it can ask the tracker for it | `src/MovieBot.Api/Wants/` (`WantStore`, `WantPresenter`, `WantEndpoints`, `ShowProgress`); `Acquisition/{Wishes,WishList}.cs`, `WishSweep`; site `pages/Wants.jsx`, `components/WantButton.jsx`, `wants.js`; bot `src/MovieBot.Bot/Notify/`; assistant `CinemaApi.WantsAsync`, `MyTasteTool` | `src/MovieBot.Api/CLAUDE.md` (the want bullet), `Acquisition/CLAUDE.md` "Wishes", `docs/api-contract.md` "Wanting" |
| Watch now (fetch for a room), Downloads (offered releases), the source floor, release picking, `ReleaseLanguages` | `Acquisition/WatchNow.cs`, `WatchNowLoader`, `Downloads.cs`, `Offers.cs` | `Acquisition/CLAUDE.md` |
| Server-started fetching that already exists, the shape to copy | `Shows/FollowWatcher` (auto-download at the best quality any follower watches at; permission captured when the follow is made, `FollowEndpoints.cs:30`), `Shows/NextEpisodes` (one episode ahead) | `src/MovieBot.Api/CLAUDE.md` (follows), `Notifications/CLAUDE.md` |
| Torrent tags as the record (`watch:<room>`, `keep`, `keep:sXXeYY`, `passed:`, `TorrentTags.Tell`) | tag vocabulary in `../moviebot-acquire` | `Acquisition/CLAUDE.md`, `src/MovieBot.Handoff/CLAUDE.md` |
| Disk: `DiskBudget` (Ok/Warning/Full, volume headroom, transcode reserve), `VolumeRoom`, `TranscodeLedger` | `../moviebot-acquire/src/MovieBot.Acquire/Download/` | `src/MovieBot.Handoff/CLAUDE.md` |
| The library's clock (when a settled title is deleted) | `Library/LibraryClock`, `/api/library/due` (hand-off applies it) | `Acquisition/CLAUDE.md` "The library's clock" |
| Transcode planner (rooms first, backfill when idle) | `Transcode/`, `GET /api/transcode/plan` | `Transcode/CLAUDE.md`, `docs/transcode-scheduling-plan.md` |
| Rooms: made on first entry of an id, open/closed, `room_admitted`, invites (`invite` kind, always pushed), guests, idle reaper (`Rooms:IdleTimeout`, 30 min) | `Sessions/` (`SessionStore`, `SessionHub`, `RoomAdmission`, `RoomDoors`, `RoomEntry`, `RoomStore`, `SessionReaper`) | `Sessions/CLAUDE.md` |
| What a person watched (credited per stretch of playing); the `watched` join lists use | `People/WatchRecorder`, `SessionStore.Watched`, `IPlayHistory` | `src/MovieBot.Api/CLAUDE.md` (watch record bullet) |
| Who is on the site | `PersonDirectory.Seen()` last-seen in memory (`/api/people` `lastSeenAt`); `SessionStore.Participants`/`Viewing`; `/api/sessions` | |
| People: no social graph; everybody named by `/api/people` is visible to everybody | `People/PersonDirectory` | |
| Notifications: `Notifier` is the one sender; bell keyed once per person; per-kind push switches (`available`, `ready`, `episode`, `invite`) | `Notifications/` | `Notifications/CLAUDE.md` |
| Background work: `IHostedService` loops on intervals, registered in `Program.cs`; there is no time-of-day scheduler | `src/MovieBot.Api/Program.cs` | |
| The database: one SQLite file, `Data/Schema` steps (`[Tables, Sorts]`), `user_version` counts steps | `Data/Schema.cs`, `Data/CinemaDatabase` | `src/MovieBot.Api/CLAUDE.md` |
| Actions: `cinema:watch`, `cinema:download`, `cinema:wish`, `cinema:offline`, ... | `Auth/CinemaActions.cs` | `docs/api-contract.md` "Signing in" |
| Settings: one file, every default declared | `src/moviebot.settings.json`; `MovieBotSettings` in moviebot-acquire | repo `CLAUDE.md` Conventions |

## 7. How to work through this

- **Implement all four phases first, then test once.** The owner's rule: each phase is one tight
  agent that writes the code and its tests, compiles (`dotnet build moviebot.slnx -c Release`, and
  the site's build if touched), and commits locally with no version bump, CHANGELOG entry, tag or
  deploy. It runs no tests, no verify suites and nothing live. The "Done when" checks under each
  phase are run together in one final pass after Phase 4: `dotnet test`, the site's verify suites,
  the migration against a copy of the live database, the deploy, and the live checks. That pass
  makes the one version bump, CHANGELOG entry and tag. The `want:` tag is a commit in `moviebot-acquire` too, addressed with `git -C`.
- **Do not `git add -A` in `moviebot/`**: the checkout has untracked scratch files (`*.err`,
  `*.jsonl`) that are not part of any change. Add paths explicitly.
- **Testing:** `dotnet test` (hub integration tests with two real SignalR clients is the pattern for
  plans and rooms); the site's verify suites per `web/site/README.md`. A scratch site needs the TMDB
  token passed by path (`~/Documents/tmdb_api_read_access_token`). Check `:8299` for an orphaned
  scratch server first.
- **Deploying:** `deploy/CLAUDE.md` is the authority. hotbox units may be restarted without asking,
  except that restarting the hand-off resets a running transcode; check nobody is mid-hand-off.
- **Docs move as each phase lands:** `docs/api-contract.md` gains "Plans"; `Acquisition/CLAUDE.md`
  gains the prefetch rules; `Notifications/CLAUDE.md` gains `together` and `plan`; then the phase's
  section here is struck.

## 8. The final pass

Every phase is implemented, built and committed locally, untested (`33c716b`, `07ac1a9`, `7c03347`,
`bde8af8` here; `ed1144a` in moviebot-acquire). This pass tests all of it once, fixes what fails,
releases and deploys. When it is done, this file is deleted.

### Tests

- `dotnet test` across the solution, and moviebot-acquire's own tests for the `want:` tag.
- The site: `npm run lint`, `lint:css`, `check`, `npm test`, `verify:routes`, `verify:mobile`,
  `verify:tv` (every `--checks`; Home and the title page must reach every control by D-pad), and
  `verify:tv-perf` against its baseline (`tvperf/baseline.json` had a key renamed and is not
  re-measured). `verify:tv --checks remoteui` timed out on the remote's Episodes control before this
  work; find out whether that is staging or a real fault.
- `npm run build` reports a bundler failure as a precache "no build line" error; build through the
  Vite API without that plugin to read the real one.
- What has no unit test: `TogetherDigest.PassAsync` and `WantMatcher.Of` (they read the library),
  and `WantFetcher.PassAsync` end to end.

### The migration on live data

Copy `/var/lib/moviebot/moviebot.db` from hotbox with `sqlite3 .backup` (never write the live
file), run the API's schema against the copy and check it: 56 wants (45 list entries plus 15 wish
subscriptions, 4 overlapping), all just me, none with `may_fetch`, the 15 wish subscribers with
`may_wish`; the list tables, `wish_subscribers` and `shared` notifications gone; the 7 wish rows
kept; schema at step 5.

### Live, after the deploy

Sign in as `claude` (`tks-login header cinema`, `tks-login browser -o <file>`). **Never invite,
match or notify a real person**: every plan made here invites nobody but `claude`, every want made
here is just me, and each is removed afterwards.

- **Wants:** `PUT` and `DELETE /api/me/wants/{imdbId}` round-trip; the migrated rows are listed; the
  toggle and `/wants` work on the site and on the TV pages.
- **Prefetch:** with no room playing, a just-me want on a small fetchable film starts its download
  within a pass, tagged `want:`, and `ready` arrives once it can be watched. A room started first
  holds the fetch back. `/api/library/due` leaves a wanted, unwatched title off.
- **Matching:** covered by the tests; with one account it is only checked to answer an empty
  `/api/me/together`.
- **Plans:** `claude` plans a ready film about 35 minutes out and fixes it as the maker. At
  `Plans:OpenMinutes` before, the room exists, closed, with `claude` admitted and the film loaded
  paused; the reminder arrives at `Plans:RemindMinutes`; joining plays; once the room is reaped the
  plan is `done`. Cancelling a second plan before it opens works. The two-person path (answering in,
  fixing at `Plans:FixAt`, a can't-answerer not admitted) is `PlanRoomTests`.

### Release

One minor version bump with its CHANGELOG entry (the site's version too, as recent entries carry
it), committed with the fixes, tagged `v<version>`; moviebot-acquire's bump per its own
convention if its change is released. Not pushed. Deploy per `deploy/CLAUDE.md`.
