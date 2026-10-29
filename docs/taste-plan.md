# Taste: likes and dislikes, and what Cinema recommends from them

A plan, not canon. What it settles moves into the docs beside the code it governs
(`docs/api-contract.md`, `web/site/README.md`, the `CLAUDE.md`s under `src/`) when it lands, and its
section here is struck. When the last phase lands, this file is deleted.

## 1. What this is for

A person tells Cinema what is and is not for them, with **Like** and **Dislike** on every title,
person, franchise and studio, at any time and without watching anything, and Cinema answers with
**For you**: what they would most like to watch next, wherever it plays from, each with the reason it is
there. Voting is meant to be fast: a deck of cards gives a profile enough to work from in a few minutes,
so recommendations are useful from the first day rather than after months of watching.

## 2. Settled

| Question | Answer |
|---|---|
| What a vote is | Like or Dislike; pressing the lit one again clears it. No third level |
| What a vote means | "This is, or is not, for me". The same whether or not the person has seen it: what they watched is recorded apart (`watch_progress`, `watched_marks`) |
| What can be voted on | Films and shows (a show as a whole, never a season or an episode), people, franchises, studios. Genres and keywords have no buttons; the deck and the taste survey reach them |
| A person's vote and their roles | One vote per person, weighed by the role in each title: directing and top billing count most, a cameo least (§4·b) |
| What a dislike hides | The title, from For you and every recommendation, nothing else. Search, the library and browsing show it; Discover's filter drawer gains Hide disliked |
| Watching without voting | Counts, weakly: finished, abandoned early, wanted and followed are small signals; an explicit vote always overrides them, and the taste page says they count |
| Fading | Explicit votes never fade; a watching signal's weight halves each year |
| Where For you draws from | The whole catalogue, one list. The library and the catalogue are not told apart: a card plays from where it plays, as every result does |
| How it is scored | In code, deterministically, against the catalogue. No model in the scoring path; the assistant reads the result and never produces it |
| A match score | None. A card carries its reason ("Because you liked Arrival", "You like Denis Villeneuve"), never a percentage |
| Who sees a vote | Only the person who cast it. Votes feed group recommendations as an aggregate and are never shown per person |
| "Liked by people you watch with" | Not built; it would show votes §2 keeps private |
| Group recommendations | For a room and for a plan: each member's score, nothing any member disliked, ranked by the lowest member's score (§4·d) |
| Votes on cards | In a card's menu (long press, or hover), the same vote as the page's, so For you is tuned from itself |
| After the credits | Each viewer is asked, privately, whether they liked it. Never shared state: the room's state carries nothing of it |
| Television and Discord | The TV app's title page has the two buttons; the bot has none |
| Seeing and undoing | A taste page under Profile: every vote with undo, what Cinema reads the person as liking (the profile's strongest features, as they are), and reset |

## 3. What stands to build on

| Piece | Where |
|---|---|
| A person's records keyed by what a title is (IMDb id), not by a library title | `title_wants`, `watched_marks` in `src/MovieBot.Api/Data/Schema.cs` |
| A person's writes ordered and pushed to their other devices | `People/Revisions.cs`, `People/ViewerPush.cs` |
| Every feature a title has, as columns and join tables | `titles`, `title_genres`, `title_keywords`, `credits`, `title_companies`, `franchise_titles`, `title_languages` in the catalogue's schema |
| TMDB's recommendations beside each title, joined to listed titles | `similar` |
| IMDb's rating and votes per title | `ratings` |
| The one list query every shelf and Discover page is | `MovieBot.Catalogue.Store/TitleQuery.cs`; the person's joins (`you`, `Only`, `Except`) are the API's |
| What the assistant knows of a person's taste | `my_taste` (`src/MovieBot.Assistant/Tools/MyTasteTool.cs`), the `taste-survey` and `recommend` skills |

The person's records live in `moviebot.db`, the catalogue in `catalogue.db`, which the API opens
read-only. Taste is the API's: it writes votes to the first and scores against the second.

## 4. The model

### 4·a Votes

```sql
CREATE TABLE taste_votes (
    user_id      TEXT NOT NULL,
    subject_kind TEXT NOT NULL CHECK (subject_kind IN ('title', 'person', 'franchise', 'company')),
    subject_id   TEXT NOT NULL,
    vote         INTEGER NOT NULL CHECK (vote IN (-1, 1)),
    voted_at     TEXT NOT NULL,
    PRIMARY KEY (user_id, subject_kind, subject_id)
) WITHOUT ROWID;
```

A subject is named by an id that outlives a rebuild of the catalogue, never the catalogue's own integer:
a title by its IMDb id, a person by their TMDB id, a franchise by its source and source id (`tmdb:<n>`,
`wikidata:Q<n>`), a company by its TMDB id. Cleared is no row.

| Route | Does |
|---|---|
| `PUT /api/me/votes/{kind}/{id}` | `{ "vote": 1 \| -1 }`; answers the record with its revision |
| `DELETE /api/me/votes/{kind}/{id}` | Clears it |
| `GET /api/me/votes` | Every vote, newest first, each with its subject's card |
| `GET /api/me/taste` | The profile's strongest features, liked and disliked, as the taste page shows them |

Every title, person, franchise and company the API serves carries `myVote` for the person asking, so
a page draws its buttons lit with no second read. A vote is pushed to the person's other devices as
preferences are.

### 4·b The profile

A profile is a weight per **feature**: a genre, a keyword, a person, a company, a franchise, an
original language, a decade. It is built from the person's signals whenever one changes, and kept.

- **A vote on a title** adds its sign to each of the title's features, each scaled by how rare the
  feature is across the catalogue (inverse document frequency), so Drama says little and a director
  says much. People are scaled by role: the director, writers and the first-billed cast fully, the
  cast below in falling steps, and nobody past a billing cut-off.
- **A vote on a person, franchise or company** sets that feature directly, at several times a title's
  share, since it is the person saying exactly what they mean.
- **Watching** adds a fraction of a vote: finished is a small like, stopped early (under a fifth, after
  ten minutes) a small dislike, a want or a follow a small like. A title the person voted on takes its
  vote and none of these.
- Weights are normalized by how many signals the person has, so the hundredth vote moves the profile as
  much as it should and no more.

The weights, cut-offs and fractions are settings in `src/moviebot.settings.json`, tuned against the
evaluation in §5.

### 4·c For you

1. **Candidates**, through the catalogue's indexes and never by scanning it: the `similar` titles of
   every liked title, the credits of liked people, the titles of liked franchises and companies, and the
   titles of the profile's strongest keywords. A few thousand titles.
2. **Score**: the profile's match (the sum of the candidate's feature weights), plus a neighbour term
   (how many liked titles name it in `similar`, by rank), plus a quality prior (IMDb's rating shrunk
   toward the mean by its votes), so a strong match with no audience does not lead.
3. **Leave out** what the person disliked, watched, marked watched, or holds a dislike on a feature of
   strongly enough to outweigh the match.
4. **Diversify** the head of the list (maximal marginal relevance over the features), so it is not one
   director's work end to end.
5. **Reason**: the term that contributed most, written as the card's line: the liked title it is
   similar to, the liked person in it, the liked franchise it belongs to.

For you is a list query of its own (`for-you`) beside the others, so it is Home's row, a sort in
Discover's drawer, and a See all page, a page at a time like any list. It is computed when asked and
kept per person until their next signal; a change to the catalogue does not wait for one. Its cost is
timed against hotbox's `catalogue.db` with the SQLite the API ships, per
`src/MovieBot.Catalogue/CLAUDE.md`.

With no signals, For you is not shown; Home offers the deck in its place.

### 4·d Together

A room's and a plan's recommendations score each candidate for every member and rank by the lowest of
those scores, leaving out anything any member disliked. A member with no signals counts for nothing,
neither lowering nor raising a score. The answer names no member's vote; its reason is the group's
("Like titles all of you liked").

### 4·e The deck

Profile → Tune your taste: one card at a time, Like, Dislike or Skip, by button, swipe or keys.
Cards are chosen to teach the profile most: well known (enough IMDb votes that the person has likely
heard of it), divisive (a wide spread of ratings), and drawn toward the features the profile knows
least, across genres, eras, languages and kinds. Every so often a person or a franchise is asked in
place of a title. A skip is remembered so the card is not asked again, and is no signal. The taste
survey's last step links to the deck.

## 5. Evaluation

Scoring is code, so it is tested: a fixture catalogue and fixture votes give a For you whose order is
asserted. Tuning uses a held-out check: for each account with enough votes, hide a fifth of its likes,
and measure how many come back in the top of For you. The weights in §4·b move only on that measurement.

## 6. Phases

### 6·a Votes

`taste_votes`, the routes, `myVote` on every subject, the buttons on the film, show, person and franchise
pages and the TV app's title page, Like and Dislike in a card's menu, the taste page's list with undo.

### 6·b For you

The profile, the `for-you` list query, Home's row, Discover's sort and Hide disliked, the reason line on
the card, the taste page's profile view and reset, the evaluation.

### 6·c The deck

Tune your taste, and Home offering it to a person with no signals.

### 6·d Watching, after the credits, the assistant

Watching signals in the profile; the after-the-credits question; `my_taste` reading votes; a `for_you`
assistant tool the `recommend` skill uses first.

### 6·e Together

For a room and for a plan.

### 6·f Measured, not promised: embeddings

Text embeddings of each title's story, made on hotrod's GPU, as a further feature for matches no
keyword names. Built only if the evaluation in §5 shows them helping.

## 7. Order

6·a, then 6·b, then 6·c; 6·d and 6·e in either order after; 6·f last, if at all.
