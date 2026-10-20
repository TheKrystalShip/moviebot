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
| fetchable from the tracker | prefetches it when the conditions in section 5 hold |
| not on the tracker at the source floor | sweeps for it hourly (what a wish does today) and tells the wanters when it turns up |
| wanted by somebody who has not watched it | keeps it in the library, within a limit (section 5) |

## 2. Decisions taken (by the owner, not to be re-opened)

- **Custom lists are removed entirely**: the `lists`, `list_members` and `list_entries` tables, the
  routes, the site's Saved pages, the member picker, the `shared` notification kind, and the
  assistant's reads of them.
- **Wishes fold into wants.** `/api/wishes`, the Notify page and the `wish_subscribers` table go; the
  sweep stays and reads the wants.
- **Wants are visible to everybody by default**, with a per-title "just me" switch. Rows brought
  over from Watch later and wishes come across **just me**, because nobody agreed to be seen when
  they saved them.
- **Prefetch runs as soon as its conditions hold**, at any hour, not in a nightly window.
- **No Discord channel for plans yet.** Plans live on the site, in the bell and in Web Push.
- **Plans are in scope**: proposing times, answering, fixing a time, the room made ahead of time,
  reminders.

## 3. Cold context: what exists today

These were mapped on 2026-10-20 and are the pieces this plan builds on. Read the `CLAUDE.md` named in
each row before changing that area.

| Piece | Where | Rules |
|---|---|---|
| Lists (Watch later and custom), keyed by IMDb id, `kind` film/show, inLibrary/watched joined at read | `src/MovieBot.Api/Lists/` (`ListEndpoints`, `ListStore`, `ListViews`); site `pages/Saved.jsx`, `components/SaveButton.jsx`, `saved.js`, routes `/saved`, `/saved/{id}` | `src/MovieBot.Api/CLAUDE.md` (the list bullet), `docs/api-contract.md` "Lists" |
| Places the save state is read or toggled | site `components/{Film,FilmActions,Spotlight,TitleCards}.jsx`, `pages/{Title,Show,Discover,Profile}.jsx`, `data.js`, `api.js`; TV `tv/pages/{TvFilm,TvShow,TvDiscover}.jsx`, `tv/hero.jsx`; assistant `src/MovieBot.Assistant/Api/CinemaApi.cs` (`api/me/lists`, `api/lists/{id}`), `Tools/MyTasteTool.cs`, `Api/Dtos.cs` | |
| Wishes: catalogue-named films not yet at the source floor; hourly sweep; `available` notification | `src/MovieBot.Api/Acquisition/{Wishes,WishList}.cs`, `WishSweep`; site `pages/Notify.jsx`; bot `src/MovieBot.Bot/Notify/` (`/notify` command, `WishEmbed`) | `Acquisition/CLAUDE.md` "Wishes" |
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
| Notifications: `Notifier` is the one sender; bell keyed once per person; per-kind push switches (`available`, `ready`, `episode`, `shared`, `invite`) | `Notifications/` | `Notifications/CLAUDE.md` |
| Background work: `IHostedService` loops on intervals, registered in `Program.cs`; there is no time-of-day scheduler | `src/MovieBot.Api/Program.cs` | |
| The database: one SQLite file, `Data/Schema` steps (`[Tables, Sorts]`), `user_version` counts steps | `Data/Schema.cs`, `Data/CinemaDatabase` | `src/MovieBot.Api/CLAUDE.md` |
| Actions: `cinema:watch`, `cinema:download`, `cinema:wish`, `cinema:offline`, ... | `Auth/CinemaActions.cs` | `docs/api-contract.md` "Signing in" |
| Settings: one file, every default declared | `src/moviebot.settings.json`; `MovieBotSettings` in moviebot-acquire | repo `CLAUDE.md` Conventions |

Live data on hotbox (`/var/lib/moviebot/moviebot.db`), read 2026-10-20: 9 lists, all Watch later,
45 entries, no members and no custom lists; 15 wish subscriptions from 6 people; 2 follows. The
migration in section 4 loses nothing.

## 4. Phase 1: Want to watch

### Data

A new step appended to `Schema.Steps` (never edit the earlier steps):

```sql
CREATE TABLE title_wants (
    user_id   TEXT NOT NULL,
    imdb_id   TEXT NOT NULL,
    kind      TEXT NOT NULL CHECK (kind IN ('film', 'show')),
    visible   INTEGER NOT NULL,
    may_fetch INTEGER NOT NULL,   -- held cinema:download when wanted (section 5)
    may_wish  INTEGER NOT NULL,   -- held cinema:wish when wanted (the sweep)
    since     TEXT NOT NULL,
    PRIMARY KEY (user_id, imdb_id)
) WITHOUT ROWID;
CREATE INDEX title_wants_title ON title_wants (imdb_id);
```

The same step brings the records over, then drops what they came from:

- every `list_entries` row becomes a want of its `added_by` person, `visible = 0`, `since = added_at`,
  `may_fetch = 0`, `may_wish = 0` (the step cannot know what the person held; the flags are set the
  next time they write the want);
- every `wish_subscribers` row becomes a film want, `visible = 0`, `since = asked_at`, `may_fetch = 0`,
  `may_wish = 1` (they held `cinema:wish` to make it);
  where both exist, the earlier `since` wins;
- `list_entries`, `list_members`, `lists` and `wish_subscribers` are dropped. `wishes` stays: it is
  the catalogue's description of a film the tracker does not have yet, which the sweep reads.

The step drops tables, so the `Schema` doc comment's "only ever adding to it" is rewritten in passing
to say what a step is (a file at one version taken to the next, in one transaction), and the
`src/MovieBot.Api/CLAUDE.md` bullet likewise.

`offline_wants` is a device asking for a file and is unrelated; keep the names apart.

### Rules

- **A want is keyed by person and IMDb id**, like the list entry it replaces, so it outlives the
  title leaving the library and is the same want when it comes back. In the library, whether the
  person watched it, and who else wants it are joined in when read, never stored.
- **Wanting takes `cinema:watch`.** Whether the server may *spend* anything on it is captured at that
  moment, as a follow's auto-download is: `may_fetch` is whether the token held `cinema:download`.
  The wish sweep looks only for films with a wanter whose `may_wish` is set, the token having held
  `cinema:wish`. Both flags are refreshed from the token every time the person writes the want.
- **A show is wanted from where the person is.** Their point in it is their next unwatched episode,
  read from the watch record when asked, never stored.
- **A want ends when the person finishes the title** (the same "watched" rule the list join uses,
  for a film; for a show, when the last aired episode is watched), when they remove it, or when a plan
  they were in plays it through (phase 4). `WatchRecorder` is where finishing is seen.
- **Visible means named to everybody signed in.** A just-me want is counted by nobody else and named
  nowhere; it still drives prefetch and the library's hold, which name nobody.
- **Making a want for a film that is not fetchable** runs today's wish checks (library, torrent
  client, tracker, in that order) and makes the `wishes` row when nothing has it. The sweep tells
  every wanter with `available`, keyed as today.

### Wire (`docs/api-contract.md`, a new "Wanting" section replacing "Lists")

- `GET /api/me/wants` lists the caller's wants with title glance, kind, `visible`, `since`, `inLibrary`,
  `watched`, the show's next episode, `others` (the count of *other* people with a visible want) and
  the wish state where there is one.
- `PUT /api/me/wants/{imdbId}` with `{ kind, visible? }` makes or updates a want (visible defaults to
  true on create). `DELETE /api/me/wants/{imdbId}` removes it.
- `GET /api/titles/{imdbId}/wanters` names the people with a visible want, excluding the caller, and,
  for a show, each one's next episode. This is what the title page and the plan form read.
- `/api/me/lists`, `/api/me/saved`, `/api/lists/**` and `/api/wishes/**` are removed, along with
  their views in `ApiJsonContext`.

### Clients

- **Site:** `SaveButton` becomes a want toggle (`WantButton`) on every title surface listed in
  section 3, with "just me" in its menu. The Saved and Notify pages become one page of the person's
  wants at `/wants` (filters: ready, fetching, waiting for a release; sort by the existing sort
  surface mechanism). Title, show and catalogue pages show the visible wanters' faces. Use krystal-ui
  controls only; no explanatory prose in the UI.
- **TV:** the same toggle and state on `TvFilm`, `TvShow`, `TvDiscover` and the hero.
- **Bot:** `/notify` makes a want (same checks, same embed outcomes) through the new route.
- **Assistant:** `CinemaApi` reads `api/me/wants` where it read lists; `MyTasteTool` uses it.

### Done when

Migration run against a copy of the live database (`sqlite3 .backup` from hotbox into the scratch
area) shows 45 + 15 rows brought over minus overlaps, all just-me; `dotnet test` passes with new
tests for the store and routes; site builds and its verify suites pass; deployed per
`deploy/CLAUDE.md`; a want toggled on the live site round-trips, and a second account sees a visible
want on the title page.

## 5. Phase 2: Prefetch

A new hosted service in the API, `Wants/WantFetcher`, on a short interval (a minute), deciding at most
one fetch per pass.

### When it may start a fetch (all must hold, each measured, never assumed)

- **Nobody is watching.** No occupied room is playing (`SessionStore`), and no Watch now download is
  arriving. A fetch already running carries on if somebody starts watching; the transcode planner
  already puts rooms first.
- **The disk has room.** `DiskBudget` reads Ok, and the release's size plus the transcode reserve fits
  in the headroom it reports. A pass that cannot read the budget starts nothing.
- **Nothing else this fetcher started is still arriving** (`Wants:Concurrent`, default 1).

### What it fetches

- Candidates are titles with at least one want whose wanter has `may_fetch`, that are not in the
  library, not in the torrent client, and are offered by the tracker at the source floor.
- **Order:** a title with a fixed plan, earliest first (phase 4); then the most wanters (just-me ones
  count); then the oldest want.
- **Release:** the same picker Watch now and FollowWatcher use (source floor, `ReleaseLanguages`), at
  the best quality any of its wanters watches at. A show fetches the season holding the earliest
  next episode among its wanters, as one torrent.
- **Tags:** `want:<imdbId>` on the torrent, in moviebot-acquire's tag vocabulary (a cross-repo change:
  one commit in each repo), plus `TorrentTags.Tell` for every `may_fetch` wanter, so `ReadyNotices`
  tells them `ready` when it can be watched, unchanged.
- The tracker is asked through the existing caches (`ShowOfferCache`, the title index) so prefetch
  adds no tracker load beyond what the rate limits already allow.

### The library's hold

`LibraryClock` treats a settled title as not due while some want on it is from a person who has not
watched it and is younger than `Wants:HoldDays` (default 90). A want older than that still prefetches
nothing again by itself; it stays on the person's page. The hand-off is unchanged: it still deletes
what the API names.

### Settings (`src/moviebot.settings.json`, a new `Wants` block)

`PassSeconds` (60), `Concurrent` (1), `HoldDays` (90). Anything else a pass needs, add here with its
default and in `packaging/`'s env file.

### Done when

On hotbox, wanting a small fetchable film with no room playing starts its download within a pass,
tagged `want:`, and the wanter gets `ready`; starting a room before it begins holds it back; the
library's due list leaves a wanted, unwatched title off.

## 6. Phase 3: Matching

- **Home shelf "Watch together"**: titles the person wants that at least one other person wants
  visibly, ready ones first, each with the other wanters' faces. Served by a new view on
  `/api/me/wants` (or `GET /api/me/together`), computed at read.
- **For a show**, two wanters are together when their next unwatched episode is the same.
- **Notification kind `together`**, one digest per person per day at most, keyed by person and day:
  sent when a title the person wants is ready and somebody else visibly wants it, naming the title and
  the people. It has a push switch like every other kind, off by default for push and always in the
  bell (`Notifications/CLAUDE.md`).
- **The title page's "Plan a watch"** button opens the plan form (phase 4) prefilled with the
  visible wanters.

Done when two test accounts wanting the same ready film each see it on the shelf and each gets one
digest.

## 7. Phase 4: Plans

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
- **A fixed plan's title goes first in prefetch** (phase 2). The plan reports `readiness` from what is
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
  finishing rule (phase 1), so nothing special is needed for the people who watched.
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

## 8. How to work through this

- **One phase at a time, each its own version bump, CHANGELOG entry, tag and deploy**, per the repo
  `CLAUDE.md`. Phase 2's tag vocabulary change is a commit in `moviebot-acquire` too, addressed with
  `git -C`.
- **Do not `git add -A` in `moviebot/`**: the checkout has untracked scratch files (`*.err`,
  `*.jsonl`) that are not part of any change. Add paths explicitly.
- **Testing:** `dotnet test` (hub integration tests with two real SignalR clients is the pattern for
  plans and rooms); the site's verify suites per `web/site/README.md`. A scratch site needs the TMDB
  token passed by path (`~/Documents/tmdb_api_read_access_token`). Check `:8299` for an orphaned
  scratch server first.
- **Deploying:** `deploy/CLAUDE.md` is the authority. hotbox units may be restarted without asking,
  except that restarting the hand-off resets a running transcode; check nobody is mid-hand-off.
- **Docs move as each phase lands:** the list bullet in `src/MovieBot.Api/CLAUDE.md` and
  `Acquisition/CLAUDE.md` "Wishes" are rewritten in present tense for wants; `docs/api-contract.md`
  gains "Wanting" and "Plans"; `Notifications/CLAUDE.md` gains `together` and `plan`; then the
  phase's section here is struck.
