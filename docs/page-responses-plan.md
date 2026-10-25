# Page responses: one answer per page, put together by the API

A plan, and the hand-off for whoever picks it up. It assumes nothing of the session that wrote it:
everything needed to start is here or named by path. Read `moviebot/CLAUDE.md`, the workspace's
`/home/heisen/tks/CLAUDE.md` and `src/MovieBot.Api/CLAUDE.md` first; their rules bind this work
(present-tense docs and comments, no emoji, every wire type in a JSON context, Native AOT with no
warnings, commit per finished piece with a version bump, CHANGELOG entry and `v<version>` tag, nothing
pushed unless asked).

## 1. Why

Discover loaded in a browser on the live site, with the API warm (2026-10-25, a HAR the user recorded
minutes after a restart), made 84 requests, 26 of them to the API, and finished after 5.7 s. Two things
make it slow: four kinds of request that are slow on the server for a person with a watch history
(§3), and a chain of round trips the browser makes one after another, each waiting on the last:

```
/api/catalogue/discover            (the page's rows)
  → /api/catalogue/titles × n      (each shelf's cards, as it nears the screen)
  → /api/catalogue/titles/{id} × 4 (the spotlight's four slides, each a whole title page)
    → /api/films/{id}/availability × 4  (whether each slide can be watched: asks the tracker)
```

The API already knows everything Discover shows a person: which rows, which shelves, which cards, and
how each card relates to them. So it answers the page in one response, and the browser asks for
nothing more than the images. The same holds for Home and for every page whose content the API decides,
which is most of them. Caching for special cases comes after, measured, if anything still needs it.

The user agreed this direction on 2026-10-25: page routes on the API, the catalogue routes kept as the
building blocks, the tracker kept out of page responses, images kept as their own requests, and a
client store keyed by card id fed by the page responses.

## 2. Where things stand

- **v4.0.0** (`moviebot` commit `61961b2`, tag `v4.0.0`, deployed to hotbox, not pushed) serves every
  surface from the local catalogue. `docs/local-catalogue-plan.md` is how it got there; its §6·d is done.
  The wire contract is `docs/api-contract.md` (Catalogue, and the HTTP table).
- The catalogue (`moviebot-catalogue`, `src/MovieBot.Catalogue/`) is still filling on hotbox: about a
  quarter of its ~207k titles are listed, so shelf counts are low and grow by themselves.
- The API reads it through `MovieBot.Catalogue.Store` (`CatalogueQueries`, `TitleQuery`), and serves
  it from `src/MovieBot.Api/Catalogue/` (`CatalogueEndpoints`, `CatalogueViewers` for the person's joins,
  `DiscoverIndex` for Discover, `FacetIndex`, `CatalogueTitles` for cards held five minutes).
- The site's client is `web/site/src/api/` (`catalogue.js`, `library.js`, `rooms.js`, `me.js` over
  `http.js`). Pages read through `useLoad`/`usePaged` (`src/data.js`), which ask on every mount: nothing
  is kept between pages.

## 3. Measured

Request counts are the API requests a page makes on desktop before scrolling, on the scratch site
(`verify/pages/`, §7). Server times are on hotbox, through the public address, signed in, warm.

| Page | API requests | Of them |
|---|---|---|
| `/discover` | 24 | 4 title pages and 4 availability checks for the spotlight; the index; 2 shelves; the facets; and the shell (below) |
| `/` (Home) | 26 | 4 list queries and 4 title pages for the spotlight, 2 availability checks, progress, plans, together, anticipated, and the shell |
| `/discover/film/{id}` | 14 | the page, its availability, its wanters, and the shell |
| `/movies/{id}` | 16 | the manifest, the catalogue page, its wanters, the library twice, and the shell |
| `/discover/show/{id}` | 17 | the page, `/api/me/shows`, the releases, the library's shows twice, and the shell |
| `/wants` | 11 | the wants page, and the shell |

**The shell, on every page load**: `/api/config` twice, `/api/me/screens`, `/api/me/offline`,
`/api/me/notifications` and `/kinds`, `/api/sessions` (the footer polls it every 30 s,
`components/SiteFooter.jsx`), `/api/me/offline/wants`, `/api/me/wants`, and the whole library
(`/api/titles` and `/api/shows`) for `useWhere` (`pages/DiscoverShelves.jsx`), which marks which cards
the library holds. The cards already carry that (`you.inLibrary`, `you.libraryId`), so `useWhere` reads
both whole lists for nothing on every page that shows cards.

The user's warm load of Discover on hotbox (the HAR), and the same routes measured by hand:

| Request (hotbox, warm) | Time | Why |
|---|---|---|
| `/api/catalogue/discover`, the user | **3,168 ms** | the per-person part of the page (below); as `claude`, who has watched nothing, it is 30 ms |
| `/api/catalogue/facets?minVotes=100` | **1,528 ms** | `minVotes` makes the query `Narrows`, so `FacetIndex` counts it in SQL over every title it names instead of from the rows it holds; `FacetRow` carries no votes |
| `/api/catalogue/titles?kind=film&keyword=k3335&minVotes=100&unwatched=true&size=12` | **1,062 ms** | with `minVotes` SQLite searches `titles_votes` (every listed title with 100 votes) and checks each for the keyword, instead of starting from the keyword's titles: 36 ms without `minVotes` on hotrod, 240 ms with it |
| `/api/catalogue/titles/{id}` × 4 (the spotlight) | **870–960 ms each** | not yet measured statement by statement; 180 ms on hotrod over the same data |
| `/api/catalogue/titles` (a shelf, a ranking, `similarTo`, `here`, `inLibrary`) | 7–80 ms | |
| `/api/films/{id}/availability` | 35–50 ms when the tracker's answer is kept, 0.5–2 s when it is asked | |
| everything of the shell, `/api/me/wants`, images | under 50 ms | |

**Discover's per-person part** (`DiscoverIndex.PageAsync`, `SeriesHead`) is what differs between the
user and `claude`. The likeliest cost: for every franchise the person is part way through, `SeriesHead`
reads the next film's card with a query of its own (`queries.CardsOf([next])`), and the person's rows
(`Because you watched`, `Popular here`, `Ready to watch`, `New releases`) each run a list query and a
count per request. Measure before fixing: time `PageAsync`'s parts for a person with a long history
(the scratch site's viewer can be given one through `verify/mobile/stage.mjs`'s pattern).

**Cold start.** `DiscoverIndex` reads every shared shelf's head and every franchise's titles in a
background pass, 139 shelves and 400 franchises in **~100 s** on hotbox. Until the first pass after a
restart has run, `/api/catalogue/discover` and `/franchises` wait for it (`DiscoverIndex.Current`), which
is the 8 s the HAR shows: it was taken seconds after a redeploy. The 7.6 s of `/api/me/wants` in the
same HAR was the same cold start (the torrent client's first answer); warm it is 14 ms.

## 4. What to build

### 4·a Page routes

One route per page the API controls, under `/api/pages/`, each answering everything the page draws in
one response for the asking person. The catalogue routes stay as they are: paging further, the filter
drawer, search, the bot and the assistant use them, and a page route is made of the same queries.

- **`/api/pages/discover`**: the index (`DiscoverPage` today), the spotlight's slides with what each
  slide draws (a card plus the page fields the hero shows: overview, crew names, certificate, trailer),
  and the first cards of every row's shelf, enough for its rail (`RAIL_TITLES`, 20). The shelves the same
  for everybody have their cards prepared in `DiscoverIndex`'s background pass, beside their heads; per
  request the API only joins `you` (watched, wanted, held) onto them and builds the person's own rows.
  Target: one request, ~50 ms, a compressed body around 50 KB.
- **`/api/pages/home`**: the spotlight, continue watching (progress joined with its titles), the rooms,
  plans, watch together, wants, recently added, the library's rows, anticipated.
- **`/api/pages/title/{imdbId}`** and **`/api/pages/library/{titleId}`**: the catalogue page, the library's
  side when it holds it (manifest summary, progress, last played), wanters, the shelves under the page
  (`titleShelfHeads` in `pages/DiscoverShelves.jsx`: similar, the director's, the first two stars'),
  and for a show what the person has of it (`/api/me/shows`).
- Then the rest of what the API decides, page by page: Want to watch, plans, profile tabs.

What stays out of a page response:
- **Anything that asks the tracker or the torrent client**: availability, a show's releases, Watch now.
  A page draws first and asks for those after, as it does now; one slow tracker answer never holds a page.
- **Images.** They are immutable, cached for a year, and the browser fetches them in parallel.
- **A list's further pages and a filter's answers**, which stay `/api/catalogue/titles` and `/facets`.

### 4·b The shell in one answer

`/api/me/shell` (or a better name): what every page's frame asks for once, in one response: the person's
notifications and their kinds, paired screens, offline saves and offline wants, sort preferences,
wants (the ribbon state every card reads), the rooms open now. `/api/config` is read before sign-in and
stays apart; the site asks for it twice today, which is a fix on its own.

### 4·c The client keeps what it has

A store keyed by card id (and by page) that the page responses fill: a card shown on Discover is the
same object on a title page, its `you` updated in one place when the person wants or watches it, and
moving back to a page already drawn draws it from the store while it asks again. It replaces `useWhere`
outright (the cards' `you` says where each leads), and `useLoad`'s per-mount asks for what a page route
answers. Krystal's `createStore`/`useStore` are already used for this kind of state (`src/remote.js`).

### 4·d Before the page routes: the slow things

1. **Discover's per-person part, 3.2 s** for a person with a history (§3): measure `PageAsync`'s parts,
   then prepare what is the same for everybody in the background pass (each franchise's cards, not only
   its ids) and keep per-request work to joins in memory.
2. **`minVotes` and the facets, 1.5 s**: give `FacetRow` the title's votes, and answer `minVotes` (and
   `maxVotes`) from the rows `FacetIndex` holds, so Discover's filter base (`FILTER_BASE`, `minVotes=100`)
   counts in memory. Count which other parameters push a query off the index, and treat them the same way.
3. **A selective filter beside `minVotes`, 1 s**: write the keyword, company, person and franchise
   filters so they drive the query (`t.id IN (SELECT title_id FROM title_keywords WHERE keyword_id = …)`
   or a `CROSS JOIN` from them, as ordered lists do), or keep `votes` from being chosen (`+t.votes`).
   Check every Discover shelf's query plan, since each is a list query with a count.
4. **A title page takes ~900 ms on hotbox.** `CatalogueQueries.Title` runs about twenty statements (card,
   names, companies, keywords, cast, crew, facts, franchises, follows, seasons, similar titles' cards).
   Time each on hotbox's catalogue (a `VACUUM INTO` snapshot taken there, §7) and fix the slow ones, as
   ordered id lists were fixed in 4.0.0 (`CROSS JOIN` from the list; see `CatalogueQueries.Query`). The
   catalogue is written continuously while it fills, so measure with the service running.
5. **The first Discover pass after a restart takes ~100 s.** Either keep the last pass on disk in the
   API's state directory and load it at start (it is the same for everybody, so it is safe to show while
   the next is read), or make the pass fast enough not to matter, or both. Measure which shelves cost
   the time first: each head is a list query with a count.

## 5. Order of work

1. The slow requests of 4·d, each measured before and after on hotbox, in that order. Discover's own
   requests are a few seconds of server time today; no page route fixes that.
2. `/api/pages/discover` with the shared shelves' cards from the background pass, and the site's Discover
   (web and TV: `pages/Discover.jsx`, `pages/DiscoverShelves.jsx`, `heroes.js`, `components/Spotlight.jsx`,
   `tv/pages/TvDiscover.jsx`, `tv/pages/TvDiscoverShelves.jsx`) drawn from it, the spotlight included.
3. The client store (4·c), `useWhere` gone, and the shell route (4·b).
4. `/api/pages/home`, then the title pages, then the rest.
5. Docs: the routes in `docs/api-contract.md`'s HTTP table and a section on page responses; the site's
   README (`web/site/README.md`, its Pages table names the routes each page reads); `src/MovieBot.Api/CLAUDE.md`
   (an invariant: a page route asks no outside service).

Each step is a finished piece: tests, the walk (§7) clean, deployed to hotbox, measured there, committed
with its version bump.

**Acceptance for Discover**: a cold load makes one page request plus the shell and images; nothing waits
on a chain; the page has drawn within a second on hotbox with the catalogue warm; the walk and the
action checks pass on desktop, phone and television.

## 6. Rules that bear on this

- **The API is the authority for what the design draws** (`moviebot/CLAUDE.md`). A page route is that
  rule applied to whole pages; it does not invent data, and a card is the catalogue's card unchanged.
- **Every wire type is in `ApiJsonContext`** (or Core's `ManifestJsonContext`), and a list bound for the
  wire is a `List<T>`; an unregistered type fails a test, not a request (`src/MovieBot.Api/CLAUDE.md`).
- **The API and the hand-off publish Native AOT**; a build with any IL or AOT warning is not done.
- **Leaves stand alone** and **nothing is fabricated**: a section with nothing measured is left out, not
  filled with a guess.
- **Personal data stays per person**: shared parts of a page are prepared once; anything joined from a
  person's record is joined per request and never cached across people.
- **Deploying**: `deploy/CLAUDE.md`. Redeploying while people watch is fine. A changed unit is installed
  by hand (`rsudo hotbox install …`, then `daemon-reload`); no unit change is expected here.

## 7. How to check it

- **The scratch site over a real catalogue.** Copy hotbox's catalogue once (a snapshot, then its images,
  ~20 GB over the LAN):
  ```bash
  mkdir -p ~/.cache/moviebot-verify/catalogue
  ssh hotbox 'sqlite3 "file:/var/lib/moviebot-catalogue/catalogue.db?mode=ro" "VACUUM INTO '\''/var/tmp/c.db'\''"'
  rsync -a hotbox:/var/tmp/c.db ~/.cache/moviebot-verify/catalogue/catalogue.db && ssh hotbox rm /var/tmp/c.db
  rsync -a hotbox:/var/lib/moviebot-catalogue/images/ ~/.cache/moviebot-verify/catalogue/images/
  cd web/site && MOVIEBOT_VERIFY_CATALOGUE=~/.cache/moviebot-verify/catalogue verify/mobile/scratch.sh
  ```
  The scratch site serves the production build and its own API on `127.0.0.1:8299`, with a scratch
  library and identity provider; `npm run build` while it runs updates the site, a change to the API
  needs `scratch.sh` started again (it builds both).
- **The page checks** (`web/site/verify/pages/`): `walk.mjs` loads every page at desktop, phone and
  television sizes and reports failed or slow requests, console and page errors and each page's API
  request count, with screenshots; `act.mjs` acts as a person would (paging, the drawer, wanting,
  planning, links) and checks each step's requests; `live.mjs` walks the live site signed in as
  `claude` (`tks-login browser -o <file>` gives its session). The request counts in §3 are the baseline
  the page routes should bring down.
- **Timing on hotbox**: `H=$(tks-login header cinema)`, then `curl -w '%{time_total}' -H "$H"
  https://movies.thekrystalship.com/api/…`. A restart makes the first Discover wait for the background
  pass; the API logs `Discover: N shelves and M franchises read in Ns.` when it has run.
- **Tests**: `dotnet test tests/MovieBot.Tests -c Release` builds a small catalogue with the catalogue
  service's own code (`tests/MovieBot.Tests/TestCatalogue.cs`) and serves it through the test host
  (`SessionFixture`); `CatalogueEndpointTests` and `CatalogueQueriesTests` are the patterns for new route
  and query tests. The site: `npm run lint`, `npm run check`, `npm test`, `npm run build` in `web/site`.

## 8. Known along the way

- A few hub and remote timing tests fail now and then (a different one most runs: `RemoteTests`,
  `RungHeadsTests`, `WatchRecordTests`, `AccessTests`); they touch nothing of the catalogue and pass on
  a rerun. Worth their own look.
- Duplicate franchises: Star Wars is a collection, a universe and a series; Harry Potter is Wizarding
  World and Harry Potter universe. The catalogue's franchise data needs merging (`src/MovieBot.Catalogue/`,
  `Entries/Universes.cs`); separate from this plan.
- A question's person part echoes the words typed ("christopher nolan") rather than the person's name
  (`Api/Catalogue/CatalogueQuestions.cs`).
- An episode's row has no rating: the catalogue keeps none per episode.
- Full-text search scores a whole row, so a title with many alternative names scores lower; an exact
  name is ranked first and votes weigh 1.0 (`CatalogueQueries.KnownWeight`). Check ranking against the
  full catalogue (§7), not the test catalogue.
- Ordered id lists in a query must drive it (`CROSS JOIN` from `json_each`); joined the other way,
  SQLite scans every listed title (1.2 s on hotbox).
