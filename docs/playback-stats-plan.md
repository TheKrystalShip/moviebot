# Playback stats

A plan, not canon. What it settles moves into the `CLAUDE.md` beside the code it governs
(`web/site/src/player/`, `src/MovieBot.Api/Sessions/`), `web/site/README.md` and
`docs/api-contract.md` when it lands, and its section here is struck. When the last phase lands, this
file is deleted.

## 1. What this is for

A viewer's trouble happens in their browser, and the server sees only its edges: the segments fetched,
the hub calls made, and what `ReportTrouble` says (`web/site/src/player/CLAUDE.md`, "What it
fetches"). A viewer who says the sound runs behind the picture, while every other viewer of the same
files is fine, leaves nothing on the server to read: the playlists and the segments' timestamps line
up, and whatever is wrong is between that browser's decoder and its speakers.

The player gets a stats panel, the counterpart of YouTube's "Stats for nerds" and Netflix's
Ctrl+Alt+Shift+D: what this player measures about its own playback, drawn over the film for the
person watching, and sent to the server when they choose to send it.

- **It shows this viewer's own measurements and nothing else.** No other viewer's numbers, and nothing
  about the room that the room's state does not already carry.
- **Every value is measured or says it is unknown.** A browser that does not offer a measurement shows
  `unknown` for it, never a zero or an estimate made to fill the space.
- **It changes nothing about playback.** It reads what the sync controller, the stream and the hub
  already hold, and the one measurement it adds (below) runs only while the panel is open.

## 2. What stands

These are canon and are not restated here:

| Piece | Authority |
|---|---|
| Drift correction: the nudge, the hard seek, waiting while the film cannot play forward | `web/site/src/player/CLAUDE.md`, `engine/session/sync.ts` |
| The server clock: round trips to `ServerTime`, the shortest round trip wins | `engine/session/clock.ts` |
| Rejoining after a reconnect and when the page is in front of somebody again | `web/site/src/player/CLAUDE.md` ("When the room cannot hear the page") |
| CMCD on every request, the playback id, `ReportTrouble` and its rate limits | `web/site/src/player/CLAUDE.md` ("What it fetches"), `src/MovieBot.Api/Sessions/SessionHub.cs` |
| Buffer ahead, the device store, rung caps while a title transcodes | `web/site/src/player/CLAUDE.md` |

## 3. What the panel shows

Four groups, each a block of label and value, refreshed once a second while the panel is open, with
one graph.

### Sound against picture

The measurement the motivating report needs, and the one nothing else in the player takes.

- **Picture offset.** `requestVideoFrameCallback` hands each presented frame's `mediaTime` and its
  `expectedDisplayTime`. The element's `currentTime` follows the audio clock, so at the moment the frame
  is shown the audio is at `currentTime + (expectedDisplayTime - now) * playbackRate`, and the frame's
  `mediaTime` minus that is how far the picture is from the sound the decoder is producing. Shown as a
  running median over the last second of frames, in milliseconds, positive when the picture is ahead.
- **Output latency.** `AudioContext.outputLatency` for the default output device: what the speakers add
  after the decoder, which is where a Bluetooth headset's 150 to 300 ms sits. An `AudioContext` is
  created only while the panel is open and closed with it; it is never connected to the film's audio.
  The value describes the default device, which is the film's device unless the person routed the tab
  elsewhere, and the label says "default output".
- **Total.** Picture offset plus output latency: how far the sound reaching the person's ears trails
  the picture, which is the number a person complaining about lag is describing.

Where `requestVideoFrameCallback` or `outputLatency` is missing, the value is `unknown` and the total
is `unknown`.

### This player against the room

- **Drift graph.** The room's expected position (`targetFor`) minus the playhead, sampled at each of
  the sync controller's checks, over the last five minutes. The nudge band (`NudgeSeconds`) and the
  hard-seek line (`HardSeekSeconds`) are drawn as guides, and each correction is a mark on the time
  axis: a nudge started, a nudge settled, a hard seek, a rejoin with its cause (reconnect, page shown
  again).
- **Rate.** The element's `playbackRate` against the room's, so a running nudge is visible.
- **Server clock.** The estimated offset from the server's clock, the round trip of the sample it was
  taken from, and how many samples are held.
- **Hub.** Connected, reconnecting or disconnected, with the number of reconnects and rejoins since
  the film loaded.

### Network

- **Throughput.** hls.js's `bandwidthEstimate`, and the rate measured from fetches over the network
  alone (`Reach.measured`), which leaves out what the device store answered.
- **Ahead.** Seconds buffered past the playhead for the picture and for the sound, each from its own
  `SourceBuffer` (hls.js's `BUFFER_APPENDED` carries both ranges): a sound buffer that runs dry while
  the picture's is full is visible here and nowhere else.
- **Fetches.** The last segment's time to first byte and total time, its size, and where it came from
  (server or device); the count of failed fetches, retries and fatal errors since the film loaded,
  with the last error's hls.js `details`.

### Playback

- **Picture.** The rung playing and the rung wanted (what `ReportQuality` sends), its resolution, the
  video codec, and the frames dropped and shown (`getVideoPlaybackQuality`).
- **Sound.** The audio track's label and id, its codec and channels.
- **Stalls.** How many, how long in all, and the longest, since the film loaded; a stall is what the
  spinner's grace already counts as one.
- **Identity of the playback.** The playback id CMCD and `ReportTrouble` name, the title id, the
  site's version, the front door (site, Activity, guest link, television), the buffer-ahead choice,
  and the browser's user agent.

## 4. Opening it

- **Hidden by default, opened by the person.** Whether it is open is kept in this browser
  (`cinema.playbackStats`), like volume, so a reload made to try again comes back with the panel up and
  its history starting over.
- **Ctrl+Alt+Shift+D toggles it.** `bindShortcuts` ignores every modified key, so the binding is its
  own listener and collides with none of the player's keys. Ctrl+Shift+D alone is the browser's "bookmark
  all tabs" in Chrome and Firefox.
- **Settings has a Playback stats row** on every frame size, in the overflow menu of a tiny frame, and
  in a television's Settings panel, so a phone, an Activity and a remote can reach it without a
  keyboard.
- **The panel sits at the top left of the frame**, over the film and under the menus, scrolls inside
  itself on a short frame, and answers no press except its own buttons, so a tap or a click goes
  through to the film. Close, Copy and Send are its only controls.
- **A guest has it too.** It moves nothing, so the view-only rules (`web/site/src/player/CLAUDE.md`,
  "A guest") do not hide it.

## 5. Sending it

The panel is only as useful as the person's description of it. Send puts the numbers where the
operator can read them.

- **Send writes a snapshot to the API's log.** The hub method `ReportStats(snapshot)` takes the
  panel's values as JSON, at most 8 KB, and writes one structured line with the viewer's display name,
  the room, the title and the playback id, as `ReportTrouble` does. It answers nothing and tells nobody
  in the room.
- **It is limited per connection**: one snapshot every ten seconds; one sent sooner is dropped and the
  button says when it may be pressed again.
- **A connection not in the room sends nothing**, the same rule `ReportTrouble` keeps. A guest may
  send.
- **The snapshot carries the drift graph's samples and marks**, so the log line holds the five
  minutes before the press and not only the instant of it.
- **Copy puts the same JSON on the clipboard**, for a person who wants to paste it somewhere.
- **Nothing is stored beyond the log.** No table, no endpoint that lists snapshots.

## 6. Where it lives

| Piece | File |
|---|---|
| The collector: ring buffers for drift samples and marks, counters, the frame-offset median | `web/site/src/player/engine/media/playbackStats.ts` |
| Drift samples and correction marks, emitted from each check | `engine/session/sync.ts` (a hook, no change to what it corrects) |
| Rejoin causes | `engine/session/hub.ts`, `engine/room.ts` |
| Fetch timings, per-buffer ranges, errors, codec and level | `engine/media/stream.ts` |
| The panel and its graph | `web/site/src/player/PlaybackStats.jsx`, `player.css` |
| The Settings row and the key | `Menus.jsx`, `Player.jsx` |
| `ReportStats` and its rate limit | `src/MovieBot.Api/Sessions/SessionHub.cs`, beside `PlayerTrouble` |

The ring buffers and counters run from the moment a film loads, so opening the panel shows what led up
to it; they are a few hundred numbers and cost nothing measurable. The frame callback and the
`AudioContext` run only while the panel is open.

## 7. Phases

1. **The panel.** Sections 3, 4 and 6 without Send. Client only, and deployable alone.
2. **Send.** `ReportStats` on the hub, the button and its limit, `docs/api-contract.md` and the
   `Sessions` `CLAUDE.md`.

Each phase is a version bump with its CHANGELOG entry, and is deployed and checked on the live site
before it is committed.

## 8. How each phase is checked

- `npm run check` and `npm run lint` in `web/site`.
- `npm test` gains `verify/playbackStats.test.mjs` for the pure parts: the frame-offset arithmetic, the
  running median, the ring buffer's window, and the snapshot's size cap.
- A headless Chromium run against the dev API (`web/site/README.md`): the key and the Settings row
  open and close the panel, every value is either a number or `unknown`, the drift graph grows while
  the film plays, and a forced hard seek leaves a mark. Headless Chromium offers
  `requestVideoFrameCallback`, so the picture offset is measured there; its output latency is whatever
  the headless audio sink reports.
- Phase 2: `dotnet test` gains hub tests for `ReportStats` (a participant's snapshot is logged, a
  non-participant's and an oversized one are not, a second within ten seconds is dropped), and on the
  live site a Send from a room is read back from `journalctl -u moviebot-api` on hotbox.
- On a real device with a Bluetooth headset: the output latency and the total move by the headset's
  delay when it is connected, which is the case the panel exists to show.

## 9. Open questions

- **Firefox and Safari.** Firefox offers `requestVideoFrameCallback`; Safari offers it but not
  `outputLatency`. Both are `unknown` where missing, and nothing in the plan depends on either being
  present. Whether a television's WebView offers them is measured on the reference TV.
- **The Discord Activity's iframe** may deny the clipboard to Copy; Send works there regardless.
