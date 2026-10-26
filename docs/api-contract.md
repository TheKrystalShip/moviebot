# API contract

What `MovieBot.Api` serves, as built. Everything is camelCase; enums are lowercase strings.

Default listen address is `http://127.0.0.1:8099`. The media root comes from `Media.Root` in
`moviebot.settings.json` (or `Media__Root` in the environment) and must be absolute in anything but a demo. `Media__ColdRoot`
names a second disk that finished films are kept on; a title is under one root or the other and
which one it is under changes no URL. Left empty, the whole library is the media root.

## HTTP

| Route | Returns |
|---|---|
| `GET /health` | `{"status":"ok","rooms":n,"watching":n,"coldStorage"?}` — occupied rooms and people, no names. `coldStorage` carries what is wrong with the disk films are kept on and is absent when nothing is, which is also the answer when there is no such disk |
| `GET /api/config` | `{discordClientId?, publicBaseUrl?, issuer, clientId, tvClientId, actionsClaim}` — where the page signs in: the identity provider's issuer, the public client the site signs in as, the public device client it signs in as on a television, and the claim a person's actions arrive in. Open |
| `GET /tv.apk` | the Android TV and Fire TV app built for this site (`tv/README.md`), `application/vnd.android.package-archive`, from `Tv:App` or `tv.apk` in the state directory; 404 when the host has none. Open |
| `GET /phone.apk` | the Android phone app built for this site (`phone/README.md`), from `Phone:App` or `phone.apk` in the state directory; 404 when the host has none. Open |
| `GET /.well-known/tks-actions.json` | the action manifest (`kgsm-docs/reference/action-manifest.md`): every action Cinema checks, in the namespace `cinema`, at `cluster`, the self actions marked `self`. Open: the provider reads it |
| `GET /api/titles` | `TitleSummary[]`. The library as listed at most ten seconds ago, as are the episodes `/api/shows` and `/api/shows/{imdbId}` group; a person's progress and last plays on it are current |
| `GET /api/titles/{id}` | `Manifest`, or 404 |
| `GET /api/titles/{id}/ticket` | `{ticket, expiresAt, expiresIn}` — what a request for this film's bytes carries, or 404 |
| `GET /api/sessions` | `RoomSummary[]` — every room the person may enter, what it is watching, how many are in it; every room on the service key. A private room is in nobody else's list |
| `GET /api/sessions/{id}` | `SessionStatePush` — creates the session if new. This and every `/api/sessions/{id}/…` route lets the person into the room when it admits them, makes it for them when there is none, and is a `403` when it is private to them (Who may come in, under Hub) |
| `GET /api/sessions/{id}/participants` | `Participant[]` |
| `GET /api/sessions/{id}/around` | `{current?, previous?, next?}` — the episode the room holds, by its title or as it arrives, and the episodes either side, each `{season, number, name?, titleId?}`: the next of its season else the first of the next, as the catalogue lists the show, or as the library holds it for a show the catalogue does not list, and previous the other way. `titleId` is absent for one the library does not hold, which is named only to somebody holding `cinema:download`. All absent for a film |
| `POST /api/sessions/{id}/title` | `SessionStatePush` — puts a film in the room, or 404 if the library has no such title. The body is `{titleId, startSeconds?}`; `startSeconds` puts the room there instead of at the beginning, held to the transcode head like a seek. Recorded as the token's person, or as MovieBot on the service key |
| `POST /api/sessions/{id}/play` | `RoomChanged` |
| `POST /api/sessions/{id}/pause` | `RoomChanged` |
| `POST /api/sessions/{id}/seek` | `RoomChanged` |
| `POST /api/sessions/{id}/seek-relative` | `RoomChanged` |
| `GET,HEAD /media/{id}/**` | playlists, segments, subtitles, poster. Opened by `?mt=` or the bearer token |
| `GET /api/catalogue/titles?…` | `CardPage` — `{titles, total, next?}`: a page of cards (Catalogue, below), `next` the next page's number while there is one. The one list query: every filter of Catalogue's list query, and the asking person's `unwatched`, `inLibrary` and `here`. A parameter it does not take is refused with `400`, as is a value it cannot read. Takes `cinema:watch`, as every `/api/catalogue` route does; a catalogue that cannot be read is a `503` that says why |
| `GET /api/catalogue/facets?…` | `Facets` — `{total, sections: [{id, options: [{value, label?, count}]}]}` over what the same filters name: `kind`, `genre`, `decade`, `length`, `rating`, `language`. A section offers its options from the query without its own picks, each counted with every other section's picks applied |
| `GET /api/catalogue/titles/{imdbId}` | `TitlePage` — a title's card and everything the catalogue holds about it (Catalogue, below), joined with the asking person; 404 for a title it does not list |
| `GET /api/catalogue/titles/{imdbId}/seasons/{n}` | `Season` — `{number, name?, overview?, airDate?, poster?, episodes: [{number, name?, overview?, airDate?, runtime?, still?}]}`, numbered as the film database numbers them; 404 for a season it does not hold |
| `GET /api/catalogue/terms?q=` | `Term[]` — `[{filter, value, name, detail?}]`: the words resolved to what the list query takes, a genre, a person, a keyword, a company, a Wikidata item or a franchise, the best known first |
| `GET /api/catalogue/ask?q=` | `CatalogueAnswer` — `{question, parts, query?, message?}`: the words read as a question (`QuestionReader`), the parts read, and `query` the list query's filters that answer it, which the site asks `/api/catalogue/titles` with. `message` says what a question names that the catalogue does not know; words that are no question answer `question: false` |
| `GET /api/catalogue/people/{id}` | `Person` — `{id, name, portrait?, department?, titles}`; their titles are `/api/catalogue/titles?person={id}`. 404 for nobody |
| `GET /api/catalogue/franchises?page=&size=` | `FranchisePage` — `{franchises, total, next?}`, each a `ShelfHead` (below) of the franchise in the person's order: the ones they are part way through first. A collection a listed universe holds three quarters of the titles of is listed only as that universe's part |
| `GET /api/catalogue/franchises/{id}` | `Franchise` — `{card: {id, kind, name, poster?, backdrop?, titles, firstYear?, lastYear?}, parts}`: a collection (`c…`), or a Wikidata universe (`Q…`) with the collections it holds as `parts`; its titles are `/api/catalogue/titles?franchise={id}&sort=order` |
| `GET /api/catalogue/discover` | `DiscoverPage` — `{spotlight, rows, sections, genres}`: the Discover page for the asking person. A row is `{kind, shelf?}`, `kind` `shelf` or a section's id (the moods, the franchises) or `browse`; a section is `{id, label, icon, series, count, shelves}`; a shelf head is `{id, title, icon, query, posters, total, watched?, next?}`, `query` its list query as a query string, `watched` and `next` (a card) a series' for the person. `spotlight` is the id of the shelf the page leads with |
| `GET,HEAD /images/{size}/{file}` | an image the catalogue keeps, by the film database's file name and a size (`w185`, `w300`, `w500`, `w1280`). A file never changes under its name, so it is served immutable for a year. Open |
| `GET,HEAD /images/titles/{imdbId}/{name}` | a `302` to a title's image by its IMDb id, `name` `poster`, `backdrop` or an episode's still `sNNeMM` (numbered as the releases number it), its location relative to this route; kept an hour, since the image a title is shown with can change. For what knows a title and not its card: the player's frame, a remote, a guest. 404 for none. Open |
| `GET /api/shows` | `ShowSummary[]` — the shows the library holds, one entry each: `{imdbId?, title, card?, seasons, episodes, seasonProgress, addedAt?, sizeBytes, lastPlayed?, resume?, firstTitleId}`. `card` is the show's catalogue card; `seasons` are the numbers of the seasons it holds and `episodes` how many episodes; `seasonProgress` is `[{season, episodes, watched}]`, each season it holds in order, with the episodes held of it and how many of those the asking person watched through |
| `GET /api/library/{scope}?shelf=&q=&sort=&state=&kind=&category=&anyGenre=&decade=&length=&minRating=&language=` | `LibraryPageView` — `{items, total, facets}`: a library page as it is shown, searched, narrowed and ordered here. `scope` is `films` (the library's films, as `/api/titles` lists them), `shows` (as `/api/shows` lists them) or `all` (both); `shelf` is `anime` for the Anime shelf's titles, `general` for every other title, and absent for both (Categories, below). `items` are `{film?, show?}`, each holding one. `q` keeps the titles whose name holds every one of its words, in any order and any case, a film's name being its name and year as its card shows it. `held` keeps the titles the asking device says it holds, only it knowing (a film by its title id, a show by its IMDb id), repeating or taking commas. `state` picks a film's state, repeating or taking commas and answered as any of them: `ready`, `preparing` or `failed`. The filter's picks are as on `/api/catalogue/discover` (Filters and facets, below), `kind` and `category` only on `all`. `sort` is `added` (the default: the newest to the library first), `first-added`, `name`, `name-desc`, `rating`, `lowest`, `newest`, `oldest`, and for `films` `shortest` and `longest`; a tie keeps the library's order. `total` is how many titles the page holds before the search, the state, `held` and the picks. `facets` is a `CatalogueFacetsView` counted over the whole page with the state picked on it and the search not, beside a `state` section of the states the page's titles are in, each counted with the picks on it, and `sorts` naming the orders above. The library is read as listed, so it never waits on a disk or the network. `404` for another scope, `400` for another shelf, sort or state, a state on `shows`, a pick of a kind on one kind, or a pick `/api/catalogue/discover` refuses. Takes `cinema:watch` |
| `GET /api/me/shows/{imdbId}` | `PersonShowView` — what the asking person has of a show, here or not: `{imdbId, seasons: [{number, name?, airDate?, overview?, poster?, episodes: [{season, number, name?, overview?, airDate?, runtime?, still?, titleId?, status?, durationSeconds?, progress?, lastPlayed?, watched}]}], resume?, followable, follow?, nextEpisode?}`. Every episode the catalogue lists and every one the library holds, numbered as the releases number them (`Shows/EpisodeNumbering`); `titleId` the library's title for one it holds; `watched` whether the asking person watched it through here or marked it watched (`POST /api/me/watched`). What the show is, is its catalogue page |
| `GET /api/me/preferences` | `ViewerPreferences` — how the token's person watches; every field absent for somebody who set nothing. |
| `PUT /api/me/preferences` | `ViewerPreferences` → the stored `ViewerPreferences`, which is then pushed to the person's connections (`PreferencesChanged`). A blank language is stored as absent. `400` for a `qualityHeight` or `offlineQualityHeight` that is not absent (Auto) or a height from 1 to 4320, and for a `subtitleStyle` outside the ranges it keeps: `size` 50 to 200, `font` one of `sans`, `serif`, `mono`, `casual`, `edge` one of `none`, `outline`, `shadow`, `raised`, `depressed`, a colour as `#rrggbb`, `backgroundOpacity` 0 to 1, `position` 0 to 30, and for a `tabBar` that is not four distinct ids of the shape in `ViewerPreferences`. Nothing is kept for a refused body. `qualityHeight` is the person's default for every film, so it is held to what a picture can be and not to one film's rungs: a film with no rung at that height plays the tallest below it |
| `GET /api/me/tracks/{titleId}` | `FilmTracks` — the subtitle and audio track the token's person watches the film with: `{titleId, subtitleId?, subtitleOff?, audioId?}`. `subtitleId` is a track id, `subtitleOff` is a chosen "no subtitles", and a film the person chose nothing for has neither and no `audioId`, so it starts on their languages (`ViewerPreferences`). The same on every device the person watches it on. `404` for a title the library does not hold |
| `PUT /api/me/tracks/{titleId}` | `{subtitleId?, subtitleOff?, audioId?}` → the film's `FilmTracks` as they now stand, then pushed to the person's connections (`TracksChanged`). A field named is set and a field left out is kept; `subtitleId` and `subtitleOff: true` each replace the subtitle choice. `400` for a body that names nothing, for `subtitleId` together with `subtitleOff: true`, for a `subtitleId` the title's manifest does not list as a track that can be shown (`available`, with a `uri`; the subtitles fetched from outside are in it), and for an `audioId` it does not list; `404` for a title the library does not hold. A guest link and the service key name nobody and are refused |
| `GET /api/me/sorts` | `{[page]: SortOrder}` — the order the token's person last picked on each page that sorts, `SortOrder` being `{key, direction}` with `direction` `asc` or `desc`; empty for somebody who picked none. The site names the pages and their keys, and a key a page does not offer is that page's own order |
| `PUT /api/me/sorts/{page}` | `SortOrder` → every order the person keeps, as `GET` answers it. `400` for a page or a key that is not a lowercase name (letters, digits, `.` and `-`, starting with a letter, at most 40 characters), a direction that is neither, or a page beyond the 32 one person keeps an order for |
| `GET /api/me/progress` | `ProgressEntry[]` — what the token's person can carry on with, newest first: `{titleId?, name, positionSeconds, durationSeconds, remainingSeconds, lastWatchedAt, episode?, showImdbId?, episodeName?, stillUrl?}`. Each film they started and did not finish; and each show once, by the episode they watched last, wherever in the show they started: that episode while unfinished, and once finished the episode after it (the next of its season, else the first of the next season, as the catalogue lists the show, or the library holds it for a show the catalogue does not list), from where they left it or from 0, carrying the finished episode's `lastWatchedAt`. An episode after that the library does not hold, or holds a failed copy of, is listed with no `titleId` once it has aired (from the day after its air date), to a person holding `cinema:download`: `durationSeconds` is the description's runtime (0 when it names none), and watching it is Watch now with its `episode`. A mark (`POST /api/me/watched`) on an episode the library does not hold is that episode finished at the time of the mark, so a season watched elsewhere is carried on from the episode after it. A show finished to its last episode is not listed. For an episode `name` is the show's, `episode` is `{season, number}`, and `episodeName` and `stillUrl` are the catalogue's name for it and its still where it has them. A title the library no longer holds is left out |
| `POST /api/me/progress` | `204` for `[{titleId, positionSeconds, watchedAt}]`, at most 500 — where a device that watched with no connection got to. Each is recorded as a room's play is, the person's entry and the film's last play, where `watchedAt` is newer than what is held; an older one, and one for a title the library no longer holds, changes nothing. A `watchedAt` ahead of the server's clock is taken as now. `400` for a position that is negative or not a number, or more than 500 entries |
| `GET /api/me/history` | `HistoryEntry[]` — every film and episode the token's person watched, newest first: `{titleId, name, positionSeconds, durationSeconds, finished, lastWatchedAt, episode?, showImdbId?}`, named as above. `finished` is a position past 90 % of the title |
| `DELETE /api/me/history/{titleId}` | `204` — forgets the person's entry for the film, and so their progress in it. The film's own last play stays |
| `POST /api/me/watched` | `204` for `WatchedChange` — `{imdbId, kind, season?, episode?, watched}`: the token's person saying they watched something, here or anywhere, or taking it back. `kind` `film` names a film; `show` names every episode of a show, of its `season`, or one `episode` of that season, as the releases number them, each one the catalogue lists or the library holds. Marked watched, an episode counts only once it has aired (on its air date) or is held, and the episodes of a season or a show are marked in order a millisecond apart, so the latest is the last. A mark is kept by what it names, not by a library title, so it holds for what the library never had and outlives what leaves it; it moves no film's last play. Wherever a title the library holds is read for the person (`progress` on a listing, `/api/me/history`, Continue watching, a show's episodes), a mark on it reads as finished at the time of the mark unless their entry for it is newer. Watching marked ends a want for the film, or for the show once its last aired episode is watched. Taken back, the marks go and so do the person's entries for the titles the library holds of what it names. `400` for an id that is no IMDb id, a `kind` that is neither, a film with a season or an episode, an episode with no season; `404` for a show change naming no episode, or, marked watched, none that has aired. Takes `cinema:profile.write` |
| `GET /api/me/follows` | `FollowView[]` — the shows the token's person follows, newest follow first: `{imdbId, title, card?, since, autoDownload, nextEpisode?}`. `card` is the show's catalogue card; `nextEpisode` is `{season, episode, name?, airDate}`, the episode the catalogue names next |
| `PUT /api/me/follows/{imdbId}` | `FollowView` for `{autoDownload}` — follows the show, or changes `autoDownload` of a follow that exists, which keeps its `since`. `400` for an id that is not an IMDb id, `404` when the catalogue lists no show under it, `409` when the show has ended or been cancelled |
| `DELETE /api/me/follows/{imdbId}` | `204` — stops following; also when the person did not |
| `GET /api/me/screens` | `ScreenView[]` — the television screens this session is paired with, oldest pairing first, connected or not (Screens, under Hub) |
| `POST /api/me/screens` | `ScreenView` for `{code}` — pairs this session with the screen that showed the code, and answers that screen; pairing again is no change. `404` for a code no screen showed, `410` for one that has run out, `403` for a screen of another person's, `400` for a television's token or one that belongs to no session |
| `DELETE /api/me/screens/{id}` | `204` — ends this session's pairing with the screen `id` names; also when there was none |

What is recorded comes from the rooms: when a room that is **playing** a film is paused, seeked or
given another film, or somebody leaves it, each person who was watching is credited with where it got
to; a film a room is playing right now is answered from the room itself. A paused room left open
records nothing. Opening a film
at `/r/{room}?title={id}&at={seconds}` into an empty room starts it there; a room already holding a
film ignores `at`. Once the room holds anything the site takes `title` and `at` off the address,
so reopening it rejoins the room wherever it is.

```jsonc
// ViewerPreferences: the defaults a film starts with, for a person wherever they sign in
{
  "subtitleLanguage": "es",   // the subtitle language to start on when the film carries it; absent for none
  "audioLanguage": "en",      // the audio language to start on when the film carries it; absent for the film's own
  "qualityHeight": 720,       // the best rung the film may play at, the connection deciding beneath it; absent for no ceiling
  "offlineQualityHeight": 480, // the rung Save offline picks: the widest no wider than this height at 16:9; absent for the device's own pick
  "skipIntros": true,         // skip an intro or recap on its own when this person is alone in the room; false when never set
  "tabBar": ["movies", "discover", "assistant", "me"], // the site's page ids in the four free slots of the phone tab bar, left to right; absent for the site's own choice
  "subtitleStyle": {          // how subtitles are drawn; absent, like any field of it, for the player's own look
    "size": 125,              // percent of the player's own size, 50 to 200
    "font": "sans",           // sans | serif | mono | casual
    "bold": false,
    "textColor": "#ffe14d",   // #rrggbb, as are the two colours below
    "edge": "outline",        // none | outline | shadow | raised | depressed
    "edgeColor": "#000000",
    "backgroundColor": "#000000",
    "backgroundOpacity": 0.5, // 0, no box, to 1
    "position": 8             // dialogue's distance above the bottom of the frame, percent of its height, 0 to 30
  }
}
```

`tabBar` is exactly four distinct ids, each 1 to 64 characters of `a-z`, `0-9` and `-`; anything else is a `400`.
The API keeps the ids as given and does not know the site's pages, so the site reads an id it does not route as
that slot's default. `PUT /api/me/preferences` replaces the whole record: a body without `tabBar` stores none.

A preference out of range is that preference left to its default: a size is clamped, an unknown font,
edge or colour spelling is dropped, and a style with nothing left in it is stored as none.

CORS reflects any origin and allows credentials. Media answers HEAD as well as GET.

Any other GET is a page of the site and is answered with its index. `/r/{room}` is answered with the
index carrying Open Graph and Twitter tags for the room: `og:title` "Watch <film> on Cinema",
`og:description` the year and how far in ("1995 · 31 min in"), `og:image` the film's backdrop (or its
poster) at the public address `Api:PublicBaseUrl`. A room holding nothing shows the film its
`?title=` names, or the site. The tags never name or count the people in the room.

### Signing in, and what each route needs

MovieBot signs nobody in. A person signs in at the OpenID Connect provider `Oidc.Issuer` names, and
every request carries the access token it minted as `Authorization: Bearer`; the hub's negotiate and
connection carry it on the query string as `access_token`, which is read for `/hub` alone. A token is
taken when it is signed ES256 by a key in the issuer's key set, carries that issuer, the audience
`Oidc.Audience` (`cinema`) and the type `at+jwt`, and has not expired (30 seconds of skew); a token for
any other audience, KGSM's among them, is refused. `sub` is the account every record here is keyed by,
`name` what it is called, and the claim `Oidc.ActionsClaim` (`tks_actions`) lists the Cinema actions it
holds. An `act` claim (`{sub: <client id>}`) names the client calling for the person, which a room and
the log record beside them.

Each route names the action it needs, and a route that names none admits nobody. Without a usable token
the answer is `401` with `WWW-Authenticate: Bearer`; with one that lacks the action, `403`. Both carry
`{error}`. The actions are `cinema:watch` (the library, rooms, the hub, tickets and media, subtitles,
the people a room can invite, the catalogue),
`cinema:offline` (saving a film onto a device, below), `cinema:download` and `cinema:wish` (below),
`cinema:guests` (handing out a room's guest link, below), and the self
actions `cinema:profile.read` and `cinema:profile.write`, which every approved account holds, for a
person's own records under `/api/me` and push. `/.well-known/tks-actions.json` serves the
same declarations the checks name.

The **service key** (`X-MovieBot-Service`) proves machine work that names nobody and opens only the
routes that do such work: `/api/library/due`, `/api/library/saving`, `/api/transcode/*`, reading the library and the rooms,
putting a film into a room (recorded as MovieBot) and the downloads list. It never stands
for a person.

The **Discord Activity** has no browser to redirect. `POST /api/auth/discord/callback` takes `{code}`,
the code the SDK's `authorize` gave it: the API redeems it with Cinema's own Discord application and
exchanges the Discord access token at the provider (RFC 8693, `subject_token_type`
`urn:tks:params:oauth:token-type:discord-access-token`) as the confidential client
`Oidc.ActivityClientId`. It answers `{accessToken, token, expiresIn}`: Discord's access token, for
`sdk.commands.authenticate`, and the provider's token for the person. Before `token` runs out the
Activity posts `{accessToken}` with the Discord token it holds and is answered the same way. The
provider's refusal is a `403` with `{error, accountStatus}`, `accountStatus` its word for the account
(`pending`, `disabled` or `unknown`); a code Discord refuses is a `400`, a provider that cannot be
reached a `502`, and a host without the Discord or provider secrets a `503`. Open.

The API records the issuer in `Oidc.IssuerRecord` the first time it starts and refuses to start against
another until `Oidc.AdoptIssuer` names it.

A **guest link** lets somebody with no account watch one room. Its code is carried where a token would
be, as `Authorization: Bearer guest.<code>` or, on the hub, `access_token=guest.<code>`, and is checked
against the room on every request, so a link taken away or run out opens nothing from that moment. It
is evaluated only by the routes that name it: the hub, `GET /api/titles/{id}` and
`GET /api/titles/{id}/ticket`, the last two for the title the guest's room holds this moment and a
`403` for any other. Every other route answers a guest link as it answers no token, `401`. On the hub a
guest calls `JoinAsGuest`, `ReportQuality`, `ReportTrouble`, `ReportStats` and `ServerTime` and nothing else: every other method throws
a `HubException` (Who may come in, below). A guest's film bytes are opened by the ticket, as anybody's.

### Opening a film's bytes

A film's bytes are opened by the access token of somebody who may watch, or by a **media ticket** on
the query string as `?mt=…`, and the ticket is what a player should use for the film itself. A title's
`poster.jpg` is open: Discord's servers fetch it to draw an embed. It is the title's poster wherever it
is shown: the catalogue's poster for the film, or for the show an episode belongs to, the one its card
names; the poster beside the title only when the catalogue lists none.

The difference is caching. A shared cache will not answer a request that carried an
`Authorization` header. A ticket says only which film and until when, and **every viewer of a title
is handed the same string**, so their requests are one URL that one cached copy answers.

- Fetch it from `GET /api/titles/{id}/ticket` before loading the film, and put it on every
  `/media/{id}/…` request for that title.
- It is scoped to the one title and expires: at `expiresAt` by the server's clock, `expiresIn`
  seconds after the reply. It outlasts a feature. A player renews it before then rather than holding
  it for as long as a page stays open, and measures its life by `expiresIn`, since a device's clock
  can be hours off the server's.
- A media request refused with `401` on a ticket the player believed good (a device clock off by
  more than the margin it renews within) is sent once more on a newly fetched ticket.
- Send the ticket **or** the header, never both: a shared cache refuses to store a response to a
  request carrying the header.
- hls.js resolves a segment URI against the playlist that listed it, and relative resolution drops
  the query string. The ticket has to go on through a loader rather than on the playlist URL.

### Room controls

The acts the hub carries, for a caller that is not in the room — the bot, and a spoken command.
Both doors apply the change the same way and the whole room is sent `StateChanged` either way, so
what a play does cannot come to depend on which door it arrived by.

```jsonc
// play, pause
{ "atSeconds": 403.0 }
// seek
{ "toSeconds": 1200.0 }
// seek-relative — negative goes back
{ "deltaSeconds": -15.0 }

// every one of them answers
{ "push": SessionStatePush, "clamped": SeekClamped }   // clamped absent unless one was applied
```

- **`atSeconds` is optional, and omitting it means "wherever the room is".** A player knows where
  its own playhead sits and says so. A caller that is not watching does not, and a missing position
  read as zero would pause the film *and* send the room back to the opening titles — which looks
  like it worked, because the film stops. The position is resolved where the change is applied.
- **`seek-relative` exists because the film is moving.** Reading the position and then seeking to
  it minus fifteen spends a round trip in between, and in a playing room that round trip comes out
  of the fifteen. Asking relatively has the subtraction happen against the position the change
  lands on. Before the start is the start; past the transcode head is clamped like any seek.
- **The change is recorded as whoever the token names.** These take `cinema:watch`; the service key
  may load a film into a room, and that change is recorded as MovieBot itself, which is the honest
  answer for a room that moved with nobody asking.
- **The clamp comes back in the body.** Over the hub it goes to the caller alone; an HTTP caller is
  the one thing a broadcast cannot reach.

`.m3u8` for a title whose status is `transcoding` is served `no-store`; once `ready` it is
`max-age=300`. Segments, init files and subtitles are `immutable` for a year. A poster or a preview
sheet can change under its own name, so at its bare address it is `max-age=0, must-revalidate`, and
asked for with `?v=` it is `immutable` for a year: a change is listed under another version.

Content types are set explicitly, because the framework knows none of them and hls.js refuses a
segment delivered as `application/octet-stream`: `application/vnd.apple.mpegurl`,
`video/iso.segment`, `video/mp4`, `text/vtt`.

### Save offline

A person keeps a film on their device and plays it there alone, with no connection. Saving one takes
`cinema:offline`, which the provider files beside `cinema:watch`; the records of what a person saved
are their own and take the self actions. A title that is `ready` is saved whole; one still
`transcoding` is saved as it is made, its offer, package and tracks `growing`; a title that `failed`
is a `409`. A title the library does not hold is asked for as a **want**: the server fetches it, and
the device saves it once the library holds it.

| Route | Returns |
|---|---|
| `GET /api/titles/{id}/offline` | `OfflineOffer` — what the title can be saved as, each track with its exact size on disk, so far for one `growing`. `404` for a title the library does not hold, `409` for one that failed. `cinema:offline` |
| `POST /api/me/offline` | `OfflinePackage` for `{titleId, rung, audio, subtitles, device, subtitleLanguage?}` — records the save and answers everything the device fetches. `rung` is one video rendition's `rendition`, `audio` one or more audio `rendition`s, `subtitles` zero or more subtitle ids, `device` the name the device goes by, 1 to 100 characters. With `subtitleLanguage` the save also takes every text subtitle in that language the title carries when the package is asked for, and once the title's source is known (it is ready, or its fingerprint is taken) one is fetched from the subtitle index for a title carrying none, waiting on the index at most 20 seconds, once per save and language. A save of the same title on the same device replaces that one, keeping its `id`, and the device's wants for the title go. `400` for a rendition or subtitle the title does not have, no audio, a bad device or language; `404` and `409` as above. `cinema:offline` |
| `GET /api/offline/estimate?imdbId=&season=&episodes=` | `OfflineEstimate` — what downloading a film, or with `season` and `episodes` (comma-separated numbers, at most 100) those episodes of a show, would come to before the library holds it: the language it was made in, and roughly how many bytes it takes at 1080, 720 and 480, from its runtime and the median the library's finished titles take a second at each height. A size is absent while the runtime or the measure is unknown. `400` for an id that is not an IMDb id or a bad episode. `cinema:offline` |
| `GET /api/me/offline` | `OfflineSave[]` — every save the token's person has, on every device, newest first. `cinema:profile.read` |
| `POST /api/me/offline/{id}/complete` | `OfflineSave` — the device holds every byte, which ends the save's hold on the title. `404` for a save that is not this person's. `cinema:profile.write`, or that save's grant |
| `DELETE /api/me/offline/{id}` | `204` — forgets the save, and its hold with it; also when there was none. `cinema:profile.write` |
| `POST /api/me/offline/wants` | `OfflineWant[]` for `{imdbId, episodes?, height?, audioLanguage?, subtitleLanguage?, device}` — a film, or with `episodes` (`[{season, episode}]`, at most 100) those of a show, wanted on the device, one want each, as each stands now. What the library neither holds nor is fetching is fetched in the background: a film's best release at 1080p from a digital source up, an episode out of its season's pack at 720p where there is one, as Watch now picks, nobody told it can be watched. With an `audioLanguage` that is not the title's own, a release whose name carries it (a token naming the language, else `MULTI` or `DUAL`) comes first, at that quality and then at any. Asking again for one the device wants keeps it with the `height` and languages asked now; one that failed is asked for afresh. `height` is the picture height the device saves it at, 1 to 4320, absent for the device's own; `audioLanguage` absent for the title's own and `subtitleLanguage` absent for none, each a language tag. `400` for an id that is not an IMDb id, a bad device, height, language or episode; `403` for somebody not holding `cinema:download` as well, since fetching spends the disk and the tracker. `cinema:offline` |
| `GET /api/me/offline/wants` | `OfflineWant[]` — every want the token's person has, on every device, the oldest first, each as it stands now. A want is waited on for `Offline:WantDays`. `cinema:profile.read` |
| `DELETE /api/me/offline/wants/{id}` | `204` — lets the want go, also when there was none; its download stays the library's. `cinema:profile.write` |
| `GET,HEAD /media/{id}/{rendition}/track.mp4` | one track whole: the rendition's init segment and every segment after it, joined, as `video/mp4`; of a title still being made, every segment its playlist lists so far. Ranged (`206`, `416`), with an exact `Content-Length` and a strong `ETag`; a growing track's `Content-Range` gives its length as `*`. `If-Range` resumes with that `ETag`, or with one this track answered earlier while it was shorter, and with any other answers the whole track. Served `no-store`. The bearer token of somebody holding `cinema:offline` who has a save of the title, or that save's grant in `X-Cinema-Save-Grant`, never a ticket: `409` for a person with no save of it or a title that failed, `404` for a rendition the title does not have, `429` with `Retry-After` past the transfers allowed at once |

```jsonc
// OfflineOffer: GET /api/titles/{id}/offline
{
  "titleId": "coherence-2013",
  "rungs": [   // the title's video renditions, tallest first; whatever its manifest lists
    { "rendition": "v0", "uri": "v0/index.m3u8", "name": "818p", "width": 1920, "height": 818, "bytes": 3333456789 },
    { "rendition": "v1", "uri": "v1/index.m3u8", "name": "546p", "width": 1280, "height": 546, "bytes": 1339012345 }
  ],
  "audio": [
    { "rendition": "a0", "uri": "a0/index.m3u8", "kind": "feature", "language": "eng", "label": "English",
      "channels": 2, "default": true, "bytes": 163456789 }
  ],
  "subtitles": [   // the text subtitles that can be shown; a bitmap track is not offered
    { "id": "s3", "kind": "feature", "language": "eng", "label": "English", "hearingImpaired": false,
      "forced": false, "bytes": 81234 }
  ],
  "growing": false   // true while the title is still being made; each rendition whose encoding has begun, measured so far
}

// OfflinePackage: POST /api/me/offline
{
  "save": OfflineSave,
  "manifest": Manifest,   // the title's, holding only what was picked: the one rung, the audio, the subtitles
  "bytes": 1502550368,    // every track and subtitle together, exactly
  "tracks": [             // the video rung first, then each audio track in the order asked
    {
      "kind": "video",                        // "video" | "audio"
      "rendition": "v1",
      "uri": "v1/index.m3u8",                 // the manifest's uri for this rendition, which ties the two together
      "url": "/media/coherence-2013/v1/track.mp4",
      "bytes": 1339012345,                    // the file's exact length: initBytes plus every segment
      "etag": "\"3f9a0c…-2697\"",             // what If-Range carries to resume it: a hash, and how many files it covers
      "initBytes": 1033,                      // the init segment, at offset 0
      "segmentSeconds": [2.002, 2.002, …],    // each segment's duration, in order
      "segmentBytes": [512345, 498765, …]     // each segment's length; its offset is initBytes plus those before it
    }
  ],
  "subtitles": [
    { "id": "s3", "kind": "feature", "language": "eng", "label": "English", "hearingImpaired": false,
      "forced": false, "url": "/media/coherence-2013/s3.vtt", "bytes": 81234 }
  ],
  "card": TitleCard,      // the catalogue's card: the film's, or an episode's show's; absent for a title the catalogue does not list
  "episode": { "season": 2, "number": 4, "name": "…", "overview": "…" },   // an episode's own; absent for a film
  "artwork": {            // each absent when there is none; open addresses but the preview sheet, which is the film's
    "poster": "/images/w500/….jpg",              // the card's, else the library's own poster
    "backdrop": "/images/w1280/….jpg",
    "still": "/images/w300/….jpg",               // an episode's
    "thumbnails": "/media/coherence-2013/thumbs.jpg"   // the scrub preview sheet the manifest's thumbnails describe
  },
  "growing": false,       // true while the title is still being made: the tracks are what is made so far
  "grant": {              // the save's grant, for the device's own downloader
    "header": "X-Cinema-Save-Grant", "value": "5c0f3b2a9e7d4c18.1760918400.…", "expiresAt": "2026-10-20T00:00:00Z"
  }
}

// OfflineWant: POST and GET /api/me/offline/wants
{
  "id": "9a41c07be2d35f60",
  "imdbId": "tt7472896",                 // the film's, or an episode's show's
  "name": "Dune: Part Two",              // the film's, or the show's, as described here; absent when not described yet
  "episode": { "season": 2, "episode": 4 },   // absent for a film
  "device": "Pixel 8",
  "height": 480,                         // absent for the device's own pick
  "audioLanguage": "es",                 // absent for the title's own
  "subtitleLanguage": "en",              // absent for none
  "createdAt": "2026-10-07T08:00:00+00:00",
  "stage": "downloading",                // finding | downloading | preparing | ready | failed
  "progress": 0.34,                      // how much of it has arrived on the server, while downloading
  "problem": null,                       // when failed: notOffered | refused | unavailable | notWatchable
  "refusal": null,                       // the disk budget's or the tracker's words, when refused
  "titleId": null                        // the library's title once it is ready
}

// OfflineSave
{
  "id": "5c0f3b2a9e7d4c18",
  "titleId": "coherence-2013",
  "rung": "v1", "audio": ["a0"], "subtitles": ["s3"],
  "device": "Pixel 8",
  "createdAt": "2026-10-06T19:00:00+00:00",
  "completedAt": "2026-10-06T19:12:40+00:00",   // absent until the device says it holds every byte
  "holdsUntil": "2026-10-08T19:00:00+00:00",    // absent once complete or past it
  "subtitleLanguage": "en"                      // absent for none; its text subtitles ride along
}

// OfflineEstimate: GET /api/offline/estimate
{
  "originalLanguage": "en",              // absent when the title is not described here
  "sizes": [
    { "height": 1080, "bytes": 3400000000 },
    { "height": 720,  "bytes": 1400000000 },
    { "height": 480,  "bytes": null }    // null while the runtime or the measure is unknown
  ]
}
```

- **An offer's and a package's sizes are bytes on disk, never an estimate.** A rung's bitrate is a
  cap under constant quality, so two films at one rung differ by a factor of two and more; every
  `bytes` there is the files' own. `OfflineEstimate` is the one estimate, for a title with no files
  yet, and a page shows it as one ("~1.4GB").
- **A track is the files already on disk, joined.** The API runs no ffmpeg for it. A segment's
  offset in the track is `initBytes` plus the lengths before it, so the playlists a device plays from
  are built from `segmentSeconds` and `segmentBytes` as byte ranges of the one file.
- **Media addresses take the bearer token.** A track, a subtitle and the preview sheet are fetched
  with `Authorization: Bearer`; a ticket does not open a track. The artwork routes are open.
- **A save's grant opens its tracks with no token.** The package's `grant` is carried as its
  `header` (`X-Cinema-Save-Grant`), never on the URL, and opens that save's tracks and its
  `complete` route until `expiresAt`, which is the end of the save's hold: a phone's download
  service fetching for hours with no page running, past every access token's life. It names the
  person, so their transfer limits count it; it opens nothing once the save is forgotten, and
  saving the title again issues a new one. A device resuming on it sends `If-Range` with the track's
  validator, as a page does.
- **A save in progress holds its title.** Until it is complete, and for `Offline:HoldHours` after it
  was made, the title stays in the library and in place: the library's clock does not let it go, its
  source is not removed, and it is neither moved between disks nor encoded again. Past that a save
  still listed holds nothing, and a track whose files have changed answers its new `ETag`, so a
  resume with `If-Range` gets the whole new track rather than a mix.
- **Transfers yield to rooms.** A person runs at most `Offline:MaxTransfersPerPerson` tracks at once
  and the server `Offline:MaxTransfers`; past either a request is a `429` with `Retry-After`. While a
  room has somebody in it, each transfer is held to `Offline:MegabitsPerTransferWhileWatched`.
- **A title still being made is saved a stretch at a time.** The device fetches each track as far as
  its package says, asks for the package again later, and resumes from what it holds with `If-Range`
  naming the `ETag` its bytes arrived under: the track is the same files with more after them, so the
  stretch held is still how it begins. Once the package is no longer `growing` the device finishes as
  for any title.
- **Where a want stands is read, never kept.** `ready` once the library holds the film or episode and
  it can be played (whole, or with something made), with `titleId`; otherwise `failed` with the
  problem that stopped its download being started, `finding` until one is, and then its download:
  `downloading` with `progress` (an episode's own file's), `preparing` once it has arrived, or
  `failed` with `unavailable` when the download has gone and `notWatchable` when it could not be
  made watchable.
- **What was watched with no connection comes back through `POST /api/me/progress`.**

### Transcode plan

What the card should be doing, published for the hand-off to carry out, and the hand-off's report of
what it is doing. Both answer the **service key** alone: a plan names every film and what rooms are
watching. The shapes are in `src/MovieBot.Core/Transcode.cs`; the rules are in
`src/MovieBot.Api/Transcode/CLAUDE.md`.

| Route | Answers |
|---|---|
| `GET /api/transcode/plan` | `TranscodePlan`, computed from the rooms, the manifests and the last report at the moment it is asked |
| `PUT /api/transcode/status` | 204 for `TranscodeStatus`; the report replaces the previous one |

`TranscodePlan` is `{plannedAt, executorReporting, units, titles}`. `units` is most urgent first, each
`{id, kind, titleId, rung?, fromSegment, disposition, rank, tier, reason, placement?}`: `kind` is
`video-rung`, `audio` or `rung-add`; `rung` is `{directory, width, height, bitrateKbps}` and is absent
on audio; `fromSegment` is the first segment still to be made; `disposition` is `run`, `hold` or
`park`; `tier` is the policy tier, one to six; `placement` is absent until placement decides. The `id`
names what the unit is and does not change as it progresses. `titles` holds one `{titleId, maySettle}`
for each finished film, and `maySettle` is true unless a room with somebody connected has it set,
paused or playing. `executorReporting` is false
when the last report is older than `Transcode:StatusStaleSeconds` or none has come, and the plan then
rests on manifests alone.

`TranscodeStatus` is `{units, disks?}`. Each unit is `{id, state, reason?, speed?, heads?, disk?,
since?}`: `state` is `running`, `held`, `parked`, `done` or `failed`; `speed` is output seconds per
wall second; `heads` is seconds of film by output name, a rung's directory or `audio`; `since` is
when the unit entered its state. Each disk is `{name, freeBytes, totalBytes}`.

### Fetching films

The API is the one writer of downloads and wishes. A download, its searches and Watch now take
`cinema:download`; a wish, kept for a film somebody wants that nothing has yet (Wanting, below), takes
`cinema:wish` from the token that makes the want; finding a film in the title index and asking whether
it can be watched take either. No request names who asked: the person is the token's, whichever
client carried it, and the download's tags, the wish and the log record them. A download may name a
Discord channel to be announced in, which the bot does. `GET /api/downloads` also answers the service
key, for the announcements the bot posts. The shapes are in
`src/MovieBot.Core/Acquisition.cs`; the API reads the tracker, `QBittorrent`, `Download`, `Selection`,
`Library` and `Notify` settings to serve them.

| Route | Returns |
|---|---|
| `GET /api/releases/suggest?q=&session=` | `ReleaseSuggestions` — picker rows for what was typed; `session` keys the debounce |
| `GET /api/releases?q=` or `?imdb=` | `RankedOffers` — the tracker's releases for a film, best first |
| `GET /api/releases/{torrentId}` | `ReleaseOffer` — a release a search or a picker offered, or 404 |
| `GET /api/shows/{imdbId}/releases` | `ShowOffers` — `{seasons: [{season, packs, episodes: [{episode, releases}]}], emptyExplanation?}`, each list a show's `ReleaseOffer`s best first, 720p ahead of 1080p. A pack is listed under every season it covers |
| `GET /api/films/{imdbId}/availability` | `FilmAvailability` — `{imdbId, libraryId?, download?: {hash, progress, isWatchable, libraryId?}, hd1080?, hd720?, notOut, notOffered, emptyExplanation?, trackerUnanswered, waiting, waiters, streams: {used, limit}}`. The library first, then the torrent client, then the tracker, which is asked only for a film neither here nor arriving; `hd1080` and `hd720` are the best `ReleaseOffer` at each from a digital source up. With neither, `notOut` says the tracker holds nothing from a digital source up, a film to wait on, and `notOffered` that it does but every such release is turned down here, which `emptyExplanation` names |
| `POST /api/watch` | `WatchNowReply` for `{imdbId, torrentId?, quality?, episodes?, room?}` — `{outcome, room?, libraryId?, streams?}`. See Watch now below |
| `GET /api/downloads` | `DownloadView[]`: `{hash, name, release, summary, progress, isFinished, isWatchable, libraryId?, imdbId?, requestedBy?}`, `requestedBy` the account of whoever asked |
| `POST /api/downloads` | `StartDownloadReply` for `{torrentId, notifyChannelId?, roomId?, episodes?}`; `episodes` (`[{season, episode}]`) fetches only those files out of a show's release, and picking more from a release already downloading switches them on in it |
| `GET /api/anticipated` | `AnticipatedView[]` — `{imdbId, kind, title, year?, display, posterUrl?, waiters, waiting}`, the films people wait on, most waited on first and, among equals, the one most recently asked for. `kind` is `film`; `waiters` counts the people with a visible want on the film, the asking person's own included, and `waiting` says whether the person asking has one; `posterUrl` is the poster the film's page shows. A wish the tracker has answered, and one whose film is in the library, is left out. It names nobody. Takes `cinema:wish` |
| `GET /api/library/due` | `string[]` — the ids of the settled films whose time is up; the **service key** alone |
| `GET /api/library/saving` | `string[]` — the ids of the titles a save still being made holds (Save offline, below); the **service key** alone |

- **A film stays in the library until it has gone `Library:LeaveAfterDays` without a play, and a
  show's season the same way, as one.** The clock is only on a title that has settled on the cold
  disk, and it counts from the latest of the last play, the moment it settled, and the moment plays
  began to be recorded; for a season, the latest play of any of its episodes and the latest of them
  to settle, so all of it falls due together. A film or season a room holds never leaves, a room on
  one episode holding its season, and neither does one a save still being made holds, in the same
  way. Where a film stands on the clock is the server's alone: no route
  serves it to a person, because a film that has left is fetched again the way any other is. The
  hand-off asks `/api/library/due` and deletes what it names, being the only process that writes the media roots; a hand-off the API cannot answer
  deletes nothing. The removal of a download's torrent and files after its seeding window is the
  tracker's clock, which the hand-off keeps on its own and no route shows.

- **A download names a release the API offered.** `torrentId` is resolved against what a search or a
  picker answered; anything else is `noLongerOffered`, before the torrent client is asked.
- **An outcome is a word and the facts behind it, never a sentence.** What to tell somebody depends
  on where they asked — a Discord channel mentions people and names slash commands, a web page does
  neither — so each client words the outcome from the same facts.
- **A service that could not be reached is a 502, not an empty list.** No downloads and a torrent
  client that did not answer are different answers, and the second carries `ErrorReply`.
- **A wish stays until its people let it go.** The sweep finds what became available and tells every
  wanter whose want carries `mayWish` with an `available` notification, and somebody who wants the film
  after that is told on the next pass. The want then lists with the release found for it until the
  person removes it. Which wishes are out is held in memory and found again by the first sweep after a
  restart.

### Watch now

A film that is not here is fetched and played in a room the moment it can be opened. `POST
/api/watch` answers at once: `inLibrary` (the film is here, and is loaded into `room`), `preparing`
(the room waits on it), `full` (a `409`: `streams` films are already being watched while they arrive,
the `Watch:MaxStreams` setting, and the person is offered Download instead) or `badRoom`. Without a
`room` a new one is made, open, with the person asking in it; a room id is 1 to 64 letters, digits, `-` or
`_`, and a room named that is private to them is a `403`. A film already arriving
has the room added to its download. Otherwise the release is `torrentId`, one the API offered, or the
best at `quality` (`1080p` unless `720p` is asked) from a digital source up; `episodes` fetches only
those out of a show's release and is how a show is watched.

A room holds one thing. What is arriving is put into the room the moment it is asked for, in place
of whatever the room held, as the person who asked: the room's state has no `titleId` and carries
`preparing: {imdbId, season?, episode?, name, stage, release?, progress?, problem?, refusal?}`, pushed
to everybody in it, `season` and `episode` naming an episode. `stage` is `finding` (the release is
being picked), `downloading` (`progress` from 0 to 1) or `failed` (`problem`: `notOffered`,
`refused`, `unavailable`, `notWatchable`); a report of how it is getting on names nobody in
`updatedBy`. The download carries the room as `watch:<room>`; once the hand-off marks it watchable
it is loaded into the room and played as the person who pressed it, and `preparing` goes. A room
given anything else meanwhile is no longer waiting on it: the download stops naming the room, and
nothing it reports reaches the room. A room kept across a restart keeps what it holds; one still
being looked for is `failed` with `unavailable`. A download watched this way still owes its
requester a "ready" notification, which is not sent while they are in a room playing it.

An episode is watched the same way, named in `episodes` (the first is the one the room waits on).
One already playable here is loaded at once. Otherwise it comes from `torrentId`, or from the show's
releases at `quality` (`720p` unless `1080p` is asked): the season's pack at it, fetching the one
file, else a release of the episode alone, else either at what the tracker has. The download carries
`watch:<room>:s05e03`, and the room is loaded once that episode's own title can play, since its
season's torrent may be watchable for another episode already; `progress` is that episode's file.
An episode watched now owes nobody a "ready" notification.

**The episode after the one a room watches is fetched ahead**, one and no more, at the quality of
the one being watched (1080p for a picture 1800 or more wide, else 720p), out of the season's pack
where there is one. Nobody is named as having asked, so nobody is told. What the tracker offers for
a show is asked once per ten minutes and shared by the show page, Watch now and this.

### Following a show

A person follows a show on its page. When a new episode of it airs and the tracker offers it, they get
an `episode` notification; one who switched on `autoDownload` has it fetched as well. Follows are the
token's person's own: there is no route by which anybody reads or changes anybody else's.

- **Only an episode after the follow is news.** A follow records the latest episode already due when
  it began, and a show followed mid-run announces nothing that was out. A season premiere months
  later counts.
- **An episode is due from the day after its air date.** An air date is a date with no zone, read
  against UTC; the day after is when the tracker can be expected to hold it. It is looked for until
  `Follow:GiveUpDays` past its air date, asked of the tracker at most once per `Follow:AskMinutes`,
  and only for a show with an episode due.
- **One download serves everybody.** Followers with `autoDownload` share one download of the episode,
  at 1080p when the latest episode of the show any of them watched has a picture at least 1800 wide
  and 720p otherwise, started as the one who followed first. That person is told by the download's own
  `ready` notice when it can be watched, and the rest by the `episode` notice. A download the disk or
  the tracker refuses leaves them with the `episode` notice alone. An episode the library holds or is
  fetching is announced and not fetched again.
- **An episode is announced once.** What was announced is kept, so a restart tells nobody twice.
- **What the person has of a show carries their side** (`/api/me/shows/{imdbId}`). `followable` is false
  once the catalogue calls the show `Ended` or `Canceled`; `follow` is `{autoDownload, since}` for a show
  the person follows and absent otherwise; `nextEpisode` is `{season, episode, name?, airDate}` and absent
  when the catalogue names none.

### Filters and facets

A filter picks in sections, each named by an id, and its picks are the list query's parameters:

| Section | Picks with | Options |
|---|---|---|
| `kind` | `kind` (`film`, `show`) and `category=anime` | `film`, `show`, `anime` (anime of either kind) |
| `genre` | `anyGenre`, a genre by IMDb's name | the genres the titles hold, in alphabetical order |
| `decade` | `decade`, a decade by its first year (`1990`) | the decades the titles hold, newest first |
| `length` | `length`, films only | `short` (under 90 minutes), `standard` (90 to 150), `long` (over 150) |
| `rating` | `minRating`, one floor | `8`, `7`, `6`, labelled `8+` |
| `language` | `language`, by ISO 639-1 code | the languages the titles were made in, the most held first, labelled by English name |

A section with several picks is answered as any of them, and the sections combine as all of them:
`anyGenre=Horror&anyGenre=Comedy&decade=1990&decade=2010` is horror or comedy, of the 1990s or the 2010s.

`Facets` is `{total, sections: [{id, options: [{value, label?, count}]}]}`: `total` how many titles the
query names with every pick, and each section's options the values held by the titles the query names
without any section's picks, so a filter showing them all holds still while somebody picks. Each option's
`count` is how many titles hold it with every other section's picks applied and without its own, so a
second option of a section that has a pick can still be picked; an option counted `0` is shown and cannot
be picked. `/api/catalogue/facets` answers them for a list query, and the library's pages
(`/api/library/{scope}`), Want to watch (`/api/me/wants/page`) and History (`/api/me/history/page`) answer
`facets` beside their page, counted the same way over the cards of the titles they list. A title the
catalogue does not list is in no option. The titles saved on a device are counted where they are held,
over their cards, with the same sections, values and rules (`web/site/src/facets.js`).

### Categories

A title's kind is how it is built, a `film` or a `show` of episodes; its category is where people look
for it: `anime` or `general`. The two are independent, so an anime film is a film on the Anime shelf and
a show's every episode is on its show's.

A title is anime when its genres hold Animation and its original language is Japanese (`ja`), and general
otherwise. It is the card's `category`, so every view carrying a card carries it; a library title the
catalogue does not list is shelved as general.

### Wanting

A person says "I want to watch this" about a film or a show, whether or not the library holds it. The
want is the one signal: the server decides what it means for the title (here and ready, arriving,
waiting for a release). A want is keyed by the person and the title's IMDb id, so it outlives the title
leaving the library and is the same want when it comes back. The shapes are in
`src/MovieBot.Core/Wants.cs`. Everything here answers for whoever the token names.

| Route | Returns |
|---|---|
| `GET /api/me/wants` | `WantView[]` — the token's person's wants, newest first. Takes `cinema:profile.read` |
| `GET /api/me/wants/page?q=&sort=&state=&kind=&category=&anyGenre=&decade=&length=&minRating=&language=` | `WantsPageView` — `{wants, total, facets}`: the person's wants as their page shows them, searched by title (`q`), narrowed by where each stands (`state`: `ready`, `fetching`, `available` or `waiting`, repeating or taking commas) and by the filter's picks, and ordered by `sort`: `added` (the default: the newest want first), `first-added`, `name`, `name-desc`, `rating`, `lowest`, `newest`, `oldest`, `most-wanted` (the most other people wanting it first) or `least-wanted`. `total`, `facets` and the refusals are as on `/api/library/{scope}`. Takes `cinema:profile.read` |
| `GET /api/me/history/page?q=&kind=&category=&anyGenre=&decade=&length=&minRating=&language=` | `HistoryPageView` — `{entries, total, facets}`: the person's history as `/api/me/history` lists it, newest first, each `{entry, title?}` with the library's title the entry names (as `/api/titles` lists it), searched by the entry's name as `/api/library/{scope}` searches and narrowed by the filter's picks over what that title is, an episode being its show's; an entry whose title is not listed is known only as a film or a show. `total` and `facets` are as on `/api/library/{scope}`, and `sorts` is empty, the history having its one order. Takes `cinema:profile.read` |
| `GET /api/me/together` | `TogetherView[]` — `{want: WantView, with: WanterView[]}`: the titles the person wants that somebody else visibly wants too, ready ones first and then the newest want, computed when asked. A film is together with everybody who wants it; a show with those whose next unwatched episode is the same as the person's. A film the person has watched is left out, and so is a want kept to its person on either side. Takes `cinema:profile.read` |
| `PUT /api/me/wants/{imdbId}` | `SetWantReply` for `{kind, visible?}` — makes the want or changes `visible`. `kind` is `film` or `show`; `visible` defaults to `true` on create and leaves the want as it is when absent on an update. `400` for an id that is not an IMDb id or a kind that is neither; `404` when the catalogue lists no title of that kind under the id. Takes `cinema:watch` |
| `DELETE /api/me/wants/{imdbId}` | `204` — removes it; `404` when the person had none. Takes `cinema:watch` |
| `GET /api/titles/{imdbId}/wanters` | `WanterView[]` — the people with a visible want on the title, the asking person left out, newest first. Takes `cinema:watch` |

- **`WantView`** is `{imdbId, kind, title, card?,
  visible, since, state?, progress?, libraryId?, inLibrary, watched, nextEpisode?, others, wish?}`.
  `state` is what is measured of the title: `ready` (the library holds it), `fetching` (the torrent
  client has it, `progress` from 0 to 1), `available` (the tracker offers a release at the source floor,
  the wish having come out) or `waiting` (a wish is kept and the tracker has nothing at the floor);
  absent when none of those is known. `libraryId` is the title that plays a film the library holds;
  `inLibrary` is true for a film the library holds or a show with any episode here; `watched` is the
  asking person having watched the film through or marked it watched, never set for a show. `nextEpisode`
  (`{season, episode, name?}`) is a show's next episode for the asking person: the one after the latest
  episode they finished, else the show's first, read from the watch record when asked and never stored.
  `others` counts the other people with a visible want on the title. `wish` is present while a wish is
  kept for the film, `{release?}` carrying the `ReleaseOffer` found once the tracker offers one.
- **`WanterView`** is `{userId, name?, since, nextEpisode?}`; `nextEpisode` is a show's next episode for
  that person.
- **`SetWantReply`** is `{outcome, want?, release?, libraryId?, libraryName?, downloadName?}`. `want` is
  the want as it stands. `outcome` is `alreadyWanted` (the person had it), and for a want made now
  what the checks found, in the order the library, the torrent client and the tracker answer: `inLibrary`
  (`libraryId`, `libraryName`), `downloading` (`downloadName`), `available` (the tracker offers
  `release` at the source floor), `waiting` (nothing has the film and a wish is kept for it) and
  `wanted` (made, with nothing more known: a show, a film whose checks could not be made, or an account
  that may not wish). A check that cannot be made is skipped rather than refused.
- **Visible means named to everybody signed in.** `visible` defaults to true; a just-me want is counted
  by nobody else and named nowhere, and still drives the wish sweep. Nobody else's want is ever
  returned except through `wanters`, and only the visible ones.
- **Wanting spends nothing by itself; what the server may spend is captured when the want is
  written.** The want records whether the token held `cinema:download` (`mayFetch`) and `cinema:wish`
  (`mayWish`), refreshed every time the person writes it. They are never on the wire.
- **A film nothing has gets a wish.** Making a want for a film the library, the torrent client and the
  tracker (at the source floor) do not have, by a token that holds `cinema:wish`, keeps the catalogue's
  description of it. The hourly sweep asks the tracker for every film with a wanter whose `mayWish` is
  set and tells each of them with an `available` notification, once. The wish lists with the release
  found until the person removes the want.
- **A want whose token held `cinema:download` is fetched ahead of the person.** When no room is playing
  and the disk has room, the server downloads a wanted title the tracker offers at the source floor
  (`Wants:PassSeconds`, `Wants:Concurrent`), and the want reads `fetching` while it arrives. The person
  is told with a `ready` notification once it can be watched. The library keeps a title wanted by
  somebody who has not watched it for `Wants:HoldDays` from the want.
- **A want ends** when the person removes it, when they finish the film, or when they finish the last
  aired episode of the show.
- **An entry is described by its card.** `card` is the title's catalogue card, absent for a title the
  catalogue no longer lists, whose `title` is then its IMDb id.
- **`GET /api/anticipated`** (`AnticipatedView[]`, below in Fetching films) counts visible wants on films
  with a wish, and the asking person's own.

### Plans

A plan is a title, the times put forward for it and the people invited. Nothing about it locks: anybody
in it suggests another time and answers any time, and the maker picks a time and picks again. A plan is
kept only while it is going: the maker cancelling it, or its room going after the picked time (it was
watched), deletes it with its times, people and answers. The room is made ahead of the picked time and everybody coming is reminded. A
plan creates no want. It is shown to its maker and the people invited and to nobody else: for anybody
else every route below answers `404`, as if the plan did not exist. The shapes are in
`src/MovieBot.Core/Plans.cs`.

A time is put forward at least 5 minutes and at most 30 days ahead, kept to the minute, and a plan holds
at most 6 times still ahead (`PlanLimits`, in `src/MovieBot.Core/Plans.cs`).

| Route | Returns |
|---|---|
| `POST /api/plans` | `PlanView` for `{imdbId, kind, times, people, season?, number?}`. `kind` is `film` or `show`; `times` is one to six instants; `people` are accounts `/api/people` names (the maker is in the plan whether named or not, at most 20 others). A show starts at `season` and `number`, else at the maker's next episode. The maker is `in` for every time. `400` for a bad id, kind, time or count, or a show with no episode to start at; `404` for somebody unknown or a title the catalogue does not list; `409` for a film the tracker shows is not out digitally, or a show's episode the catalogue dates to a day it is not yet due on (a tracker that does not answer, or an episode with no date, refuses nothing). Takes `cinema:watch` |
| `GET /api/me/plans` | `PlanView[]` — the plans the person is in, soonest first by the picked time, else the earliest time still ahead. Takes `cinema:watch` |
| `GET /api/plans/{id}` | `PlanView`. Takes `cinema:watch` |
| `POST /api/plans/{id}/times` | `PlanView` for `{at}` — anybody in the plan suggests another time, and is `in` for it; everybody else is told (`plan` notification `suggested`). `400` for a time not ahead as above; `409` for a time the plan has, or a plan holding its most times ahead. Takes `cinema:watch` |
| `PUT /api/plans/{id}/answers/{at}` | `PlanView` for `{answer}` — the person's answer to the time `at` (the instant as `times[].at` names it): `in`, `maybe` or `cant`, changeable at any time. Once the room is open, `in` or `maybe` to the picked time lets the person into it. `400` for another answer or a bad time; `404` for a time the plan does not have. Takes `cinema:watch` |
| `POST /api/plans/{id}/people` | `InviteReply` for `{userId}` — anybody in the plan brings one more person into it, after everybody already in it; the person is told (`plan` notification `invited`, naming who asked), and once the room is open they are let into it. `delivery` is `push` or `bell`, as for a room's invite. `404` for a plan the asker is not in or somebody unknown; `409` for somebody already in it, or a plan inviting its most (20 besides the maker). Takes `cinema:watch` |
| `POST /api/plans/{id}/fix` | `PlanView` for `{at}` — the maker picks one of the plan's times, which must be ahead, or moves the pick to another; every time and its answers stay. A `proposed` plan becomes `fixed`; a `fixed` or `open` one keeps its state, and once the room is open everybody `in` or `maybe` to the new time is let in. The others are told (`plan` notification `fixed`, saying the time moved when there was one). `403` for anybody else; `404` for a time the plan does not have ahead. Takes `cinema:watch` |
| `DELETE /api/plans/{id}` | `204` — the maker cancels the plan, whatever it is at, which deletes it; the others are told (`plan` notification `cancelled`). `403` for anybody else. Takes `cinema:watch` |

- **`PlanView`** is `{id, imdbId, kind, title, card?, season?, number?, state, madeBy, madeAt,
  mine, fixedTime?, times, people, readiness?, progress?, libraryId?, roomId?}`. `state` is `proposed`,
  `fixed` (a time is picked) or `open`. `madeBy`, each of `people` and each time's `suggestedBy` is `{userId, name?, maker}`.
  `times` is every time put forward, earliest first, those already passed included, as
  `{at, in, mine?, suggestedBy?, answers: [{userId, name?, answer}]}` (`in` counts who said so, `mine`
  the asking person's answer). `fixedTime` is the picked one. `libraryId` is the title that plays the
  plan's film or episode once the library holds it, and `card` the title's catalogue card.
- **Answers pick nothing.** However many say `in`, a time is the plan's only once the maker picks it.
- **`readiness` is what is measured of the title, never a guess at whether it will be in time**: `ready`
  (the library holds the film, or the plan's episode), `fetching` (the torrent client has it, `progress`
  from 0 to 1), `waiting` (the tracker offered nothing at the source floor when last asked) or `queued`.
- **A plan with a picked time has its title fetched first.** The maker's `cinema:download`, captured
  when the plan is made, lets the prefetcher fetch the title for the plan (`Acquisition/CLAUDE.md`,
  "Prefetch"), ahead of every title only wanted, and the library keeps it until the plan's time has passed.
- **The room is made `Plans:OpenMinutes` ahead** of the picked time: closed, holding the plan's title
  paused at the start (or the show's episode), with everybody who said `in` or `maybe` let in, so anybody
  else knocks. `roomId` names it and the plan is `open`. The room is kept until `Rooms:IdleTimeout` past
  the picked time; once it is gone after that, the plan was watched and is deleted.
- **Reminders and notices** are `plan` notifications, one for each event (`invited`, `suggested`,
  `fixed`, `reminder`, `cancelled`), each said once to each person; a pick and a reminder are said again for each time picked. The reminder goes to everybody `in` or
  `maybe` `Plans:RemindMinutes` before the picked time and opens the room. A plan whose times have all
  passed is never ended for it: it waits for somebody to suggest another.
- **A plan's people are told it changed** over the hub: `PlanChanged(planId)` goes to the group of each
  person in it (`PersonGroups.Everything`), and a page reads the plan again.

### Notifications

A notification is one sentence for one person, kept for a year in the bell and pushed to every
browser they subscribed. `TheKrystalShip.Notifications` keeps and serves them (its routes are the
contract of `kgsm-docs/reference/notifications.md`); Cinema declares the kinds and maps the routes under
`/api/me`, each taking `cinema:profile.read`, and the anonymous button route at `/api/push/actions/{handle}`
(Cinema's pushes carry no buttons). The library keeps `notifications.db` and `vapid.json` beside
`moviebot.db`. Everything under `/api/me` answers for whoever the token names.

| Route | Returns |
|---|---|
| `GET /api/me/notifications?before=&limit=` | `{unread, items: [{id, kind, text, detail?, url?, action?, createdAt, read}], more}`, newest first |
| `POST /api/me/notifications/read` | 204 for `{ids?}` — marks those read, or all of them when none are named |
| `GET /api/me/notifications/kinds` | `{kinds: [{id, title, description?, icon, tone, switchable, enabledByDefault}]}` |
| `GET /api/me/notifications/preferences` | `{kinds: {id: bool}}`, every switchable kind's effective value; each is on until switched off, except `together`, which is off until switched on |
| `PUT /api/me/notifications/preferences` | `{kinds: {id: bool}}`, any subset → the same as `GET` |
| `GET /api/me/push/key` | `{publicKey, enabled}` — this server's VAPID key, base64url, which a browser subscribes with |
| `POST /api/me/push/subscribe` | 204 for `{endpoint, p256dh, auth}`; a browser registered again replaces its own row, and belongs to whoever registered it last. An endpoint that is not `https` is a `400` |
| `DELETE /api/me/push/subscribe` | 204 for `{endpoint}` |
| `GET /api/me/push/devices` | `{endpoints: [string]}`, the caller's |
| `GET /api/people?q=` | `PersonView[]` — everybody the server has a name for but the person asking, `{userId, name, lastSeenAt?}`, most recently seen first; with `q`, those whose name holds every one of its words |
| `POST /api/rooms/{room}/invite` | `InviteReply` for `{userId}` — lets them into the room and tells them: `{delivery}`, `push` when a push service took it for at least one of their devices and `bell` when it waits in their bell alone. 404 for somebody unknown, 403 for a room private to the person inviting |
| `POST /api/rooms/{room}/guests` | `GuestLink` = `{path, expiresAt}` — the room's guest link, `path` being the site's `/g/<code>`: the one the room has while it is good, or a new one lasting `Rooms.GuestPassLifetime`, so everybody in the room who asks is handed the same. `cinema:guests`, and 403 for a room private to the person asking |
| `DELETE /api/rooms/{room}` | 204 — forgets a room with nobody in it at once, as the reaper does once it has stood idle for `Rooms.IdleTimeout`. 404 for a room there is none of (none is made), 403 for one private to the person asking, 409 for one somebody is in or a plan's room the reaper is holding. `cinema:watch` |
| `DELETE /api/rooms/{room}/guests` | 204 — takes the link away; every guest who came in on it is sent `GuestAccessEnded("ended")` and disconnected. `cinema:guests` |
| `GET /api/guest/{code}` | `GuestInvitation` = `{sessionId, name?, expiresAt}` — the room a guest link opens and the film it holds, for the page the link lands on; a code this server did not sign, or a link no longer good, is a 404. Open |

- **Kinds.** `available`: a film somebody waits on can be downloaded. `ready`: a download they started
  can be watched, or could not be made watchable. `episode`: a new episode of a show they follow is
  out and the tracker offers it, "Show S5 · E4 is out.", opening `/tv/{imdbId}/season/{n}`. `invite`:
  somebody asked them into a room. `together`: titles they want are ready and others want them too,
  one digest a day. `plan`: a plan they are in was proposed to them, fixed, is about to start, or was
  cancelled. An invite and a plan cannot be switched off: they are always kept and pushed.
- **One thing is said once.** Each notification carries the thing it is about, and a person holding a
  notification about it is not told again: a download once, a wish once, an episode once.
- **A kind switched off is neither kept nor pushed.**
- **The text is written once for every surface.** It mentions nobody and names no command, so the
  bell and the push show it as it is.
- **Taken is all that is known.** A push service accepting a push is not a phone showing it, and
  `delivery: "push"` says only the first.
- **Who is owed news of a download is on its torrent.** `tell` is written beside `requester:<account>`
  when it starts, and comes off once the person has been told.

## Manifest

```jsonc
{
  "id": "clip",
  "title": "Gladiator 2000 Extended Cut",
  "durationSeconds": 43.501,
  "status": "transcoding",        // "transcoding" | "ready" | "failed"
  "kind": "film",                 // "film" | "show"; a manifest without it is a film
  "episode": { "season": 2, "number": 4 },  // an episode's place in its show, which "film" then names; absent for a film
  "headSeconds": 20.0,            // absent once ready; the shortest playlist: every rung and the audio have reached this
  "audioHeadSeconds": 31.0,       // how far the audio has reached; absent once ready, and absent when audio is not tracked apart from the rungs
  "error": "…",                   // present only when status is "failed"
  "poster": "poster.jpg",         // the title index's poster put beside the title; absent when there was none
  "master": "master.m3u8",        // load THIS, not a bare video rendition
  "video": {
    "width": 1920,
    "height": 800,
    "sourceCodec": "hevc",
    "sourceHdr": "hdr10+dovi-p8.1",
    "renditions": [
      { "name": "800p", "bitrateKbps": 9000, "width": 1920, "height": 800, "uri": "v0/index.m3u8", "cq": 25, "headSeconds": 20.0 },
      { "name": "536p", "bitrateKbps": 4000, "width": 1280, "height": 536, "uri": "v1/index.m3u8", "cq": 25, "headSeconds": 28.5 }
    ]
  },
  "audio": [
    { "id": "a0", "kind": "feature", "language": "eng", "label": "English",
      "channels": 2, "default": true, "uri": "a0/index.m3u8" },
    { "id": "a1", "kind": "commentary", "language": "eng",
      "label": "Commentary with director Ridley Scott and actor Russell Crowe",
      "channels": 2, "uri": "a1/index.m3u8" },
    { "id": "a2", "kind": "feature", "language": "eng", "label": "English — Dialogue boost",
      "channels": 2, "uri": "a2/index.m3u8" }
  ],
  "markers": [
    { "kind": "recap", "startSeconds": 0.0, "endSeconds": 61.5, "from": "audio" },
    { "kind": "intro", "startSeconds": 489.0, "endSeconds": 523.458, "from": "chapter" },
    { "kind": "credits", "startSeconds": 2799.8, "endSeconds": 2875.957, "from": "audio" }
  ],
  "subtitles": [
    { "id": "s3", "kind": "feature", "language": "eng", "label": "English",
      "source": "embedded-text", "available": true, "uri": "s3.vtt" },
    { "id": "s4", "kind": "feature", "language": "eng", "label": "English SDH",
      "hearingImpaired": true, "source": "embedded-text", "available": true, "uri": "s4.vtt" },
    { "id": "s33", "kind": "feature", "language": "spa", "label": "Spanish (Latin American)",
      "source": "bitmap", "available": false, "reason": "needs-ocr" }
  ]
}
```

`kind` is `"feature"` or `"commentary"`; `source` is `"embedded-text"`, `"sidecar"` or
`"bitmap"`. All `uri` values are relative to `/media/{id}/`. A rendition's `cq` is the constant
quality it was encoded at, absent where none is recorded.

**Rates.** A rendition's `bitrateKbps` is what the encoder was given, a ceiling a rung encoded at a
constant quality stays well under. Once a title is ready, each rendition and audio track carries
`rate: {averageKbps, peakKbps}`, what its segments measured over the film and at the heaviest one;
it is absent before. The master states a variant's `BANDWIDTH` as its rung's peak plus the heaviest
audio track's, and its `AVERAGE-BANDWIDTH` as their averages, and states the ceiling alone as
`BANDWIDTH` for a rung not yet measured.

**Heads.** While a title transcodes, each rendition's `headSeconds` and the manifest's
`audioHeadSeconds` say how far that playlist has reached, and `headSeconds` is the shortest of them.
All of them are absent once the title is `ready`. A manifest with no rendition `headSeconds` says
every rung is at the overall head. A title is playable as far as its best rung, held back to the
audio.

**Load `master`, not `video.renditions[].uri`.** The master playlist is what binds the audio
renditions to the video; a player handed a bare video rendition plays the film silently with no
audio track to switch to. Subtitles are not in the master — they are standalone WebVTT files
attached as text tracks from `subtitles[].uri`.

Every feature track has a second mix with the dialogue raised, listed after the source's own
tracks with the label `"<track> — Dialogue boost"`. It is made after the main pass, so `audio`
gains those entries once, before `status` turns `ready`, and the master is rewritten with them in
the same moment. A player that loaded the master earlier offers them after it loads the film again.

`markers` are the stretches of an episode a viewer can skip, in time order, and are empty for a film
and for an episode nothing was found in. `kind` is `"intro"` (the title sequence), `"recap"` (the
"previously on" at the head of the episode) or `"credits"` (the closing credits, which end the episode
or are followed by a preview or a scene); `startSeconds` and `endSeconds` are positions in the
episode, and skipping seeks to `endSeconds`. Credits that run to the end of the episode end at its
`durationSeconds`. `from` is `"chapter"` when the container names the
stretch, and `"audio"` when it was found by comparing the episode's sound with the show's other
episodes, which places the start within about half a second and the end up to a second early, never
late; credits found that way can start later than the credits do, never earlier. A chapter's own edges are the container's and can differ from the audio's by several seconds,
since a chapter named `Intro` may end before the titles do. An episode has at most one marker of
each kind. Markers are written once the episode is `ready` and can appear
on an episode already in the library, so a player reads them from every `TitleChanged` as well as
from the first load.

A track with `available: false` is listed on purpose — a language that is simply missing from the
menu reads as a bug. Render it disabled with its `reason`, do not hide it.

`TitleSummary` is `{id, title, durationSeconds, status, kind, episode?, unit, headSeconds?, poster?,
posterVersion?, film?, card?, sizeBytes?, addedAt?, progress?, lastPlayed?}`. `kind` is `"film"` or
`"show"`, as the manifest says. `poster` is present whenever the title has a poster to serve, and
`posterVersion` is when the file it is served from was last written, in milliseconds since the epoch; a
page asks for the poster as `/media/{id}/{poster}?v={posterVersion}`. Every episode of a show is a title of its own: `episode` is `{season, number}`, `film`
is the show, `title` is the show's name with the episode's place (`Severance S02E04`), and `unit` is
its season's (a film's is its own id). `card` is the film's catalogue card, or an episode's show's
(Catalogue), absent for a title the catalogue does not list; its `category` is the shelf the title is on.
`progress` is `{positionSeconds, finished, lastWatchedAt}` for the person the
token names, absent when they have not watched the film and always absent for the service key.
`lastPlayed` is `{at, byId, byName?}`, the last time anybody played the film and who (their Discord
id), absent when nobody has; `byName` is absent when that person's name was never heard.

`RoomSummary` is `{sessionId, titleId?, name?, durationSeconds?, paused, positionSeconds,
participants, open}`. `name` is what the film is called, `positionSeconds` is where the room was when
the listing was taken, `participants` is a count: the people in a room are listed by the room
itself, never here, and `open` says anybody who may watch can come in.

## Catalogue

Everything Cinema knows about a title is the catalogue's (`src/MovieBot.Catalogue/CLAUDE.md`): one entry
per film and show, IMDb's datasets the base with TMDB and Wikidata layered over it, kept on this host with
the images a page shows. The API reads it and reaches no source, so navigating the site never waits on
anybody else. A title is wherever it is listed the same card, and one page.

**Ids.** A title is its IMDb id (`tt0232500`), which every record a person made names too. A person is the
catalogue's person id. A franchise is `c<collection>` for a film database collection and its Wikidata item
(`Q642878`) for a universe; a keyword is `k<id>`, a company (studio or network) `s<id>`, a Wikidata item its
`Q` id, and a Wikidata fact a property and an item (`P840:Q90`, set in Paris; `P144/P50:Q…`, based on a work
by an author).

**The card**, `TitleCard`, wherever a title is listed:

| Field | |
|---|---|
| `id`, `kind`, `title`, `originalTitle?` | `kind` is `film` or `show`; `originalTitle` when it differs |
| `year?`, `endYear?`, `released?`, `runtime?` | `endYear` a show's last; `released` `yyyy-MM-dd`; `runtime` in minutes |
| `category` | `anime` when the genres hold Animation and the original language is Japanese, else `general` (Categories) |
| `genres` | IMDb's genres, the one vocabulary of films and shows |
| `language?` | the original language, an ISO 639-1 code |
| `rating?` | `{value?, votes, by}`: IMDb's where it rates the title, else the film database's (`by` `imdb` or `tmdb`); no `value` under 25 votes |
| `poster?`, `backdrop?` | image URLs (`/images/…`, below) |
| `plot?` | the overview's first sentence, at most 200 characters |
| `madeBy`, `starring` | a film's directors or a show's creators, and the two top-billed, as names |
| `changedAt?` | when anything a page shows of it last changed |
| `credit?` | on a list filtered by one person: `{department, role}`, their part in it |
| `you?` | the asking person's, joined by the API: `{inLibrary, libraryId?, watched, wanted}`; `libraryId` the library's title for a film it holds; `watched` a film they watched through here or marked watched, here or not |

**The page**, `TitlePage`, `GET /api/catalogue/titles/{imdbId}`: the card, and `overview?`, `tagline?`,
`status?`, `logo?`, `ratings` (`{imdb?, tmdb?}`, both sources' ratings), `certificates` (by country; a page
shows the viewer's own, read from the browser's languages, since Cinema fixes no country), `countries`,
`languages`, `companies` and `networks` (`{id, name}`), `keywords` (`{id, name}`), `cast`
(`{personId, name, character?, portrait?}`), `crew` (`{personId, name, job}`), `trailers`
(`{key, name, kind}`, each playing from `https://www.youtube-nocookie.com/embed/{key}`), `facts`
(Wikidata's: `setIn`, `filmedIn`, `basedOn` as `{work, authors}`, `awards`, `nominations`, `subjects`, each
named thing `{id, name}`), `franchises` (`{id, name, ordinal?}`, its place in each), `follows` and
`followedBy` (cards), `budget?` and `revenue?` (US dollars), a show's `seasons`
(`{number, name?, airDate?, poster?, episodes}`) and `nextEpisode?` (`{season, number, name?, airDate}`), and
`similar` (cards).

**The list query**, `GET /api/catalogue/titles`, answers every list of titles: a search, a shelf, a genre,
a person's work, a franchise, a ranking, what is out or coming and what is like a title are all its filters,
and they combine. `page` and `size` (20 by default, at most 100) page it.

- `q`: words, matched against every name a title goes by, its English title first; the closest match
  first, a well-known title before an obscure one of the same name.
- `kind` (`film`, `show`), `category` (`general`, `anime`); `genre` (all of them), `anyGenre`, `notGenre`.
- `from`, `to` (years), `decade` (`1990`); `released` and `releasedBefore` (dates); `length` (`short` under
  90 minutes, `standard` to 150, `long`), `minRuntime`, `maxRuntime`.
- `language` (ISO 639-1), `country` (ISO 3166).
- `keyword` (all of them), `anyKeyword`, `company`; `person` with `role` (`cast`, `directing`, `writing`, `any`).
- `minRating`, `maxRating`, `minVotes`, `maxVotes`; `status` (`Released`, `Ended`, `Returning Series`,
  `Canceled`, `In Production`), `maxSeasons`.
- `fact`: a Wikidata fact, any of them repeated.
- `franchise`, `ranking` (`trending-films`, `popular-shows`, …), `similarTo` (an IMDb id).
- `unwatched`, `inLibrary` (`true`, `false`), and `here` (`watched`: what the people here watched most
  lately; `ready`: what the library holds ready, newest first): the asking person's, applied by the API.
- `sort`: `popular`, `rating` (IMDb's weighted by votes), `lowest`, `newest`, `oldest`, `title`, `votes`, or
  `order`, the ranking's, the franchise's or the similar titles' own, which needs one of them.

`GET /api/catalogue/facets` takes the same filters (Filters and facets).

**Images.** Every image is `/images/{size}/{file}`, the film database's file at the size it is kept at; a
poster is `w500`, a backdrop `w1280`, a portrait `w185` and a still `w300`. A file never changes under its
name, so its URL is cached for good; a title shown with another image names another URL.

**Credit.** The catalogue's words and images are TMDB's, IMDb's and Wikidata's. TMDB's terms ask for the
words "This product uses TMDB and the TMDB APIs but is not endorsed, certified, or otherwise approved by
TMDB" wherever they are shown, and every page that shows them carries them.

## Hub

SignalR at `/hub/session`. Camel-cased payloads, string enums.

**Call:**

| Method | Signature | Notes |
|---|---|---|
| `Join` | `(sessionId) → JoinReply` | call first; everything else but `Knock` throws until you are in. The viewer is the connection's token: its `sub` and `name`. `{admitted: true, push, door}` puts them in the room, and a room there is none of is made, open, with them let in; `{admitted: false}` is a room private to them, and says nothing else about it (Who may come in, below) |
| `Rejoin` | `(sessionId, account: PlayerAccount?) → JoinReply` | `Join` for a page coming back into a room it was in, with what its player did while the room could not hear it: `{epoch, revision, titleId, positionSeconds, paused}`, the last state it applied and where its playhead is. The room takes it as its state when nobody else with an account is in it and the room either holds that film with nothing done to it since that revision or holds nothing; the state names no actor. Otherwise it is `Join`. A null `account` (a player holding no film) is `Join` |
| `JoinAsGuest` | `(name: string?) → JoinReply` | a guest link's `Join`: the link names the room. `{admitted: true, push, you}` puts the guest in, `you` the `Participant` the room has them as (`guest: true`, an id of the connection's, `name` cleaned of control and formatting characters and held to 32, "Guest" when empty); calling it again on the same connection keeps them who they were. `{admitted: false, refused}` says why not: `ended` (the link is no longer the room's), `unattended` (nobody with an account is in the room, nor has been within `Rooms.GuestGrace`) or `full` (`Rooms.GuestsPerRoom`). A connection that is no guest is refused |
| `JoinAsRemote` | `(screenId) → JoinReply` | a paired phone's `Join`: the connection becomes a **remote** of the screen `screenId` names (Screens, below), in the room that screen is in now. `{admitted: true, push}` is the room's state; `push` is absent while the screen is in no room, and the remote is put in one when the screen joins it. A `HubException` when the connection is no phone (a screen, a token of no session, a guest, a connection with no account), when the phone is not paired with that screen, or when the connection is in a room. Calling it again on the connection changes the screen it drives |
| `WatchOnScreen` | `(room, titleId: string?, startSeconds: double?)` | a remote puts a title on the screen it drives while that screen is in no room: the screen is sent `Watch` and opens `room`. The screen is the remote's binding and the person the token's, and the pairing is checked again. `room` is the phone's own choice (1 to 64 letters, digits, `-` or `_`), `titleId` a title the library holds (absent for a room already waiting on a film being fetched, Watch now) and `startSeconds` a place in it, finite and not negative. A `HubException` when the connection is no remote, its pairing ended, the screen is not connected or is already in a room, or an argument is not as above. A screen already in a room is moved through that room with `LoadTitle`. Both arguments are always sent, null for none |
| `Knock` | `(sessionId) → bool` | asks to be let into a room private to this viewer. `true` is no need to ask, so `Join` now; otherwise the room is told who is asking and this connection is answered with `KnockAnswered`. The asking ends with the connection |
| `SetOpen` | `(open: bool)` | opens the room to anybody holding `cinema:watch`, or closes it to anybody not already let in |
| `Answer` | `(userId, admit: bool)` | lets somebody asking in, or tells them they are not |
| `Next` | `()` | the episode after what the room holds, put on, or fetched into the room at the quality of the one playing for somebody holding `cinema:download`; a `HubException` when there is none to put on. One step is taken in a room at a time: a `Next`, `Previous` or `PlayEpisode` that arrives while another is being taken is dropped, and answers as done |
| `Previous` | `()` | the episode before, the same way |
| `PlayEpisode` | `(season, number)` | an episode of the show the room holds, the same way |
| `LoadTitle` | `(titleId, startSeconds: double?) → SessionStatePush` | from `startSeconds`, held to the transcode head, or from 0 when null; a playing room keeps playing, a paused one stays paused. Both arguments are always sent, null for no start |
| `Play` | `(atSeconds) → SessionStatePush` | |
| `Pause` | `(atSeconds) → SessionStatePush` | |
| `Seek` | `(toSeconds) → SessionStatePush` | may be clamped |
| `ServerTime` | `() → DateTimeOffset` | for clock-offset estimation |
| `Screens` | `() → ScreenView[]` | the television screens this connection's session is paired with, connected or not, with the room each is in and what it holds (Screens, below). Empty for a screen and for a connection that belongs to no session; a guest is refused |
| `PairingCode` | `() → PairingCodeView` | a screen's code for a phone to scan: `{path, expiresAt, renewSeconds}`, `path` the site's `/p/<code>`. Only a screen is given one |
| `Remotes` | `() → int` | how many remotes drive this screen now. Only a screen asks |
| `Phones` | `() → PairedPhone[]` | the phones paired with this screen, oldest first: `{id, name, pairedAt}`. Only a screen asks |
| `Unpair` | `(id)` | ends a pairing of this connection's session: from a screen, with the phone `id` names; from any other connection of a session, with the screen it names. Only the caller's own person's pairings are reached; one there is none of is no change |
| `Buffering` | `(waiting: bool, atSeconds: double)` | this viewer's player has been waiting for the film past a stutter, stopped at `atSeconds`, or is playing again. The roster carries it; when every player in the room is waiting the room is held (`waiting` in the state) where the furthest behind of them stopped, and let go when one plays, somebody arrives, or a waiting one leaves |
| `ReportQuality` | `(playingHeight: int?, wantedHeight: int?)` | the rung height this viewer plays, and the one its own adaptive choice would pick with nothing holding it back; on change and about every 30 s. Answers nothing and is sent to nobody: the server keeps it per connection for its transcode planner and drops it when the connection leaves. A player that cannot say (native HLS) sends nothing, and is taken to want the tallest rung |
| `ReportTrouble` | `(kind: string, atSeconds: double, detail: string?, playback: string?)` | what went wrong with this viewer's film: `kind` is one of `network`, `media`, `stuck`, `device-slow`, `no-start`, `gave-up`, `detail` a short account (at most 160 characters are kept), `playback` the CMCD session id this playback's requests carry. Answers nothing and is sent to nobody: it is written to the API's log, at most six lines a minute per connection, with what was said past that counted. A player says each kind at most once every 30 s; a connection not in the room is ignored |
| `ReportStats` | `(snapshot: string) → bool` | what this viewer's playback stats panel shows, sent when they press Send: a JSON object of at most 8192 characters (`web/site/src/player/CLAUDE.md`, Playback stats). Sent to nobody: it is written to the API's log as one line under the viewer's name, the room and the film. `true` once written; `false` for anything but a JSON object, one past the limit, or a second within 10 s on the same connection. A connection not in the room is refused (`HubException`) |

**Receive:**

| Event | Payload |
|---|---|
| `StateChanged` | `SessionStatePush` — sent to everyone including the caller |
| `SeekClamped` | `{requestedSeconds, grantedSeconds, headSeconds}` — sent to the caller only; `headSeconds` is the best rung's head held back to the audio, which is at least `transcodeHead` |
| `ParticipantsChanged` | `Participant[]` = `{userId, displayName, buffering, guest}[]` — sent when somebody joins, leaves, or starts or stops waiting for the film; `guest` marks somebody on the room's guest link |
| `TitleChanged` | `Manifest` — the manifest of the title this room holds changed: head, preview sheet, subtitles, status. Sent to every room holding the title, within a couple of seconds of the write |
| `DoorChanged` | `RoomDoor` = `{open, knocking: [{userId, displayName}], guests?: {expiresAt}}` — sent to the people with an account in the room, never to a guest, when it is opened or closed, when somebody starts or stops asking, and when its guest link is given, taken away or runs out; `knocking` holds each person once, in the order they asked, and `guests` is there while the room has a link, never the link itself |
| `KnockAnswered` | `bool` — sent to a connection that asked to be let in: `true` when somebody let them in or opened the room, `false` when somebody said no |
| `RemoteMoved` | `JoinReply` — sent to a remote when its screen's room changes: `push` is the state of the room the screen is in now, absent when the screen has left its rooms or disconnected. The remote is already out of the room it was in and in the new one when it is sent; the state it carries settles the comparison (rule 1, below) whatever revision it holds |
| `ScreenArrived` | `ScreenView` — a screen paired with this session connected, after having no connection, or was paired with it. Sent to the session's connections that are not screens |
| `ScreenChanged` | `ScreenView` — a screen paired with this session changed name, room, what the room holds or whether the room is paused, or lost its last connection (`connected: false`, no `room`). Sent to the same connections |
| `Watch` | `{room, titleId?, startSeconds?}` — sent to a screen's own connections when a remote of it called `WatchOnScreen`; the screen navigates to `/r/<room>`, with `?title=` and `&at=` as the site's Watch writes them |
| `PreferencesChanged` | `ViewerPreferences` — how this person watches changed (`PUT /api/me/preferences`): the whole record, with `revision`. Sent to every connection of the person's that has an account behind it, a playing player's and a phone's remote among them, and to nobody else's: not another person's, not a guest's, and never into the room |
| `TracksChanged` | `FilmTracks` — the subtitle or audio track this person watches a film with changed (`PUT /api/me/tracks/{titleId}`): that film's tracks as they now stand, with `revision`. Sent to the same connections as `PreferencesChanged`; a player holding that film applies it, any other connection ignores it |
| `ScreenLeft` | `string` — the id of a screen whose pairing with this session ended. Sent to the same connections |
| `RemotesChanged` | `int` — a remote of this screen joined it, left it or was dropped; how many drive it now. Sent to the screen's own connections and never into the room: a remote stays out of `ParticipantsChanged` |
| `PhonesChanged` | `PairedPhone[]` — a phone paired with this screen or unpaired from it; the phones paired now. Sent to the screen's connections |
| `GuestAccessEnded` | `string` — sent to a guest as they are sent away, and their connection is closed after it: `ended` (the link was taken away or ran out) or `unattended` (nobody with an account has been in the room for `Rooms.GuestGrace`) |

### Screens

Every hub connection whose token names a person joins that person's group; a guest's joins none. A
connection is a **screen** when its token's `client_id` is the television client (`Oidc.TvClientId`,
`tvClientId` on `/api/config`) and the token carries a `sid`, the anchor session; the server decides it
from the validated token and nothing the page sends. A screen is its `sid`, which survives token
refresh, so a television signed in again is a new screen. The name is the token's `tks_device_name`,
read each time a connection opens. A television holds a connection for as long as it is signed in, so a
rename made at the anchor reaches the screen's name when that connection next reconnects.

```jsonc
// ScreenView
{
  "id": "sid-…",            // the anchor session
  "name": "Living room",    // absent when the device session has none
  "connected": true,        // a hub connection of the screen is open now
  "room": "abc",            // the room its newest connection joined; absent when it is in none
  "titleId": "heat-1995",   // what that room holds; absent when it holds nothing
  "title": "Heat (1995)",   // the title's name as the library lists it
  "paused": true            // whether that room is paused
}
```

A **phone** is any other connection or request whose token carries a `sid`: a browser or an installed
app signed in on a session of its own. It is **paired** with a screen by sending the code the screen
shows (`POST /api/me/screens`), and stays paired until the phone or the screen unpairs, or a session of
the two ends. A pairing belongs to the session that scanned, so a browser tab and the installed app on
one phone are two phones. A token that belongs to no session, as the Activity's does, pairs nothing.

- **A code is short-lived and names its screen and person.** It is sixteen bytes written URL-safe in
  22 characters: when it runs out, a tag of the screen, and a signature over both and the screen's
  person under a key derived from `Auth.MediaTicketKey`. Nothing is stored; it pairs for five minutes,
  and a screen shows a new one every `renewSeconds` (a minute), so the code on screen always has most
  of its life left and a photo of one pairs nothing once it has run out. A code is checked against the
  screens known now, so the screen that showed it must have been connected since the API last started.
- **Only the phones paired with a screen hear it, and only its own person's.** `ScreenArrived`,
  `ScreenChanged` and `ScreenLeft` go to each paired session's connections that are not screens, and
  `PhonesChanged` to the screen's own; `Screens`, `Phones` and every route answer for the account and
  the session in the caller's token, and take no argument naming another person.
- **Pairings are kept** in the API's database, by the phone's `sid` and the screen's, with the person,
  the phone's name and when it paired. The phone's name is read from the browser that paired, as
  "Chrome on Android" or "Safari on iPhone". Cinema hears no revocation, so a session unseen for longer
  than the provider keeps a session it has not renewed (`Oidc.SessionLifetime`) has ended, and its
  pairings are forgotten; a session is seen whenever a connection of it opens or closes and whenever it
  asks for its pairings, and a screen connected now is seen now. A television signed in again under the
  name of one of the person's screens that is not connected ends that screen's pairings, with
  `ScreenLeft`: it is the same television, and is paired again by scanning it again.
- **A screen not connected stays known** (`connected: false`, no room): a paired phone lists it under
  the name it last had. Screens themselves are kept in memory; a restart drops them, and each reappears
  when its television connects.
- A screen's room follows its newest connection's `Join`, and `ScreenChanged` is sent whenever the
  room's title or paused state changes.

A **remote** is a phone's connection that drives one of its paired screens' rooms (`JoinAsRemote`). The
server decides it from the validated token and the pairing, and the page names only the screen it wants:
the phone's `sid` must be paired with that screen, both of the token's person, checked again on every
message the remote sends.

- **It binds to the screen, not the room.** A remote's room is the room its screen is in right now,
  asked each time it acts, so it never moves a room its screen has left. When the screen's newest `Join`
  is another room the server takes the remote out of the old room's group and into the new one and
  sends `RemoteMoved`; when the screen's last connection closes, or it has no room connection, the
  remote is in none (`RemoteMoved` without a `push`) and what it asks is refused until the screen is in
  one. A screen that is not connected is a remote's screen all the same.
- **It drives the room as a member does and is in nothing the room counts.** `Play`, `Pause`, `Seek`,
  `LoadTitle`, `Next`, `Previous` and `PlayEpisode` go through the same place as everybody's, and the
  room's `StateChanged`, `TitleChanged` and `ParticipantsChanged` reach it. It is not in `Participant[]`,
  the room's count, `/health`'s `watching` or the room listing, it holds nobody (`waiting` never counts
  it), it is no viewer anything is recorded for, and it is in no transcode plan. `Buffering`,
  `ReportQuality`, `ReportTrouble`, `ReportStats`, `Join`, `JoinAsGuest`, `Knock`, `SetOpen` and `Answer` refuse it.
- **An act of a remote names the person alone.** `updatedBy` is the screen's person, as when they act
  at the television; no field says it came from a phone.
- **The pairing ends it.** Unpairing from either side, or a television signed in again under the same
  name, takes the remote out of the room, and what it asks next is refused. Its own `ScreenLeft` says so.

### Who may come in

A room is open from the moment it is made, whichever client its maker came through. Whoever makes it
is let in by making it — by `Join`, by any room route below, or by Watch now — and so is anybody who
enters while it is open. Once somebody in it closes it (`SetOpen`), nobody else comes in until somebody
in it lets them in, invites them (`POST /api/rooms/{room}/invite`), or opens it again. Arriving opens
no room that already stands. Entering an open room lets that person in for good,
so closing it again shuts out nobody who has been in, a reload included, and lets in nobody new. Who was
let in is kept with the room across a restart. What a room private to a person tells them is that it
is private and nothing more: `Join` answers `{admitted: false}`, every room route a `403` with
`{"error": "This room is private."}`, Watch now naming the room the same `403`, and the listing leaves
it out. The service key is let into every room.

A **guest** comes in on the room's guest link and never through its door: the link admits no account,
lets nobody in for good, and is the door's neither to open nor to close. Somebody in the room holding
`cinema:guests` gets the link (`POST /api/rooms/{room}/guests`) and anybody holding it may take it away;
the room keeps one link, journalled with the room, until it is taken away, runs out
(`Rooms.GuestPassLifetime`), or the room is forgotten. A guest watches and nothing more. They hear
`StateChanged`, `TitleChanged` and `ParticipantsChanged`, never the door; nothing their player does
holds the room or is recorded as anybody watching; and every hub method but `JoinAsGuest`,
`ReportQuality`, `ReportTrouble`, `ReportStats` and `ServerTime` refuses them twice over, once by the hub's filter and once by the
method itself, which needs a person's token or the actor `Join` records for one. Guests stay only while
somebody with an account is in the room: once nobody has been for `Rooms.GuestGrace`, each is sent
`GuestAccessEnded("unattended")` and disconnected, and a guest arriving then is refused. A good guest
link's page, `/g/<code>`, unfurls with its room's film whether or not the room is open, as a room's page
does for an open one.

```jsonc
// SessionStatePush
{
  "state": {
    "sessionId": "abc",
    "titleId": "clip",
    "paused": false,
    "positionSeconds": 42.0,       // true as of anchorUtc, NOT a ticking number
    "anchorUtc": "2026-09-01T18:22:07.123+00:00",
    "rate": 1.0,
    "updatedBy": { "userId": "u1", "displayName": "Alice" },
    "revision": 7,
    "epoch": "3f1c...",            // which run of the room the revision belongs to
    "transcodeHead": 300.0,        // the shortest playlist; absent once the title is ready
    "rungHeads": [                 // each rung's reach, by advertised height; absent once ready and for a manifest that records one head
      { "height": 1080, "seconds": 300.0 },
      { "height": 720, "seconds": 900.0 }
    ],
    "audioHead": 1000.0,           // absent unless the manifest tracks audio apart
    "waiting": false               // every player in the room is waiting for the film: held, its clock still; set by the server, with no updatedBy
  },
  "serverTime": "2026-09-01T18:22:07.456+00:00"
}
```

## Rules a client must follow

1. **Discard any push whose `revision` is not greater than the last applied, within one
   `epoch`.** Revisions are monotonic and server-assigned; this is what makes reordered and
   duplicated pushes harmless. They count within a run of a room and mean nothing across two:
   rooms live in memory, and one built again — swept for being empty, or lost with the process —
   counts from zero. A push carrying an epoch you have not seen resets the comparison. Without
   that a client discards everything a rebuilt room says for as long as its page stays open, and
   goes on driving the film from a state nothing can correct.
   A state handed back by `Join` is an answer to "where is the room", not a broadcast that might
   have overtaken another, so apply it whatever revision it carries.
2. **Derive the position, never read a ticking number.**
   `paused || waiting ? positionSeconds : positionSeconds + (serverNow - anchorUtc) * rate`, where
   `serverNow` is local time corrected by the offset learned from `serverTime`. A client that
   missed ten seconds of pushes still computes the right answer.
3. **Suppress echoes by comparison, not by counting.** Applying remote state moves the playhead
   and raises the very events that publish intent; without suppression two connected clients feed
   each other forever. Record the position you applied and drop the resulting event when it
   matches; do **not** set a flag and clear it when the expected event arrives. A seek interrupted
   by a second seek raises no `seeked` at all, so a flag waiting for one stays set and silently
   swallows the next thing the person actually does.
4. **Never clamp seeks locally.** Ask for what the person clicked. The server grants a position
   short of the head and answers `SeekClamped` to you alone; show that, do not pre-empt it. A
   client's head is always slightly stale, and local clamping splits the room's timeline.
5. **Publish a seek from the control that made it, never from the element's `seeked`.** The
   media element seeks for reasons of its own — the start position a player picks for a playlist
   still being written is the end of it — and publishing those drags the whole room to the
   transcode head the moment somebody opens the film. `seeked` is for recognising the seek you
   applied, and nothing else.
6. **Do not extrapolate a state the server has not confirmed.** The anchor keeps running whether
   or not the film does, so deriving forward from the last thing heard while disconnected returns
   a position for a film that may have been stopped minutes ago. Hold the last state; derive from
   it again once the room can be heard from.
7. **Clamp a derived position to the film.** A room left playing derives a position that keeps
   growing; the film does not, and a seek past the end of a media element never completes — which
   blocks every seek after it.
8. **A buffering viewer never holds anybody else.** A stalled client falls behind and resyncs
   itself; one person's slow connection does not stop the film for the room. Only a room in which
   every player is waiting is held, by the server, at the playhead the furthest behind reported,
   since a timeline nobody can follow only sends each player, once it has the film again, to a
   place it has not fetched. Report where the playhead stopped, and say so again if a held room
   reaches a player that is playing.

## Revisions of a person's settings

A write to `/api/me/preferences` or `/api/me/tracks/{titleId}` answers with the record carrying a `revision`,
and the push of that write carries the same number; a read carries none and a request never names one. The
numbers are stamped inside the transaction that keeps the write, so they follow the order the database kept
the writes in, and they only rise: each is at least the clock in milliseconds and one past the last of its
stream. One stream is a person's preferences and another a person's tracks for one film; numbers of
different streams are not comparable. A device applies a push only when it is newer than the last record it
applied or its own write was answered with, and holds a push that arrives while one of its own writes is in
flight until that write has answered (`player/engine/session/ordered.ts`), so an echo of a write that is
older than the device's latest never shows.

## Shared versus local

Shared, and nothing else: which title, paused, position, rate, who changed it.

**Subtitle selection, audio track, how subtitles look and quality are per-person**, never the room's:
the API keeps them for the person (`/api/me/preferences`, `/api/me/tracks/{titleId}`) and tells that
person's connections when they change, so every device they watch on, and a phone driving a television,
read one record. They are never in the room's state, its roster or its groups: one person needing
subtitles should not put them on five other screens. A guest has no account, so a guest's choices stay
in their browser. **Volume and brightness** are about the speakers and the screen in front of the person
and live in the browser's `localStorage`; shared volume is a way for one person to deafen everyone else.
