# The local catalogue: one entry per title, every source layered on it

A plan, not canon. What it settles moves into the docs beside the code it governs
(`docs/api-contract.md`, `web/site/README.md`, the `CLAUDE.md`s under `src/`) when it lands, and its
section here is struck. When the last phase lands, this file is deleted.

## 1. What this is for

Every title Cinema shows is **one entry in the API's own database**, built from IMDb, TMDB and Wikidata
together, and **that entry is what every page is served**: a shelf, a search result, a discovery, a
franchise, a person's films, the title page. The same film therefore carries the same rating, the same
poster and the same facts everywhere, and anything one page can sort, filter or group it by, every page
can.

**A request from the site never reaches a third party.** The upstreams are read by scheduled jobs that
write the catalogue; the API answers from the catalogue alone, and a job that fails leaves the last good
data standing. The tracker (`moviebot-acquire`) is outside this plan: it keeps reaching what it reaches.

## 2. Settled

| Question | Answer |
|---|---|
| Which titles exist | IMDb's films and shows with at least `Imdb:MinVotes` (100) votes, **and a poster**, and **that are not a placeholder** (§4·e). IMDb is the base every entry starts from; a title IMDb lacks has no entry |
| Where the jobs run | A unit of their own, `moviebot-catalogue`, writing the database the API reads |
| Franchises | TMDB's collections define them; Wikidata adds what collections cannot say (§4·f) |
| Upcoming titles | A ranking admits titles under the vote line, poster and placeholder rules still holding (§4·e) |
| Search quality | Local full-text search over the catalogue, accepted as it comes and improved later |
| Images | Kept on disk under the state directory and served by the API: every entry's poster (w500), backdrop (w1280) and logo (w500); portraits of the top-billed cast (w185); episode stills (w300) for shows in the library only. What is not kept is not shown |
| Titles shown | The English title and the original title. Language is for display only: the tracker is asked by IMDb id |
| How it is built | From scratch beside what stands, with no schema migration chain. Existing data is carried over only once the new catalogue serves the site (§7) |

## 3. The terms each source sets

- **TMDB** (`themoviedb.org/api-terms-of-use`): nothing obtained from TMDB is cached **longer than six
  months** (§1.C), images included, and TMDB is attributed with its logo and the notice "uses TMDB and the
  TMDB APIs but is not endorsed, certified, or otherwise approved by TMDB" (§3). Its layer of the catalogue
  is therefore never older than six months (§5·b). The catalogue is never handed to anyone else as a data
  set (§2.A).
- **IMDb datasets**: personal and non-commercial use, never republished as a database.
- **Wikidata**: CC0, kept indefinitely; a descriptive User-Agent; five concurrent queries per address.

## 4. The model

### 4·a Identity

A title's key is the catalogue's own integer id. `title_ids` maps it to each source's id: the IMDb `tt`
id (unique, required), the TMDB id with its kind (a TMDB film and a TMDB show can share a number) and the
Wikidata item. IMDb merges titles now and then; `imdb_redirects` maps a merged-away `tt` id to its
survivor, so a link, a want or a list entry made with the old id still resolves.

People are keyed the same way: an internal id mapped to IMDb `nm`, TMDB person id and Wikidata item.

### 4·b Layers, then the entry

Each source keeps **its own layer**, as it last answered and when:

| Layer | Holds | Comes from |
|---|---|---|
| `imdb_layer` | primary and original title, kind, years, runtime, genres; its rating and votes in `ratings` | `title.basics`, `title.ratings` |
| `tmdb_layer` | the whole `append_to_response` answer in English: overview, tagline, release dates and certificates, credits, keywords, companies, countries, languages, collection, recommendations, images, videos, seasons | `/movie/{id}`, `/tv/{id}`, `/tv/{id}/season/{n}` |
| `wikidata_layer` | the facts the catalogue filters by: set in (P840), filmed in (P915), based on (P144) and its author, awards (P166) and nominations (P1411), main subject (P921), genre (P136), series (P179) | SPARQL by IMDb id (P345) |

**The entry is built from the layers by rule, never by the order they arrived in.** Refreshing a source
replaces its layer and rebuilds the entry in one transaction. The rules:

| Field | Taken from |
|---|---|
| English title | TMDB's English title; IMDb's primary title when TMDB has none |
| Original title, original language | IMDb's original title; TMDB's original language |
| Year, runtime | IMDb |
| Genres | IMDb's, which every entry has; TMDB's are kept as a second facet |
| Overview, tagline, certificate, release dates | TMDB |
| Credits | TMDB, people matched to IMDb `nm` ids |
| Ratings | Both, as separate facts: IMDb rating and votes, TMDB rating and votes. Neither overrides the other |
| Franchise | TMDB's collection, with Wikidata's universe, order and spin-offs (§4·f) |
| Places, adaptations, awards, subjects | Wikidata |
| Poster | TMDB's first English poster, kept on disk |

An entry whose TMDB or Wikidata layer is missing is still a whole entry: the fields that layer gives are
empty, which the views say, never fill in.

### 4·c Tables the site queries

Everything a page filters, sorts, groups or searches by is a column or a join table, never JSON:

- `titles`: the built entry, one row per title.
- `title_genres`, `title_keywords`, `title_companies`, `title_countries`, `title_languages`.
- `credits` (title, person, department, job or character, order) and `people`.
- `franchises` and `franchise_titles` (order within the franchise).
- `title_facts` (title, property, Wikidata item, label): one row per place, award, source, subject.
- `similar` (title, other title, rank), from TMDB's recommendations.
- `seasons`, `episodes` for shows.
- `titles_fts` and `people_fts`: SQLite FTS5 over the English, original and alternative titles, and over
  people's names.
- `images` (title, kind, file, source path, fetched at).

### 4·d Lists hold ids, never copies

A ranking the catalogue cannot make itself (trending, popular, now playing, upcoming, charts) is kept as
`rankings` (list, rank, title id, fetched at). A shelf, a search, a discovery, a franchise and a person's
films are queries or ordered lists of ids. **The title in any of them is built at serve time from its
entry**, by one builder, into one card type; the title page's type carries everything the card carries,
from the same row.

### 4·e What becomes an entry

An IMDb title is a candidate when it has `Imdb:MinVotes` votes. It becomes an entry once its TMDB layer
gives it a poster, and stays a candidate, invisible to every page, until then. A title whose poster TMDB
removes leaves the catalogue at its next rebuild.

A ranking (§5·d) admits titles under the vote line, because what is upcoming has no votes yet. A title it
admits still needs a poster, and **an unreleased title named `Untitled …` is a placeholder and never
enters**. The name alone does not decide it: inside the vote line every `Untitled …` title is a released
film (*Untitled Horror Movie*, 2021, 1,633 votes; *Untitled Home Invasion Romance*, 2025, 1,372 votes; two
films called *Untitled*).

### 4·f Franchises

TMDB's collections are the franchises: one per collection, its films in release order. Wikidata adds
what a collection cannot say, matched to a collection by the IMDb ids of the films they share (only a
few hundred Wikidata items carry a TMDB collection id):

- **Universes above collections.** The Marvel Cinematic Universe groups ten TMDB collections and its
  shows; Middle-earth, Batman in film and Asterix each group two. A universe is a franchise of its own,
  holding its collections.
- **Shows.** A TMDB collection holds films only; Wikidata puts shows in the same franchise (Star Wars:
  19 films, 26 shows; the MCU: 45 films, 28 shows).
- **Order.** Series order (P1545 on "part of the series", P179) where release order is not the story's:
  2,480 films across 626 series carry one.
- **Spin-offs.** "Follows" and "followed by" (P155, P156) link a spin-off to the film it comes from:
  *Hobbs & Shaw* follows *The Fate of the Furious*.

## 5. Keeping it current

### 5·a IMDb, daily

The importer reads the datasets when their `HEAD` changes, as it does now: which titles exist, their
base fields and ratings. A title new to the catalogue queues its TMDB and Wikidata fetches; a title that
drops under `MinVotes` keeps its entry.

### 5·b TMDB, on the changes feed and inside six months

- **First fill**: every entry's TMDB id from `/find` by IMDb id, then one `append_to_response` call per
  title and one per season of a show.
- **Daily**: `/movie/changes` and `/tv/changes` name what TMDB edited; only those are fetched again.
- **The six-month line**: a sweep refetches the oldest TMDB layers so that none passes five months. Over
  the boundary that is roughly 1,400 titles a day.
- **Ratings**: the changes feed does not carry votes (6·a). TMDB's rating is asked for weekly for what a
  ranking holds or came out in the last sixty days, and by the six-month sweep for the rest.

### 5·c Wikidata, weekly

Batched SPARQL by IMDb id over the whole catalogue, a few thousand ids a query, inside the five-query
limit.

### 5·d Rankings, every few hours

Each list TMDB ranks is fetched to its last shown page and stored as ids; a title in it that the
catalogue lacks is fetched before the list is swapped in.

### 5·e Posters

Fetched with the TMDB layer at the sizes the site shows, refetched when TMDB's poster path changes and
inside the same six-month line.

## 6. Phases

### 6·a Measured (2026-10-25)

A random 400 films and 200 shows from the boundary, asked of each source from hotrod.

| | Films | Shows |
|---|---|---|
| Candidates (`MinVotes` 100) | 163,402 | 43,646 |
| Found on TMDB by IMDb id | 98% | 84.5% |
| With a poster, so entries | 97.5%, about 159k | 82.5%, about 36k |
| On Wikidata | 80% (91% of those with 1,000+ votes) | 80% (96% of 1,000+) |

**TMDB** held 33 requests a second at 20 at once, every one answered 200, median 0.18 s. A details call
with everything appended is 25 KB on average. A show has 2.6 seasons and 66 episodes on average. TMDB's
`external_ids` carries the Wikidata item for 84% of titles.

- **First fill**: about 207k `/find` calls, 197k details and 96k seasons, so 500k requests, **about
  4.2 hours** at that rate.
- **The six-month line**: about 2,200 requests a day.
- **The changes feed** names at least 6,000 films and 2,225 shows a day across all of TMDB, most of them
  outside the catalogue; only those inside it are fetched.
- **It does not carry votes.** The Fast and the Furious went from 11,277 to 11,282 votes in a day, and
  13 days of its changes name only keywords, videos, images and alternative titles. Over 120 changed
  films and 120 changed shows, no `vote_*` key appeared. TMDB's rating therefore has its own cadence
  (§5·b): weekly for what a ranking holds or came out in the last sixty days, the six-month sweep for the
  rest. IMDb's rating, the one a title is shown with, is refreshed daily from the dataset for nothing.

**Images**, mean size at the sizes the site uses, times the entries that have one:

| Image | Mean | Share | Whole catalogue |
|---|---|---|---|
| Poster, w500 | 92.5 KB | every entry | 17.6 GB |
| Poster, w342 | 49.3 KB | every entry | 9.4 GB |
| Backdrop, w1280 | 171.6 KB | 84% | 28 GB |
| Logo, w500 | 72.0 KB | 32% | 4.5 GB |
| Portrait, w185 | 13.3 KB | 8 of the top 15 billed | per person, deduplicated; not counted |
| Episode still, w300 | not measured | 2.4M episodes | tens of GB |

hotbox has 86 GB free on `/` (where `/var/lib/moviebot` is), 111 GB on `/home` and 157 GB on the cold
media disk.

**IMDb datasets**, compressed: `title.basics` 229 MB, `title.ratings` 9 MB, `title.akas` 519 MB,
`title.episode` 55 MB, `title.crew` 84 MB, `title.principals` 788 MB, `name.basics` 312 MB. Credits come
from TMDB (§4·b), alternative titles and episodes too, so the layer needs `basics` and `ratings` alone.

**Wikidata** answers the coverage of 600 IMDb ids in 0.6 s and fourteen fact properties for them in
1.3 s (1,473 rows), so the whole catalogue is a few hundred queries. Of the sample: genre 62%, set in
17%, filmed in 8%, based on 8%, main subject 6%, follows/followed by 4%, nominations 4%, awards 3%,
series 1%.

**The library**: `watch_progress`, `title_plays` and `offline_saves` name a library title by its slug
(`2001-a-space-odyssey-1968`, `chernobyl-s01e01`), and every library manifest carries the title's IMDb
id (199 of 199), so §7 maps them through the manifest.

### 6·b The catalogue

Landed: `src/MovieBot.Catalogue/CLAUDE.md`, `deploy/CLAUDE.md`, `docs/deploying.md`. Left of it: IMDb's
merges (`imdb_redirects`, §4·a), learned when a title's TMDB layer names another IMDb id than the title's
and IMDb's dataset no longer lists the title's own.

### 6·c The views

The card and title-page types, one builder, and the queries: search, discover with every filter the
site has, franchise, person, similar, rankings. The question reader (`/api/catalogue/ask`) resolves to
the same queries.

### 6·d The API serves from it

Each endpoint moves to the catalogue, one at a time, behind its current contract; the site does not
change while it does.

### 6·e The SPA checks

A test that asks every endpoint able to return a fixture title (The Fast and the Furious, `tt0232500`; a
show; a title with sparse data) and asserts the card is the same each time. Then a walk through every
page and filter of the site with the same titles.

### 6·f Carry over, then remove

§7, then the old metadata tables and the code that wrote them are deleted.

## 7. What carries over

Metadata does not: everything in `film_descriptions`, `show_descriptions`, `glances`, `answers`,
`trending`, `title_scores`, `images` and `franchises` is refetched, not migrated. What a person made does:
`title_wants`, `list_entries`, `follows` and `wishes` already name titles by IMDb id and resolve through
`title_ids` unchanged; `watch_progress`, `title_plays` and `offline_saves` name a library title, which
its manifest maps to an IMDb id. `poster_pins` holds which poster a title was first shown with,
which the poster rule of §4·b settles instead.

