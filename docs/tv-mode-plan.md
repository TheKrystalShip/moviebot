# TV mode for Krystal Cinema

A plan, not canon. What it settles moves into the docs beside the code it governs
(`web/site/README.md`, `web/site/src/player/CLAUDE.md`, the wrapper's own README under `tv/`) when
it lands, and its section here is struck. When the last step lands, this file is deleted.

Each step below is taken through a deep dive before it is built: the questions listed under it are
answered, by measurement where they can be, and the answers are written into the step before code is
written.

## 1. What this is for

Krystal Cinema works on an Android TV or Fire TV: driven entirely by the remote, readable from across
a room, signed in without typing a password, and playing films in the shared room like any other
viewer. Apple TV is out of scope.

The site runs inside a thin Android app, the wrapper, which shows it in a WebView. A TV's own browser
is not a target: Silk, the Fire TV browser, turns the D-pad into an on-screen cursor and never hands
the page the keys.

The reference device is the user's Xiaomi TV running Fire TV, reached over the LAN with `adb`
(`~/Android/Sdk/platform-tools/adb connect 192.168.1.23:5555`, already authorised for hotrod):

| | Measured |
|---|---|
| Model | Amazon `AFTBR92D74` (`ginzaeu`), Fire OS 8.1.8.2, Android 11 (API 30), leanback only |
| WebView | `com.amazon.webview.chromium` 148 |
| Screen | 3840×2160 panel; a page sees 960×540 at a pixel ratio of 4 |
| What a page is told | `(pointer: coarse)` and `(hover: hover)` both match, `(any-pointer: fine)` matches, no `navigator.mediaSession`, `document.fullscreenEnabled` false, the user agent says `Mobile Safari` and `wv` |
| Frame rate | The WebView paces `requestAnimationFrame` at about 50 a second on any page, a blank one included, on a 60 Hz display |
| Hardware | MediaTek `merak`, four Cortex-A55 cores, 32-bit userland; 1.8 GB of memory with about 500 MB available |
| Decoding | `c2.mtk.avc.decoder` in hardware for H.264 |
| Screensaver | On, after five minutes |

## 2. Decisions

| Decision | Outcome |
|---|---|
| Chromecast | Rejected. The Web Sender works only in Chrome on desktop and Android, and keeping the room's shared timeline needs a custom receiver |
| How the TV runs the site | A WebView wrapper app around the site, not a TV browser and not a Trusted Web Activity (Fire TV has no Chrome to host one) |
| Spatial navigation | Our own, in the site, reading geometry from the DOM. Norigin is rejected because it has to wrap every focusable element in its hooks, which ties krystal-ui to it |
| Sign-in | RFC 8628, served by tks-auth 4.5.0 (`tks-auth/src/Auth.Anchor/CLAUDE.md`, "The device grant"): a code and a QR on the TV, approved on a phone |
| One person per TV | A TV is signed in as one person. Changing person is signing out and in again; there is no profile picker |
| Typing on the TV | The system keyboard, which OK on a focused field in the WebView raises. Fire OS's keyboard carries its own QR code for typing from a phone and takes voice from the remote's microphone, at no cost to us |
| The TV's layout | A design of its own, made in the Krystal Cinema design project beside the desktop and phone ones, the way the phone has its tab bar instead of the sidebar. The desktop layout at 960×540 is not the base |
| The phone channel | Out of scope; see section 6 |
| Where the wrapper lives | `moviebot/tv/`, its own version clock, tagged `tv-v<version>`. That prefix keeps it out of `release.yml`, which fires on `v*` |
| Building the wrapper | On hotrod: a JDK and Android's command-line tools under the user's home, no Android Studio. Installed on the TV with `adb` over the LAN once developer options are on |

## 3. What stands

These exist and are not restated here:

| Piece | Authority |
|---|---|
| The device grant: `/device_authorization`, polling `/token`, the `/device` approval page, device sessions surviving the phone's sign-out | `tks-auth/src/Auth.Anchor/CLAUDE.md` |
| The player's seam between front doors, `createSiteEnvironment` and the Discord one | `web/site/src/player/engine/environment.ts`, `web/site/src/player/CLAUDE.md` |
| Every control moving the whole room, and the minimal key bindings that follow from it | `web/site/src/player/engine/media/shortcuts.ts`, `web/site/src/player/CLAUDE.md` |
| Taps gathered into one seek | `web/site/src/player/Gestures.jsx` |
| The site's session: `oidc-client-ts`, tokens in `localStorage` under `cinema.oidc.`, `restore`, `freshToken`, `signOut` | `web/site/src/session.js`, `web/site/README.md` "Signing in" |
| The API accepting any token whose audience is the Cinema application, whatever client asked for it | `src/MovieBot.Api/Auth/CinemaAuthentication.cs` |
| Media: HLS fMP4, H.264 High at level 4.2 with stereo AAC-LC, WebVTT drawn by the page, `MediaSource.isTypeSupported` asked before anything is fetched | `web/site/README.md` "A viewer who cannot play the film" |

What does not exist: anything that knows it is on a TV, any focus management beyond the browser's
Tab order, a focus style beyond the browser's own on most controls (the site's CSS has 36 `:hover`
rules against 6 `:focus` ones), a client registered for the TV, and the wrapper.

## 4. Steps

In order. Step 1 does not depend on the TV design: the navigator works from whatever is on screen, so
it is built against the current pages and keeps working when step 2 replaces their layout. Steps 1
and 2 are built and checked in a desktop browser with `?tv=1` and the `verify:tv` harness, and on the
Xiaomi through the probe app (section 5).

### Step 1: knowing it is a TV, moving focus, showing focus, the remote's keys

The core of the work, starting with Home → a film → Play and the player, then every other page.

What the deep dive measured on the Xiaomi, with the site's production build from the scratch site in
the probe app:

- **The keys reaching a page**, from the physical remote. OK arrives as `Enter` with code
  `NumpadEnter` (keyCode 13); the arrows as `ArrowUp`, `ArrowDown`, `ArrowLeft`, `ArrowRight`;
  Play/Pause as `MediaPlayPause` (keyCode 179); Rewind and Fast-forward as `MediaRewind` and
  `MediaFastForward`. Separate play and pause keys, which this remote lacks, arrive as `MediaPlay` and
  `MediaPause`. **Back and Menu never reach the page**: Back reaches the activity's `onBackPressed`
  as the key is released, Menu reaches the activity with the WebView declining it. While Fire OS's
  keyboard is up it takes Back to close itself, and OK on a text field raises the keyboard without
  the page seeing a key.
- **A held key** repeats from about 400 ms after the press, every 50 ms, and every repeat reaches the
  page with `repeat` false. A hold is known by the cadence: a keydown within about 100 ms of the
  last one for the same key is a repeat, which no person pressing again can produce.
- **The WebView moves focus by itself on an arrow the page does not take**, and on the site that
  movement is unusable: from the first focus, Down skips the whole sidebar to Notifications, Right
  lands on the assistant's button and stays there, and the featured film's Watch and every row are
  never reached. The navigator takes every arrow in TV mode (`preventDefault`).
- **Nothing is focused when a page opens**, so OK does nothing until something is: a room opens with
  the film loaded and paused and neither OK nor Play/Pause starts it.
- **The focus ring is krystal-ui's universal `:focus-visible` box-shadow**, 3px at 35% opacity: barely
  visible on the Watch button even in a screenshot.
- **The TV says it is a phone.** `(pointer: coarse)` matches, which is what `Player.jsx` and
  `environment.ts` take to mean a phone; `(hover: hover)` matches too, so krystal-ui's
  `ShowcaseCard` hides its tools until hover or focus inside the card. Detection cannot rest on media
  queries.
- **A native `<select>` works as it is**: OK opens the system's own list, driven by the remote.
- **A text field works as it is**: OK raises Fire OS's keyboard and what is typed reaches the field as
  it is typed. Its action key reads Next and submits the form, after which the keyboard stays up over
  the results, because focus stays in the field.
- **The player plays**: 1080p H.264 through hls.js on MSE, no frames dropped over 20 seconds, the
  controls hiding on their own; the existing shortcuts already answer the remote's arrows (two presses
  of Right moved the room 10 seconds) and Space.
- **Out-of-sight pages are `aria-hidden`.** `PageStage` mounts the incoming page with
  `aria-hidden="true"` and `is-incoming` until it is promoted, and calls `onShown` when it is.
- **Overlays already close on Escape** (krystal-ui's `Modal`, `Dialog`, the toolbar's popovers, the
  account menu), and mark themselves with `role="dialog"` or `role="menu"`; none puts focus back on
  what opened it.

What step 1 builds, from those:

- **Detection.** TV mode is on when the wrapper's marker is in the user agent, or when the address
  carries `?tv=1` (kept for the session, for development). It sets an attribute on the root element
  that every TV style hangs from, and the player gets a `tv` environment beside `site` and the
  Discord one, which is never a phone: no landscape, no full screen, no gestures. Desktop and phone
  are untouched.
- **The navigator**, in the site. It takes every arrow key in TV mode. On one it gathers what is
  focusable and visible, leaving out anything under `aria-hidden` or `inert`, a scroll container
  focusable only to be scrolled (`Rail`'s track), and a control nested inside a focusable card; scores
  each candidate in the arrow's direction by distance and alignment from the focused element's
  rectangle; and moves focus there. Rules on top of the geometry:
  - the topmost open `role="dialog"`, `role="menu"` or `aria-modal` element holds focus inside it;
    when it closes, focus goes back to what was focused when it opened;
  - a row of posters remembers the card last focused, so leaving the row and coming back lands on it;
  - Left from the content's left edge enters the sidebar at the item lit for the page, not at the
    geometrically nearest one;
  - the focused element is scrolled into a steady place: a row sideways so the focused card keeps its
    position, the page's column (`.app__main`) so the focused row sits at the same height;
  - when `PageStage` shows a page, focus goes to its first useful element, and Back to a page puts
    focus back where it was when that page was left;
  - a submitted search moves focus off its field to the first result, which closes the keyboard.
- **The highlight.** In TV mode the focused element carries a ring that reads from across a room
  (opaque, thick, with a gap that separates it from a filled button of the same colour) and a card is
  raised a little. It belongs to krystal-ui, so its components draw it from the root attribute rather
  than the site overriding the package's class names (bump, publish, re-pin). How it finally looks is
  the TV design's; step 1 makes it visible.
- **Back and Menu as page events.** The page exposes one entry the wrapper calls for each, which
  answers whether the page used the key; the wrapper closes the app only on a Back the page did not
  use. In a desktop browser in TV mode, Escape stands for Back and the context-menu key for Menu.
- **The keys.**

  | Key | Browsing | In the player |
  |---|---|---|
  | Arrows | Move focus | A press of Left or Right skips 10 seconds, presses gathered into one seek as `Gestures.jsx` gathers taps; holding scrubs with the thumbnail strip and sends one seek on release, as the scrub bar does; Up and Down reveal the controls |
  | OK | Activate | The first press reveals the controls; with them shown it activates the focused control, the play button first |
  | Back | Close the open overlay; otherwise go back a page; on Home move focus to the sidebar, and from there leave the app | Close a menu, hide the controls, otherwise leave the room |
  | Play/Pause, Play, Pause | Nothing | Act directly |
  | Rewind, Fast-forward | Nothing | Skip as Left and Right do |
  | Menu | Into the focused card's tools | Audio and subtitles |

Done when: with `?tv=1` and the keyboard alone, every page's every control is reachable and usable,
focus is never lost to the body, and a film plays, pauses, skips, changes audio and subtitles, and
leaves.

### Step 2: the TV design, and building it

The TV gets its own design in the Krystal Cinema design project, beside the desktop and phone ones,
and the site draws it in TV mode the way it draws the phone's tab bar on a phone. The design is made
first and the build follows it.

What the design has to answer for, from what the Xiaomi showed:

- A 960×540 page seen from a sofa: the desktop layout there spends a quarter of the width on the
  sidebar, puts the first row of films below the fold under the featured film, and draws controls
  sized for a mouse.
- A way around without the sidebar as it is: what replaces it, and how focus enters and leaves it.
- The focused element as the one thing the eye follows: the highlight, and how a row moves under it.
- Every page that stays in TV mode, the player's controls among them, with no volume control (the
  TV's buttons own the volume) and subtitles sized for the distance.
- Left out of TV mode: the assistant dock and its button, push notifications, installing the app,
  copying the invite link, editing lists. Each is a phone's job.
- A margin for TVs that crop their edges, if the Xiaomi crops.

What the build has to keep, measured:

- The WebView paces frames at about 50 a second on any page. Scrolling a row of the current site
  holds that with its worst frame at 20 ms; scrolling the page and the idle Home page have frames of
  50 to 100 ms, which the build profiles and removes.
- Not drawn in TV mode, for cost: `backdrop-filter` (seven places across `site.css` and `player.css`),
  the blurred artwork behind a search hit, the page fade animations, `assistant-icon-blink`.
- Artwork is already sized for the screen (posters 500 to 600 wide, backdrops 1280, drawn at up to
  390 CSS pixels); poster backgrounds drawn through CSS load eagerly.
- At 960 wide the current Profile overflows sideways: its tab strip runs off the right edge and the
  column grows a horizontal scrollbar.

Done when: every page reads from a sofa and scrolls without stutter on the Xiaomi.

### Step 3: signing the TV in

- Register the TV's client: `tks-auth app client add <cinema application> --client-id cinema-tv
  --device`. `/api/config` names it beside the site's client id: `AuthOptions` gains it, declared
  with its default in `src/moviebot.settings.json`, and `packaging/` and `docs/deploying.md` name it,
  since a release carries it.
- On a TV with no session, the site shows the sign-in screen instead of going to the provider: the
  user code large, `verification_uri` as text, a QR code of `verification_uri_complete`, and the
  remaining time. It polls `/token` at the interval it was given, adding five seconds on `slow_down`,
  and starts over with a new code on `expired_token`.
- Approval hands the tokens to `session.js` as its session, so `freshToken`, the refresh grant and
  every caller work unchanged. `oidc-client-ts` has no device grant, so the request and the polling
  are the site's own.
- Signing out on the TV forgets the session here and revokes its refresh token.

Deep dive:

- How `session.js` takes a session it did not create: building an `oidc-client-ts` `User` and storing
  it, or a TV session store behind the same functions.
- Which QR library, its size in the bundle, and whether it is loaded only in TV mode.
- How long a device session lasts unused, under tks-auth's refresh window, and what the TV shows when
  it has run out.
- What the screen shows when the provider cannot be reached, and when the person refuses.

Done when: a fresh TV signs in by scanning the QR with a phone, survives the phone signing out and the
app being restarted, and renews on its own.

### Step 4: the wrapper app

`moviebot/tv/`: one Kotlin activity holding a full-screen WebView on `https://movies.thekrystalship.com`.

- Leanback launcher entry and banner, `android.software.leanback` not required, no touchscreen
  required, so it installs and launches on Android TV and Fire TV alike.
- The marker appended to the WebView's user agent.
- Back forwarded to the page; the page answers whether it handled it, and the app closes only when it
  did not.
- Media playback allowed without a gesture inside the app, and the screen kept on while a film plays,
  through a small bridge the player calls.
- Signed with a key kept outside the repository; built by a script on hotrod; installed with `adb`.

Deep dive:

- The JDK and the Android command-line tools on hotrod: which packages, where they live, and that
  nothing of them lands in a repository.
- The Fire OS version on the Xiaomi and the WebView version it carries, which decides what CSS and
  JavaScript the site can rely on there.
- The bridge between the page and the app: what it exposes, and that only the site's own origin can
  reach it.
- What the app does with a link out of the site (the provider's pages, TMDB's credit).
- How the friend's TV gets it: the Downloader app and an address to fetch it from.

Done when: the app installs from hotrod onto the Xiaomi, launches from the Fire TV home screen, and
steps 1 to 3 hold there.

## 5. Testing

- **`npm run verify:tv`**, beside `verify:mobile`: a TV-sized Chromium with `?tv=1`, driven by key
  presses alone. On every page it walks the navigator from the first focus through every reachable
  element and fails on a control it cannot reach, on focus falling to the body, and on an overlay it
  cannot leave. Screenshots per page with the focus drawn, like the mobile harness.
- **The Xiaomi** for what only a device shows: the keys the remote really sends, the viewport, the
  frame rate, decoding, the screensaver, and the QR scanned by a real phone.

Until the wrapper exists, the Xiaomi is reached through a probe app kept outside any repository, in
`~/Android/tvprobe` beside the toolchain (`~/Android/jdk`, a JDK 21, and `~/Android/Sdk` with
`platform-tools`, `build-tools;35.0.0` and `platforms;android-35`). It is one Java activity holding a
full-screen WebView with remote debugging on and the screen kept awake, logging every key it sees and
every console line under the logcat tag `tvprobe`; it opens the address in its `url` extra.

```bash
web/site/verify/mobile/scratch.sh                        # the scratch site, with MOVIEBOT_VERIFY_TMDB_TOKEN_FILE
~/Android/tvprobe/build.sh [url]                         # build, install, open; reverses 8299, 8300, 8765; devtools on :9333
node ~/Android/tvprobe/drive.mjs stage                   # stage the scratch site (keeps the staged rooms open while it runs)
node ~/Android/tvprobe/drive.mjs plant http://127.0.0.1:9333   # sign the TV in as the staged viewer
node ~/Android/tvprobe/drive.mjs walk http://127.0.0.1:9333 DPAD_DOWN DPAD_RIGHT   # press keys, log focus
~/Android/Sdk/platform-tools/adb exec-out screencap -p > shot.png                   # what the TV shows
```

The scratch library is `moviebot/media/`, which holds every film's manifest, playlists, artwork and
subtitles copied from hotbox without their segments, so pages draw and nothing plays; a film needed
playing has its first segments copied into `~/.cache/moviebot-verify/library/` itself.

## 6. Follow-up, not in this plan

**The phone channel.** Every open copy of the site joins the API's hub under its person, and a TV
announces itself as a screen. A phone signed in as the same person then gets Play on TV for a title,
a keyboard for a focused field on the TV (sending the field's whole text rather than keystrokes), and
joining the TV's room as a remote that plays no video. The room's QR on the TV lets people on the
sofa join the same way. Only the same person's sessions see each other, checked by the server on every
message.
