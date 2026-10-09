# Krystal Cinema

MovieBot's site and its player: the library in a browser for the people the identity provider lets
watch, and the Discord Activity, which is the same player. It is served by `MovieBot.Api` from the
root of its `wwwroot` and built on `@thekrystalship/krystal-ui`, the design system the KGSM Control
Panel is built on too. The design is the Krystal Cinema project (desktop, mobile and embed pages); the
API is the authority for everything it draws.

```bash
npm install        # @thekrystalship/krystal-ui comes from GitHub Packages, which wants a token
npm run dev        # http://localhost:5175
npm run lint
npm run check      # the player's engine, types only
npm test           # the engine's pure rung logic (verify/heads.test.mjs)
npm run build      # -> dist/; scripts/build-player.sh installs it into the API's wwwroot
npm run verify     # the player in a real browser, against a running API and dev server
npm run verify:routes  # every address, cold and by its flows, against a scratch site (verify/routes.mjs)
npm run verify:mobile  # every page on iPhones and Galaxies, against a scratch site (verify/mobile/)
npm run verify:pwa     # installable, and the service worker keeps only what it may (verify/pwa.mjs)
npm run verify:tv      # every page driven by a remote's keys alone, against a scratch site (verify/tv.mjs)
npm run verify:offline # Download of a whole film and of one still being made, and the installed app with no connection and with the API down, against a scratch site (verify/saving.mjs, verify/growing.mjs, verify/offline.mjs)
```

The dev server proxies `/api`, `/media`, `/films`, `/shows`, `/popular`, `/glances`, `/catalogue` and `/hub` to the API (`MOVIEBOT_API`, default
`http://127.0.0.1:8099`) and `/assistant` to moviebot-assistant (`MOVIEBOT_ASSISTANT`, default
`http://127.0.0.1:8191`), which is what the vhost does in production.

## Pages

| Path | What it is |
|---|---|
| `/` | A film to watch, then a row each for where the person left off, their Watch later (with Watch on a film the library holds, and See all opening it), the rooms watching something now, what was recently added, and the shelf as Movies and TV Shows, each krystal-ui's scroll-snapped `Rail`. The Movies and TV Shows rows are dealt in a new order on every visit, kept while the page is open, and end in a tile that opens Discover |
| `/movies`, `/tv` | Every film, or every show (one card each, however many of its episodes are here), with krystal-ui's `Toolbar`: a search by name, one Filters popover (state and genre for films, genre for shows, and Downloaded for either where the browser downloads) with the active ones as chips, and a sort with its direction. With a genre picked, the grid ends in a Discover more tile opening Discover's catalogue of that genre. On a phone a Movies \| TV shows switch under the heading moves between the two |
| `/movies/<titleId>` | One film in the library: what it is, who played it last, save, Download, and watch or resume. An episode's id forwards to its show's page at its season |
| `/tv/<imdbId>`, `/tv/<imdbId>/season/<n>` | One show the library holds an episode of, at a season (the default season has no segment): what it is, its seasons as chips, each season's episodes with their stills and how far this person got, and resume. Every episode plays from its row, one that has to be fetched while it is prepared, and nothing on a row says which; with nothing of the show here Watch plays its first episode. A followable show has Follow beside its actions; once followed the button is a menu holding whether new episodes are downloaded automatically, and Unfollow, and the next episode's day is written beside it. Save is beside them. Each episode that can be downloaded has Download at its row's end, and each season Download season in its head. A show the library holds nothing of forwards to its `/discover/show/` address |
| `/discover/show/<imdbId>`, `/discover/show/<imdbId>/season/<n>` | The same page for a show the library holds no episode of, where its episodes are picked to download. A show the library holds an episode of forwards to its `/tv/` address |
| `/discover/film/<imdbId>` | A film that has to be fetched: what it is, and Watch (it plays while it is prepared) and Download when the tracker offers it, how far the preparing has got while it is under way (`preparing · N%`), or Notify when it is not out digitally yet, and Save. A film that plays now forwards to its `/movies/` address |
| `/discover?person=<personId>` | Discover's catalogue of one person's work: their portrait, name and what they are known for, then the films and shows they are credited on, most voted on first (`/api/catalogue/people/<personId>`), each card with the part they played or the job they did, leading to its page as any other answer does. Every cast member on a film's or show's page leads here, and so does each director, writer and creator in its credits |
| `/discover?genre=<name>&kind=<film\|show>` | Discover's catalogue of one genre: the most popular films or shows in it (`/api/catalogue/genre`), as the same cards |
| `/discover`, `/discover?q=` | Finding anything to watch, here or not. A large field and, with nothing typed, the films and shows popular this week (`/api/popular`), each leading to its page. With a query, the catalogue's films and shows together in its own order (`/api/titles/find`, or films alone for somebody who may only wish), each by its poster, rating, year and cast: the first answer large, with what it is about and the actions its own page offers, asked for as soon as it is shown (`components/FilmActions.jsx` for a film, Watch from its seasons for a show), and the rest as cards under More results, a skeleton of both while the catalogue is asked. Anything the server can play by that name that the catalogue did not answer with joins those cards. A result is a result wherever it plays from: nothing on a card says whether it is here, and one that plays now plays from the card. `/`, Ctrl+K or Cmd+K anywhere outside a text field opens it with the field focused, and a fine pointer focuses the field on arrival |
| `/saved` | Saved: the signed-in person's lists, Watch later first, then the rest most recently changed first, each its newest posters, name and size, and whose it is when it is somebody else's; and making a new one |
| `/saved/<id>` | One list (`later` is the person's own Watch later): its titles newest saved first under Today, Yesterday, This week and each month, with krystal-ui's `Toolbar` to search titles and notes, narrow by kind and watched, and sort. Each is a poster card with its rating, year and length, its note or when it was saved and by whom on a shared list, and Watched when the person watched it; an owner or editor writes the note in a sheet over the page and takes it off, from the card's tools. Its owner renames it, deletes it, and puts people on it as editors or viewers; a member leaves it |
| `/notify` | The films the signed-in person is waiting on, as poster cards marked Out now with the release once one is, each with Stop waiting; and finding a film by name in the catalogue, as `/discover`'s result cards, to wait on from its page |
| `/profile`, `/profile/<tab>` | The signed-in person; History, the first tab, has no segment: History (each film a poster card with how far the person got along its foot, when they watched it, Resume, and a way to remove it), Requests (the films they wait on, each a poster card), Following (each followed show a poster card with its next episode, whether new episodes are downloaded, switched on the card, and Unfollow), Notifications, Playback (audio language, quality, skipping intros and recaps automatically, and the quality Download picks where the browser can download, its choices under its words on a phone), Subtitles (the language a film starts in, and how subtitles look with a preview of them), Appearance (the theme) and Account (who is signed in, and a link to the account's page at the provider, where it is managed); every tab one column at the page's width; signing out |
| `/about` | What the site is, and where its film details come from |
| `/signin` | Where the identity provider sends the browser back with its code; opened on its own, it goes to the provider |
| `/r/<room>?title=<id>` | A room: the player, full screen, with a way back to the page it was opened from. A room waiting on a Watch now shows the film being found, arriving and starting, and plays it on its own |
| `/embed/` | The Discord Activity: the same player with no site around it |

A page is a path, so it can be linked to and reloaded: the API answers any path that is not its own
with this site's index. Paths are hierarchical, each beginning with the path of the page it belongs
under, and canonical: `src/paths.js` builds every address and `src/router.js` parses them. A trailing
slash, an extra segment, a season or a profile tab that does not exist, and an address the API sent or a
person holds in an earlier shape (`/title/<id>`, `/show/<id>?season=<n>`, `/film/<id>`,
`/profile?tab=<t>`) all resolve to the canonical address by replacing the entry, so Back never lands on
one. A first segment the site has no page for draws Not found and keeps the address as typed. A season
and a profile tab are part of the address and not of the page: pressing one replaces the entry and the
page stays where it is. Where the library stands decides a show's and a film's address: a film it holds
is at `/movies/`, one it does not at `/discover/film/`, a show with an episode here at `/tv/` and
one with none at `/discover/show/`, and the page forwards by replacing the entry when the address names
the other.

A page navigated to is prepared out of sight, while the one being left stays, until its first reads
are answered or half a second has passed. A film's or show's page whose description takes longer — a
title the film database is being asked about for the first time — is drawn as its frame, with
krystal-ui's `Skeleton` blocks where the words, the artwork and the cast will be, until they arrive.
A profile tab moves the same way under the tab strip, which answers at once; a list that takes longer
holds one row of placeholder cards. The scrolling column keeps its scrollbar's room at every height, so
a page growing past the screen never moves sideways.

Every page but Home carries a breadcrumb trail above it (krystal-ui's `Breadcrumbs`), read from the
address a level at a time: Home, the section, then the film, show, list, season or profile tab, each
crumb but the last a link to the address prefix it names. `src/crumbs.js` builds it from the route and
`src/nav.js` holds the section and tab names the sidebar, the footer, the tab bar and the trail share. A
film's, show's or list's name is known once its page has loaded, so the page gives it to the trail
(`useCrumb`), keyed to its own page so a name never outlives the page; until then the crumb is the id,
and a page that finds nothing names that. The trail is drawn once, from the route of the page shown, so
it never runs ahead of the page while the next is prepared. A search is not a crumb.

Save, on a film's and a show's page, puts it on the person's Watch later in one press. Once it is on
any list the button reads Saved and opens the lists the person can add to, each switched on where it
holds the title, and New list, which makes one and saves to it: a popover beside the button on a wide
screen, the drawer on a phone.

Download keeps a film or an episode on this device to play with no connection (`src/offline/`,
`components/DownloadButton.jsx`). It is an outline button beside Save on a film's page, in the
library or not (on a phone Watch spans the column and Save and Download share the row under it),
beside Watch on a film found in Discover, a square button at the end of each episode's row on a
show's page, and Download season in each season's head, for whoever holds `cinema:offline`, in a
browser with the Origin Private File System that plays through hls.js, never on a television or in
the Activity. A title whole in the library is downloaded by anybody who holds it; one still being
made, one the tracker offers, and a season take `cinema:download` too, because the server fetches
and prepares them first (`offline/wants.js`), and the device downloads each as it is made. Download
season asks for every episode of the season the library holds or the tracker offers that this
device has not downloaded or asked for (Download N more once some are), at one quality, and says
Downloading, then Downloaded, once every one is on its way or here. Its face says where the download
stands: Download, then Preparing with how much of it has arrived on the server while the server
fetches and prepares it, Queued, Downloading, Paused or Waiting with how far it has got (of a title
still being made, of the whole title), Not downloaded when it failed, and Downloaded in the accent
once it is here; an episode's button draws a ring of its progress while preparing and under way and a
check once downloaded, and its row says the same under its name (Preparing · 12%, Downloading · 34%,
Downloaded · 480p in the accent). Pressed, it opens one sheet, the drawer on a phone and a dialog with the
backdrop above it on a wide screen, headed by the title's still or poster, a small label (Download,
Preparing, Downloading, Downloaded), its name and a line under it. Before a download of a title whole
in the library it holds Quality, one choice
among the title's rungs, and Audio and Subtitles, any number, each row with its size, as krystal-ui's
`OptionList`; the person's own choices are picked (`offline/choose.js`), and the subtitles beyond
their languages and the picked audio's fold under "N more languages". Its foot is a `Meter` of the
device's space with the download's share and what is free, and Download with the download's size; a
device without room is told how much the download needs, and cannot start it. Before a download of
anything else, whose tracks are not known until the server has it, it holds Quality, 1080p, 720p or
480p, each with roughly what it takes (~1.4GB) where that can be estimated, the person's Download
quality picked, else 480p on a phone or a tablet and 720p elsewhere; Audio, a language, the title's
own first ("English (original)") and the person's audio language picked; and Subtitles, a language
or Off, the person's picked. The server prefers a release carrying the audio asked for, the device
takes the tracks in those languages where the title has them, and a subtitle language the title
lacks is fetched for it. The device's space and Download carry the estimate too, and a device the
estimate does not fit cannot start it. Preparing, the sheet shows how much has arrived on the server and what it is
doing (Finding a release, Arriving on the server, Being prepared), and Cancel; one that could not be
fetched says why, with Remove and Try again. Under way, the sheet
shows the percent and the bytes, a meter, a row each for the picture, the sound and the subtitles
with how far each has got, Cancel, and Pause or Resume; downloaded, its quality, size, audio and
subtitles, when it was downloaded, Remove and Play. Cancel and Remove ask first (Remove download?,
Keep or Remove). Safari in a tab, rather than from the Home Screen, deletes a site's downloads after
seven days unvisited, and the sheet says so there. The Library marks a downloaded film's card with a
Downloaded pill on its poster and one under way with Downloading and its percent and a bar along the
poster's foot, and a show's card with how many of its episodes are downloaded; its Filters popover
narrows to what is downloaded. Save is the lists' word, and Download is this one's.

A download moves only while the site is open: a phone slows a hidden page and stops it before long,
and with the screen locked it stops sooner. While the page is hidden a notification says what is
downloading and how far it has got (Downloading · 42% · 2 more, or Preparing · 12% while the
server prepares it), and one says Downloaded when a download finishes then; tapped, they open the
library. They need the browser's leave to notify, which the first Download on a device asks for
(`src/offline/notice.js`).

Wide screens get around by the sidebar, which has Discover and Saved under Home. A phone has
krystal-ui's `TabBar` along its bottom edge instead, and no drawer: Home (lit on `/`),
Library (lit under `/movies` and `/tv`), Discover (lit under `/discover`), Assistant,
which opens the dock above the bar rather than a page, and Me (`/profile`, lit on `/notify` and
`/saved` too), whose page leads to Saved. What each page lights is one table in `App.jsx`, by the route's
view. The tab of the page already open scrolls it back to its top. The
pages no tab names are linked from the footer every page ends with. A television has neither and
gets around by its own bar (below).

## Signing in

Krystal Cinema signs nobody in. It is a public OpenID Connect client (`src/session.js`, on
`oidc-client-ts`) of the provider `/api/config` names, with the client id it names. A page opened with
nothing held sends the browser there at once, authorization code with PKCE, and the person signs in on
the provider's own pages; the provider sends the browser back to `<origin>/signin` with a code redeemed
for an access token and a refresh token, and the page that was asked for opens. The access token is the
bearer on every call to the API, the hub and the assistant. It lives five minutes and is renewed by the
refresh grant a minute before it runs out and whenever a call is refused; the session is kept in this
browser's `localStorage` under `cinema.oidc.`, so a tab opened days later carries on without a bounce.

A session that ends while a page is open is not followed to the provider: the page says so and offers
the way back, because leaving unasked would discard whatever somebody was in the middle of. A sign-in
the provider refuses, and a provider that does not answer, are shown the same way.

Signing out is the provider's: its end-session endpoint ends the person's sign-in there and every
session minted under it, and sends the browser back to `<origin>/`, which holds nothing and goes to the
provider's sign-in. The account's page at the provider lists its sessions, and Profile links to it.
Somebody with films saved on the device is first asked whether to keep them for the account's next
sign-in here or remove them.

With no connection a session is never ended: one whose access token has run out stands as the stored
user's `sub`, name and actions until the connection is back, when the refresh grant runs and its answer
decides. The page never goes to the provider while offline, and what `/api/config` said is kept in
`localStorage` (`cinema.config`) so the page still knows whose session it holds. A device that holds no
session says that a first sign-in needs a connection, and signs in once there is one.

The token lists the Cinema actions the person holds, in the claim `/api/config` names, and the page
shows a control only to somebody holding the action it needs (`can` and `useCan` in `src/session.js`):
Download and Watch on a film that is not here need `cinema:download`, Notify `cinema:wish`. An
approved account without `cinema:watch` sees one page saying it has no access to
Krystal Cinema, with Sign out and the account's page. The API checks every action again.

A television signs in with the device authorization grant (RFC 8628) as the device client
`/api/config` names (`tvClientId`), and never goes to the provider. With nothing held it shows a code
(`src/tv/TvSignIn.jsx`): the address to enter it at, the code itself, a QR code of the address with the
code in it, drawn by `lean-qr`, which only a television fetches, and the time left on it. It polls the
token endpoint at the interval it was given, five seconds longer on each `slow_down`, and a code that
runs out is replaced unasked. A poll that fails on the network keeps the code and keeps polling, and is
shown only if it is still failing when the code runs out; a provider that cannot be reached for a code
is shown with Retry and asked again on a lengthening wait up to a minute; a refusal is shown with New
code; a provider that will not sign this client in that way is shown and not asked again. The tokens
that come back are stored as `oidc-client-ts`'s own user under the device client, so renewal and
restoring are the same as anywhere. The session lasts as long as the provider keeps it from its last
renewal. One that ends while a page is open puts the code over the page, which stays drawn beneath it
out of reach and is where approval returns; a room is left rather than kept under it, and joined again
after approval at the room's position. Signing out on a television revokes the refresh token at the
provider's revocation endpoint (RFC 7009) and forgets the session, since it holds no provider session
to end.

The Activity signs in through the API instead, since it has no browser to redirect: the code the
Embedded App SDK gives it is posted to `/api/auth/discord/callback`, which answers Discord's access
token for the SDK and the provider's token for the person, and the same call with the Discord token is
how it renews.

## Watching

Watching opens `/r/<room>?title=<id>`: a room of its own for whoever opens it, or the room somebody is
already watching in. The room is the address, so the link is what Invite hands to somebody else, and a
voice channel's room is open on the site at `/r/<channel id>`. `title` is put on once, by whoever
arrives while the room is showing nothing; somebody joining a room already holding it says nothing,
because loading it again would send everyone back to the beginning.

Discord launches the Activity at the root of its URL mapping. A load carrying Discord's launch
parameters is the Activity's, so the index rewrites its address to `/embed/` before the page reads it,
and the player signs the viewer in through the Embedded App SDK and the API rather than the site's
session.

A film saved whole on the device plays from it. Alone, at `/r/<room>?title=<id>&offline=1`, it is
the room's player on a timeline of its own, with no hub, no ticket and no request to the server for
the film, so it plays with no connection; where it got to is written down on the device and sent to
the API when there is a connection. In a room, the hub keeps the timeline and the device answers for
the bytes, and if the room is lost the film carries on alone from where the room was.

How the player behaves, and why: `src/player/CLAUDE.md`.

### How the timeline holds together

`src/player/engine/session/sync.ts` is the whole of it.

- **Revisions gate everything.** A push whose revision is not greater than the last applied is
  discarded, which makes reordered and duplicated pushes harmless.
- **Position is derived, never read.** `paused ? positionSeconds : positionSeconds + (serverNow -
  anchorUtc) * rate`, against a clock offset learned from round trips to `ServerTime`; the sample with
  the shortest round trip wins.
- **Echoes are recognised, not counted.** Applying remote state raises the same events that publish
  intent, and an event that tells the room what the room just said is dropped by comparing against
  what was applied.
- **Seeks are never clamped here.** The scrub bar asks for what was let go on and does not move the
  playhead; the server grants a position and answers `SeekClamped` to that viewer alone.
- **A viewer that falls behind fixes itself.** Every two seconds the client compares where the room is
  against its playhead: over two seconds of drift it seeks, between half a second and two it trims
  `playbackRate` to 1.02 or 0.98 until inside a quarter second. Nothing about a stalled viewer moves
  the room; the room is only told the viewer is waiting.

### A viewer who cannot play the film

The player asks `MediaSource.isTypeSupported` for what the ingest writes — H.264 High at level 4.2 and
stereo AAC-LC — before fetching anything. Discord's desktop client decodes H.264 only on the graphics
card, so with hardware acceleration off it answers no, and the player says so, naming the setting. A
fatal media error is recovered from twice, the second time swapping the audio codec, and then the
player stops and says why for as long as that holds: recovering without a limit resets the element
about ten times a second and fetches the film's opening each time.

### Verifying

`npm run verify` drives a real browser against a running API and this dev server: two and three
viewers in a room through play, pause, seek and the waiting mark, the join gate, the menus, a person's
preferences reaching a fresh player, each frame size, and a film still transcoding (its manifest is
edited in place and put back). It needs Chrome or Chromium **with H.264 and AAC** (`CHROME_PATH`, or
Playwright's own download), the API (`MOVIEBOT_API`, `MOVIEBOT_SERVICE_KEY`, `MOVIEBOT_MEDIA` for its
media root) and a film (`MOVIEBOT_TITLE`) with more than one audio track and a subtitle track. A jsdom
mount has no media pipeline, so it agrees with itself while the film never decodes a frame; a real
browser is the only thing that catches what matters here.

`npm run verify:mobile` looks at every page and every tapped-open state on a set of iPhones (WebKit)
and Galaxies (Chromium), against a scratch site with a library and a person of its own, and leaves a
screenshot per view per phone, a contact sheet per view and a report of what reads as broken on a
phone. How to run it, what it measures and how to add a page or a phone: `verify/mobile/README.md`.

`npm run verify:tv` opens every page in TV mode at a Fire TV's 960×540 against the same scratch site,
and walks it with the arrow keys from where focus starts: every control on it has to be reached, each
on screen when it is, and focus never left on nothing. Then an overlay taking focus when OK opens it
and handing it back on Back (Save's lists and the bell's), Back going back a page to the control it
was left on, Back on Home going to the bar and then being the app's, and
the player: play focused, OK playing, presses gathered into one skip, slower presses skipping from the
scrub bar, a held key scrubbing and seeking once, Menu, Play/Pause and Back. The player's checks need
video in the scratch library, which `moviebot/media/` holds only for the films given their segments
(`coherence-2013` by default, `MOVIEBOT_VERIFY_TV_FILM` for another). Last, signing in: the scratch
site's test provider (`verify/issuer.mjs`) serves the device grant and revocation the way tks-auth
does, with doors of its own where the check approves or refuses for the person, shortens a code or a
token, and stops answering; the check sees the code and its QR, approval opening the page asked for,
the session renewing on its own and surviving a restart, signing out revoking it, a refusal, an expired
code, a provider that cannot be reached, and a session that ends under a page. `--checks` and
`--pages` narrow a run.

`npm run verify:offline` runs `verify/saving.mjs` and then `verify/offline.mjs`. The first saves a
title through its page's picker, in a signed-in Chromium reaching the scratch API through a proxy of
its own that holds each track to a megabyte a second: the picker's rungs and defaults, the page closed
mid-save and the next page carrying the save on by `Range` and `If-Range`, Pause holding across a
reload and Resume finishing it, every track on the device byte for byte its playlist's files joined,
the API told the save is complete, a device without room refused before anything is fetched, and a
save the API no longer lists removed when the connection comes back. It needs a title in the scratch
library whose playlists name only files it has (`MOVIEBOT_VERIFY_SAVE_TITLE`, by default the first);
a library linked from a checkout's `media/`, which holds a film's first segments only, has none, and
the check says so. The second installs the worker in a signed-in Chromium against the same scratch site,
writes downloads into the Origin Private File System in the store's layout (a film, an episode of a
show the API describes and a download under way, with the library's own artwork and placeholder
tracks) and where the film was got to, loads once with a connection so the site keeps the show, cuts
the network and opens the app cold in a second tab: nothing saying it is offline; Home's hero,
Continue watching and rows holding only what is downloaded; the film's page and its Watch address;
the Library listing and searching only downloads; the show's page drawn whole from the kept show with
the downloaded episode playable and the rest dimmed; Discover and every page the device cannot draw
out of reach; Profile on Playback with the server's tabs disabled; the sidebar, the bell and a phone's
tab bar; an expired session standing, a device with no session asking for a connection, the
connection coming back, and the connection lost on a film's page. The walk repeats with the network up
and the API refused, then answered by a gateway's `502`, and checks that a `500` from the API is not
offline. Each state is screenshotted under `<dir>/runs/offline-<time>/`, a phone's Home, Library,
show and Discover among them.

`npm run verify:device` saves a film through the API as the saver does (the package, each track whole
from `track.mp4`, the subtitle and the artwork) into a signed-in Chromium's Origin Private File
System on the same scratch site, cuts the network, and plays it from the device: it starts with the
person's kept subtitle language, plays, seeks by key and by the bar, pauses and writes down where it
was, resumes there when opened again, reaches its end, and asks the network for nothing; with the
connection back, both positions reach the API. Then a room of two, one viewer streaming and one
playing from the device: they stay together, the device's viewer asks the server for none of the film
and reports the saved rung, and with the connection cut it carries on alone from where the room was,
while the viewer with nothing saved is told the room cannot be reached. It needs a Chromium with H.264
and AAC and a ready film every one of whose segments is on disk, which is what the API offers to save
(`MOVIEBOT_VERIFY_SAVED`, by default the first such film, `clip` first), and screenshots each state
under `<dir>/runs/device-<time>/`.

`npm run verify:routes` cold-loads every address in a fresh signed-in browser and checks where it lands and
what it draws (the heading, the sidebar item and tab lit, Not found), including every earlier shape of an
address and every non-canonical spelling; then the flows between addresses: a season chip and a profile
tab write the address in place and survive a reload, a search keeps its query across Back, a forward does
not bounce Back, and a signed-out deep link returns after sign-in. It runs against the scratch site
`verify/mobile/scratch.sh` serves, whose library holds films and no show, so a show the library holds an
episode of is the one case it does not reach.

## Installing

The site installs as an app, Krystal Cinema, from Android Chrome's "Install app" and from Safari's
"Add to Home Screen", and opens standalone.

- **`public/manifest.webmanifest`** is what Chrome reads: name, standalone display, the canvas colour
  and a 192, a 512 and a maskable icon, without which it offers no install. It names no
  `orientation`, because any value there holds every page of the installed app to it against the
  phone's rotation lock. The player turns the screen sideways itself, for as long as it is open
  (`src/player/CLAUDE.md`).
- **`index.html`** carries what Safari reads instead: the home-screen icons and title, standalone
  display, the black status bar the page starts below, and a launch screen per iPhone and iPad size.
- **The artwork** under `public/icons/` and `public/splash/` is written by `scripts/make-icons.mjs`
  (needs `rsvg-convert`; no build runs it): the Movies entry's clapperboard on the accent's tile. Its
  launch-screen list and the `index.html` tags mirror each other.
- **`public/sw.js`** is the service worker. It holds the whole build: `vite.config.js` writes every
  file the build put in `dist/` into it (the index as `/`; source maps and the launch screens left
  out) with a version hashed from their contents, and a worker installs only once every one of them
  is in its cache, the player's lazy chunk, the fonts and the icons among them, so the app opened with
  no connection never meets a chunk it has not seen. A deploy is a new version, which drops the last
  build's cache once it activates. It allowlists: page loads, network first with this build's index
  as the shell when the network fails or a gateway answers in the site's place, and the build's own
  files, cache first. Anything else, the API, a film's media and the hub among it, goes to the network
  untouched, so a cached answer can never outlive a session or hand one person's data to another, and
  a saved film is never in its cache. It is registered by `src/registerSW.js` in a production build
  only, and never in the Activity, which runs inside Discord at Discord's origin. It also shows the
  API's pushes: the sentence as the API wrote it, opening the page it names in a window of the site
  already open, or a new one.

## Without a connection

The installed app opens and gets around with no connection, and plays what is downloaded to the
device. Nothing says it is offline: the pages are the same pages, showing only what the device holds,
with what needs the server dimmed and out of reach.

- **Knowing it is offline** (`src/connectivity.js`): no network at all (`navigator.onLine`), a call to
  the API that fails on the network, one answered by a gateway (a `502`, `503` or `504` that is not the
  API's JSON) or by a captive portal (an HTML page where the API answers JSON or nothing), or a probe
  of `/api/config` unanswered in five seconds. A `500` from the API is the server answering, and is
  not offline. While offline the probe repeats from two seconds, doubling up to a minute, and at once
  on the browser's `online` event or the page coming back into view; anything the server answers is
  online again.
- **What is drawn.** A page opened with no connection is the same page drawn from the device, from
  finished downloads only (`offline/hooks.js` puts them in the API's shapes). Home is its header, a
  hero of the film downloaded last ("Downloaded yesterday"), Continue watching with the downloaded
  titles this person is part way through, and a Movies and a TV Shows row of what is downloaded, each
  leading to the library; a row that would list what is not on the device is not drawn. The library's
  Movies and TV Shows list what is downloaded, a show once however many of its episodes are, with
  search, filters and sort working on them. A downloaded film's page is its page, and Watch plays it
  from the device. A show's page is drawn whole from the show kept on the device beside its episodes
  (`offline/shows.js`): every season and episode with its name, number and still; a downloaded episode
  plays from the device and the rest are dimmed and cannot be played. Profile opens on Playback, which
  with Subtitles shows the preferences kept on the device and changes nothing; Appearance works, and
  Account signs out. About is drawn as ever.
- **What needs the server** is drawn dimmed and does nothing: Discover, Saved and Notify me in the
  sidebar, Discover and Assistant in the tab bar, the bell, Profile's History, Requests, Following and
  Notifications, Save, Follow, Notify, Discover's search, the assistant's button, and every other
  control that asks the server. Discover, the assistant's dock and every page the device cannot draw
  (a film not downloaded, Saved, a list, Notify me, a room), reached by an address, Back or a push,
  say "You're offline" and that the place is not available offline, and lead to the library.
- **What is kept for it.** Beside the downloads themselves, the device keeps an account's preferences
  (`offline/preferences.js`), where the person got to (`offline/progress.js`: the last
  `GET /api/me/progress` answer and the device's own log) and, for each show with a downloaded episode,
  the show as `GET /api/shows/{imdbId}` describes it with its artwork. No other answer of the API's is
  kept.
- **Losing the connection on a page** leaves the page as it is, and disables what asks the server on
  it, as above; Watch on a film downloaded here plays it from the device. In a room the player says
  the room cannot be reached, and when the film is downloaded to the device offers to carry on alone
  from it, from where the room was.
- **Coming back** renews the session, draws again from the server every page
  that was drawn from the device, sends where films played from the device got to, and sets the
  device's saves beside the API's records (`offline/sync.js`): removals made offline are forgotten
  there, a save the API no longer lists or an account that no longer holds `cinema:watch` is removed
  from the device, every save under way that is not paused carries on, and every show a downloaded
  episode belongs to is kept.
- **The device's store** is `src/offline/`, whose `CLAUDE.md` is the contract between the saver, the
  pages and the player: the layout in the Origin Private File System, `meta.json`, the read API, and
  the address a saved film plays at (`?offline=1` on a room's).

## On a television

The site runs on an Android TV or a Fire TV inside the app in `tv/` (`tv/README.md`), which shows it
in a WebView, driven by the remote. `src/tv/` is all of it that is the site's own.

- **Knowing it is a television** (`src/tv/tv.js`): the wrapper's mark, `KrystalCinemaTV`, in the
  user agent, or `?tv=1` on any address, which holds for the rest of the tab (`?tv=0` lets go of it)
  so the mode can be worked on in a desktop browser. Media queries cannot say it: the reference Fire
  TV's WebView matches `(pointer: coarse)` and `(hover: hover)` alike. The root element carries
  `data-tv`, which krystal-ui's television parts (`styles/tv.css`) and every TV rule hang from.
- **The television's own pages** (`src/tv/TvFrame.jsx`, `src/tv/pages/`), drawn to the TV design at
  the WebView's 960 by 540 page, on krystal-ui's `TvBar`, `TvBackdrop`, `TvSpotlight`, `TvRow`,
  `TvCard` and `TvTile`. No sidebar, footer or breadcrumb: a bar across the top holds Home, Discover,
  Movies, TV Shows and Saved, with the bell and the person at its right, on those five pages; a
  film's, a show's and the profile's pages have none, and Back leaves them. The bar moves away once
  a page scrolls or its rows hold focus, and comes back with focus. Home, Movies, TV Shows and a
  show's episodes put a spotlight at the top of the screen on the card focused below
  (`src/tv/spot.jsx`): its artwork behind the page, its name, facts and story, turning to a new card
  once focus has rested on it for 300 ms so a held arrow loads nothing; the focused row holds a line
  on the screen and the rows above it are put away. A row ends in the tile that leads to all of it.
  Focus lifts what it is on: a card grows, rises and takes a hairline and the accent's light while
  the cards around it dim, and a button or a place in the bar turns into a light pill. Nothing says
  whether the server holds a title: a film is watched the same way either way. Profile keeps its
  sections down the left and the open one on the right, About among them at its own address. The player is the
  site's own, with its menus as a panel down the right and its subtitles sized for the distance.
- **Moving focus** (`src/tv/navigator.js`): every arrow key is taken and focus moves to the nearest
  control that way on screen, from the focused element's rectangle, with the rules the module
  states: an open dialog or menu holds focus and hands it back when it closes, a row remembers its
  last card and scrolls to put the focused one first, a navigation landmark is entered at the current
  page, a shown page focuses its first control (or the one it was left on, when it is come back to),
  and focus is never left on nothing. A page column marked `data-tv-line` puts the focused row on
  that line; anything in a `data-tv-top` takes the column to its top; anything else is centred only
  when it nears an edge. A card's own controls are reached with Menu and left with Back. The
  WebView's own arrow navigation is never let through: on these pages it skips whole regions.
- **The remote's keys, as the page receives them** from the reference Fire TV: OK is `Enter`, the
  arrows `ArrowUp`, `ArrowDown`, `ArrowLeft`, `ArrowRight`, and the media keys `MediaPlayPause`,
  `MediaPlay`, `MediaPause`, `MediaRewind` and `MediaFastForward`. A held key repeats from about
  400 ms after the press, every 50 ms, with every repeat's `repeat` false. **Back and Menu never reach
  a page**: the wrapper calls `window.krystalTv.back()` and `window.krystalTv.menu()`, and each answers
  whether the page used the key; the wrapper closes the app only on a Back the page did not use. In a
  desktop browser in TV mode, Escape stands for Back and the context-menu key for Menu.
- **Back** closes an open overlay first, then whatever a screen holds of its own (the player's), then
  goes back a page. On a page with the bar it first moves focus up into the bar; on Home, from the
  bar, it is the app's, and so it is on the sign-in code.
- **Signing in** is a code approved on a phone: "Signing in" above.
- **Typing** is Fire OS's keyboard, which OK on a field raises, with its own QR code for typing on a
  phone. A submitted search moves focus off the field, which closes the keyboard over the results.
- **No link leaves the site on a television**: a TV's browser, where it has one (Silk on Fire TV), is
  driven by an on-screen cursor rather than the remote's focus, and the app refuses any link off the
  site: Profile's account shows the provider's address as text, to open on a phone, and About's
  TMDB logo is not a link.
- **Left out on a television**: the assistant, pushes (and Profile's Notifications with them),
  installing the app, making and changing lists, and in the player the volume (the set's own buttons
  hold it), full screen (the screen already is) and the invite (nobody across the room is handed a
  link to copy). Nothing is blurred behind anything, for the WebView's frame budget.

How the player answers the remote: `src/player/CLAUDE.md`.

## Notifications

The bell lists what the API told this person (`src/notifications.js`, read every minute and whenever
the page comes back into view): in the sidebar above the person on a wide screen, at the top right of
the front page on a phone, and in the bar on a television. Profile's Notifications tab turns pushes on for this browser, through
krystal-ui's `pushBrowser` against `/api/push/key` and `/api/me/push`, and holds a switch
for each kind that is pushed, new episodes of followed shows among them. A browser can be pushed to only once it has a service worker, which is a
production build; on an iPhone, only once the site is on the Home Screen, and the tab says so. The
player's invite panel lists everybody who can be asked into the room and says, per person, whether
the invite went out as a push or waits in their bell, never that it was seen.

Installing needs HTTPS, which localhost is exempt from. `npm run verify:pwa [origin]` asks a real
Chromium whether the site is installable, decodes every icon and launch screen at the size it is
named for, drives the API, media and hub routes through the controlled page to show none of them was
kept, and checks that every file of the build the worker lists is in its cache and nothing else is;
it defaults to the scratch site and takes a deployed origin as readily.

## The assistant

The dock holds krystal-ui's `Chat`, talking at `/assistant` to the plugin of the assistant harness as
the signed-in person, over the assistant wire contract (`tks-assistant/docs/wire-contract.md`). Replies
stream; the microphone sends a voice note to the plugin's `/speech/transcribe` and the transcript returns
to the composer. A `question` frame is drawn as a form of choices and the answers go as the next turn,
which is how the taste survey runs. A `titles` result is drawn under the reply that names it, one card
for each title the reply names: poster, name, year, and *In library* and *Watched* where they hold,
leading to the title's page. The dock holds no room controls and no buttons for what a title's page does.

## Rules

- **The design is the UI and the API is the authority for its data.** Nothing is drawn from a
  fixture: what the design shows, the API serves, and a row the API has nothing for is left out rather
  than filled in.
- **Everything is fetched from this origin.** Film details and their artwork come from TMDB through
  the API, which keeps them and serves its own copies (`/films/`, `/shows/`, `/popular/`, `/glances/`,
  `/catalogue/`); no page reaches TMDB, IMDb or
  any other outside host itself.
- **TMDB is credited.** Its terms are free personal use with attribution: `/about` carries its logo
  and notice ("This product uses the TMDB API but is not endorsed or certified by TMDB.") and says
  what comes from it. The logo is TMDB's own, from its
  attribution page, in `src/assets/tmdb.svg`.
- **One module attaches the token.** `src/api.js` is the only place a bearer is added to a call to
  the API; the assistant's client and the player are given a way to ask for a good token, never a
  token to keep. A 401 renews the session once and makes the call again, and a second 401 signs the
  browser out.
- **Every act is the person's own.** The API takes who asked from the token, so the site sends no
  name of its own and cannot act for anybody else.
