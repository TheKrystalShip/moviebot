# Discover: every franchise worth a shelf

A plan, not canon. What it settles moves into the docs beside the code it governs
(`docs/api-contract.md`, `web/site/README.md`, `src/MovieBot.Api/CLAUDE.md`,
`src/moviebot.settings.json`, `packaging/config/moviebot.env.example`) when it lands, and this file
is deleted then.

## 1. What this is for

Discover's Franchises category (`/discover?browse=franchises`, and the franchise row on `/discover`)
shows only the series someone typed into the code. The film database has thousands of series, and a
few hundred of them are ones people know: Avatar, Planet of the Apes, How to Train Your Dragon,
Taken, The Mummy, Austin Powers, Pacific Rim and more are all missing. This plan finds those series
in the background, ranks them, and shows them as franchise shelves. The hand-picked ones stay first.
Each person sees the series they are partway through ahead of the rest.

## 2. What stands

| Piece | Where |
|---|---|
| The franchises, a fixed list of 22 `(id, title, TMDB collection id)` tuples turned into `ShelfSource.Collection` shelves in section `franchises` | `src/MovieBot.Api/Films/DiscoverShelves.cs`, `Shelves()`, the block after the comment `// Franchises: each series' films, in the order they came out.` |
| `Shelf`, `ShelfSource.Collection`, `Shelf.DatabaseId`; `Fixed` (static, every shared shelf) and `ById` (static lookup) | `DiscoverShelves.cs`, top of file |
| A series' films: `CatalogueSearch.CollectionAsync(collectionId)`, kept under `collections/{id}` for `CatalogueSearch.CollectionKept` (7 days), whole in one answer (no page 2) | `src/MovieBot.Api/Films/CatalogueSearch.cs` |
| The database call: `TmdbClient.CollectionAsync` → `TitleCollection { TmdbId, Name, Films: ListedTitle[] }`, each `ListedTitle` carrying `Votes`, `Released` and `Popularity`. Parts are not filtered for `adult` | `../moviebot-acquire/src/MovieBot.Acquire/Tmdb/TmdbClient.Discovery.cs`, `Discovery.cs`, `TmdbModels.cs` (`TmdbCollection`, `TmdbListedTitle.Adult`) |
| The index: `IndexAsync` draws every shelf of `moods` and `franchises` as a tile (`Drawn`): first four posters, `total`, `more`, and for a series `watched` and `next` (via `Shortened`) from `_known`, the in-memory first page of each shared shelf. `KnownAsync` asks any shelf not yet known, four at a time | `DiscoverShelves.IndexAsync` |
| The warmer: `DiscoverWarmer` calls `DiscoverShelves.WarmAsync` every `Tmdb:ShelvesWarmMinutes` (60). It walks `Fixed`, asks each shelf's first page (asks the database only for what is due) and fills `_known` | `DiscoverWarmer.cs`, `DiscoverShelves.WarmAsync` |
| The background pattern to copy: `WikidataWarmer` (a `BackgroundService` that asks only what has come due, slowly, and logs what could not be answered) and `ImdbImporter`/`ImdbDatasets` (a typed `HttpClient` that streams a gzip file without holding it whole, with a stub handler in tests) | `WikidataWarmer.cs`, `WikidataSets.WarmAsync`, `ImdbImport.cs` |
| The site: `TileRail` (the row on `/discover`), `CategoryPage` (a series category is a `kc-browse-grid` of `ShelfTile` strips from `section.shelves`, its count `section.shelves.length`), `MoreCards` (paging as the grid's end nears) | `web/site/src/pages/DiscoverShelves.jsx` |
| The television: the same, as `TvShelfTile` rows that wrap | `web/site/src/tv/pages/TvDiscoverShelves.jsx` |
| Tests: `star-wars` and `james-bond` are asserted by id; the fake database answers `/collection/<Series>` | `tests/MovieBot.Tests/DiscoverShelvesTests.cs`, `tests/MovieBot.Tests/FilmDatabaseStub.cs` |

## 3. The sources, as measured (2026-10-25)

**TMDB's daily export of every collection id.** It is at
`https://files.tmdb.org/p/exports/collection_ids_MM_DD_YYYY.json.gz`, with no key, about 165 KB
gzipped. It is one JSON object per line, `{"id":10,"name":"Star Wars Collection"}`. Each day's file
appears in the morning (UTC) and older days stay up, so yesterday's is always there. The
2026-10-24 file has 9,999 lines with unique ids up to 1,805,791, including all 22 current ids and
7,656 ids above 500,000. A round 9,999 may mean TMDB caps the file. New collections are in it all the
same, so the cap only matters if a known series turns out to be missing (check that in §7).

**`GET /3/collection/{id}`**, the call `TmdbClient.CollectionAsync` already makes. It is one call per
collection, and its `parts` carry `vote_count`, `release_date`, `popularity` and `adult`. 400 random
collections from the export, asked 8 at a time, all answered. Scaled to the whole export:

| Filter (released films, sum of the parts' TMDB vote counts) | Estimated series |
|---|---|
| 2+ films | ~9,500 |
| 2+ films, ≥ 1,000 votes | ~1,250 |
| 2+ films, ≥ 5,000 votes | ~475 |
| 2+ films, ≥ 7,500 votes | ~400 |
| **2+ films, ≥ 10,000 votes** | **~300** |
| 3+ films, ≥ 10,000 votes | ~200 |

At 2 films and 10,000 votes, the sample gives The Mummy, How to Train Your Dragon, Taken,
Planet of the Apes (Reboot), Austin Powers, John Wick, Avatar, Bird Box, Wreck-It Ralph,
Pacific Rim, Naruto and Suicide Squad. Requiring 3+ films drops real two-film franchises (Pacific Rim,
Wreck-It Ralph) and series still being made (Dune has two films out). The sample is 400 entries, so
these counts are rough. The first full scan gives the real ones (§7).

**Wikidata** links 4,057 distinct TMDB collection ids (P7986) and types about 2,200 items as film
series (Q24856). Every collection it links to is also in TMDB's export, so it adds no franchise. It is
left out of this plan.

**IMDb's datasets** do not group films into series. They add nothing here.

## 4. The design

### 4·a The scan: `Films/FranchiseScan.cs` (new)

- **`FranchiseExport`** is a typed `HttpClient` like `ImdbDatasets`, so a test gives it a handler.
  - It reads one day's export file, streamed and gzip-decoded line by line, never held whole or
    written to disk.
  - It tries today's file (UTC), then each earlier day back to `Franchises:ExportLookbackDays`
    (default 3).
  - It yields `(int Id, string Name)` pairs and skips lines that do not parse.
- **`FranchiseScanner`** is a `BackgroundService` shaped like `WikidataWarmer`:
  - It waits `FirstPass` (e.g. 3 minutes, after `DiscoverWarmer`'s 2), then loops every
    `Franchises:WarmMinutes`.
  - Each pass reads the export once a day at most. It holds the ids in memory for the pass, never
    persisted.
  - It asks `GET /collection/{id}` for every id whose digest (below) is due. A digest that passed
    the threshold last time is due after `Franchises:RefreshDays` (7). Any other is due after
    `Franchises:OutsiderRefreshDays` (30), since a forgotten series rarely becomes a known one
    within a month. An id with no digest is due at once.
  - It asks at most `Franchises:RequestsPerSecond` (default 4) calls a second, one at a time. The
    first full scan of about 10,000 ids then takes about 42 minutes. After that, a week sees roughly
    300 re-asks of passing series plus about 2,300 others, about 10 minutes.
  - It calls `TmdbClient.CollectionAsync` through a scope, as `WikidataSets` does with its client.
    It does not go through `AnswerKeeper`: keeping 10,000 full answers would put tens of MB of
    series nobody shows into `answers`. The full answer of a series that is shown is kept by the
    shelf path as it is now (`CatalogueSearch.CollectionAsync` from the warmer).
  - An id the database answers 404 or with no name for loses its digest. A call that throws
    `HttpRequestException` leaves the digest as it was and counts as unanswered. The pass logs that
    count at Warning, as `WikidataWarmer` does.
  - When the pass ends, it rebuilds the in-memory catalogue (4·c).
  - It is off with `Franchises:Enabled` false, `Franchises:WarmMinutes` 0, or no TMDB key
    (`TmdbOptions.IsConfigured`).

### 4·b What is kept: a `franchises` table (new schema step)

Add one step at the end of `Schema.Steps` (`src/MovieBot.Api/Data/Schema.cs`). Another session may
have added a step since this was written, so append after whatever the last step is:

```sql
-- A series the film database lists, as the franchise scan last measured it.
CREATE TABLE franchises (
    collection_id INTEGER PRIMARY KEY,
    name          TEXT NOT NULL,     -- the database's name, "Star Wars Collection"
    films         INTEGER NOT NULL,  -- released, non-adult parts
    votes         INTEGER NOT NULL,  -- sum of those parts' vote counts
    newest        TEXT,              -- latest release date among them, ISO
    asked_at      TEXT NOT NULL
);
CREATE INDEX franchises_rank ON franchises (votes DESC);
```

This is columns rather than a JSON value because the choice of what to shelve is a query over
exactly these numbers ("films ≥ ?, votes ≥ ?, by votes"), and they are the columns it is looked up
and aged by. The store is `Films/FranchiseStore` (or inside `FranchiseScan.cs`): one row per write,
each in its own immediate transaction, as `Data/CinemaDatabase` does everywhere. When writing it,
copy the shape of another store's file, `Films/TitleScores.cs` for one.

What counts:
- **A film:** a part with a `release_date` on or before today and `adult` not true.
- **Votes:** the TMDB `vote_count`, used only to rank. It is never shown, so the one-rating rule in
  `src/MovieBot.Api/CLAUDE.md` is untouched.

### 4·c The catalogue in memory: `Films/Franchises.cs` (new singleton)

- **What it holds:** the chosen series, as `Shelf` records in section `franchises` with
  `Source = ShelfSource.Collection` and `DatabaseId = collection_id`.
- **When it is built:** at startup from the table, and again after each scan pass.
- **The choice:** `films >= Franchises:MinFilms (2) AND votes >= Franchises:MinVotes (10000)`,
  ordered by `votes DESC`, at most `Franchises:MostShown` (400). The cap stops a loose threshold from
  flooding the page.
- **The curated series:** the 22 move out of `DiscoverShelves.Shelves()` into `Franchises` as a fixed
  `Curated` list. They keep their ids (`star-wars`, ...) and titles and are always first, in their
  written order, whatever the scan says. A scanned collection whose id is curated is not added again.
  Curated series are shown on a host with no scan, no key or the scan off, so nothing regresses there.
- **The title:** the database's name with a trailing ` Collection` removed (`Star Wars Collection` →
  `Star Wars`). A trailing parenthetical such as `(Reboot)` or `(Live-Action)` stays, since it tells
  two series apart.
- **The id:** a slug of the title (lowercase, ASCII letters and digits, runs of anything else turned
  into `-`, ends trimmed: `planet-of-the-apes-reboot`).
  - When two chosen series slug the same, or a slug is any fixed shelf's id, every one after the
    first takes `-<collection_id>`.
  - Ids are written into addresses (`/discover?shelf=<id>`), so a series keeps its id while its
    name stands. The slug comes from the name alone, never from the rank.
- **Its interface:** `IReadOnlyList<Shelf> All` (curated, then scanned) and
  `Shelf? Find(string id)`, both read lock-free off a reference that is swapped whole on each
  rebuild.

### 4·d `DiscoverShelves` reads franchises from `Franchises`

`Fixed` stays static, without the franchises. Wherever "every shared shelf" is read, it becomes
`Fixed` plus `franchises.All`:

- **`Find`:** `ById.GetValueOrDefault(id) ?? franchises.Find(id) ?? Personal(...) ?? ...`.
- **`IndexAsync` `shown`:** add `franchises.All`. The section count and caption cover all of them.
- **`WarmAsync`:** walk `franchises.All` after `Fixed`, so every chosen series' first page sits in
  `_known` and the answer stays in `answers`.
  - The cost: `collections/{id}` is kept 7 days, so a warmer pass asks only for what is due. That is
    about 300 more calls a week, spread out.
  - `DiscoverWarmer`'s log line counts `Fixed.Count + franchises.All.Count`.
- **`SeriesSection` drawing in `IndexAsync`:** the index stops carrying every series as a tile (4·e).

Inject `Franchises` into the `DiscoverShelves` constructor and register both (`Franchises`, and
`FranchiseScanner` as a hosted service plus `FranchiseExport` as a typed client) where `WikidataSets`
and `WikidataWarmer` are registered (`src/MovieBot.Api/Program.cs`, beside
`AddHttpClient<WikidataClient>` and `AddHostedService<WikidataWarmer>`).

### 4·e The index and a new paged route for a category of series

Today the index carries every series as a drawn tile. At 300+ series that is a large index answer, and
on a cold start `KnownAsync` would ask 300 shelves before `/api/discover` answers.

- **The index carries the row only.** In `IndexAsync`, the `franchises` section's `shelves` is the
  row's tiles, at most `RowTiles` (24): the viewer's series in progress, then the curated ones, both
  drawn as now. `KnownAsync` is asked for those only, not for every series.
  - New on `ShelfSectionView`: `int Count`, how many shelves the category holds in all, which the
    category page's heading shows. `Caption` keeps counting all of them. Fill `Count` for every
    section (it equals `Shelves.Count` for any category that is not series), so a page reads one
    field.
- **The order a person sees:**
  1. **In progress:** `0 < watched < total`, most recently watched first. When reading the record
     for that is awkward, use the order of `Franchises.All` instead.
  2. **Not started:** curated first in written order, then scanned by votes.
  3. **Finished:** `watched == total`, last.

  `watched` and `total` already come from `_known` and `LibraryShelf.Saw` in `Drawn`. Factor `Drawn`
  out of `IndexAsync` into a private method both callers use.
- **`GET /api/discover/sections/{id}?page=`** (new) answers
  `ShelfSectionPageView { id, label, icon, series, count, shelves: ShelfHeadView[], next? }`.
  - **Pages:** 40 heads a page, at most `CatalogueSearch.PagesListed` pages, in the order above for
    the asking person.
  - **Which categories:** only a series category (`series: true`). Any other id is `404`, since
    other categories carry all their shelves in the index already.
  - **Unknown shelves:** a head whose shelf `_known` does not hold yet is asked through `KnownAsync`
    for that page only, four at a time, as the index does now.
  - **Auth:** the same as `/api/discover` (read its `MapGet` in `DiscoverEndpoints.cs` and copy the
    `RequireAction...` call).
  - **Serializer:** register `ShelfSectionPageView` and its `List<ShelfHeadView>` in `ApiJsonContext`
    (`src/MovieBot.Api/CLAUDE.md`: every type on the wire is named, and a list is built as a
    `List<T>`).
  - **Tile ordering:** a series with `total == 0` (the database has no released films under it) is
    left off, as the site does now with `h.total !== 0`.

### 4·f The site and the television

- **`web/site/src/api.js`:** add `discoverSection: (id, page = 1) => call("GET", withQuery(\`/api/discover/sections/${enc(id)}\`, { page: page > 1 ? page : null }))`.
- **`CategoryPage`** (`pages/DiscoverShelves.jsx`): for a `section.series`, read the grid from
  `usePaged((page) => api.discoverSection(id, page), [id])` instead of `section.shelves`.
  - Page the grid with `MoreCards`. Check how `usePaged` names its list (`titles` for a shelf) in
    `web/site/src/data.js`. It may need a way to take `shelves` instead.
  - The heading count is `section.count`.
  - Show a skeleton of tiles while the first page loads.
- **`TileRail`** (the row on `/discover`): no change beyond the subtitle, which reads
  `counted(section.count, "shelf", "shelves")` so it says how many there are in all, not how many
  the row shows. See all already leads to the category.
- **`tv/pages/TvDiscoverShelves.jsx`:** the series category page pages the same way. Television
  focus has rules of its own (`web/site/README.md`, the television section; the `tv-focus-harness`
  and `tv-perf-harness` memories), so the next page's tiles join the wrapping rows without moving
  focus.

### 4·g The acquire half (`../moviebot-acquire`)

`TmdbClient.CollectionAsync` leaves out parts with `adult == true`, the same way the credits code
does at `TmdbClient.Discovery.cs` line ~295 (`part.Adult == true`). It is one line, with its own patch
version and CHANGELOG entry there and one commit in that repo. The scan counts what the client
returns, so the filter belongs where every caller benefits. A shelf's films get the same filter.

## 5. Settings

Every setting goes in `src/moviebot.settings.json` with its default and a comment in the file's own
style. It also goes in `packaging/config/moviebot.env.example` (it names every setting) and in
`docs/deploying.md` if that guide lists optional ones. None is required to start, so `install.sh` is
untouched. The options class is `FranchiseOptions`, `Section = "Franchises"`, resolved inside the DI
factory (`src/MovieBot.Api/CLAUDE.md`).

```jsonc
"Franchises": {
  "Enabled": true,
  "ExportBaseUrl": "https://files.tmdb.org/p/exports/",
  "ExportLookbackDays": 3,
  "WarmMinutes": 60,
  "RefreshDays": 7,
  "OutsiderRefreshDays": 30,
  "RequestsPerSecond": 4,
  "MinFilms": 2,
  "MinVotes": 10000,
  "MostShown": 400
}
```

The test host sets `Franchises:Enabled` false and `Franchises:WarmMinutes` 0 beside the
`Wikidata:*` lines in the fixture (`tests/MovieBot.Tests/*Fixture*.cs`, around `Imdb:Enabled`), and
registers `FranchiseExport` with a stub handler the way `WikidataClient` is.

## 6. Steps, in order

Check `git -C moviebot status` and `git -C moviebot-acquire status` first.
- **Uncommitted changes:** when this was written, another session had uncommitted work in
  `DiscoverShelves.cs`, `CatalogueSearch.cs`, `CatalogueFacets.cs`, `ImdbRatings.cs`, a new
  `CatalogueWholeShelves.cs`, and in acquire's `ListedTitle.cs` and `TmdbClient.Discovery.cs` (shelves
  read whole for the filter). Do not commit or revert anyone else's changes. Wait for that work to be
  committed, or build on top of it, and stage only this feature's files.
- **Adding to `WarmAsync`:** that work changed `WarmAsync` (it holds query shelves whole). Add the
  franchise walk without disturbing it.

1. **Acquire:** filter `adult` parts in `CollectionAsync` (4·g). Bump the patch version, add the
   CHANGELOG entry, commit, tag.
2. **The store and schema step** (4·b) and `FranchiseOptions` plus settings (§5).
3. **`FranchiseExport` and `FranchiseScanner`** (4·a).
4. **`Franchises`** (4·c), and move the 22 curated tuples into it.
5. **`DiscoverShelves`** reads `Franchises` (4·d).
6. **The index row, `Count`, and `GET /api/discover/sections/{id}`** (4·e).
7. **The site and TV** (4·f).
8. **Tests** (below), the docs (§8), version, CHANGELOG, then build, test and deploy (§7).
9. **Commit:** one commit in moviebot, one in moviebot-acquire, each tagged. Commits stay local; do
   not push.

**Tests** (`tests/MovieBot.Tests/`; `dotnet test` runs them):
- **The export reader:** reads a gzipped stub of three lines, one malformed and skipped; falls back
  a day when today's file is a 404.
- **The scan:** a pass over a stub export asks each collection once; writes a digest with films and
  votes counted as §4·b says (an unreleased part and an adult part do not count); a second pass
  asks nothing still fresh; an outsider is due after `OutsiderRefreshDays` and a passing one after
  `RefreshDays` (use `TimeProvider`, as the fixtures do for clocks); a 404 removes the digest.
- **`Franchises`:** the threshold and `MostShown` choose and order; curated come first and are not
  repeated; `Star Wars Collection` is titled `Star Wars` with id `star-wars-…` deduplicated against
  the curated `star-wars`; two series that slug alike both get distinct ids.
- **The index:** the `franchises` section carries at most the row's tiles, a series in progress
  first, and `count` for all of them.
- **The section route:** pages of 40 with `next`; a series the viewer finished comes last; a
  non-series id is 404; unauthenticated is refused as `/api/discover` is.
- **The existing tests** asserting `star-wars` and `james-bond` keep passing unchanged, since the
  curated ids stand.
- **The site:** `npm test` in `web/site` if it covers `DiscoverShelves.jsx`. Check
  `web/site/package.json` for the test and visual commands, and the smoke described in
  `web/site/README.md`.

**Version:** a minor bump on the repository's one clock (the newest CHANGELOG heading, read by
`scripts/version.sh`; a new route, a new field and a new table). The CHANGELOG entry is one
`### Added` paragraph in the house style (read the top entries): first what a person now sees, then
the wire changes (`/api/discover/sections/{id}`, `count` on a section) and the settings. Tag
`v<version>`.

## 7. Verifying on the live host

The API runs on hotbox; MovieBot's deploy authority is `deploy/CLAUDE.md`. Redeploying while
people watch is safe and needs no asking.

1. **Deploy** the API and the site as `deploy/CLAUDE.md` says.
2. **Watch the first scan** in hotbox's journal (`journalctl -u moviebot-api`). It should take about
   40–45 minutes at 4 a second. Then measure the real counts:
   `sqlite3 /var/lib/moviebot/moviebot.db "SELECT count(*) FROM franchises; SELECT count(*) FROM franchises WHERE films>=2 AND votes>=10000;"`.
   The file is `0600` and holds every person's records, so read it with `rsudo hotbox` (with
   `</dev/null`) and never write to it by hand.
3. **Check the top of the list by eye:** `SELECT name, films, votes FROM franchises ORDER BY votes DESC LIMIT 60;`.
   Look for known series that are missing, which would point at the export's 9,999 cap (§3), and
   for anything that should not be a franchise shelf, such as anthology or "collection" groupings.
   When the threshold needs moving, change the settings default, not code.
4. **Check the site:** open `https://movies.thekrystalship.com/discover?browse=franchises` signed in
   (`tks-login browser -o <file>` gives a Playwright storage state). The grid pages as it is
   scrolled, a series the account is partway through is first, and the heading count matches the
   table's chosen count plus the curated ones not among them. Check the row on `/discover` and a new
   series' shelf page (`/discover?shelf=avatar`).
5. **Check a television:** the same pages under the TV build if the deploy carries it, with the
   television checks `web/site/README.md` names for Discover.
6. **Measure the index:** time `GET /api/discover` against the deploy before. The index must not
   grow with the number of series.

## 8. Docs to update when it lands

All of these are written in present-tense canon, per the workspace `CLAUDE.md`.

- **`docs/api-contract.md`:**
  - `GET /api/discover`: the `franchises` section's `shelves` is the row's tiles in the person's
    order, and `count` is on every section.
  - The new `GET /api/discover/sections/{id}`.
- **`web/site/README.md`:** the `/discover?browse=<id>` row (Franchises pages its strips from the new
  route) and the television section's line on the franchises.
- **`src/MovieBot.Api/CLAUDE.md`:** in the film database invariant, a sentence on the franchise scan:
  - it reads TMDB's daily export of collection ids and measures each series in the background,
    slowly, into `franchises`;
  - the shelves are the curated series and the best measured ones;
  - no page asks the database for it.
- **`src/moviebot.settings.json`** and **`packaging/config/moviebot.env.example`**: the `Franchises`
  block, in the files' own comment style.
- Delete this file.
