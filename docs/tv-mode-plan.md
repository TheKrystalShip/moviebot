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
| Screen | 3840×2160 panel, 1920×1080 drawn at density 320, so a page sees about 960×540 at a pixel ratio of 2 |
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
| Typing on the TV | The system keyboard, which a focused field in the WebView already raises. The Google TV and Fire TV phone apps can type into it from a phone at no cost to us |
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

In order. Steps 1 and 2 are built and checked in a desktop browser with `?tv=1` and the `verify:tv`
harness before any TV is involved; step 4 is when the Xiaomi becomes the check.

### Step 1: knowing it is a TV, moving focus, showing focus, the remote's keys

The core of the work, starting with Home → a film → Play and the player, then every other page.

- **Detection.** TV mode is on when the wrapper's marker is in the user agent, or when the address
  carries `?tv=1` (kept for the session, for development). It sets an attribute on the root element
  that every TV style hangs from, and the player gets a `tv` environment beside `site` and the
  Discord one. Desktop and phone layouts are untouched.
- **The navigator.** On an arrow key it gathers what is focusable and visible, scores each candidate
  in the arrow's direction by distance and alignment from the focused element's rectangle, and moves
  focus there. Rules on top of the geometry:
  - an open popover, sheet, drawer or dialog holds focus inside it until it closes, and closing it
    puts focus back on what opened it;
  - a row of posters (krystal-ui's `Rail`) remembers the card last focused, so leaving the row and
    coming back lands on it;
  - the focused element is scrolled into a steady place: a row scrolls sideways so the focused card
    keeps its position, the page scrolls so the focused row sits at the same height;
  - a page that loads puts focus on its first useful element, never on nothing.
- **The highlight.** In TV mode the focused element carries a thick accent ring, a slight scale and a
  shadow. The treatment belongs to krystal-ui, so its components draw it themselves from the root
  attribute rather than the site overriding the package's class names (bump, publish, re-pin).
  Everything revealed only on hover is reachable from focus, or from the Menu key.
- **The keys.**

  | Key | Browsing | In the player |
  |---|---|---|
  | Arrows | Move focus | Left and right skip, presses gathered into one seek as `Gestures.jsx` does; up and down reveal the controls |
  | OK | Activate | The first press reveals the controls; with them shown it plays or pauses |
  | Back | Close the open overlay, otherwise go back a page; on Home it leaves the app | Hide the controls, otherwise leave the room |
  | Play/Pause, Rewind, Fast-forward | Nothing | Act directly |
  | Menu | The focused card's actions | Audio and subtitles |

Deep dive:

- Which DOM key values the Fire TV WebView delivers for OK, Back, Menu and the media keys, and which
  never reach the page (Back is the wrapper's to forward; Home and the microphone are the system's).
- Whether the navigator's scoring handles the site's real layouts: the featured film above the rows,
  the Library grid with its toolbar, a show's season chips above its episode list, the profile's tabs.
- What happens to focus across a page change, a profile tab, a season chip and Back, given that the
  site prepares a page out of sight before swapping it in.
- Which krystal-ui components need focus behaviour of their own (`Rail`, `Toolbar`, the Filters
  popover, `Select`, the drawer, `Breadcrumbs`), and which hover-only affordances exist in the site
  and in the package.
- Holding an arrow in the player: a growing step, or the same step repeated.

Done when: with `?tv=1` and the keyboard alone, every page's every control is reachable and usable,
focus is never lost to the body, and a film plays, pauses, skips, changes audio and subtitles, and
leaves.

### Step 2: the ten-foot layout and the cost of drawing it

- A margin around the whole page for TVs that crop their edges.
- Type and targets sized for distance: nothing under about 20px at the viewport the TV reports.
- The sidebar becomes a strip of icons that opens when focus enters it.
- No scrollbars.
- Not drawn in TV mode, for cost on cheap hardware: `backdrop-filter` (seven places across
  `site.css` and `player.css`), the blurred artwork behind a search hit, the page fade animations.
- Poster and still images loaded lazily and at the size they are drawn.
- Left out of TV mode: the assistant dock, push notifications, installing the app, copying the
  invite link, editing lists. Each is a phone's job.
- In the player: no volume control (the TV's buttons own the volume), larger default subtitles.

Deep dive:

- The viewport and `devicePixelRatio` the Fire TV WebView reports (960×540 at 2 is common, and is
  measured on the Xiaomi rather than assumed), and whether the desktop layout is the right base at
  that size.
- Whether the TV crops at all, and by how much.
- Frame rate on the Xiaomi while a row scrolls and while the player's controls are over a playing film.
- The default subtitle size at that distance, and whether the person's own setting overrides it.

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

## 6. Follow-up, not in this plan

**The phone channel.** Every open copy of the site joins the API's hub under its person, and a TV
announces itself as a screen. A phone signed in as the same person then gets Play on TV for a title,
a keyboard for a focused field on the TV (sending the field's whole text rather than keystrokes), and
joining the TV's room as a remote that plays no video. The room's QR on the TV lets people on the
sofa join the same way. Only the same person's sessions see each other, checked by the server on every
message.
