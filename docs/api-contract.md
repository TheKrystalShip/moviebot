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
| `GET /api/config` | `{discordClientId?, publicBaseUrl?, issuer, clientId, actionsClaim}` — where the page signs in: the identity provider's issuer, the public client the site signs in as, and the claim a person's actions arrive in. Open |
| `GET /.well-known/tks-actions.json` | the action manifest (`kgsm-docs/reference/action-manifest.md`): every action Cinema checks, in the namespace `cinema`, at `cluster`, the self actions marked `self`. Open: the provider reads it |
| `GET /api/titles` | `TitleSummary[]`. The library as listed at most ten seconds ago, as are the episodes `/api/shows` and `/api/shows/{imdbId}` group; a person's progress and last plays on it are current |
| `GET /api/titles/{id}` | `Manifest`, or 404 |
| `GET /api/titles/{id}/ticket` | `{ticket, expiresAt}` — what a request for this film's bytes carries, or 404 |
| `GET /api/sessions` | `RoomSummary[]` — every room, what it is watching, how many are in it |
| `GET /api/sessions/{id}` | `SessionStatePush` — creates the session if new |
| `GET /api/sessions/{id}/participants` | `Participant[]` |
| `POST /api/sessions/{id}/title` | `SessionStatePush` — puts a film in the room, or 404 if the library has no such title. The body is `{titleId, startSeconds?}`; `startSeconds` puts the room there instead of at the beginning, held to the transcode head like a seek. Recorded as the token's person, or as MovieBot on the service key |
| `POST /api/sessions/{id}/play` | `RoomChanged` |
| `POST /api/sessions/{id}/pause` | `RoomChanged` |
| `POST /api/sessions/{id}/seek` | `RoomChanged` |
| `POST /api/sessions/{id}/seek-relative` | `RoomChanged` |
| `GET,HEAD /media/{id}/**` | playlists, segments, subtitles, poster. Opened by `?mt=` or the bearer token |
| `GET /api/films/{imdbId}` | `FilmView` — a film as the film database describes it, in the library or not, with artwork at this server's addresses. `404` when the database holds no film under the id, `503` when it cannot be asked and nothing is kept |
| `GET,HEAD /films/{imdbId}/{poster.jpg,backdrop.jpg,cast-N.jpg}` | the artwork kept beside a description. Open: it is public artwork and an `<img>` carries no token |
| `GET /api/popular` | `{films, shows, fetchedAt?}` — the films and shows the film database counts as trending this week, most first, each `{imdbId, title, year?, rating?, votes, posterUrl?}`, read from what is kept. Both lists are empty and `fetchedAt` absent until the database has first been asked |
| `GET,HEAD /popular/{imdbId}.jpg` | a trending entry's poster, open like a film's artwork |
| `GET,HEAD /glances/{imdbId}/poster.jpg` | a search result's or a catalogue title's poster from the film database, the one its page shows, fetched and kept the first time it is wanted; open like a film's artwork |
| `GET /api/catalogue/people/{personId}` | `CataloguePersonView` — `{personId, name, knownFor?, portraitUrl?, titles}`: a person by the film database's id for them (a cast member's `personId`, a director's, writer's or creator's in `personIds`), and up to 40 films and shows they are credited on, the most voted on first. `titles` are `CatalogueTitleView`s, each with `role`, the part played or the job done (`Director`, `Writer`…), several joined by ` · `. A credit as themselves and any on a talk, news or reality show is left out, and so is a title with no IMDb id. `knownFor` is the database's department for them (`Acting`, `Directing`…). Kept a week. `404` when the database has nobody under the id, `503` when it cannot be asked and nothing is kept |
| `GET /api/catalogue/genre?kind=&name=` | `CatalogueGenreView` — `{kind, genre, titles}`: up to 40 of the most popular films (`kind=film`) or shows (`kind=show`) in the genre `name` names, in any case, each with at least 100 votes; `genre` is the database's own spelling. A genre's name is the one a description's `genres` carries, so a shelf of a genre leads on to it in one call. Films and shows have separate genres (`Science Fiction` is a film's, `Sci-Fi & Fantasy` a show's). Kept a day. `400` without a kind or a name, `404` for a name the database has no genre by, `503` when it cannot be asked and nothing is kept |
| `GET,HEAD /catalogue/people/{personId}.jpg` | a person's portrait, kept with their credits; open like a film's artwork |
| `GET /api/shows` | `ShowSummary[]` — the shows the library holds, one entry each: `{imdbId?, title, firstYear?, lastYear?, status?, details?, seasons, episodes, addedAt?, sizeBytes, lastPlayed?, resume?, firstTitleId}` |
| `GET /api/shows/{imdbId}` | `ShowView` — a show as the film database describes it, in the library or not, every season with its episodes joined with the library and the asking person's progress. `404` when neither the database nor the library knows it, `503` when the database cannot be asked and nothing is kept |
| `GET /api/titles/{id}/neighbours` | `{previous?, next?, name?, previousName?, nextName?, fetchable?, fetchableName?}` — the episodes either side of one, among those the library holds, as `TitleSummary`, and what each is called where the show's description says; `fetchable` (`{season, number}`) is the episode right after this one when it is not here and the tracker offers it, which a player's next fetches into the room. All absent for a film. 404 for a title the library does not hold |
| `GET,HEAD /shows/{imdbId}/{poster.jpg,backdrop.jpg,cast-N.jpg,sNNeMM.jpg}` | a show's artwork and its episodes' stills, open like a film's |
| `GET /api/me/preferences` | `ViewerPreferences` — how the token's person watches; every field absent for somebody who set nothing. |
| `PUT /api/me/preferences` | `ViewerPreferences` → the stored `ViewerPreferences`. A blank language or a height that is not positive is stored as absent |
| `GET /api/me/progress` | `ProgressEntry[]` — the films and episodes the token's person started and did not finish, newest first: `{titleId, name, positionSeconds, durationSeconds, remainingSeconds, lastWatchedAt, episode?, showImdbId?}`. For an episode `name` is the show's and `episode` is `{season, number}`. A title the library no longer holds is left out |
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
film ignores `at`.

```jsonc
// ViewerPreferences: the defaults a film starts with, for a person wherever they sign in
{
  "subtitleLanguage": "es",   // the subtitle language to start on when the film carries it; absent for none
  "audioLanguage": "en",      // the audio language to start on when the film carries it; absent for the film's own
  "qualityHeight": 720,       // the rung to hold the film at; absent to let the connection decide
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
the people a room can invite, what the film database says of a title, a person or a genre), `cinema:download`, `cinema:wish` and `cinema:keep` (below), and the self
actions `cinema:profile.read` and `cinema:profile.write`, which every approved account holds, for a
person's own records under `/api/me`, `/api/lists` and push. `/.well-known/tks-actions.json` serves the
same declarations the checks name.

The **service key** (`X-MovieBot-Service`) proves machine work that names nobody and opens only the
routes that do such work: `/api/library/due`, `/api/transcode/*`, reading the library and the rooms,
putting a film into a room (recorded as MovieBot), the downloads list and a film's stay. It never stands
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

### Opening a film's bytes

A film's bytes are opened by the access token of somebody who may watch, or by a **media ticket** on
the query string as `?mt=…`, and the ticket is what a player should use for the film itself. A title's
`poster.jpg` is open: Discord's servers fetch it to draw an embed.

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
`max-age=300`. Segments, init files, subtitles and posters are `immutable` for a year.

Content types are set explicitly, because the framework knows none of them and hls.js refuses a
segment delivered as `application/octet-stream`: `application/vnd.apple.mpegurl`,
`video/iso.segment`, `video/mp4`, `text/vtt`.

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

The API is the one writer of downloads, wishes and keeps. A download, its searches and Watch now take
`cinema:download`; a wish takes `cinema:wish`; keeping or letting a film go takes `cinema:keep`;
finding a film in the title index and asking whether it can be watched take either of the first two;
reading how long films stay takes `cinema:watch`. No request names who asked: the person is the
token's, whichever client carried it, and the download's tags, the wish, the keep and the log record
them. A download may name a Discord channel to be announced in, which the bot does. `GET /api/downloads`
and a film's stay also answer the service key, for the announcements the bot posts. The shapes are in
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
| `GET /api/titles/find?q=` | `FoundTitle[]` — films and shows together, in the index's own order; never an episode. Takes `cinema:download`. A `FoundTitle` is `{imdbId, title, year?, display, kind, starring?, posterUrl?, rating?, votes, overview?}`, `kind` `film` or `show`, `posterUrl` the poster the title's page shows, served from here, or the index's own where the film database has none, `rating` and `overview` the film database's, absent where it has none or has not answered within the search |
| `GET /api/films/{imdbId}/availability` | `FilmAvailability` — `{imdbId, libraryId?, download?: {hash, progress, isWatchable, libraryId?}, hd1080?, hd720?, notOut, notOffered, emptyExplanation?, trackerUnanswered, waiting, waiters, streams: {used, limit}}`. The library first, then the torrent client, then the tracker, which is asked only for a film neither here nor arriving; `hd1080` and `hd720` are the best `ReleaseOffer` at each from a digital source up. With neither, `notOut` says the tracker holds nothing from a digital source up, a film to wait on, and `notOffered` that it does but every such release is turned down here, which `emptyExplanation` names |
| `POST /api/watch` | `WatchNowReply` for `{imdbId, torrentId?, quality?, episodes?, room?}` — `{outcome, room?, libraryId?, streams?}`. See Watch now below |
| `GET /api/downloads` | `DownloadView[]`: `{hash, name, release, summary, progress, isFinished, isWatchable, libraryId?, imdbId?, requestedBy?}`, `requestedBy` the account of whoever asked |
| `POST /api/downloads` | `StartDownloadReply` for `{torrentId, notifyChannelId?, roomId?, episodes?}`; `episodes` (`[{season, episode}]`) fetches only those files out of a show's release, and picking more from a release already downloading switches them on in it |
| `GET /api/wishes` | `WishView[]` — the wishes the person waits on; one the tracker has answered carries `release`, the `ReleaseOffer` found for it |
| `POST /api/wishes` | `AddWishReply` for `{chosen}` |
| `DELETE /api/wishes/{imdbId}` | `CancelWishReply` — the person comes off the wish |
| `GET /api/titles/{id}/stay` | `TitleStay`, or 404 for a film the library does not hold |
| `GET /api/stays` | `TitleStay[]` — every settled film and season, soonest to leave first, the kept ones after them; a season is listed once, by its first episode |
| `PUT /api/titles/{id}/keep` | `TitleStay`; a film already kept stays its first keeper's. Keeping an episode keeps its season |
| `DELETE /api/titles/{id}/keep` | `TitleStay`; 404 for a film the library does not hold |
| `GET /api/library/due` | `string[]` — the ids of the settled films whose time is up; the **service key** alone |

- **A film stays in the library until it has gone `Library:LeaveAfterDays` without a play, and a
  show's season the same way, as one.** The clock is only on a title that has settled on the cold
  disk, and it counts from the latest of the last play, the moment it settled, and the moment plays
  began to be recorded; for a season, the latest play of any of its episodes and the latest of them
  to settle, so all of it falls due together. A film or season somebody keeps never leaves, and
  neither does one a room holds, a room on one episode holding its season. `TitleStay` is `{titleId,
  unit, season?, settled, keeperId?, keeperName?, keptAt?, lastPlayedAt?, leavesAt?, daysLeft?,
  leaveAfterDays}`: `unit` is what stays or leaves as one (a film's id, or a season's,
  `tt11280740:s02`), `leaveAfterDays` is the rule itself, `leavesAt` and `daysLeft` are absent while
  it is kept or has not settled, and `keeperName` is the keeper's name where one is known, the id
  otherwise. A keep is held by the API, by unit, and outlives the torrent. The hand-off asks `/api/library/due` and deletes
  what it names, being the only process that writes the media roots; a hand-off the API cannot answer
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
`room` a new one is made; a room id is 1 to 64 letters, digits, `-` or `_`. A film already arriving
has the room added to its download. Otherwise the release is `torrentId`, one the API offered, or the
best at `quality` (`1080p` unless `720p` is asked) from a digital source up; `episodes` fetches only
those out of a show's release and is how a show is watched.

What the room waits on is on its state, `preparing: {imdbId, name, stage, release?, progress?,
problem?, refusal?}`, pushed to everybody in it: `stage` is `finding` (the release is being picked),
`downloading` (`progress` from 0 to 1) or `failed` (`problem`: `notOffered`, `refused`,
`unavailable`, `notWatchable`). These states name nobody in `updatedBy`. The download carries the
room as `watch:<room>`; once the hand-off marks it watchable the film is loaded into the room and
played as the person who pressed it, and `preparing` goes. A download watched this way still owes its
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

### Lists

A list holds films and shows somebody means to watch later. Every person has a Watch later, which is
theirs alone, and makes as many named lists as they like, each of which its owner may share with
people the server has a name for. A list is shown to its owner and its members and to nobody else:
for anybody else a list's id is a `404`, never a `403`. The shapes are in `src/MovieBot.Api/Lists/`.

| Route | Returns |
|---|---|
| `GET /api/me/lists?holding=` | `ListSummary[]` — the lists the token's person can see, their Watch later first and then the rest most recently changed first: `{id, name, watchLater, role, owner, members, count, updatedAt, posters, holds?}`. `role` is `owner`, `editor` or `viewer`; `owner` is `{userId, name?}`; `members` are `{userId, name?, role, since}`; `posters` are the newest entries' posters, at most four. With `holding` (an IMDb id) each list says in `holds` whether it holds that title |
| `POST /api/lists` | `ListView` for `{name}` — a new list the token's person owns. `400` for a name blank or over 80 characters once trimmed, `409` past 100 lists |
| `GET /api/lists/{id}` | `ListView` — `{list: ListSummary, entries}`, the entries newest saved first: `{imdbId, kind, title, year?, posterUrl?, rating?, runtimeMinutes?, genres, addedAt, addedBy, note?, libraryId?, inLibrary, watched}`. `addedBy` is `{userId, name?}`; `libraryId` is the title that plays a film the library holds; `inLibrary` is true for a film the library holds or a show with any episode here; `watched` is the asking person having watched the film through, and never set for a show |
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
| `GET /api/me/notifications/preferences` | `NotificationPreferences` — `{available, ready, leaving, episode, shared, kept}`, which kinds are pushed; everything but `kept` is on until switched off |
| `PUT /api/me/notifications/preferences` | `NotificationPreferences`, as stored |
| `GET /api/push/key` | `{publicKey}` — this server's VAPID key, base64url, which a browser subscribes with |
| `GET /api/me/push?endpoint=` | `{subscribed, devices}` — whether that browser is registered to this person, and how many are |
| `POST /api/me/push` | 204 for a browser's `PushSubscription.toJSON()`, `{endpoint, keys: {p256dh, auth}}`; a browser registered again replaces its own row, and belongs to whoever registered it last. An endpoint that is not `https` is a `400` |
| `DELETE /api/me/push?endpoint=` | 204, or 404 for a browser that is not this person's |
| `GET /api/people` | `PersonView[]` — everybody the server has a name for but the person asking, `{userId, name, lastSeenAt?}`, most recently seen first |
| `POST /api/rooms/{room}/invite` | `InviteReply` for `{userId}` — `{delivery}`, `push` when a push service took it for at least one of their devices and `bell` when it waits in their bell alone. 404 for somebody unknown |

- **Kinds.** `available`: a film somebody waits on can be downloaded. `ready`: a download they started
  can be watched, or could not be made watchable. `leaving`: something they watched leaves the
  library within a day. `episode`: a new episode of a show they follow is out and the tracker offers
  it, "Show S5 · E4 is out.", opening `/tv/{imdbId}/season/{n}`. `kept`: somebody else kept, or
  let go of, something they watched. `shared`: somebody put them on one of their lists, "Ana shared
  their list Friday night with you.", opening `/saved/{id}`. `invite`:
  somebody asked them into a room; an invite is pushed whatever the person chose.
- **One thing is said once.** Each notification carries the thing it is about, and a person holding a
  notification about it is not told again: a download once, a wish once, a title leaving once per
  date it is due, since a play moves the date, an episode once, a list once per person put on it.
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
  "poster": "poster.jpg",         // absent when the container carried no cover art
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
film?, sizeBytes?, addedAt?, details?, progress?, lastPlayed?}`. `kind` is `"film"` or `"show"`, as the
manifest says. Every episode of a show is a title of its own: `episode` is `{season, number}`, `film`
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
  "directors": ["Michael Mann"], "writers": ["Michael Mann"],
  "personIds": { "Michael Mann": 638 },   // each director and writer by name: the id /api/catalogue/people takes
  "cast": [{ "personId": 1158, "name": "Al Pacino", "character": "Lt. Vincent Hanna", "portraitUrl": "/films/tt0113277/cast-0.jpg" }],
  "originalLanguage": "en", "languages": ["English", "Spanish"], "countries": ["United States of America"],
  "posterUrl": "/films/tt0113277/poster.jpg", "backdropUrl": "/films/tt0113277/backdrop.jpg"
}
```

```jsonc
// ShowView: GET /api/shows/{imdbId}
{
  "imdbId": "tt11280740", "tmdbId": 95396, "title": "Severance", "firstYear": 2022, "lastYear": 2025,
  "status": "Returning Series", "inProduction": true, "certification": "TV-MA",
  "genres": ["Drama", "Mystery"], "overview": "…", "rating": 8.4, "votes": 2900,
  "creators": ["Dan Erickson"], "personIds": { "Dan Erickson": 1220098 },
  "cast": [{ "personId": 34544, "name": "Adam Scott", "character": "Mark Scout", "portraitUrl": "/shows/tt11280740/cast-0.jpg" }],
  "languages": ["English"], "countries": ["United States of America"], "networks": ["Apple TV+"],
  "posterUrl": "/shows/tt11280740/poster.jpg", "backdropUrl": "/shows/tt11280740/backdrop.jpg",
  "seasons": [{
    "number": 2, "name": "Season 2", "airDate": "2025-01-16",
    "stay": { "titleId": "severance-s02e01", "unit": "tt11280740:s02", "season": 2, "daysLeft": 7, … },  // absent while the library holds none of it
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
  "nextEpisode": { "season": 3, "episode": 1, "name": "…", "airDate": "2027-01-15" }  // absent when the database names none
}
```

`resume` is the latest episode the person watched if they did not finish it, else the one after it
when the library holds it; absent otherwise. Seasons are the database's numbered ones and any the
library holds that it does not list; specials are left out.

A cast member's `personId` and the ids in `personIds` are the film database's own for those people,
what `/api/catalogue/people/{personId}` takes. A kept description whose cast carries no id is asked of
the database again the next time it is read, and until then `personId` is absent and `personIds` empty.

`CatalogueTitleView` is `{imdbId, kind, title, year?, rating?, votes, overview?, posterUrl?, role?}`:
`kind` is `film` or `show`, `posterUrl` the poster the title's page shows, served from here, and
`role` is set among a person's credits only. Whether the library holds the title is the library's to
say (`/api/titles`, `/api/shows`), joined by `imdbId`.

Everything here is TMDB's, and a surface showing it credits TMDB, as its terms ask.

`RoomSummary` is `{sessionId, titleId?, name?, durationSeconds?, paused, positionSeconds,
participants}`. `name` is what the film is called, `positionSeconds` is where the room was when
the listing was taken, and `participants` is a count: the people in a room are listed by the room
itself, never here.

## Hub

SignalR at `/hub/session`. Camel-cased payloads, string enums.

**Call:**

| Method | Signature | Notes |
|---|---|---|
| `Join` | `(sessionId) → SessionStatePush` | call first; everything else throws until you do. The viewer is the connection's token: its `sub` and `name` |
| `LoadTitle` | `(titleId, startSeconds: double?) → SessionStatePush` | from `startSeconds`, held to the transcode head, or from 0 when null; a playing room keeps playing, a paused one stays paused. Both arguments are always sent, null for no start |
| `Play` | `(atSeconds) → SessionStatePush` | |
| `Pause` | `(atSeconds) → SessionStatePush` | |
| `Seek` | `(toSeconds) → SessionStatePush` | may be clamped |
| `ServerTime` | `() → DateTimeOffset` | for clock-offset estimation |
| `Buffering` | `(waiting: bool)` | this viewer's player has been waiting for the film past a stutter, or is playing again. Moves nothing; the roster carries it |
| `ReportQuality` | `(playingHeight: int?, wantedHeight: int?)` | the rung height this viewer plays, and the one its own adaptive choice would pick with nothing holding it back; on change and about every 30 s. Answers nothing and is sent to nobody: the server keeps it per connection for its transcode planner and drops it when the connection leaves. A player that cannot say (native HLS) sends nothing, and is taken to want the tallest rung |

**Receive:**

| Event | Payload |
|---|---|
| `StateChanged` | `SessionStatePush` — sent to everyone including the caller |
| `SeekClamped` | `{requestedSeconds, grantedSeconds, headSeconds}` — sent to the caller only; `headSeconds` is the best rung's head held back to the audio, which is at least `transcodeHead` |
| `ParticipantsChanged` | `Participant[]` = `{userId, displayName, buffering}[]` — sent when somebody joins, leaves, or starts or stops waiting for the film |
| `TitleChanged` | `Manifest` — the manifest of the title this room holds changed: head, preview sheet, subtitles, status. Sent to every room holding the title, within a couple of seconds of the write |

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
    "audioHead": 1000.0            // absent unless the manifest tracks audio apart
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
   `paused ? positionSeconds : positionSeconds + (serverNow - anchorUtc) * rate`, where
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
8. **A buffering viewer must not pause the room.** A stalled client falls behind and resyncs
   itself. "Wait for me" is a button a person presses, never automatic.

## Shared versus local

Shared, and nothing else: which title, paused, position, rate, who changed it.

**Volume, subtitle selection, audio track and quality are per-viewer** and live in
`localStorage`. They never go on the wire. Shared volume is a way for one person to deafen
everyone else, and one person needing subtitles should not put them on five other screens.
