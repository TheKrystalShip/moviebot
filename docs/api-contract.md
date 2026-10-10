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
| `GET /.well-known/tks-actions.json` | the action manifest (`kgsm-docs/reference/action-manifest.md`): every action Cinema checks, in the namespace `cinema`, at `cluster`, the self actions marked `self`. Open: the provider reads it |
| `GET /api/titles` | `TitleSummary[]`. The library as listed at most ten seconds ago, as are the episodes `/api/shows` and `/api/shows/{imdbId}` group; a person's progress and last plays on it are current |
| `GET /api/titles/{id}` | `Manifest`, or 404 |
| `GET /api/titles/{id}/ticket` | `{ticket, expiresAt}` — what a request for this film's bytes carries, or 404 |
| `GET /api/sessions` | `RoomSummary[]` — every room the person may enter, what it is watching, how many are in it; every room on the service key. A private room is in nobody else's list |
| `GET /api/sessions/{id}` | `SessionStatePush` — creates the session if new. This and every `/api/sessions/{id}/…` route lets the person into the room when it admits them, makes it for them when there is none, and is a `403` when it is private to them (Who may come in, under Hub) |
| `GET /api/sessions/{id}/participants` | `Participant[]` |
| `GET /api/sessions/{id}/around` | `{current?, previous?, next?}` — the episode the room holds, by its title or as it arrives, and the episodes either side, each `{season, number, name?, titleId?}`: the next of its season else the first of the next, as the film database lists the show, or as the library holds it for a show it has not described, and previous the other way. `titleId` is absent for one the library does not hold, which is named only to somebody holding `cinema:download`. All absent for a film |
| `POST /api/sessions/{id}/title` | `SessionStatePush` — puts a film in the room, or 404 if the library has no such title. The body is `{titleId, startSeconds?}`; `startSeconds` puts the room there instead of at the beginning, held to the transcode head like a seek. Recorded as the token's person, or as MovieBot on the service key |
| `POST /api/sessions/{id}/play` | `RoomChanged` |
| `POST /api/sessions/{id}/pause` | `RoomChanged` |
| `POST /api/sessions/{id}/seek` | `RoomChanged` |
| `POST /api/sessions/{id}/seek-relative` | `RoomChanged` |
| `GET,HEAD /media/{id}/**` | playlists, segments, subtitles, poster. Opened by `?mt=` or the bearer token |
| `GET /api/films/{imdbId}` | `FilmView` — a film as the film database describes it, in the library or not, with artwork at this server's addresses. `404` when the database holds no film under the id, `503` when it cannot be asked and nothing is kept |
| `GET,HEAD /films/{imdbId}/{poster.jpg,backdrop.jpg,cast-N.jpg}` | the artwork kept beside a description. Open: it is public artwork and an `<img>` carries no token |
| `GET /api/popular` | `{films, shows, fetchedAt?}` — the films and shows the film database counts as trending this week, most first, each `{imdbId, title, year?, rating?, votes, posterUrl?, category?}`, read from what is kept. Both lists are empty and `fetchedAt` absent until the database has first been asked |
| `GET,HEAD /popular/{imdbId}.jpg` | a trending entry's poster, open like a film's artwork |
| `GET,HEAD /glances/{imdbId}/poster.jpg` | a search result's or a catalogue title's poster from the film database, the one its page shows, fetched and kept the first time it is wanted; open like a film's artwork |
| `GET /api/catalogue/people/{personId}` | `CataloguePersonView` — `{personId, name, knownFor?, portraitUrl?, titles}`: a person by the film database's id for them (a cast member's `personId`, a director's, writer's or creator's in `personIds`), and up to 40 films and shows they are credited on, the most voted on first. `titles` are `CatalogueTitleView`s, each with `role`, the part played or the job done (`Director`, `Writer`…), several joined by ` · `. A credit as themselves and any on a talk, news or reality show is left out, and so is a title with no IMDb id. `knownFor` is the database's department for them (`Acting`, `Directing`…). Kept a week. `404` when the database has nobody under the id, `503` when it cannot be asked and nothing is kept |
| `GET /api/catalogue/genre?kind=&name=&category=` | `CatalogueGenreView` — `{kind, genre, category?, titles}`: up to 40 of the most popular films (`kind=film`) or shows (`kind=show`) in the genre `name` names, in any case, each with at least 100 votes; `genre` is the database's own spelling. `category=anime` lists the anime among them (Categories, below), and is the one category a genre takes; `category` in the answer says so. A genre's name is the one a description's `genres` carries, so a shelf of a genre leads on to it in one call. Films and shows have separate genres (`Science Fiction` is a film's, `Sci-Fi & Fantasy` a show's). Kept a day. `400` without a kind or a name, or for a category that is not `anime`, `404` for a name the database has no genre by, `503` when it cannot be asked and nothing is kept |
| `GET /api/catalogue/search?q=&kind=&year=` | `CatalogueSearchView` — `{query, titles}`: films and shows by their name or words from it, best match first (a title named exactly as typed leads), at most 10, each a `TitleHitView`. `kind` is `film` or `show`; `year` keeps the titles released in it or the year either side, and drops those with no date. `400` for a blank `q` or a kind that is neither, `503` when the database cannot be asked and nothing is kept. Kept a day |
| `GET /api/catalogue/people?q=` | `CataloguePeopleView` — `{query, people}`: the people a name could mean, best match first, each `{personId, name, knownFor?, portraitUrl?, knownForTitles}` where `knownForTitles` are up to three `Name (year)`. `portraitUrl` is set once the person's credits have been asked for. Kept a day. `400` for a blank `q` |
| `GET /api/catalogue/people/{personId}/credits?from=&to=&role=&kind=&appearances=` | `PersonCreditsView` — `{personId, name, knownFor?, portraitUrl?, asOf, total, credits}`: everything the person is credited on, cast and crew, films and shows, **oldest first**, each a `CreditView`. `from` and `to` are years (the same year twice is that year); `role` is `acting`, `directing`, `writing` or `any` (the default, every credit including producing and the rest of the crew); `kind` is `film` or `show`. A part as oneself and any part on a talk, news or reality show is left out unless `appearances=true`. A credit with no date is an announcement and is listed whenever the years asked for reach this year or later, or have no end. At most 300 credits come back, the newest, and `total` counts every match. Kept a day. `400` for a role or kind that is not one or `from` after `to`, `404` when the database has nobody under the id, `503` when it cannot be asked and nothing is kept |
| `GET /api/catalogue/discover?kind=&genre=&exclude=&includeFamily=&from=&to=&with=&about=&language=&like=&goodOnly=&minRating=&minVotes=&sort=&limit=&category=` | `CatalogueDiscoverView` — `{kind, resolved, titles}`: up to 20 films (the default) or shows found by what they are like, each a `TitleHitView`. Every filter is optional and they combine. `genre`, `with` and `about` repeat or take commas. `genre` names genres, in any case, or a word of one (`action` for a show's `Action & Adventure`), a title in all of them. A search with `genre` leaves out the titles filed under Animation, Family (and Kids, for shows) unless `genre` names one of them or `includeFamily=true`; `exclude` names further genres to leave out, and one that `genre` asks for is a `400`. `from` and `to` are release years. `with` names people, resolved to the database's best match for each and in all the results (films only). `about` is keywords by words (`black and white`, `time loop`), resolved to the database's keyword of that name else its best match. `language` is an original language by code or English name. `goodOnly` (default true) asks for a rating of at least 6.5 on at least 200 votes; `minRating` and `minVotes` name the floors instead; a sort by rating orders the five pages of the most voted matches by a rating weighted by votes (each average counts in proportion to its votes, the rest toward the mean of those matches, with the votes of their lower quartile as the prior), so a title a few hundred people rated 8.4 does not outrank one thousands rated 7.6. `sort` is `popular` (the default), `rating`, `newest` (only titles already out) or `oldest`. `category=anime` keeps the anime alone: it adds Animation to `genre` and sets `language` to Japanese, which `resolved` reports, and a `language` naming another is a `400`. `like` is a title to find others similar to: an IMDb id, or a name with the year in brackets when needed; it starts from the titles the database recommends for it and counts as similar and narrows them by the other filters, in the database's order unless `sort` is named, and fixes `kind` to its own. `resolved` says what each word matched: `{genres, excluded, people:[{personId, name, knownFor?}], keywords, language?, like?, minRating?, minVotes?, sort}`. `404` when a genre, person, keyword, language or `like` title is not in the database (a genre's refusal lists the genres there are), `400` for a kind, sort or category that is not one, `from` after `to`, `with` on shows, or `like` of the other kind, `503` when the database cannot be asked and nothing is kept. Kept a day |
| `GET /api/catalogue/released?kind=&days=&category=` · `GET /api/catalogue/upcoming?kind=&days=&category=` | `CatalogueListView` — `{kind, from, to, titles}`: the most looked at films (`kind=film`, the default) or shows released in the last `days` days (default 45, at most 120), newest first; or with a date in the next `days` days (default 180, at most 365), soonest first; with `category=anime`, of the anime alone. At most 20 `TitleHitView`s. Kept six hours. `400` for a kind that is neither or a category that is not `anime` |
| `GET /api/catalogue/added?kind=&category=&limit=` | `LibraryAdditionsView` — `{titles}`: the library's newest films and shows, newest first, at most `limit` (default 20, at most 50), each `{imdbId?, kind, title, year?, genres, rating?, plot?, posterUrl?, libraryId, addedAt, watched, category?}`; `category` (`anime` or `general`) keeps the titles on that shelf, a title nothing describes yet counting as general; `libraryId` is the film's title or the show's first episode and `addedAt` the film's arrival or the show's latest episode's. Read from what is kept: nothing waits on the database |
| `GET,HEAD /catalogue/people/{personId}.jpg` | a person's portrait, kept with their credits; open like a film's artwork |
| `GET /api/shows` | `ShowSummary[]` — the shows the library holds, one entry each: `{imdbId?, title, firstYear?, lastYear?, status?, details?, seasons, episodes, addedAt?, sizeBytes, lastPlayed?, resume?, firstTitleId, category?}` |
| `GET /api/shows/{imdbId}` | `ShowView` — a show as the film database describes it, in the library or not, every season with its episodes joined with the library and the asking person's progress. `404` when neither the database nor the library knows it, `503` when the database cannot be asked and nothing is kept |
| `GET,HEAD /shows/{imdbId}/{poster.jpg,backdrop.jpg,cast-N.jpg,sNNeMM.jpg}` | a show's artwork and its episodes' stills, open like a film's |
| `GET /api/me/preferences` | `ViewerPreferences` — how the token's person watches; every field absent for somebody who set nothing. |
| `PUT /api/me/preferences` | `ViewerPreferences` → the stored `ViewerPreferences`. A blank language or a height that is not positive is stored as absent |
| `GET /api/me/progress` | `ProgressEntry[]` — what the token's person can carry on with, newest first: `{titleId?, name, positionSeconds, durationSeconds, remainingSeconds, lastWatchedAt, episode?, showImdbId?, episodeName?, stillUrl?}`. Each film they started and did not finish; and each show once, by the episode they watched last, wherever in the show they started: that episode while unfinished, and once finished the episode after it (the next of its season, else the first of the next season, as the film database lists the show, or the library holds it for a show the database has not described), from where they left it or from 0, carrying the finished episode's `lastWatchedAt`. An episode after that the library does not hold, or holds a failed copy of, is listed with no `titleId` once it has aired (from the day after its air date), to a person holding `cinema:download`: `durationSeconds` is the description's runtime (0 when it names none), and watching it is Watch now with its `episode`. A show finished to its last episode is not listed. For an episode `name` is the show's, `episode` is `{season, number}`, and `episodeName` and `stillUrl` are the description's name for it and its still where it has them. A title the library no longer holds is left out |
| `POST /api/me/progress` | `204` for `[{titleId, positionSeconds, watchedAt}]`, at most 500 — where a device that watched with no connection got to. Each is recorded as a room's play is, the person's entry and the film's last play, where `watchedAt` is newer than what is held; an older one, and one for a title the library no longer holds, changes nothing. A `watchedAt` ahead of the server's clock is taken as now. `400` for a position that is negative or not a number, or more than 500 entries |
| `GET /api/me/history` | `HistoryEntry[]` — every film and episode the token's person watched, newest first: `{titleId, name, positionSeconds, durationSeconds, finished, lastWatchedAt, episode?, showImdbId?}`, named as above. `finished` is a position past 90 % of the title |
| `DELETE /api/me/history/{titleId}` | `204` — forgets the person's entry for the film, and so their progress in it. The film's own last play stays |
| `GET /api/me/follows` | `FollowView[]` — the shows the token's person follows, newest follow first: `{imdbId, title, posterUrl?, since, autoDownload, nextEpisode?}`. `nextEpisode` is `{season, episode, name?, airDate}`, the episode the film database expects next |
| `PUT /api/me/follows/{imdbId}` | `FollowView` for `{autoDownload}` — follows the show, or changes `autoDownload` of a follow that exists, which keeps its `since`. `400` for an id that is not an IMDb id, `404` when the film database has no show under it, `409` when the show has ended or been cancelled, `503` when the database cannot be asked and nothing is kept |
| `DELETE /api/me/follows/{imdbId}` | `204` — stops following; also when the person did not |

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

A preference out of range is that preference left to its default: a size is clamped, an unknown font,
edge or colour spelling is dropped, and a style with nothing left in it is stored as none.

CORS reflects any origin and allows credentials. Media answers HEAD as well as GET.

Any other GET is a page of the site and is answered with its index. `/r/{room}` is answered with the
index carrying Open Graph and Twitter tags for the room: `og:title` "Watch <film> on Krystal Cinema",
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
the people a room can invite, what the film database says of a title, a person or a genre),
`cinema:offline` (saving a film onto a device, below), `cinema:download` and `cinema:wish` (below),
`cinema:guests` (handing out a room's guest link, below), and the self
actions `cinema:profile.read` and `cinema:profile.write`, which every approved account holds, for a
person's own records under `/api/me`, `/api/lists` and push. `/.well-known/tks-actions.json` serves the
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
guest calls `JoinAsGuest`, `ReportQuality` and `ServerTime` and nothing else: every other method throws
a `HubException` (Who may come in, below). A guest's film bytes are opened by the ticket, as anybody's.

### Opening a film's bytes

A film's bytes are opened by the access token of somebody who may watch, or by a **media ticket** on
the query string as `?mt=…`, and the ticket is what a player should use for the film itself. A title's
`poster.jpg` is open: Discord's servers fetch it to draw an embed. It is the title's poster wherever it
is shown: the film database's poster kept for the film, or for the show an episode belongs to, the same
one a search result and the title's page show; the poster beside the title only when the database holds
none. Each title's database poster is pinned to the first the API showed it with, so a title keeps one
poster from the search that found it to the shelf that holds it.

The difference is caching. A shared cache will not answer a request that carried an
`Authorization` header. A ticket says only which film and until when, and **every viewer of a title
is handed the same string**, so their requests are one URL that one cached copy answers.

- Fetch it from `GET /api/titles/{id}/ticket` before loading the film, and put it on every
  `/media/{id}/…` request for that title.
- It is scoped to the one title and expires; `expiresAt` says when. It outlasts a feature.
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
| `POST /api/me/offline/{id}/complete` | `OfflineSave` — the device holds every byte, which ends the save's hold on the title. `404` for a save that is not this person's. `cinema:profile.write` |
| `DELETE /api/me/offline/{id}` | `204` — forgets the save, and its hold with it; also when there was none. `cinema:profile.write` |
| `POST /api/me/offline/wants` | `OfflineWant[]` for `{imdbId, episodes?, height?, audioLanguage?, subtitleLanguage?, device}` — a film, or with `episodes` (`[{season, episode}]`, at most 100) those of a show, wanted on the device, one want each, as each stands now. What the library neither holds nor is fetching is fetched in the background: a film's best release at 1080p from a digital source up, an episode out of its season's pack at 720p where there is one, as Watch now picks, nobody told it can be watched. With an `audioLanguage` that is not the title's own, a release whose name carries it (a token naming the language, else `MULTI` or `DUAL`) comes first, at that quality and then at any. Asking again for one the device wants keeps it with the `height` and languages asked now; one that failed is asked for afresh. `height` is the picture height the device saves it at, 1 to 4320, absent for the device's own; `audioLanguage` absent for the title's own and `subtitleLanguage` absent for none, each a language tag. `400` for an id that is not an IMDb id, a bad device, height, language or episode; `403` for somebody not holding `cinema:download` as well, since fetching spends the disk and the tracker. `cinema:offline` |
| `GET /api/me/offline/wants` | `OfflineWant[]` — every want the token's person has, on every device, the oldest first, each as it stands now. A want is waited on for `Offline:WantDays`. `cinema:profile.read` |
| `DELETE /api/me/offline/wants/{id}` | `204` — lets the want go, also when there was none; its download stays the library's. `cinema:profile.write` |
| `GET,HEAD /media/{id}/{rendition}/track.mp4` | one track whole: the rendition's init segment and every segment after it, joined, as `video/mp4`; of a title still being made, every segment its playlist lists so far. Ranged (`206`, `416`), with an exact `Content-Length` and a strong `ETag`; a growing track's `Content-Range` gives its length as `*`. `If-Range` resumes with that `ETag`, or with one this track answered earlier while it was shorter, and with any other answers the whole track. Served `no-store`. The bearer token of somebody holding `cinema:offline` who has a save of the title, never a ticket: `409` for a person with no save of it or a title that failed, `404` for a rendition the title does not have, `429` with `Retry-After` past the transfers allowed at once |

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
  "details": FilmFacts,   // what /api/titles carries as `details`: the film's, or an episode's show's; absent when not described
  "episode": { "season": 2, "number": 4, "name": "…", "overview": "…" },   // an episode's own; absent for a film
  "artwork": {            // each absent when there is none; open addresses but the preview sheet, which is the film's
    "poster": "/films/tt2866360/poster.jpg",
    "backdrop": "/films/tt2866360/backdrop.jpg",
    "still": "/shows/tt…/s02e04.jpg",             // an episode's
    "thumbnails": "/media/coherence-2013/thumbs.jpg"   // the scrub preview sheet the manifest's thumbnails describe
  },
  "growing": false,       // true while the title is still being made: the tracks are what is made so far
  "category": "anime"     // the shelf it is on (Categories); absent while nothing says
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
`cinema:download`; a wish takes `cinema:wish`; finding a film in the title index and asking whether
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
| `GET /api/shows/find?q=` | `FoundTitle[]` — the shows the title index offers for a name; never a film or an episode |
| `GET /api/shows/{imdbId}/releases` | `ShowOffers` — `{seasons: [{season, packs, episodes: [{episode, releases}]}], emptyExplanation?}`, each list a show's `ReleaseOffer`s best first, 720p ahead of 1080p. A pack is listed under every season it covers |
| `GET /api/films/find?q=` | `FoundTitle[]` — the films the title index offers for a name; never a show |
| `GET /api/titles/find?q=` | `FoundTitle[]` — films and shows together, in the index's own order; never an episode. Takes `cinema:download`. A `FoundTitle` is `{imdbId, title, year?, display, kind, starring?, posterUrl?, rating?, votes, overview?, category?}`, `kind` `film` or `show`, `posterUrl` the poster the title's page shows, served from here, or the index's own where the film database has none, `rating` and `overview` the film database's, absent where it has none or has not answered within the search |
| `GET /api/films/{imdbId}/availability` | `FilmAvailability` — `{imdbId, libraryId?, download?: {hash, progress, isWatchable, libraryId?}, hd1080?, hd720?, notOut, notOffered, emptyExplanation?, trackerUnanswered, waiting, waiters, streams: {used, limit}}`. The library first, then the torrent client, then the tracker, which is asked only for a film neither here nor arriving; `hd1080` and `hd720` are the best `ReleaseOffer` at each from a digital source up. With neither, `notOut` says the tracker holds nothing from a digital source up, a film to wait on, and `notOffered` that it does but every such release is turned down here, which `emptyExplanation` names |
| `POST /api/watch` | `WatchNowReply` for `{imdbId, torrentId?, quality?, episodes?, room?}` — `{outcome, room?, libraryId?, streams?}`. See Watch now below |
| `GET /api/downloads` | `DownloadView[]`: `{hash, name, release, summary, progress, isFinished, isWatchable, libraryId?, imdbId?, requestedBy?}`, `requestedBy` the account of whoever asked |
| `POST /api/downloads` | `StartDownloadReply` for `{torrentId, notifyChannelId?, roomId?, episodes?}`; `episodes` (`[{season, episode}]`) fetches only those files out of a show's release, and picking more from a release already downloading switches them on in it |
| `GET /api/wishes` | `WishView[]` — the wishes the person waits on; one the tracker has answered carries `release`, the `ReleaseOffer` found for it. A wish's `posterUrl` is the poster the film's page shows, served from here at a relative address, or the title index's own, absolute, where the film database has none |
| `POST /api/wishes` | `AddWishReply` for `{chosen}` |
| `DELETE /api/wishes/{imdbId}` | `CancelWishReply` — the person comes off the wish |
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
  subscriber with an `available` notification, and somebody who joins the wish after that is told on
  the next pass. The wish then lists with the release found for it, which is what the site's Notify
  page shows, until the person takes themselves off. Which wishes are out is held in memory and found
  again by the first sweep after a restart.

### Watch now

A film that is not here is fetched and played in a room the moment it can be opened. `POST
/api/watch` answers at once: `inLibrary` (the film is here, and is loaded into `room`), `preparing`
(the room waits on it), `full` (a `409`: `streams` films are already being watched while they arrive,
the `Watch:MaxStreams` setting, and the person is offered Download instead) or `badRoom`. Without a
`room` a new one is made, private to the person asking; a room id is 1 to 64 letters, digits, `-` or
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
- **`ShowView` carries the asking person's side.** `followable` is false once the film database calls
  the show `Ended` or `Canceled`; `follow` is `{autoDownload, since}` for a show the person follows and
  absent otherwise; `nextEpisode` is `{season, episode, name?, airDate}` and absent when the database
  names none.

### Categories

A title's kind is how it is built, a `film` or a `show` of episodes; its category is where people look
for it: `anime` or `general`. The two are independent, so an anime film is a film on the Anime shelf
and a show's every episode is on its show's.

A title is anime when the film database files it under Animation and its original language is
Japanese (`ja`), and general otherwise. A title whose genres nothing has described has no category,
and is shelved as general. Somebody holding `cinema:download` may shelve a title against the rule,
kept by IMDb id; a setting that agrees with the rule is not kept, so the title follows its description
again.

Every view that names a title carries `category`, `anime` or `general`, absent while nothing says:
`TitleSummary`, `ShowSummary`, `FilmView`, `ShowView`, `TitleHitView`, `CreditView`,
`CatalogueTitleView`, a library addition, a trending entry, a `FoundTitle`, a list entry and an
`OfflinePackage`. A catalogue answer reads it from the title's kept description where there is one,
else from the genres and language the answer carries.

| Route | Returns |
|---|---|
| `PUT /api/categories/{imdbId}` | `{imdbId, category, set}` for `{kind, category}` — shelves the `film` or `show` as `anime` or `general`; `set` is whether a setting is kept against the rule. `400` for an id that is not an IMDb id, a kind that is neither or a category that is not one. `cinema:download` |

### Lists

A list holds films and shows somebody means to watch later. Every person has a Watch later, which is
theirs alone, and makes as many named lists as they like, each of which its owner may share with
people the server has a name for. A list is shown to its owner and its members and to nobody else:
for anybody else a list's id is a `404`, never a `403`. The shapes are in `src/MovieBot.Api/Lists/`.

| Route | Returns |
|---|---|
| `GET /api/me/lists?holding=` | `ListSummary[]` — the lists the token's person can see, their Watch later first and then the rest most recently changed first: `{id, name, watchLater, role, owner, members, count, updatedAt, posters, holds?}`. `role` is `owner`, `editor` or `viewer`; `owner` is `{userId, name?}`; `members` are `{userId, name?, role, since}`; `posters` are the newest entries' posters, at most four. With `holding` (an IMDb id) each list says in `holds` whether it holds that title |
| `POST /api/lists` | `ListView` for `{name}` — a new list the token's person owns. `400` for a name blank or over 80 characters once trimmed, `409` past 100 lists |
| `GET /api/lists/{id}` | `ListView` — `{list: ListSummary, entries}`, the entries newest saved first: `{imdbId, kind, title, year?, posterUrl?, rating?, runtimeMinutes?, genres, addedAt, addedBy, note?, libraryId?, inLibrary, watched, category?}`. `addedBy` is `{userId, name?}`; `libraryId` is the title that plays a film the library holds; `inLibrary` is true for a film the library holds or a show with any episode here; `watched` is the asking person having watched the film through, and never set for a show |
| `PATCH /api/lists/{id}` | `ListSummary` for `{name}` — the owner renames it |
| `DELETE /api/lists/{id}` | `204` — the owner deletes it, for everyone on it |
| `PUT /api/lists/{id}/entries/{imdbId}` | `ListEntryView` for `{kind, note?}` — saves a `film` or a `show` to the list, or changes the note of one already on it. An owner or an editor. `400` for an id that is not an IMDb id, a kind that is neither, or a note over 500 characters; `404` when the film database has no title of that kind under the id; `503` when it cannot be asked; `409` past 2000 entries |
| `DELETE /api/lists/{id}/entries/{imdbId}` | `204` — takes it off; also when it was not on it |
| `PUT /api/lists/{id}/members/{userId}` | `ListSummary` for `{role}` — the owner puts somebody on the list as an `editor` or a `viewer`, or changes which. `404` for somebody the server has no name for, `409` past 50 members |
| `DELETE /api/lists/{id}/members/{userId}` | `204` — the owner takes somebody off, or a member takes themselves off, which is leaving |

- **`later` is the asking person's Watch later.** It is listed before anybody saves anything and
  written on the first save. It has no members and keeps its name, so renaming, deleting or sharing it
  is a `409`.
- **An entry keeps the moment it was first saved.** Saving a title that is already on the list changes
  its note and nothing else: an absent `note` leaves it, an empty one clears it, and `addedAt` and
  `addedBy` stay.
- **What a list may do is its role.** An `owner` renames, deletes and says who is on it; an `editor`
  saves and takes off; a `viewer` reads. A role that does not allow a change is a `403`.
- **Somebody put on a list is told once**, by a `shared` notification opening `/saved/{id}`.
- **An entry is described from what is kept.** Saving asks the film database about a title new to the
  list; the API then keeps every listed title described, so reading a list never waits on the network.

### Notifications

A notification is one sentence for one person, kept for their bell and, where they allow its kind,
pushed to every browser they subscribed. Everything under `/api/me` answers for whoever the token
names. The shapes are in `src/MovieBot.Core/Notifications.cs`.

| Route | Returns |
|---|---|
| `GET /api/me/notifications` | `NotificationsReply` — `{unread, items: [{id, kind, text, url?, action?, createdAt, read}]}`, newest first; a person keeps their newest 50, none older than 30 days |
| `POST /api/me/notifications/read` | 204 for `{ids?}` — marks those read, or all of them when none are named |
| `GET /api/me/notifications/preferences` | `NotificationPreferences` — `{available, ready, episode, shared}`, which kinds are pushed; each is on until switched off |
| `PUT /api/me/notifications/preferences` | `NotificationPreferences`, as stored |
| `GET /api/push/key` | `{publicKey}` — this server's VAPID key, base64url, which a browser subscribes with |
| `GET /api/me/push?endpoint=` | `{subscribed, devices}` — whether that browser is registered to this person, and how many are |
| `POST /api/me/push` | 204 for a browser's `PushSubscription.toJSON()`, `{endpoint, keys: {p256dh, auth}}`; a browser registered again replaces its own row, and belongs to whoever registered it last. An endpoint that is not `https` is a `400` |
| `DELETE /api/me/push?endpoint=` | 204, or 404 for a browser that is not this person's |
| `GET /api/people` | `PersonView[]` — everybody the server has a name for but the person asking, `{userId, name, lastSeenAt?}`, most recently seen first |
| `POST /api/rooms/{room}/invite` | `InviteReply` for `{userId}` — lets them into the room and tells them: `{delivery}`, `push` when a push service took it for at least one of their devices and `bell` when it waits in their bell alone. 404 for somebody unknown, 403 for a room private to the person inviting |
| `POST /api/rooms/{room}/guests` | `GuestLink` = `{path, expiresAt}` — the room's guest link, `path` being the site's `/g/<code>`: the one the room has while it is good, or a new one lasting `Rooms.GuestPassLifetime`, so everybody in the room who asks is handed the same. `cinema:guests`, and 403 for a room private to the person asking |
| `DELETE /api/rooms/{room}/guests` | 204 — takes the link away; every guest who came in on it is sent `GuestAccessEnded("ended")` and disconnected. `cinema:guests` |
| `GET /api/guest/{code}` | `GuestInvitation` = `{sessionId, name?, expiresAt}` — the room a guest link opens and the film it holds, for the page the link lands on; a code this server did not sign, or a link no longer good, is a 404. Open |

- **Kinds.** `available`: a film somebody waits on can be downloaded. `ready`: a download they started
  can be watched, or could not be made watchable. `episode`: a new episode of a show they follow is
  out and the tracker offers it, "Show S5 · E4 is out.", opening `/tv/{imdbId}/season/{n}`. `shared`:
  somebody put them on one of their lists, "Ana shared
  their list Friday night with you.", opening `/saved/{id}`. `invite`:
  somebody asked them into a room; an invite is pushed whatever the person chose.
- **One thing is said once.** Each notification carries the thing it is about, and a person holding a
  notification about it is not told again: a download once, a wish once, an episode once, a list once
  per person put on it.
- **A switch holds back the push, not the record.** A kind switched off still lands in the bell.
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
    { "kind": "intro", "startSeconds": 489.0, "endSeconds": 523.458, "from": "chapter" }
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
and for an episode nothing was found in. `kind` is `"intro"` (the title sequence) or `"recap"` (the
"previously on" at the head of the episode); `startSeconds` and `endSeconds` are positions in the
episode, and skipping seeks to `endSeconds`. `from` is `"chapter"` when the container names the
stretch, and `"audio"` when it was found by comparing the episode's sound with the show's other
episodes, which places the start within about half a second and the end up to a second early, never
late. A chapter's own edges are the container's and can differ from the audio's by several seconds,
since a chapter named `Intro` may end before the titles do. An episode has at most one marker of
each kind. Markers are written once the episode is `ready` and can appear
on an episode already in the library, so a player reads them from every `TitleChanged` as well as
from the first load.

A track with `available: false` is listed on purpose — a language that is simply missing from the
menu reads as a bug. Render it disabled with its `reason`, do not hide it.

`TitleSummary` is `{id, title, durationSeconds, status, kind, episode?, unit, headSeconds?, poster?,
posterVersion?, film?, sizeBytes?, addedAt?, details?, category?, progress?, lastPlayed?}`. `kind` is `"film"` or
`"show"`, as the manifest says; `category` is its shelf (Categories), an episode's its show's. `poster` is present whenever the title has a poster to serve, and
`posterVersion` is when the file it is served from was last written, in milliseconds since the epoch; a
page asks for the poster as `/media/{id}/{poster}?v={posterVersion}`. Every episode of a show is a title of its own: `episode` is `{season, number}`, `film`
is the show, `title` is the show's name with the episode's place (`Severance S02E04`), and `unit` is
its season's (a film's is its own id). `details` is what the film database said, as far as a
listing shows it — `{rating?, votes, certification?, genres, runtimeMinutes?, overview?, directors,
posterUrl?, backdropUrl?}` — and is absent for a film not yet described; the listing never waits on
the network for it. `progress` is `{positionSeconds, finished, lastWatchedAt}` for the person the
token names, absent when they have not watched the film and always absent for the service key.
`lastPlayed` is `{at, byId, byName?}`, the last time anybody played the film and who (their Discord
id), absent when nobody has; `byName` is absent when that person's name was never heard.

```jsonc
// FilmView: GET /api/films/{imdbId}
{
  "imdbId": "tt0113277", "tmdbId": 949, "title": "Heat", "year": 1995, "releaseDate": "1995-12-15",
  "runtimeMinutes": 170, "certification": "R", "genres": ["Crime", "Drama", "Action"],
  "overview": "…", "tagline": "…", "rating": 7.9, "votes": 8740,   // TMDB's own rating, absent with no votes
  "keywords": ["heist", "los angeles"], "blackAndWhite": false,     // the database has no tag for colour: false is "not tagged black and white"
  "libraryId": "heat-1995", "watched": false,                       // libraryId absent when the library does not hold it; watched is the asking person's
  "directors": ["Michael Mann"], "writers": ["Michael Mann"],
  "personIds": { "Michael Mann": 638 },   // each director and writer by name: the id /api/catalogue/people takes
  "cast": [{ "personId": 1158, "name": "Al Pacino", "character": "Lt. Vincent Hanna", "portraitUrl": "/films/tt0113277/cast-0.jpg" }],
  "originalLanguage": "en", "languages": ["English", "Spanish"], "countries": ["United States of America"],
  "posterUrl": "/films/tt0113277/poster.jpg", "backdropUrl": "/films/tt0113277/backdrop.jpg",
  "category": "general"                   // the shelf it is on (Categories); absent while nothing says
}
```

```jsonc
// ShowView: GET /api/shows/{imdbId}
{
  "imdbId": "tt11280740", "tmdbId": 95396, "title": "Severance", "firstYear": 2022, "lastYear": 2025,
  "status": "Returning Series", "inProduction": true, "certification": "TV-MA",
  "genres": ["Drama", "Mystery"], "overview": "…", "rating": 8.4, "votes": 2900,
  "keywords": ["office", "conspiracy"], "blackAndWhite": false,
  "creators": ["Dan Erickson"], "personIds": { "Dan Erickson": 1220098 },
  "cast": [{ "personId": 34544, "name": "Adam Scott", "character": "Mark Scout", "portraitUrl": "/shows/tt11280740/cast-0.jpg" }],
  "languages": ["English"], "countries": ["United States of America"], "networks": ["Apple TV+"],
  "posterUrl": "/shows/tt11280740/poster.jpg", "backdropUrl": "/shows/tt11280740/backdrop.jpg",
  "seasons": [{
    "number": 2, "name": "Season 2", "airDate": "2025-01-16",
    "episodes": [{
      "season": 2, "number": 4, "name": "Woe's Hollow", "airDate": "2025-02-07", "runtimeMinutes": 51,
      "rating": 8.6, "votes": 120, "stillUrl": "/shows/tt11280740/s02e04.jpg",
      "titleId": "severance-s02e04", "status": "ready", "durationSeconds": 3080.2,  // absent when the library does not hold it
      "progress": { "positionSeconds": 1260, "finished": false, "lastWatchedAt": "…" }
    }]
  }],
  "resume": { "titleId": "severance-s02e04", "season": 2, "episode": 4, "positionSeconds": 1260, "durationSeconds": 3080.2 },
  "followable": true,
  "follow": { "autoDownload": false, "since": "…" },  // absent when the asking person does not follow it
  "nextEpisode": { "season": 3, "episode": 1, "name": "…", "airDate": "2027-01-15" },  // absent when the database names none
  "category": "general"
}
```

`resume` is the latest episode the person watched if they did not finish it, else the one after it
when the library holds it; absent otherwise. Seasons are the database's numbered ones and any the
library holds that it does not list; specials are left out. Episodes are numbered as the show's
releases number them, which is how the library, the tracker and every route that takes a season and
an episode number them too: where the film database numbers a season differently, its names, stills,
dates and airing episodes are put into the releases' numbering, and an episode it lists that no
release counts is a special.

A cast member's `personId` and the ids in `personIds` are the film database's own for those people,
what `/api/catalogue/people/{personId}` takes. A kept description whose cast carries no id is asked of
the database again the next time it is read, and until then `personId` is absent and `personIds` empty.

`CatalogueTitleView` is `{imdbId, kind, title, year?, rating?, votes, overview?, posterUrl?, role?, category?}`:
`kind` is `film` or `show`, `posterUrl` the poster the title's page shows, served from here, and
`role` is set among a person's credits only. Whether the library holds the title is the library's to
say (`/api/titles`, `/api/shows`), joined by `imdbId`.

`TitleHitView` is what a search, a discovery and the lists of the week list:
`{imdbId?, tmdbId, kind, title, year?, releaseDate?, genres, rating?, votes, plot?, madeBy, starring, originalLanguage?, posterUrl?, inLibrary, libraryId?, watched, category?}`.
`madeBy` is a film's directors or a show's creators and `starring` the two top-billed actors; `plot` is the
first sentence of what it is about, cut at 200 characters, and the title's page holds the rest.
`imdbId` is absent for a title the database holds no IMDb id for, as an announced one often is: it has
no page and no poster here, and `tmdbId`, the database's own id, is all that names it. `inLibrary`,
`libraryId` (a film's title that plays it) and `watched` (the asking person watched the film through; never
true for a show or for the service key) are joined from the library as listed and the watch record, and are
never kept with the answer.

`CreditView` is `{tmdbId, imdbId?, kind, title, year?, releaseDate?, status, role?, rating?, votes, posterUrl?, inLibrary, libraryId?, watched, category?}`.
`status` is `released` (its date has come), `upcoming` (dated in the future) or `announced` (no date, or one the
database files as only planned or rumoured). `role` is the parts played and jobs done on the title, joined by
` · `. A credit with no IMDb id has no page here, as for a `TitleHitView`.

Everything here is TMDB's, and a surface showing it credits TMDB, as its terms ask.

`RoomSummary` is `{sessionId, titleId?, name?, durationSeconds?, paused, positionSeconds,
participants, open}`. `name` is what the film is called, `positionSeconds` is where the room was when
the listing was taken, `participants` is a count: the people in a room are listed by the room
itself, never here, and `open` says anybody who may watch can come in.

## Hub

SignalR at `/hub/session`. Camel-cased payloads, string enums.

**Call:**

| Method | Signature | Notes |
|---|---|---|
| `Join` | `(sessionId) → JoinReply` | call first; everything else but `Knock` throws until you are in. The viewer is the connection's token: its `sub` and `name`. `{admitted: true, push, door}` puts them in the room, and a room there is none of is made, private, with them let in; `{admitted: false}` is a room private to them, and says nothing else about it (Who may come in, below) |
| `JoinAsGuest` | `(name: string?) → JoinReply` | a guest link's `Join`: the link names the room. `{admitted: true, push, you}` puts the guest in, `you` the `Participant` the room has them as (`guest: true`, an id of the connection's, `name` cleaned of control and formatting characters and held to 32, "Guest" when empty); calling it again on the same connection keeps them who they were. `{admitted: false, refused}` says why not: `ended` (the link is no longer the room's), `unattended` (nobody with an account is in the room, nor has been within `Rooms.GuestGrace`) or `full` (`Rooms.GuestsPerRoom`). A connection that is no guest is refused |
| `Knock` | `(sessionId) → bool` | asks to be let into a room private to this viewer. `true` is no need to ask, so `Join` now; otherwise the room is told who is asking and this connection is answered with `KnockAnswered`. The asking ends with the connection |
| `SetOpen` | `(open: bool)` | opens the room to anybody holding `cinema:watch`, or closes it to anybody not already let in |
| `Answer` | `(userId, admit: bool)` | lets somebody asking in, or tells them they are not |
| `Next` | `()` | the episode after what the room holds, put on, or fetched into the room at the quality of the one playing for somebody holding `cinema:download`; a `HubException` when there is none to put on |
| `Previous` | `()` | the episode before, the same way |
| `PlayEpisode` | `(season, number)` | an episode of the show the room holds, the same way |
| `LoadTitle` | `(titleId, startSeconds: double?) → SessionStatePush` | from `startSeconds`, held to the transcode head, or from 0 when null; a playing room keeps playing, a paused one stays paused. Both arguments are always sent, null for no start |
| `Play` | `(atSeconds) → SessionStatePush` | |
| `Pause` | `(atSeconds) → SessionStatePush` | |
| `Seek` | `(toSeconds) → SessionStatePush` | may be clamped |
| `ServerTime` | `() → DateTimeOffset` | for clock-offset estimation |
| `Buffering` | `(waiting: bool, atSeconds: double)` | this viewer's player has been waiting for the film past a stutter, stopped at `atSeconds`, or is playing again. The roster carries it; when every player in the room is waiting the room is held (`waiting` in the state) where the furthest behind of them stopped, and let go when one plays, somebody arrives, or a waiting one leaves |
| `ReportQuality` | `(playingHeight: int?, wantedHeight: int?)` | the rung height this viewer plays, and the one its own adaptive choice would pick with nothing holding it back; on change and about every 30 s. Answers nothing and is sent to nobody: the server keeps it per connection for its transcode planner and drops it when the connection leaves. A player that cannot say (native HLS) sends nothing, and is taken to want the tallest rung |

**Receive:**

| Event | Payload |
|---|---|
| `StateChanged` | `SessionStatePush` — sent to everyone including the caller |
| `SeekClamped` | `{requestedSeconds, grantedSeconds, headSeconds}` — sent to the caller only; `headSeconds` is the best rung's head held back to the audio, which is at least `transcodeHead` |
| `ParticipantsChanged` | `Participant[]` = `{userId, displayName, buffering, guest}[]` — sent when somebody joins, leaves, or starts or stops waiting for the film; `guest` marks somebody on the room's guest link |
| `TitleChanged` | `Manifest` — the manifest of the title this room holds changed: head, preview sheet, subtitles, status. Sent to every room holding the title, within a couple of seconds of the write |
| `DoorChanged` | `RoomDoor` = `{open, knocking: [{userId, displayName}], guests?: {expiresAt}}` — sent to the people with an account in the room, never to a guest, when it is opened or closed, when somebody starts or stops asking, and when its guest link is given, taken away or runs out; `knocking` holds each person once, in the order they asked, and `guests` is there while the room has a link, never the link itself |
| `KnockAnswered` | `bool` — sent to a connection that asked to be let in: `true` when somebody let them in or opened the room, `false` when somebody said no |
| `GuestAccessEnded` | `string` — sent to a guest as they are sent away, and their connection is closed after it: `ended` (the link was taken away or ran out) or `unattended` (nobody with an account has been in the room for `Rooms.GuestGrace`) |

### Who may come in

A room is private from the moment it is made. Whoever makes it is let in by making it — by `Join`,
by any room route below, or by Watch now — and nobody else is, until somebody in it lets them in, invites
them (`POST /api/rooms/{room}/invite`), or opens it. Entering an open room lets that person in for good,
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
`ReportQuality` and `ServerTime` refuses them twice over, once by the hub's filter and once by the
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

## Shared versus local

Shared, and nothing else: which title, paused, position, rate, who changed it.

**Volume, subtitle selection, audio track and quality are per-viewer** and live in
`localStorage`. They never go on the wire. Shared volume is a way for one person to deafen
everyone else, and one person needing subtitles should not put them on five other screens.
