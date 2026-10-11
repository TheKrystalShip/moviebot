# Remote mode for Krystal Cinema

A plan, not canon. What it settles moves into the docs beside the code it governs
(`docs/api-contract.md`, `web/site/README.md`, `tv/README.md`, and for the anchor's half
`tks-auth/src/Auth.Anchor/CLAUDE.md` and `kgsm-docs/systems/moviebot/sign-in.md`) when it lands, and
its section here is struck. When the last step lands, this file is deleted.

Each step below is taken through a deep dive before it is built: the questions listed under it are
answered, by measurement where they can be, and the answers are written into the step before code is
written.

## 1. What this is for

A phone drives the film on a TV running the Cinema app: pause, seek, skip, the next episode, the TV's
own subtitles and audio, and putting a different film on, browsed on the phone rather than with a
D-pad. The phone becomes the browser and the TV the screen.

The case it is built around: a film night, the phone scans the TV once, a film goes on, the phone goes
in a pocket for two hours and comes out at the end to put the next one on, and the remote still works
without scanning again.

## 2. Decisions

| Decision | Outcome |
|---|---|
| What a remote is | A phone that joins the TV's room as a remote: it drives the room's shared timeline through the hub like any member, and plays no video. Most of the control surface already exists (`SessionHub`: `Play`, `Pause`, `Seek`, `LoadTitle`, `Next`, `Previous`, `PlayEpisode`) |
| Whose screens a phone sees | Only its own person's. The server checks it on every message |
| What a screen is | A connection signed in through the `cinema-tv` client, decided by the server from the token, never by what the page says. Its identity is its anchor session (`sid`), which survives token refresh |
| More than one TV per person | Yes. Every screen has a name, and a phone paired to several picks one |
| Where a screen's name comes from | The anchor. The wrapper reads the TV's own name and hands it to the page; the page sends it with the device authorization; the anchor shows it, editable, on the approval page and keeps it on the device session |
| How a phone finds a TV | A QR code on the TV, and nothing else. A scan proves the phone can see the screen and works whatever network the phone is on. Matching by network is a follow-up |
| How long a pairing lasts | Until somebody ends it. It survives the phone app closing, the phone sleeping and the TV app closing; it ends when the phone or the TV unpairs, or when either session ends. There is no idle expiry |
| Pairing and remote mode | Separate. A pairing says this phone may drive that screen; remote mode is a toggle on the phone saying it is driving it now. Turning remote mode off unpairs nothing, and turning it back on needs no scan. The toggle's state is kept across the app closing |
| Watching on the phone | Any time. Remote mode never locks the phone to the TV: in remote mode a title's main button is Watch on TV, with Watch and the phone glyph beside it, which turns remote mode off and plays on the phone |
| What the remote binds to | The screen, not the room. When the TV leaves a room and opens another, the remote follows it |
| What the remote does while the TV plays nothing | Watch on TV from the phone's own pages. There is no touch D-pad and no keyboard for the TV's fields: the physical remote does the first, and browsing on the phone replaces the second |
| Watch on TV over a playing film | Asks on the phone first while the TV is mid-film; replaces at once when the TV is idle or near the end |
| What the TV shows | The pairing code beside the paused player's info card, centred in the same box; and, while paused with a remote attached, a phone glyph beside who is watching. Nothing while a film plays |
| Volume | Not on the remote. A page can only scale the video under the TV's own volume, which the remote and CEC hold |
| Lock-screen media controls on the phone | None. Android shows them only for a page actually playing sound |
| Which surfaces carry the remote | The phone. The protocol names no surface, so a desktop tab is the same messages under another layout |
| The design | Locked: the canvas https://claude.ai/artifact/12BZT8yRQ7ZbCu79YDud1g, drawn with the site's own stylesheets. Each step builds what it draws |
| What the remote is made of | Components the site already has: the `Drawer`, the download sheet's head and option list, the player's scrub bar, control row, episodes list and subtitle style editor, `EmptyState`, `Rail` and the Continue watching card. Nothing is redrawn for the remote |
| Confirming a pairing | None. Scanning pairs at once and opens the remote; a toast says so and carries Undo. Pairing is undone as easily as it is made, and the scan already says what the person meant |
| Gestures | Shortcuts for controls already on screen, never the only way to anything. The picture takes the player's touch handling (tap the middle, double-tap a side, sweep sideways to scrub, the room moving on release) with its vertical sweeps off. Pulling the drawer down by its handle collapses it to its strip; swiping the strip sideways turns remote mode off, with Undo in a toast. A gesture that lands on the TV gives a short vibration where the phone allows one |

## 3. What stands

| Piece | Authority |
|---|---|
| Rooms, the shared timeline, the hub and who may come in | `docs/api-contract.md` "Hub" |
| Per-viewer settings (subtitles, audio, quality) that are never the room's | `README.md`, `web/site/src/player/CLAUDE.md` |
| The TV's design, its navigator, Back and Menu | `docs/tv-mode-plan.md`, `web/site/README.md` "On a television" |
| The TV's sign-in through the device grant, the `cinema-tv` client | `web/site/README.md` "Signing in", `tks-auth/src/Auth.Anchor/CLAUDE.md` "The device grant" |
| A token's `sid` | `kgsm-docs/reference/session-tokens.md` |
| The wrapper and its bridge, `window.krystalTv` | `tv/README.md` |

## 4. Steps

### Step 1: naming a screen

The wrapper reads the name the TV's owner gave it and hands it to the page through `window.krystalTv`;
the page sends it as an extension parameter on `/device_authorization` (RFC 8628 allows them); the
anchor's `/device` page shows it in an editable field beside Approve and keeps what is approved on the
device session. Crosses into tks-auth and the wrapper, which ships under its own `tv-v*` clock.

Questions:

- Fire OS fills `Settings.Global.DEVICE_NAME` with the name set under Settings, My Fire TV: on the
  Xiaomi it reads `Fire TV de Cristina`, the same as its Bluetooth name (measured over `adb`, Fire OS
  8.1.8.2, API 30). It is the owner's own wording and often carries a person's name, which is one more
  reason the approval page lets it be edited. `Build.MODEL` is `AFTBR92D74`, no name at all.
- The wrapper hands it to the page as `window.KrystalCinemaTV.deviceName()` (tv 1.1.0,
  `tv/README.md`), and the app reads it with no permission: on the Xiaomi the page gets
  `Fire TV de Cristina`. Still to read on an API 31 or later Google TV, where the Bluetooth name is
  behind `BLUETOOTH_CONNECT`, to confirm `DEVICE_NAME` is not.
- Built (tks-auth 4.7.0, site 0.66.0): the site sends the name as `device_name` on
  `/device_authorization`; the approval page offers it in a field the person may change; the name
  decided is the device session's, and Cinema reads it as `tks_device_name` on the TV's access tokens
  (`kgsm-docs/reference/oidc-provider.md`, `session-tokens.md`). The anchor reads it from the session at
  every refresh, so a rename reaches Cinema with the TV's next access token. Measured end to end on the
  Xiaomi: the approval page offered `Fire TV de Cristina`, and the TV's token carried it.
- A name is kept cleaned (no control characters, one space for any run of white space, trimmed) and cut
  to 64 characters a person sees; it is never refused, since a refusal would refuse the sign-in.
- A screen is renamed on the anchor's account page (tks-auth 4.8.0, `POST /account/sessions/name`), and
  the TV carries the new name from its next refresh. Measured live: a device renamed there carried the new
  name on the next token it was issued.

### Step 2: screens

Every open copy of the site joins a hub group of its own person. A `cinema-tv` connection is a screen:
the server keeps, per screen, its name, whether it is connected, the room it is in, and what that room
holds. Paired phones hear a screen arrive, leave and change room.

Questions:

- One hub carries a person's channel beside the rooms. A connection with an account joins its person's
  groups when it opens (`OnConnectedAsync`) and the rooms' groups when it joins a room, so a phone's
  reconnect is the one it already makes. A second hub would be a second connection and a second token
  refresh on every phone and TV for nothing the first cannot carry. Not measured: no reason to build a
  second connection to time it against.
- The server sees a screen gone when its connection closes. A clean close (the app closing, the page
  unloading) is seen at once. An abrupt loss (power, Wi-Fi, a sleeping TV) is seen when the server has
  heard nothing from the client for SignalR's `ClientTimeoutInterval`, 30 s; the API sets neither it nor
  `KeepAliveInterval` (15 s), and the site's client sets neither, so the defaults hold, and nginx's
  `proxy_read_timeout` on `/hub/` is an hour and never the limit. A phone can offer a dead TV for up to
  half a minute after it dies; read from the settings. Measured on the Xiaomi: the app killed is seen within a
  second, since Android closes its sockets, and the TV on Home, in a room and gone all read correctly on
  the live API.
- A television signed in again is a new anchor session, so it is a new screen by its `sid`. The old one
  stays as not connected, and is forgotten when a screen of the same name connects (a reinstalled app
  keeps the TV's name) or after seven days unseen, which sends `ScreenLeft`. Forgetting loses nothing: a
  screen that connects is known again. Cinema hears no revocation, so a session ended at the anchor is
  forgotten the same way.
- Screens are kept in memory. A restart drops them and they reappear when their televisions connect.
- The television holds a hub connection that belongs to its person and to no room whenever it is signed
  in (`web/site/src/tv/screen.js`), so it is a screen on Home, on a film's page and everywhere else; it
  never calls `Join`. The room connection the player opens is a separate connection, and the screen's
  room is the room of its newest `Join`. The other surfaces open a person connection when pairing gives
  them something to hear.
- That connection is long-lived, so a rename made at the anchor reaches the screen's name when the
  connection next reconnects, and not before.

### Step 3: pairing by QR

The TV shows a QR code in two places: a Remote entry in its Profile, which also lists the phones
paired to it with Unpair on each, and the paused player, beside its info card. The code is short-lived and rotates,
so a photo of it opens nothing later. Scanning it opens a page of the site on the phone, which signs in
if it must, pairs that phone's session with that screen for good and opens the remote, with a toast
carrying Undo. A phone of another person is refused; bringing friends in is a follow-up.

Questions:

- Pairings are kept in `moviebot.db` (`screen_pairings`), keyed by the phone's `sid` and the screen's,
  with the person, the phone's name, when it paired and when each side was last seen. The site's
  tokens carry `sid` (`kgsm-docs/reference/session-tokens.md`: every application access token but a
  token exchange's), and so does the television's; the Activity's token comes from a token exchange and
  carries none, so the Activity pairs nothing, which costs nothing, since a scanned link never opens in
  Discord. Cinema hears no revocation, but the provider bounds a session nobody renews by its refresh
  lifetime (30 days on the anchor), and a session renewed is a session used here: a phone with a
  pairing and every screen hold a hub connection while they are open. So a pairing is forgotten once
  either side has gone unseen for longer than `Oidc:SessionLifetime`, the anchor's figure, with a screen
  connected now counting as seen; a phone that signed out is forgotten that way, and so is a television
  that never connects again. A television signed in again under the name of one of the person's screens
  that is not connected ends that screen's pairings at once, as `ScreenPresence` forgets the screen
  itself: it is the same set signed in again, and is paired by scanning it again. Tying pairings to
  `ScreenPresence`'s seven days alone would end a pairing during a holiday, which the plan rules out.
- A scan opens whatever the phone's camera hands the link to. On Android an installed WebAPK registers
  intent filters for its scope (the manifest's `scope` is `/`, so `/p/` is inside it), and a link opened
  from another app opens the app; whether Android 12 and later route a camera's link to it rather than
  the browser depends on its links being approved for the app, which needs the real phone to see. On
  iOS a home-screen app never captures a link: the scan opens Safari, which holds a session of its own.
  The pairing is with whichever session scanned, and the docs say so: on iOS that is Safari, not the
  installed app. Not measured: both need a real phone.
- A code lives five minutes and the screen shows a new one every minute, so the code on screen always
  has at least four minutes left, enough for a phone that must sign in first (the page keeps the code in
  its address through the sign-in), and a photo of it pairs nothing once it has run out. Another
  person's phone is refused whatever the code's age, so the lifetime only bounds the same person's own
  stale photos. The code is signed, not stored: sixteen bytes (expiry, a four-byte tag of the screen,
  an eight-byte signature over the expiry, the screen and its person, under a key derived from
  `Auth:MediaTicketKey` with a label of its own), written in 22 characters. The renewal goes to the
  television as a span, so a television whose clock is wrong still renews in time.
- The address is `https://movies.thekrystalship.com/p/` and 22 characters, 58 bytes, which `lean-qr`
  encodes as version 4 at error correction M, 33 modules (measured). Profile draws it 200 page pixels
  across and the paused player 128, as the canvas does. On a 55-inch set (1.21 m wide) seen from 3 m by a
  camera with a 67-degree field analysed 1920 pixels wide, a page pixel is 0.6 of a camera pixel, about
  3.3 camera pixels per module in Profile and 2.1 beside the paused card. `npm run verify:tv` reads both
  codes with jsQR from screenshots scaled to that 0.6 and pairs from what it read; scaled further,
  Profile's still reads at 0.30 and the paused one at 0.45 (measured), so the paused code holds from 3 m
  on a 55-inch set with a quarter to spare and is at its limit on a 43-inch one. That is pixels alone,
  with no focus, glare or angle: the sofa needs the real phone.
- Built (3.19.0, site 0.68.0). The API issues codes to screens alone (`PairingCode`), pairs over
  `POST /api/me/screens`, unpairs from either side, lists a screen's phones and a phone's screens, and
  tells a screen's presence to its paired phones alone. The television shows the code in Profile's
  Remote with its paired phones, and beside the paused card. The phone's landing page pairs, turns
  remote mode on (`cinema:remote`), goes Home with Undo, and holds a person connection while it has
  pairings. A phone is named by its browser, as the anchor's account page names a session ("Chrome on
  Android", "Safari on iPhone"), since its session carries no name.

### Step 4: the remote in the room

A remote joins the screen's room as a participant of its own kind. It drives the room as any member
does. It is not in `ParticipantsChanged`, the room's head count, `/health`'s `watching` or anybody's
progress, and it never reports buffering or quality. When its screen moves to another room, the remote
moves with it.

Questions:

- What does the room hold that a buffering or quality report from a remote would corrupt today, and
  does refusing them on the hub cover every path?
- Every act is recorded with who did it. Does an act from a remote name the person alone, or the
  person and the phone?
- How quickly is a phone that comes out of a pocket live again: from unlock to controls that answer,
  measured on a real phone with the app backgrounded for two hours.

### Step 5: the phone's remote

The remote is a `Drawer`. While one of the person's paired screens is connected and remote mode is
on, it sits let down above the tab bar as its head: the episode's still, the screen's name, the title
and Pause. Pushed up, or tapped, it opens: the screen's name (choosing among several screens) and the
Remote switch in its head; the still with the player's Skip intro; the title; and at the bottom the
player's own scrub bar and control row (Play or Pause, back and forward ten seconds, previous and next
episode, Episodes, Subtitles and Quality). Episodes and Subtitles open the player's own drawers,
aimed at the TV. With remote mode on, a title's main button is Watch on TV, with Watch beside it.

Gestures, each a shortcut for a control on screen:

- The still answers a finger as the player's film does (`player/Gestures.jsx`): a tap in the middle
  plays or pauses, a double tap at either side skips, gathering further taps, and a sideways sweep
  scrubs, the room moving when the finger lifts. The vertical sweeps are off.
- The handle drags the drawer: down collapses the remote to its head, up opens it. Dragging belongs
  to `Drawer` itself, so every drawer on the site closes the same way. The handle is a tab inside the
  edge it drags by: its long side lies along the edge, its ends are cut at 45 degrees, and no part of it
  crosses outside the panel. It is `--border-strong` at rest and teal under a pointer or while held. The
  assistant dock's resize handle (krystal-ui `.assistant-dock__resize`) takes the same tab on the inside
  of its left edge, so the two are one handle, standing and lying. A drawer has square corners. The tab's
  hit area is the drawer's whole head. The download sheet's drawer (`.kc-dl-drawer`,
  `components/download.css`) sets its own corners and handle size, and takes the same tab and corners.
- The collapsed head swiped sideways turns remote mode off, with Undo in a toast.
- A gesture that lands on the TV gives a short vibration where the phone allows one.

Questions:

- Dragging a drawer must not start the browser's pull to refresh, and the sideways swipe must not take
  a page's vertical scroll. What `touch-action` and `overscroll-behavior` hold both on Android Chrome
  and iOS Safari?
- How far, or how fast, does a drag have to go before the drawer settles collapsed, and the head away?
- Where does a toast sit while the drawer is open, given a phone's toasts sit just above the tab bar?

### Step 6: commands to the screen alone

Some of what the remote changes is the TV's own and never the room's: subtitle language, size and
background, the audio track and quality. These go to the screen's connection alone. So does Watch on
TV when the TV is not in the player, which takes it there.

Questions:

- Which of these the TV's player can already take from outside the remote's keys, and what it
  answers when it cannot (a language the film has no subtitles in).
- Does the phone show the TV's current choices, which means the screen reports them back?

## 5. Testing

- **`npm run verify:tv`** grows a second browser context: the TV at 960×540 with the TV mark, and a
  phone context paired to it, driving one room.
- **The hub's integration tests** drive a screen and a remote as real SignalR clients, as the session
  tests already do.
- **The Xiaomi and a real phone** for what only devices show: the QR scanned from the sofa, the scan
  opening the installed app, and the remote coming back after the phone has slept.

## 6. Follow-up, not in this plan

- **Matching by network.** A phone on the same public address as a screen is offered it without a
  scan. Home Wi-Fi clients reach the site through the router's public address, so a phone at home and
  the TV look the same to the server; a phone on mobile data or a VPN does not.
- **Friends on the sofa.** A QR that brings another person's phone into the TV's room as a remote,
  driving the room but not the TV's own settings, and crediting them as watching.
- **A room's playlist.** What plays after the current film, for everybody in the room.
- **A desktop tab as a remote.** The same messages under the desktop's layout.
