# Discover: free-text questions, deeper shelves, more sources

A plan, not canon. What it settles moves into the docs beside the code it governs
(`docs/api-contract.md`, `web/site/README.md`, the `CLAUDE.md`s under `src/MovieBot.Api/`) when it
lands, and its section here is struck. When the last phase lands, this file is deleted.

## 1. What this is for

Discover answers a question in words, as a person would put it: *top 10 horror movies from 2000*,
*korean thrillers from the 2010s*, *keanu reeves sci-fi*, *best stephen king adaptations*. And it
shows far more than it does: shelves that go on past their first page, and kinds of shelf the film
database alone cannot make (award winners, adaptations, where a film is set, finer subgenres).

## 2. What stands

| Piece | Where |
|---|---|
| The structured discovery: genres, years, people, keywords, language, runtime, rating and votes, sort, limit; each name resolved against the film database | `CatalogueSearch.DiscoverAsync`, `GET /api/catalogue/discover` |
| The shelves of the page, fixed questions by the database's own ids, kept fresh by `DiscoverWarmer` | `DiscoverShelves`, `GET /api/discover`, `GET /api/discover/shelves/{id}` |
| A search by name, films and shows | `GET /api/films/find`, `GET /api/titles/find` |
| The assistant's resolve-then-discover for half-remembered films | `src/MovieBot.Assistant/Tools/FindByCluesTool.cs` |

A discovery answers a page of the database at a time: 20 titles, a shelf 40.

## 3. The sources, as measured (2026-10-24)

**TMDB**, the database every title already comes from. Sixty parallel requests all answered; no rate
headers. Discover filters on cast and crew, companies, keywords (all or any), genres in and out, dates,
rating and votes, runtime, language, origin country, certification, and streaming service by region
(Netflix in Spain, horror: 381 films), up to 500 pages. One `append_to_response` call gives a title's
videos, recommendations, reviews, keywords, logos and backdrops, budget and revenue, tagline,
watch providers and Wikidata id. Its weakness is thin votes once a question is narrowed: horror from
2000 with at least 1,000 votes is 7 films. Free for non-commercial use with TMDB attributed; watch
providers are JustWatch's data and are attributed to JustWatch.

**IMDb's non-commercial datasets** (`datasets.imdbws.com`, refreshed daily). `title.basics` 229 MB
and `title.ratings` 9 MB compressed; 760k films, 109k with at least 1,000 votes; filtered in seconds.
Horror from 2000 at 10,000 votes or more ranks American Psycho (7.6, 862k votes), Pitch Black, Shadow
of the Vampire, Ginger Snaps, Final Destination. The votes are deep enough for a "top 10" to mean
something. Personal and non-commercial use only; never republished as a database. The paid IMDb API
(AWS Data Exchange, contract-priced) and scraping are out.

**Wikidata** (SPARQL, CC0, no key; a descriptive User-Agent; 5 parallel queries per address, 60 s of
query time a minute, 60 s timeout; a new query backend arrives in 2026–27 and may move the endpoint).
Each shelf question answered in 0.2–2 s: Best Picture winners 99 (complete, every one with IMDb and
TMDB ids), nominees 601, Palme d'Or 83, Stephen King adaptations 67 (every major one), set in Paris
1,927, filmed in Barcelona 173, about the Second World War 1,315, slasher 786, heist 569, neo-noir
480, found footage 198. Uneven where few people edit: Goya Best Film 12 winners, folk horror 12, body
horror 5. 239k of the 276k films with an IMDb id carry a TMDB id. `wbsearchentities` resolves a name
to its item (Stephen King → Q39829).

## 4. Phases

### 4·a The question reader

Landed: `docs/api-contract.md` (`/api/catalogue/ask`), `web/site/README.md` (Discover).

### 4·b Ask the assistant

Landed: `web/site/README.md` (Discover).

### 4·c Deeper shelves

Landed: `docs/api-contract.md` (`/api/catalogue/discover`, `/api/catalogue/ask`, `/api/discover/shelves/{id}`), `web/site/README.md` (Discover).

### 4·d IMDb ranking

Landed: `docs/api-contract.md` (IMDb's datasets), `src/MovieBot.Api/CLAUDE.md`.

### 4·e Wikidata shelves and facts

Landed: `docs/api-contract.md` (Wikidata), `src/MovieBot.Api/CLAUDE.md`.

### 4·f TMDB extras

Landed: `docs/api-contract.md` (What a description carries, `/api/catalogue/discover`, `/api/catalogue/ask`, `/api/discover`), `src/MovieBot.Api/CLAUDE.md`.

### 4·g Design phase (required before 4·c–4·f reach the page)

**Not to be skipped.** Once the data of 4·c–4·f is served, Discover and the title page are designed
around it in the Cinema design: how a question's reading is drawn and edited, how many shelves the
page holds and in what order, where award, adaptation and place shelves sit, how trailers and where-to-watch sit on a title page, and the attribution TMDB,
JustWatch and IMDb require. Until it is done, 4·c–4·f are served by the API and drawn only as the
existing components already draw them.
4·f serves the design a title's trailers, providers with JustWatch's attribution, recommendations, reviews, logo, tagline, box office, keyword links and Wikidata awards and sources to place.

## 5. Order

4·a and 4·b together (the hand-off is where the reader's misses go), then 4·c and 4·d (they
change what a question answers), then 4·e and 4·f, then 4·g.
