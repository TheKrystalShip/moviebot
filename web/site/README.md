# Krystal Cinema

MovieBot's site and its player: the library in a browser for the people the identity provider lets
watch, and the Discord Activity, which is the same site. It is served by `MovieBot.Api` from the
root of its `wwwroot` and built on `@thekrystalship/krystal`, the design system the KGSM Control
Panel is built on too. The design is the Krystal Cinema project (desktop, mobile and embed pages); the
API is the authority for everything it draws.

```bash
npm install        # @thekrystalship/krystal comes from GitHub Packages, which wants a token
npm run dev        # http://localhost:5175
npm run lint       # ESLint, with the design system's rules as warnings
npm run lint:css   # Stylelint, with the design system's rules as errors
npm run check      # the player's engine, types only
npm test           # the engine's pure rung logic (verify/heads.test.mjs)
npm run build      # -> dist/; scripts/build-player.sh installs it into the API's wwwroot
npm run verify     # the player in a real browser, against a running API and dev server
npm run verify:routes  # every address, cold and by its flows, against a scratch site (verify/routes.mjs)
npm run verify:mobile  # every page on iPhones and Galaxies, against a scratch site (verify/mobile/)
npm run verify:pull    # a drawer pulled by its head with real touch events, against a scratch site (verify/mobile/pull.mjs)
npm run verify:dock    # the phone's remote dock grown and shrunk by a finger, against a scratch site (verify/mobile/dock.mjs)
npm run verify:pwa     # installable, and the service worker keeps only what it may (verify/pwa.mjs)
npm run verify:tv      # every page driven by a remote's keys alone, against a scratch site (verify/tv.mjs)
npm run verify:tv-perf # how a remote's presses feel, timed at a television's pace against a baseline (verify/tvperf/)
npm run verify:stage   # nothing of a page being prepared out of sight is seen or reachable, against a scratch site (verify/stage.mjs)
npm run verify:tv-sweep # every key press on the reference television, timed and traced, for finding stutters (verify/tvsweep/)
npm run verify:offline # Download of a whole film and of one still being made, and the installed app with no connection and with the API down, against a scratch site (verify/saving.mjs, verify/growing.mjs, verify/offline.mjs)
```

The dev server proxies `/api`, `/media`, `/films`, `/shows`, `/popular`, `/glances`, `/catalogue` and `/hub` to the API (`MOVIEBOT_API`, default
`http://127.0.0.1:8099`) and `/assistant` to moviebot-assistant (`MOVIEBOT_ASSISTANT`, default
`http://127.0.0.1:8191`), which is what the vhost does in production.

## Pages

| Path | What it is |
|---|---|
| `/` | The top (`components/Spotlight.jsx`, its slides chosen in `src/heroes.js`), turning through what is worth watching now: the film people are watching together (Join the room), the newest ready film on the shelf, the title most watched here in the last 30 days (`popular-here`), the film and the show the film database counts most popular this week, and the film most waited on here (`/api/anticipated`), or else the next one out; each is left out when there is none, a title is shown once, and they are chosen once and kept while the page is open. Each slide is the title's artwork, why it is here, its facts, story and maker, and what its own page offers (Watch, Watch or Notify for a film that has to be fetched, Save for a show) with More info. The slides turn every 8 seconds on krystal's `useCarousel`, the shown mark under them filling over the slide's time; they hold still under a mouse, under keyboard focus and on a hidden page, never turn by themselves under reduced motion, turn by a swipe, and lie in one place so the top keeps the height of its tallest and the artwork fades from one to the next. A slide is laid out as soon as it is described and turned to once its artwork has arrived, so the top has its height before it is shown, and the first slide is one of the page's first reads. Then a row each for where the person left off, their Watch later (with Watch on a film the library holds, and See all opening it), the rooms watching something now, what was recently added, and the shelf as Movies, TV Shows and Anime, each krystal's scroll-snapped `Rail`. Anime is one row of its films and shows together, and Movies and TV Shows hold the rest. The three are dealt in a new order on every visit, kept while the page is open, and Movies and TV Shows end in a tile that opens Discover |
| `/movies`, `/tv` | Every film, or every show (one card each, however many of its episodes are here), with krystal's `Toolbar`: a search by name, one Filters popover (state and genre for films, genre for shows, and Downloaded for either on a phone or a tablet that downloads) with the active ones as chips, and a sort with its direction. With a genre picked, the grid ends in a Discover more tile opening Discover's catalogue of that genre. A title the API shelves as anime is on neither (Categories in `docs/api-contract.md`). On a phone a Movies \| TV shows \| Anime switch under the heading moves between the three |
| `/anime`, `/anime/films` | Anime: its series, or its films, as the same page as `/tv` and `/movies` with the titles the API shelves as anime. A Series \| Films switch under the heading moves between the two on every screen, and a genre's Discover more tile opens that genre's anime |
| `/movies/<titleId>`, `/anime/films/<titleId>` | One film in the library: what it is, who played it last, save, Download, and watch or resume, under the shelf the API puts it on. An episode's id forwards to its show's page at its season |
| `/tv/<imdbId>`, `/anime/<imdbId>`, each with `/season/<n>` | One show the library holds an episode of, under the shelf the API puts it on, at a season (the default season has no segment): what it is, its seasons as chips, each season's episodes with their stills and how far this person got, and resume. Every episode plays from its row, one that has to be fetched while it is prepared, and nothing on a row says which; with nothing of the show here Watch plays its first episode. A followable show has Follow beside its actions; once followed the button is a menu holding whether new episodes are downloaded automatically, and Unfollow, and the next episode's day is written beside it. Save is beside them. Each episode that can be downloaded has Download at its row's end, and each season Download season in its head. A show the library holds nothing of forwards to its `/discover/show/` address |
| `/discover/show/<imdbId>`, `/discover/show/<imdbId>/season/<n>` | The same page for a show the library holds no episode of, where its episodes are picked to download. A show the library holds an episode of forwards to its `/tv/` address |
| `/discover/film/<imdbId>` | A film that has to be fetched: what it is, and Watch (it plays while it is prepared) and Download when the tracker offers it, how far the preparing has got while it is under way (`preparing · N%`), or Notify when it is not out digitally yet, and Save. A film that plays now forwards to its `/movies/` address |
| `/discover?person=<personId>` | Discover's catalogue of one person's work: their portrait, name and what they are known for, then the films and shows they are credited on, most voted on first (`/api/catalogue/people/<personId>`), each card with the part they played or the job they did, leading to its page as any other answer does. Every cast member on a film's or show's page leads here, and so does each director, writer and creator in its credits |
| `/discover?genre=<name>&kind=<film\|show>`, with `&category=anime` | Discover's catalogue of one genre: the most popular films or shows in it (`/api/catalogue/genre`), or its anime alone, as the same cards |
| `/discover?browse=<id>` | One browse category: its name and how many shelves, Surprise me (from its shelves), and its shelves as rails, each asked for as it nears the screen; Franchises draws its series as a grid of `CollectionTile` strips instead |
| `/discover?shelf=<id>` | Every title on one shelf, up to 40, under its category's name, with krystal's `Toolbar` as on the library's pages: one Filters popover (Watched, which the device keeps, the kind on a shelf of both, the decade, the least rating) with the active ones as chips, a sort (the shelf's own order, rating, release date) with its direction, the count, and Surprise me among what is shown |
| `/discover?filter=<film\|show>`, with `&g=`, `&decade=`, `&length=`, `&rating=`, `&lang=` and `&sort=` | Discover's filter, with the same `Toolbar`: one Filters popover holding the kind, a genre (`g`, since `genre` is a genre's own page), a decade by its first year, a film's length (`short` under 90 minutes, `standard` 90 to 150, `long` over), the least rating, an original language from the ones `/api/discover` offers, and Watched; a sort by popularity, rating or release date. Every choice replaces the address. The matching titles (`/api/catalogue/discover`, 20 of them) as cards, with the count and Surprise me. Any rating still asks for 100 votes, so a title nobody has seen is not listed |
| `/discover`, `/discover?q=` | Finding anything to watch, here or not. A large field with Filters beside it (the filter's popover; picking anything opens `/discover?filter=` at that choice) and, with nothing typed, a spotlight drawn and turning as Home's top is, on the films from the season's shelf or the week's trending films (`spotlight` on `/api/discover`), the first four the person has not watched with a different one leading each day: each one's artwork, facts and story, Watch (or Notify) as its own page offers it, More info, and Surprise me, which opens a title picked at random from a shelf picked at random, never one the person watched. Then the page's rows (`/api/discover`), each a krystal `Rail`: a shelf's first 20 posters with See all, asked for (`/api/discover/shelves/<id>`) only once it nears the screen and left out when it has nothing on it or could not be answered; the moods as `CollectionTile` fans and the franchises as strips, which follow the person through each series (the films ahead dimmed, a segment per film, the next named); and the browse categories as a grid of fans, three across and two on a phone. With a query, the catalogue's films and shows together in its own order (`/api/titles/find`, or films alone for somebody who may only wish), each by its poster, rating, year and cast: the first answer large, with what it is about and the actions its own page offers, asked for as soon as it is shown (`components/FilmActions.jsx` for a film, Watch from its seasons for a show), and the rest as cards under More results, a skeleton of both while the catalogue is asked. Anything the server can play by that name that the catalogue did not answer with joins those cards. A result is a result wherever it plays from: nothing on a card says whether it is here, and one that plays now plays from the card. `/`, Ctrl+K or Cmd+K anywhere outside a text field opens it with the field focused, and a fine pointer focuses the field on arrival |
| `/saved` | Saved: the signed-in person's lists, Watch later first, then the rest most recently changed first, each its newest posters, name and size, and whose it is when it is somebody else's; and making a new one |
| `/saved/<id>` | One list (`later` is the person's own Watch later): its titles, with krystal's `Toolbar` to search titles and notes, narrow by kind and watched, and sort, by when each was saved (newest first unless the person picked otherwise, under Today, Yesterday, This week and each month), name, year, rating or length. Each is a poster card with its rating, year and a film's length, its note or when it was saved and by whom on a shared list, and Watched when the person watched it; an owner or editor writes the note in a sheet over the page and takes it off, from the card's tools. Its owner renames it, deletes it, and puts people on it as editors or viewers; a member leaves it |
| `/notify` | The films the signed-in person is waiting on, as poster cards marked Out now with the release once one is, each with Stop waiting; and finding a film by name in the catalogue, as `/discover`'s result cards, to wait on from its page |
| `/profile`, `/profile/<tab>` | The signed-in person; History, the first tab, has no segment: History (each film a poster card with how far the person got along its foot, when they watched it, Resume, and a way to remove it), Requests (the films they wait on, each a poster card), Following (each followed show a poster card with its next episode, whether new episodes are downloaded, switched on the card, and Unfollow), Notifications, Playback (audio language, quality, skipping intros and recaps automatically, and the quality Download picks on a phone or a tablet that can download, its choices under its words on a phone), Subtitles (the language a film starts in, and how subtitles look with a preview of them), Appearance (the theme) and Account (who is signed in, and a link to the account's page at the provider, where it is managed); every tab one column at the page's width; signing out |
| `/about` | What the site is, and where its film details come from |
| `/chat` | The assistant, as the page a phone's Assistant tab opens; a wide screen has it in the dock beside every other page (below) |
| `/signed-in` | Where the identity provider sends the browser back with its code: the sign-in is completed and the page that was asked for opens; opened on its own it goes Home. `/signin` opens Home |
| `/r/<room>?title=<id>` | A room: the player, full screen, with a way back to the page it was opened from. A room waiting on a Watch now shows the film being found, arriving and starting, and plays it on its own |
| `/embed/` | Where the Discord Activity opens: it signs in through Discord, then goes to the player when the call's room holds something or is private to the viewer, and to Home otherwise |
| `/g/<code>` | A room's guest link: the film it holds and a name to watch it as, then the player with nothing that moves the room and no way back to the site. Nobody signs in |
| `/p/<code>` | A television's pairing code, opened by scanning the QR code it shows: signed in first when the phone holds no session, it pairs this browser's session with the television at once, with nothing to confirm, turns remote mode on, and goes Home with a toast saying so that carries Undo; a code that pairs nothing goes Home with the reason. On a television and in the Activity it goes Home |

Every page that sorts opens in the order the person last picked on it, wherever they picked it: the
site, the Activity and the television read the same orders, which the API keeps per account
(`/api/me/sorts`) and `src/sorts.js` names the pages for. Every list shares one order, a list on a
television included; Movies and TV Shows have one each, which their anime pages share; every Discover
shelf shares one; and the filter takes its own when the address names no order, an address's `sort`
winning over it. A page opens in the order last kept on the device and takes the API's when it
answers, unless the person picked one meanwhile; with no connection a pick orders the page and is not
kept.

A page is a path, so it can be linked to and reloaded: the API answers any path that is not its own
with this site's index. Paths are hierarchical, each beginning with the path of the page it belongs
under, and canonical: `src/paths.js` builds every address and `src/router.js` parses them. A trailing
slash, an extra segment, a season or a profile tab that does not exist, and an address the API sent or a
person holds in an earlier shape (`/title/<id>`, `/show/<id>?season=<n>`, `/film/<id>`,
`/profile?tab=<t>`) all resolve to the canonical address by replacing the entry, so Back never lands on
one. A first segment the site has no page for draws Not found and keeps the address as typed. A season
and a profile tab are part of the address and not of the page: pressing one replaces the entry and the
page stays where it is. Where the library stands and the shelf the API puts a title on decide a show's and a film's address: a
film it holds is at `/movies/`, or `/anime/films/` for anime, one it does not at `/discover/film/`, a
show with an episode here at `/tv/`, or `/anime/` for anime, and one with none at `/discover/show/`, and
the page forwards by replacing the entry when the address names another. A link that knows the title's
category builds its address; one that does not lands on the general shelf's and is forwarded.

Opening the site shows the splash (`src/splash.js`): the mark on the canvas, drawn by `index.html`
with the document's first bytes. It is the drawing of every screen a device shows before the site runs
(the iOS launch images, the colours the manifest gives Android's launch screen, the television app's
WebView), so it takes over from them unseen. It stays while the session is restored and the first page
is prepared beneath it, until that page's first reads are answered and the artwork on its first
screen has decoded, then fades. A screen that is ready as soon as it is drawn, a sign-in code or a
player, lets it go at once, and it is gone three seconds after the document started whatever is still
coming.

A page navigated to is prepared out of sight, while the one being left stays, until its first reads
are answered and the artwork on its first screen has decoded, or half a second has passed. A film's or show's page whose description takes longer — a
title the film database is being asked about for the first time — is drawn as its frame, with
krystal's `Skeleton` blocks where the words, the artwork and the cast will be, until they arrive.
A profile tab moves the same way under the tab strip, which answers at once; a list that takes longer
holds one row of placeholder cards. The scrolling column keeps its scrollbar's room at every height, so
a page growing past the screen never moves sideways.

Every page but Home carries a breadcrumb trail above it (krystal's `Breadcrumbs`), read from the
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

Every poster card says on its poster, above the name, what it holds (`kindLine` in
`src/components/Film.jsx`): Movie, or Series in the accent, each Anime movie or Anime series for a title
the API shelves as anime. A show's card in the library draws a short bar per season it holds along the
poster's foot, each filled by the share of its episodes the person watched through (`seasonProgress`
on `GET /api/shows`), and gives its seasons and episodes where a film's gives its year and length.

Every poster card of a film or a show, in the library, in Discover and in the Anticipated row, carries
the same save as a ribbon hanging from its top edge (krystal's `ShowcaseCard` `ribbon`). A press on
a title not saved puts it on Watch later; on one saved, the ribbon is filled and always shown, and
opens the same lists beside it. Not saved, it shows on the card a mouse is over or that holds focus,
and always on a touch screen. What is saved is one set for every card (`src/saved.js`): the ids
`GET /api/me/saved` answers, read once, again whenever the page comes back into view, the connection
comes back or somebody else signs in, and changed at once by every save made here, the ribbon's and the
button's alike. A card draws no ribbon until the set is known, and a page being prepared out of sight
waits for it.

Download keeps a film or an episode on this device to play with no connection (`src/offline/`,
`components/DownloadButton.jsx`). It is an outline button beside Save on a film's page, in the
library or not (on a phone Watch spans the column and Save and Download share the row under it),
beside Watch on a film found in Discover, a square button at the end of each episode's row on a
show's page, and Download season in each season's head, for whoever holds `cinema:offline`, on a
phone or a tablet (a finger as the main pointer) in a browser with the Origin Private File System
that plays through hls.js, never on a computer, a television or in the Activity. A title whole in the library is downloaded by anybody who holds it; one still being
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
among the title's rungs, and Audio and Subtitles, any number, each row with its size, as krystal's
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
krystal's `TabBar` along its bottom edge instead, and no drawer: Home (lit on `/`),
Library (lit under `/movies`, `/tv` and `/anime`), Discover (lit under `/discover`), Assistant
(`/chat`), and Me (`/profile`, lit on `/notify` and `/saved` too), whose page leads to Saved. What each page lights is one table in `App.jsx`, by the route's
view. The tab of the page already open scrolls it back to its top. The
pages no tab names are linked from the footer every page ends with. A television has neither and
gets around by its own bar (below).

Every menu, list and sheet a phone opens from the bottom edge is krystal's `Drawer`: the player's
menus draw it `contained` in the frame, the remote's picker and the download sheet draw it over the page
(the download sheet with a `head` of its own), and each is pulled down by its head to close. A wide
screen draws the same content in krystal's `Popover`, `Menu` or `Dialog`.

## Signing in

Krystal Cinema signs nobody in. It is a public client of the provider `/api/config` names, with the client
id it names, and `@thekrystalship/auth-client` keeps the session (`src/session.js` is the site's glue
over it: where to sign in, the Cinema actions, the shape the pages read). A page opened with nothing held
sends the browser there at once, authorization code with PKCE, and the person signs in on the provider's
own pages; the provider sends the browser back to `<origin>/signed-in` with a code the client redeems, and
the page that was asked for opens. The access token is the bearer on every call to the API, the hub and
the assistant (`src/api.js` calls through the session's `authorizedFetch`). It lives five minutes and
is renewed ahead of expiry and replayed when a call is refused; the session is kept in this browser's
`localStorage` under `tks-auth-client:<client id>:`, so a tab opened days later carries on without a
bounce.

Signing in is invisible while it can recover. Opened offline, with the provider unreachable, or while a
renewal is being retried, a session stands as it was stored, and the page draws nothing about it. The
only state a page draws is a session that has ended and nothing can bring back: it takes the place of
the page, with one action, Sign in, which comes back to the page that was asked for. It is not followed
to the provider unasked, because leaving would discard whatever somebody was in the middle of. A
sign-in callback that cannot be completed goes Home and signs in again once; a second failure within a
minute ends the session. A provider that does not answer a first sign-in is shown the same way.

Signing out is the provider's: its end-session endpoint ends the person's sign-in there and every
session minted under it, and sends the browser back to `<origin>/`, which holds nothing and goes to the
provider's sign-in. The account's page at the provider lists its sessions, and Profile links to it.
Somebody with films saved on the device is first asked whether to keep them for the account's next
sign-in here or remove them.

What `/api/config` said is kept in `localStorage` (`cinema.config`) so a page opened with no connection
still knows whose session it holds. A device that holds no session says that a first sign-in needs a
connection, and signs in once there is one.

The token lists the Cinema actions the person holds, in the claim `/api/config` names, and the page
shows a control only to somebody holding the action it needs (`can` and `useCan` in `src/session.js`):
Download and Watch on a film that is not here need `cinema:download`, Notify `cinema:wish`. An
approved account without `cinema:watch` sees one page saying it has no access to
Krystal Cinema, with Sign out and the account's page. The API checks every action again.

A television signs in with the device authorization grant (RFC 8628) as the device client
`/api/config` names (`tvClientId`), and never goes to the provider. It asks for its code with the name the
TV's owner gave it (`device_name`, from the wrapper's `window.KrystalCinemaTV.deviceName()`), which the
provider's approval page offers to keep or change. With nothing held it shows a code
(`src/tv/TvSignIn.jsx`): the address to enter it at, the code itself, a QR code of the address with the
code in it, drawn by `lean-qr`, which only a television fetches, and the time left on it. The client
polls for the person's decision, and a code that runs out is replaced unasked. A provider that cannot be
reached for a code is shown with Retry and asked again on a lengthening wait up to a minute; a refusal is
shown with New code; a provider that will not sign this client in that way is shown and not asked
again. The session is stored and renewed as anywhere, and lasts as long as the provider keeps it from
its last renewal. One that ends while a page is open puts the code over the page, which stays drawn
beneath it out of reach and is where approval returns; a room is left rather than kept under it, and
joined again after approval at the room's position. Signing out on a television revokes the refresh
token at the provider and forgets the session, since it holds no provider session to end.

The Activity signs in through the API instead, since it has no browser to redirect: the code the
Embedded App SDK gives it is posted to `/api/auth/discord/callback`, which answers Discord's access
token for the SDK and the provider's token for the person, and the same call with the Discord token is
how it renews (`src/discord/activity.js`). The provider's token is held as the site's session, kept in
memory alone, so every page and the assistant use it as they use one signed in in a browser. The
session is the Discord account in the Activity, so nothing offers Sign out there, and one that ends
says to start the Activity again.

## Watching

Watching opens `/r/<room>?title=<id>`: a room of its own for whoever opens it, or the room somebody is
already watching in. The room is the address, so the link is what Invite hands to somebody else, and a
voice channel's room is open on the site at `/r/<channel id>`. `title` is put on once, on arrival, in
place of whatever else the room holds, from `at` when the address carries it; somebody joining a room
already holding it says nothing, because loading it again would send everyone back to the beginning.

Discord launches the Activity at the root of its URL mapping. A load carrying Discord's launch
parameters is the Activity's, so the index rewrites its address to `/embed/` before the page reads it.
The launch is kept in the frame's sessionStorage (`src/discord/launch.js`), so the frame stays the
Activity at every address of the site, a reload among them; Discord's proxy forwards every path to this
origin. The Activity is the whole site, with three differences:

- **Its one room is the call's**, the channel the Activity runs in, which is the room the bot loads a
  film into for that channel. Every Watch, Resume and Watch now plays there, in place of whatever the
  call was watching, and the player there has the site's way back. Rooms elsewhere are not offered.
- **The call moving on takes everybody with it.** While somebody browses, the call's room is read every
  few seconds (`src/components/CallBar.jsx`) and a bar above every page names what the call is
  watching, with Watch. The room being given something else opens the player for everybody browsing;
  what it held when they started browsing does not, so somebody who left the film to look around stays
  where they are until the call moves on. A room is open from the moment it is made, so everybody in
  the call is in it; a call whose room somebody closed answers nothing, and its door is met on
  pressing Watch.
- **Nothing is kept on the device.** Download, playing from the device and the service worker are not
  there, and a link off the site is opened by Discord, outside the Activity.

Somebody without an account watches on a room's guest link, which a person in the room holding
`cinema:guests` hands out from the invite panel's Guests row, alongside how many guests are in and
until when the link lasts, and Stop guest access, which sends them away. The link opens `/g/<code>`,
which answers to no site session: it asks the API what the link opens (`/api/guest/{code}`), asks for
a name, and the press that enters is the gesture the browser wants before playing. The player then has
the guest's own environment (`engine/guestEnvironment.ts`): the link is the credential, how they watch
is this browser's, and everything that moves the room is neither drawn nor answered
(`src/player/CLAUDE.md`). Why a guest is not watching (the link ended, nobody with an account is in
the room, the room is full) is said in place of the film.

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

`npm run verify` drives a real browser against a running API and this dev server: a guest on a room's
link, with no sign-in, held to watching through the page, the keys and the media element's own play
and pause, and sent away when the link is; two and three
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
on screen when it is, and focus never left on nothing. Then Saved and a list with nothing on
them, for people made for the purpose: the empty state's Discover focused, and Up and Back reaching the
bar from it. Then an overlay taking focus when OK opens it
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
`--pages` narrow a run. The site has to be staged first (`node verify/mobile/run.mjs` does it once after
`scratch.sh` starts, and saving a title needs `scratch.sh` started with a TMDB token); on a site not
staged the run stops before checking anything, since Saved is then an empty page.

`npm run verify:tv-perf` times what a remote does, the way the reference television draws it: the
walks in `verify/tvperf/walks.mjs` from pages loaded cold, each key measured for how long it goes
unanswered, how long the screen stands still and how many times the page rearranges before it
settles. The lab runs Chromium against the same scratch site at the television's page, pixel ratio
and pace, and fails on a step past `verify/tvperf/baseline.json`; a release passes it. With
`--target tv` the same walks run on the television itself over adb. How to run it, what each measure
is and how the lab is fitted to the television: `verify/tvperf/README.md`.

`npm run verify:tv-sweep` times every key press of a walk on the reference television itself, over
adb, against the production site or (`--scratch`) the scratch site through `adb reverse`: how long
focus takes to move, how many frames miss the panel's budget, and with `--trace` where the main thread,
the GPU and image decoding spent the time. It is the workbench for finding why the television
stutters, with ablations that switch one thing off to measure what it costs; `verify:tv-perf` stays the
gate. How to run and read it: `verify/tvsweep/README.md`. The work in progress and its baselines:
`docs/tv-performance-plan.md`.

`npm run verify:stage` moves between Home and Discover on a desktop and on a phone, holding back one
read the page navigated to waits on so it sits out of sight with the rest of it drawn, and records
every frame until it is shown. It fails on a frame where anything inside that page is drawn, or a
control in it could take focus, and on a move where the page was never seen out of sight with the
slide at its top ready, since that move looked at nothing.

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
  titles this person is part way through, and a Movies, a TV Shows and an Anime row of what is
  downloaded, each leading to the library; a row that would list what is not on the device is not
  drawn. The library's Movies, TV Shows and Anime list what is downloaded, shelved by the category the
  save kept, a show once however many of its episodes are, with
  search, filters and sort working on them. A downloaded film's page is its page, and Watch plays it
  from the device. A show's page is drawn whole from the show kept on the device beside its episodes
  (`offline/shows.js`): every season and episode with its name, number and still; a downloaded episode
  plays from the device and the rest are dimmed and cannot be played. Profile opens on Playback, which
  with Subtitles shows the preferences kept on the device and changes nothing; Appearance works, and
  Account signs out. About is drawn as ever.
- **What needs the server** is drawn dimmed and does nothing: Discover, Saved and Notify me in the
  sidebar, Discover and Assistant in the tab bar, the bell, Profile's History, Requests, Following and
  Notifications, Save, Follow, Notify, Discover's search, the assistant's button, and every other
  control that asks the server. Discover, the assistant and every page the device cannot draw
  (a film not downloaded, Saved, a list, Notify me, a room), reached by an address, Back or a push,
  say "You're offline" and that the place is not available offline, and lead to the library.
- **What is kept for it.** Beside the downloads themselves, the device keeps an account's preferences
  (`offline/preferences.js`), where the person got to (`offline/progress.js`: the last
  `GET /api/me/progress` answer and the device's own log), the order picked on each page that sorts
  (`src/sorts.js`) and, for each show with a downloaded episode, the show as `GET /api/shows/{imdbId}`
  describes it with its artwork. No other answer of the API's is kept.
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
  `data-tv`, which krystal's television parts (`styles/tv.css`) and every TV rule hang from.
- **The television's own pages** (`src/tv/TvFrame.jsx`, `src/tv/pages/`), drawn to the TV design at
  the WebView's 960 by 540 page, on krystal's `TvBar`, `TvBackdrop`, `TvSpotlight`, `TvRow`,
  `TvCard`, `TvTile` and `CollectionTile`. No sidebar, footer or breadcrumb: a bar across the top holds Home, Discover,
  Movies, TV Shows, Anime and Saved, with the bell and the person at its right, on those six pages; a
  film's, a show's and the profile's pages have none, and Back leaves them. The bar moves away once
  a page scrolls or its rows hold focus, and comes back with focus. Home and Discover open on their tops' slides
  (`src/tv/hero.jsx`) over each one's artwork, turning by themselves as on a computer: a press starts a
  slide's time over, focus stays on the action in the same place as a slide turns, and the slides hold
  still while focus is on the page below them. Discover's rows follow: the shelves as rows of posters, and the moods, the
  franchises and the browse categories as rows of `CollectionTile`s, focused as cards are; a
  category is its shelves as rows, the franchises as tiles in rows that wrap, and a shelf its posters
  in rows that wrap. Home, Movies, TV Shows, Anime (its
  films and series in the same genre rows) and a show's episodes put a spotlight at the top of the screen on the card focused below
  (`src/tv/spot.jsx`): its artwork behind the page, its name, facts and story, turning to a new card
  once focus has rested on it for 300 ms so a held arrow loads nothing; the focused row holds a line
  on the screen and the rows above it are put away. A row ends in the tile that leads to all of it.
  Focus lifts what it is on: a card grows, rises and takes a hairline and the accent's light while
  the cards around it dim, and a button or a place in the bar turns into a light pill. Nothing says
  whether the server holds a title: a film is watched the same way either way. Profile keeps its
  sections down the left and the open one on the right, About among them at its own address. A
  list's page is headed by the list's name, with its count of titles under it once it holds one.
  Saved with nothing saved and a list with no titles show krystal's `EmptyState`, "Nothing saved yet" over a
  Discover button, which takes initial focus (`data-tv-initial`) so a remote has somewhere to land. The player is the
  site's own, with its menus as a panel down the right and its subtitles sized for the distance.
- **Moving focus** (`src/tv/navigator.js`, the page glue over krystal's `lib/spatialFocus`): every arrow key is taken and focus moves to the nearest
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
- **The television's connection** (`src/tv/screen.js`): while a television is signed in it holds one
  hub connection that belongs to its person and to no room (`src/personConnection.js`), built as a
  room's is (`buildHubConnection` in `hub.ts`). The server knows a television as a screen for as long as a connection signed in through
  the television client is open (`docs/api-contract.md`, "Screens"), and a room's connection exists
  only while a room is open, so this one makes the television present on every page. It never joins a
  room and reports neither buffering nor quality. It is opened on sign-in, retried on a timer when it
  cannot connect, replaced at once when the page returns (visible again, restored from the cache or the
  network back) and its socket is dead or does not answer the server's clock within two seconds, and
  closed on sign-out. The screen's name is read when it connects, so a rename made
  at the provider shows on the next reconnect. It is what asks for the pairing code and is told the
  phones paired with the television.
- **Pairing a phone** (`src/tv/TvRemote.jsx`): Profile's Remote, between Playback and Subtitles and
  on a television alone, shows the television's name beside its title, a QR code of the address a
  phone opens to pair with it (`/p/<code>`, drawn by `src/tv/Qr.jsx`), and the phones paired with it,
  each named by its browser with the day it paired and Unpair, the row's one control, which focus
  lifts with it. The paused player shows the same code beside its paused card, centred in the same box,
  under the word Remote. The code comes from the television's own connection, which alone is given one,
  and is replaced on the span the server gives, so the one on screen always has most of its life left
  (`docs/api-contract.md`, "Screens"). It is a version 4 code at error correction M, 33 modules across:
  200 pixels of the page in Profile and 128 beside the paused card.
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

## Driving a television

A phone pairs with a television by scanning the code it shows (`/p/<code>`, Pages above), and the
pairing belongs to the session that scanned: a browser tab and the installed app are two phones, and
on iOS, where a home-screen app never opens a scanned link, the scan pairs Safari. While its session
has a television paired, the site holds a hub connection of the person's (`src/remote.js`, on
`src/personConnection.js`), on which the server tells it each paired television arriving, changing room
and leaving; with none paired it holds nothing, and it asks again whenever somebody signs in or the
connection comes back. Remote mode, whether this device is driving its television now, is the device's
own, kept under `cinema:remote` across the app closing: pairing turns it on, and undoing the last
pairing turns it off.

While remote mode is on and a paired television is connected, the phone is a remote of it
(`src/remoteRoom.js`, driven by `src/remote.js`): a room connection of its own, made a remote with
`JoinAsRemote`, that is in the room the television is in and moves to the next when the television opens
another, or into none when it leaves its rooms. Its state is applied by the player's own rules
(`RevisionGate`, `ServerClock`, `derivePosition`), `useRemoteRoom` gives a component the screen driven,
whether the room can be heard from and its state, `remotePosition` where the room is now, and
`roomCommands` are the intents it sends (`play`, `pause`, `seek`, `skip`, `loadTitle`, `next`,
`previous`, `playEpisode`). It reports no buffering and no quality and is in nobody's roster. With
several televisions connected the first is driven, and `chooseScreen` drives another. `useRemoteMode`
is the switch's state. `npm run verify:tv` drives a room from a phone with nothing on screen, and
`node verify/resume.mjs` measures how quickly such a phone is live again after its page was away.

The remote is one dock (`components/RemoteDock.jsx`), drawn by the site's own components and nothing
redrawn: the download sheet's head, the player's scrub bar and control row, its
episodes menu, `EmptyState`, `Rail` and the Continue watching card. While remote mode is on and a paired
television is connected it stands on the tab bar, its bottom edge on the bar's top at every height, and the
tab bar stays uncovered and live. Collapsed it is one row (the still, the television's name, what plays,
Play or Pause). A finger moves its top edge and its height follows: pushing the row up grows it, and as it
grows the row fades out and the open remote fades in, uncovered from its head down, over a scrim that
darkens with it. Pulling the open remote's head down shrinks it the same way, as does pulling its body down
while the body is scrolled to its top. Let go, it settles open or collapsed by how far or how fast the
finger went; a tap on the row opens it, and the scrim, Escape or a tab collapse it. Swiping the collapsed
row sideways turns remote mode off with Undo in a toast, unpairing nothing. `npm run verify:dock` holds
the dock to this under a finger.
The open remote's head names the television, which opens the picker (an option list, "Nothing playing" or "Off"
for one that is not connected), and holds the Remote switch. The picker and the Episodes list are drawers over the dock, each with a back button that returns to the remote. Below are the still, what plays, and the
player's scrub bar (with its preview) and controls: Play or Pause, back and forward ten seconds, and for an
episode previous, next and Episodes, the player's own list aimed at the television. A film has no
episode controls. Subtitles, Audio (only a film with more than one audio track) and Quality follow, each the
player's own menu aimed at the television (below). The controls are two rows, each as wide as the bottom block with equal gaps between its
controls and its first and last on the block's edges, at every phone width, for a film and an episode alike: back, previous episode, Play or Pause, next
episode and forward (a film has no episode steps) above, and Episodes, Subtitles, Audio and Quality below, each control keeping its 44px size. With nothing playing it shows "Nothing playing" and Continue watching, each card Watch
on TV. The still takes the player's touch handling (`player/Gestures.jsx` with `levels: false`): a tap in
the middle plays or pauses, a double tap at a side skips, a sweep sideways scrubs and the room moves on
release, vertical sweeps do nothing; a gesture the television took vibrates the phone where
`navigator.vibrate` exists. Skip intro stands on the still when the player would offer it
(`player/stretches.js`). `remoteFilm.js` reads what the television holds (manifest, the episodes either
side, the preview sheet) for the dock.

The television's own settings are the API's, never the room's: the subtitle track, how subtitles look,
the audio track and quality. The remote's Subtitles, Audio and Quality buttons open the player's own menus
(`Menu` with `aimedAt`: the television's name in the head and a back arrow to the remote; Style is the
subtitle menu's editor, its back arrow returning to the subtitle list), and what they read and write is
the person's record, not the television's report (`src/remoteMenus.js`, `src/remoteSettings.js`). The
labels and the checked rows are `/api/me/preferences` and `/api/me/tracks/<film>`, read when the remote
connects and when the television's film changes and kept current by `PreferencesChanged` and
`TracksChanged` on the remote's connection; where the person chose no track for the film, they are what
the film starts on (`startingTracks`, the player's own rule), so the phone never names a track the
television is not on. Quality reads "Auto" or the fixed height: the height playing is a measurement
only the television's player has, and is never shown. A choice shows at once and is written through the
API, which refuses what the film does not have and pushes the change to the person's connections; a
refusal is said in a toast and the record read again. A style change is sent once the choosing pauses
(250 ms) with the whole stored style. The television's player applies a push for the film it holds
(`engine/room.ts`, below), the television's own menus write the same record, and the phone's labels
follow. Nothing here is in the room's state or roster.

A television draws a phone glyph beside who is watching while it is paused and a remote drives it
(`tv/screen.js` hears `RemotesChanged`; the room's roster is untouched).

A toast sits at the top of the screen while any drawer or the open remote dock is open (`body.k-has-drawer`,
`body.has-remote-open`) and above the collapsed dock otherwise, so a toast is never under the thing it is about.

With remote mode on and a television connected, a title's main button is Watch on TV (`remoteWatch.js`)
and Watch with the phone glyph beside it, which turns remote mode off and plays on the phone. What the
television is doing decides how a title gets there: in no room, the phone picks the room and calls
`WatchOnScreen`, the television navigates to the room's address as the site's Watch writes it (so its Back
returns), and the phone takes the screen's next announcement showing that room and title as the answer
and says the TV did not answer when none comes in a few seconds; in a room holding nothing, `LoadTitle`;
already on the title, nothing; mid-film, the phone asks first (`components/ReplaceSheet.jsx`), and in the
closing stretch (`inClosingStretch`) it replaces at once. A title put on plays: a film arriving in a
paused room is played once the remote hears the room hold it. A film the library lacks goes through Watch
now with the television's room, or the phone's own for a television in none.

## Notifications

The bell lists what the API told this person (`src/notifications.js`, read every minute and whenever
the page comes back into view): in the sidebar above the person on a wide screen, at the top right of
the front page on a phone, and in the bar on a television. Profile's Notifications tab turns pushes on for this browser, through
krystal's `pushBrowser` against `/api/push/key` and `/api/me/push`, and holds a switch
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

The assistant is `@thekrystalship/krystal-chat`'s `Chat` (`CinemaAssistant.jsx`), talking at `/assistant` to the plugin of
the assistant harness as the signed-in person, over the assistant wire contract
(`tks-assistant/docs/wire-contract.md`). A phone has it as a page, `/chat`, among the tab bar's
(`pages/Assistant.jsx`), drawn in the chat's page layout: on the page's own background, filling the
column down to the tab bar or the remote dock, a way back to an empty conversation in its header, the
conversations in a sheet, the opening suggestions as chips over a one-row composer, and the tab bar
and the remote dock giving their room to the keyboard while the person writes. A wide screen has it
in the dock beside whatever page is open, and as a page at `/chat` with no dock beside it. Its address
is not `/assistant`, which is the plugin's on the same origin. Replies
stream; the microphone sends a voice note to the plugin's `/speech/transcribe` and the transcript returns
to the composer. A `question` frame is drawn as a form of choices and the answers go as the next turn,
which is how the taste survey runs. A `titles` result is drawn under the reply that names it, one card
for each title the reply names: poster, name, year, and *In library* and *Watched* where they hold,
leading to the title's page. The assistant holds no room controls and no buttons for what a title's page does.

## Rules

- **The design is the UI and the API is the authority for its data.** Nothing is drawn from a
  fixture: what the design shows, the API serves, and a row the API has nothing for is left out rather
  than filled in.
- **The site's stylesheets sit around the design system's cascade layers.** `src/layers.css` is
  imported first and orders them: `site.reset` before the system's layers, `site.base` between its base
  and its components, so the document rules (`html`, `body`, `a`, `button`) in `site.css` give way to the
  system's components. Every other rule is unlayered and so restyles a component by an ordinary
  selector. The system's classes carry `k-`; the site's own are `kc-` (the shell, pages and lists) and
  `mp-` (the player).
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
  token to keep. A 401 renews the session once and makes the call again, and a second 401 ends the
  session.
- **Every act is the person's own.** The API takes who asked from the token, so the site sends no
  name of its own and cannot act for anybody else.
