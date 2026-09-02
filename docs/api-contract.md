# API contract

What `MovieBot.Api` serves, as built. Everything is camelCase; enums are lowercase strings.

Default listen address is `http://127.0.0.1:8099`. The media root comes from `Media__Root`
(or `Media:Root` in configuration) and must be absolute in anything but a demo.

## HTTP

| Route | Returns |
|---|---|
| `GET /health` | `{"status":"ok","rooms":n,"watching":n}` — occupied rooms and people, no names |
| `GET /api/config` | `{discordClientId, publicBaseUrl?}` |
| `GET /api/titles` | `TitleSummary[]` |
| `GET /api/titles/{id}` | `Manifest`, or 404 |
| `GET /api/sessions` | `RoomSummary[]` — every room, what it is watching, how many are in it |
| `GET /api/sessions/{id}` | `SessionStatePush` — creates the session if new |
| `GET /api/sessions/{id}/participants` | `Participant[]` |
| `GET,HEAD /media/{id}/**` | playlists, segments, subtitles, poster |

CORS reflects any origin and allows credentials. Media answers HEAD as well as GET.

`.m3u8` for a title whose status is `transcoding` is served `no-store`; once `ready` it is
`max-age=300`. Segments, init files, subtitles and posters are `immutable` for a year.

Content types are set explicitly, because the framework knows none of them and hls.js refuses a
segment delivered as `application/octet-stream`: `application/vnd.apple.mpegurl`,
`video/iso.segment`, `video/mp4`, `text/vtt`.

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
    "renditions": [{ "name": "800p", "bitrateKbps": 9000, "uri": "v0/index.m3u8" }]
  },
  "audio": [
    { "id": "a0", "kind": "feature", "language": "eng", "label": "English",
      "channels": 2, "default": true, "uri": "a0/index.m3u8" },
    { "id": "a1", "kind": "commentary", "language": "eng",
      "label": "Commentary with director Ridley Scott and actor Russell Crowe",
      "channels": 2, "uri": "a1/index.m3u8" }
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

A track with `available: false` is listed on purpose — a language that is simply missing from the
menu reads as a bug. Render it disabled with its `reason`, do not hide it.

`TitleSummary` is `{id, title, durationSeconds, status, headSeconds?, poster?}`.

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
| `LoadTitle` | `(titleId) → SessionStatePush` | from 0; a playing room keeps playing, a paused one stays paused |
| `Play` | `(atSeconds) → SessionStatePush` | |
| `Pause` | `(atSeconds) → SessionStatePush` | |
| `Seek` | `(toSeconds) → SessionStatePush` | may be clamped |
| `ServerTime` | `() → DateTimeOffset` | for clock-offset estimation |

**Receive:**

| Event | Payload |
|---|---|
| `StateChanged` | `SessionStatePush` — sent to everyone including the caller |
| `SeekClamped` | `{requestedSeconds, grantedSeconds, headSeconds}` — sent to the caller only |
| `ParticipantsChanged` | `Participant[]` = `{userId, displayName}[]` |
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
