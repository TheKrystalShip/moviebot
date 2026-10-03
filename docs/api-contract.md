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
| `GET /api/config` | `{discordClientId, publicBaseUrl?}` |
| `GET /api/titles` | `TitleSummary[]` |
| `GET /api/titles/{id}` | `Manifest`, or 404 |
| `GET /api/titles/{id}/ticket` | `{ticket, expiresAt}` — what a request for this film's bytes carries, or 404 |
| `GET /api/sessions` | `RoomSummary[]` — every room, what it is watching, how many are in it |
| `GET /api/sessions/{id}` | `SessionStatePush` — creates the session if new |
| `GET /api/sessions/{id}/participants` | `Participant[]` |
| `POST /api/sessions/{id}/title` | `SessionStatePush` — puts a film in the room, or 404 if the library has no such title. The body is `{titleId, userId?, displayName?, startSeconds?}`; `startSeconds` puts the room there instead of at the beginning, held to the transcode head like a seek |
| `POST /api/sessions/{id}/play` | `RoomChanged` |
| `POST /api/sessions/{id}/pause` | `RoomChanged` |
| `POST /api/sessions/{id}/seek` | `RoomChanged` |
| `POST /api/sessions/{id}/seek-relative` | `RoomChanged` |
| `GET,HEAD /media/{id}/**` | playlists, segments, subtitles, poster. Opened by `?mt=` or the bearer token |
| `GET /api/films/{imdbId}` | `FilmView` — a film as the film database describes it, in the library or not, with artwork at this server's addresses. `404` when the database holds no film under the id, `503` when it cannot be asked and nothing is kept |
| `GET,HEAD /films/{imdbId}/{poster.jpg,backdrop.jpg,cast-N.jpg}` | the artwork kept beside a description. Open: it is public artwork and an `<img>` carries no token |
| `GET /api/me/preferences` | `ViewerPreferences` — how the token's person watches; every field absent for somebody who set nothing. `403` for the service key, which names nobody |
| `PUT /api/me/preferences` | `ViewerPreferences` → the stored `ViewerPreferences`. A blank language or a height that is not positive is stored as absent |
| `GET /api/me/progress` | `ProgressEntry[]` — the films the token's person started and did not finish, newest first: `{titleId, name, positionSeconds, durationSeconds, remainingSeconds, lastWatchedAt}`. A film the library no longer holds is left out. `403` for the service key |
| `GET /api/me/history` | `HistoryEntry[]` — every film the token's person watched, newest first: `{titleId, name, positionSeconds, durationSeconds, finished, lastWatchedAt}`. `finished` is a position past 90 % of the film. `403` for the service key |
| `DELETE /api/me/history/{titleId}` | `204` — forgets the person's entry for the film, and so their progress in it. The film's own last play stays. `403` for the service key |
| `GET /api/me/sessions` | `DeviceSession[]` — the devices the token's person is signed in on, most recently seen first: `{id, device, issuedAt, lastSeenAt, current}`. `current` marks the one asking. `403` for the service key |
| `DELETE /api/me/sessions/{id}` | 204 — ends one of the person's own sessions. Another person's id, or one that does not exist, is `404` |
| `DELETE /api/me/sessions` | 204 — ends every session of the person except the current one |

What is recorded is sampled from rooms: every 30 seconds each person in a room that is **playing** a
film is credited with the room's position, so a paused room left open ages nothing. Opening a film
at `/r/{room}?title={id}&at={seconds}` into an empty room starts it there; a room already holding a
film ignores `at`.

```jsonc
// ViewerPreferences: the defaults a film starts with, for a person wherever they sign in
{
  "subtitleLanguage": "es",   // the subtitle language to start on when the film carries it; absent for none
  "audioLanguage": "en",      // the audio language to start on when the film carries it; absent for the film's own
  "qualityHeight": 720        // the rung to hold the film at; absent to let the connection decide
}
```

CORS reflects any origin and allows credentials. Media answers HEAD as well as GET.

Any other GET is a page of the site and is answered with its index. `/r/{room}` is answered with the
index carrying Open Graph and Twitter tags for the room: `og:title` "Watch <film> on Krystal Cinema",
`og:description` the year and how far in ("1995 · 31 min in"), `og:image` the film's backdrop (or its
poster) at the public address `Api:PublicBaseUrl`. A room holding nothing shows the film its
`?title=` names, or the site. The tags never name or count the people in the room.

### Opening a film's bytes

Everything closed is opened by the room token, carried as `Authorization: Bearer`. `/media` also
takes a **media ticket** on the query string as `?mt=…`, and that is what a player should use for
the film itself.

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
{ "atSeconds": 403.0, "userId": "…", "displayName": "…" }
// seek
{ "toSeconds": 1200.0, "userId": "…", "displayName": "…" }
// seek-relative — negative goes back
{ "deltaSeconds": -15.0, "userId": "…", "displayName": "…" }

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
- **`userId` and `displayName` are optional and name whoever asked.** Absent, the change is
  recorded as MovieBot itself, which is the honest answer for a room that moved with nobody asking.
- **The clamp comes back in the body.** Over the hub it goes to the caller alone; an HTTP caller is
  the one thing a broadcast cannot reach.

`.m3u8` for a title whose status is `transcoding` is served `no-store`; once `ready` it is
`max-age=300`. Segments, init files, subtitles and posters are `immutable` for a year.

Content types are set explicitly, because the framework knows none of them and hls.js refuses a
segment delivered as `application/octet-stream`: `application/vnd.apple.mpegurl`,
`video/iso.segment`, `video/mp4`, `text/vtt`.

### Fetching films

The API is the one writer of downloads, wishes and keeps. These routes spend the disk and the
tracker's ratio, so they answer the **service key** (`X-MovieBot-Service`) or a **member's token**,
one whose `member` claim says Discord confirmed at sign-in that its bearer is in the server; any
other token is a `403`. A service names the person it acts for on every act as
`requestedBy: {id, name}`, and that is who the download's tags and the log record. A member is the
person: `requestedBy` and `userId` are taken from the token whatever the request says, and a
member's download or wish names no Discord channel. `/wishes/available` and `/wishes/{imdbId}/told`
are the bot's, and answer the service key alone. The shapes are in `src/MovieBot.Core/Acquisition.cs`;
the API reads the tracker, `QBittorrent`, `Download`, `Selection`, `Retention` and `Notify` settings
to serve them.

### Signing in

`POST /api/auth/discord/callback` takes `{code, redirectUri?, sessionId?}` and answers
`{accessToken, roomToken, user}`. The Activity sends the code its SDK was given and no redirect; it
only opens inside the server, and its token says nothing more than who is watching. The site sends
the code from the OAuth2 redirect, asked for with `identify guilds.members.read`, and its
`redirectUri`: then the token is issued only to a member of one of `Discord.GuildIds`, and carries
`member`. Somebody outside the server is a `403`; a Discord that cannot say is a `502`.

A token's payload is `sub`, `name`, `room`, `member`, `exp` and `jti`, a random id naming that one
sign-in. Each sign-in is recorded with a short summary of the browser that made it ("Chrome on
Android"; "Discord Activity" for the Activity's), which is what `/api/me/sessions` lists. A session
the person ended is refused with the same `401` as a token that was never valid, on every route and
at the hub's negotiate. A token without a `jti` is valid until `exp` and is not listed.

| Route | Returns |
|---|---|
| `GET /api/releases/suggest?q=&session=` | `ReleaseSuggestions` — picker rows for what was typed; `session` keys the debounce |
| `GET /api/releases?q=` or `?imdb=` | `RankedOffers` — the tracker's releases for a film, best first |
| `GET /api/releases/{torrentId}` | `ReleaseOffer` — a release a search or a picker offered, or 404 |
| `GET /api/downloads` | `DownloadView[]` |
| `POST /api/downloads` | `StartDownloadReply` for `{torrentId, requestedBy, notifyChannelId?, roomId?}` |
| `GET /api/wishes?userId=` | `WishView[]` — every wish, or the ones that person waits on |
| `POST /api/wishes` | `AddWishReply` for `{chosen, requestedBy, channelId?}` |
| `DELETE /api/wishes/{imdbId}?userId=` | `CancelWishReply` |
| `GET /api/wishes/available` | `AvailableWish[]` — wishes the tracker can now answer, found by the hourly sweep |
| `POST /api/wishes/{imdbId}/told` | 204 for `{channelIds}` — whoever asked in those channels is told and comes off the wish |
| `GET /api/keeps` | `KeptFilmView[]` |
| `PUT /api/keeps/{hash}` | `KeepReply` for `{requestedBy}` |
| `POST /api/keeps/{hash}/release` | `KeepReply` for `{requestedBy}` |
| `GET /api/titles/{id}/retention` | `KeptFilmView`, or 204 for a title that did not arrive as a download |

- **A download names a release the API offered.** `torrentId` is resolved against what a search or a
  picker answered; anything else is `noLongerOffered`, before the torrent client is asked.
- **An outcome is a word and the facts behind it, never a sentence.** What to tell somebody depends
  on where they asked — a Discord channel mentions people and names slash commands, a web page does
  neither — so each client words the outcome from the same facts.
- **A service that could not be reached is a 502, not an empty list.** No downloads and a torrent
  client that did not answer are different answers, and the second carries `ErrorReply`.
- **A wish stays until its people are told.** The sweep finds what became available; the client
  that announces it says which channels it reached, and only those subscribers come off. Somebody
  who asked without a channel stays on the wish.

## Manifest

```jsonc
{
  "id": "clip",
  "title": "Gladiator 2000 Extended Cut",
  "durationSeconds": 43.501,
  "status": "transcoding",        // "transcoding" | "ready" | "failed"
  "headSeconds": 20.0,            // absent once ready
  "error": "…",                   // present only when status is "failed"
  "poster": "poster.jpg",         // absent when the container carried no cover art
  "master": "master.m3u8",        // load THIS, not a bare video rendition
  "video": {
    "width": 1920,
    "height": 800,
    "sourceCodec": "hevc",
    "sourceHdr": "hdr10+dovi-p8.1",
    "renditions": [
      { "name": "800p", "bitrateKbps": 9000, "width": 1920, "height": 800, "uri": "v0/index.m3u8" },
      { "name": "536p", "bitrateKbps": 4000, "width": 1280, "height": 536, "uri": "v1/index.m3u8" }
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
`"bitmap"`. All `uri` values are relative to `/media/{id}/`.

**Load `master`, not `video.renditions[].uri`.** The master playlist is what binds the audio
renditions to the video; a player handed a bare video rendition plays the film silently with no
audio track to switch to. Subtitles are not in the master — they are standalone WebVTT files
attached as text tracks from `subtitles[].uri`.

Every feature track has a second mix with the dialogue raised, listed after the source's own
tracks with the label `"<track> — Dialogue boost"`. It is made after the main pass, so `audio`
gains those entries once, before `status` turns `ready`, and the master is rewritten with them in
the same moment. A player that loaded the master earlier offers them after it loads the film again.

A track with `available: false` is listed on purpose — a language that is simply missing from the
menu reads as a bug. Render it disabled with its `reason`, do not hide it.

`TitleSummary` is `{id, title, durationSeconds, status, headSeconds?, poster?, film?, sizeBytes?,
addedAt?, details?, progress?, lastPlayed?}`. `details` is what the film database said, as far as a
listing shows it — `{rating?, votes, certification?, genres, runtimeMinutes?, overview?, directors,
posterUrl?, backdropUrl?}` — and is absent for a film not yet described; the listing never waits on
the network for it. `progress` is `{positionSeconds, finished, lastWatchedAt}` for the person the
token names, absent when they have not watched the film and always absent for the service key.
`lastPlayed` is `{at, byName?}`, the last time anybody played the film, absent when nobody has;
`byName` is absent when that person's name was never heard.

```jsonc
// FilmView: GET /api/films/{imdbId}
{
  "imdbId": "tt0113277", "tmdbId": 949, "title": "Heat", "year": 1995, "releaseDate": "1995-12-15",
  "runtimeMinutes": 170, "certification": "R", "genres": ["Crime", "Drama", "Action"],
  "overview": "…", "tagline": "…", "rating": 7.9, "votes": 8740,   // TMDB's own rating, absent with no votes
  "directors": ["Michael Mann"], "writers": ["Michael Mann"],
  "cast": [{ "name": "Al Pacino", "character": "Lt. Vincent Hanna", "portraitUrl": "/films/tt0113277/cast-0.jpg" }],
  "originalLanguage": "en", "languages": ["English", "Spanish"], "countries": ["United States of America"],
  "posterUrl": "/films/tt0113277/poster.jpg", "backdropUrl": "/films/tt0113277/backdrop.jpg"
}
```

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
| `Join` | `(sessionId, userId, displayName) → SessionStatePush` | call first; everything else throws until you do |
| `LoadTitle` | `(titleId, startSeconds: double?) → SessionStatePush` | from `startSeconds`, held to the transcode head, or from 0 when null; a playing room keeps playing, a paused one stays paused. Both arguments are always sent, null for no start |
| `Play` | `(atSeconds) → SessionStatePush` | |
| `Pause` | `(atSeconds) → SessionStatePush` | |
| `Seek` | `(toSeconds) → SessionStatePush` | may be clamped |
| `ServerTime` | `() → DateTimeOffset` | for clock-offset estimation |
| `Buffering` | `(waiting: bool)` | this viewer's player has been waiting for the film past a stutter, or is playing again. Moves nothing; the roster carries it |

**Receive:**

| Event | Payload |
|---|---|
| `StateChanged` | `SessionStatePush` — sent to everyone including the caller |
| `SeekClamped` | `{requestedSeconds, grantedSeconds, headSeconds}` — sent to the caller only |
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
    "transcodeHead": 300.0         // absent once the title is ready
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
