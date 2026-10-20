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

## 6. Phase 4: Plans

### Data

```sql
CREATE TABLE plans (
    id         TEXT PRIMARY KEY,
    imdb_id    TEXT NOT NULL,
    kind       TEXT NOT NULL CHECK (kind IN ('film', 'show')),
    season     INTEGER,            -- a show's episode the plan starts at
    number     INTEGER,
    made_by    TEXT NOT NULL,
    made_at    TEXT NOT NULL,
    state      TEXT NOT NULL CHECK (state IN ('proposed', 'fixed', 'open', 'done', 'cancelled')),
    fixed_time TEXT,
    room_id    TEXT
);
CREATE TABLE plan_times   (plan_id TEXT NOT NULL REFERENCES plans (id) ON DELETE CASCADE, at TEXT NOT NULL, PRIMARY KEY (plan_id, at));
CREATE TABLE plan_people  (plan_id TEXT NOT NULL REFERENCES plans (id) ON DELETE CASCADE, user_id TEXT NOT NULL, invited_at TEXT NOT NULL, PRIMARY KEY (plan_id, user_id));
CREATE TABLE plan_answers (plan_id TEXT NOT NULL, at TEXT NOT NULL, user_id TEXT NOT NULL, answer TEXT NOT NULL CHECK (answer IN ('in', 'maybe', 'cant')), answered_at TEXT NOT NULL,
                           PRIMARY KEY (plan_id, at, user_id), FOREIGN KEY (plan_id, at) REFERENCES plan_times (plan_id, at) ON DELETE CASCADE);
```

### Rules

- **Making a plan** takes `cinema:watch`: a title, one to three times between 15 minutes and 30 days
  ahead, and the people invited (anybody `/api/people` names; the form is prefilled with visible
  wanters). The maker is `in` for every time they proposed. Each invitee gets a `plan` notification,
  pushed whatever their switches say, as an invite is.
- **Answering:** each invitee answers each time `in`, `maybe` or `cant`, and may change it until the
  plan opens.
- **A time is fixed** when the maker fixes it, or when a time reaches `Plans:FixAt` `in` answers
  (default 2, counting the maker): the first to get there. Fixing tells everybody invited (`plan`
  kind), and the other times go. People may still answer `in` to the fixed time.
- **A fixed plan's title goes first in prefetch** (`WantFetchRules.Order` orders by `WantCandidate.PlannedAt` before anything else; the fetcher leaves it null until plans fill it). The plan reports `readiness` from what is
  measured: `ready`, `fetching` with progress, `queued`, `waiting` (no release at the floor), never a
  guess at whether it will be in time.
- **Reminder:** `Plans:RemindMinutes` (15) before the time, everybody `in` or `maybe` gets a `plan`
  notice linking to the room.
- **The room is made ahead of time**, `Plans:OpenMinutes` (30) before: a room with the plan's title
  loaded, paused at the start (or the show's episode), **closed**, with everybody `in` or `maybe`
  admitted (`room_admitted`), so anybody else knocks. The plan holds `room_id` and is `open`. The
  reaper leaves a plan's room alone until `Rooms:IdleTimeout` past the fixed time, then reaps it as
  any room. Room creation goes through the same code a first entry uses; read `Sessions/CLAUDE.md`
  first, since every change to a room goes through one place.
- **Done:** the plan is `done` when its room is reaped after playing. Wants end through the ordinary
  finishing rule (`src/MovieBot.Api/CLAUDE.md`), so nothing special is needed for the people who watched.
- **Cancelling:** the maker cancels a plan before it opens; everybody invited is told.
- **A person's plans** are on `/plans` and on Home above "Watch together"; a plan is shown only to its
  maker and invitees (anybody else is answered as if it did not exist, as lists were).

### Wire

`POST /api/plans`, `GET /api/me/plans`, `GET /api/plans/{id}`, `PUT /api/plans/{id}/answers/{at}`
(`{ answer }`), `POST /api/plans/{id}/fix` (`{ at }`, maker only), `DELETE /api/plans/{id}` (maker
only). Plan changes are pushed to the people in it over the hub's per-person group
(`PersonGroups.Everything`), so an open plan page updates without polling.

### Notifications

Kind `plan`, keyed by plan and event (`invited`, `fixed`, `reminder`, `cancelled`), so each is said
once. Add it and `together` to `notification_preferences`, the contract's kinds and the site's
switches.

### Done when

Two accounts on hotbox: one plans a ready film 20 minutes out with two times, the other answers `in`
to one; the plan fixes, both are told; at 30 minutes the room exists closed with both admitted; the
reminder arrives; both join and watch; the plan ends `done`.

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
