# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [3.68.2] - 2026-10-24

### Fixed
- A shelf's page opens in the order its rail shows. An order picked on it is kept in the address
  (`&sort=`) rather than for the account, so it no longer carries to every other shelf.

## [3.68.1] - 2026-10-24

### Fixed
- A shelf's count says `40+` when the shelf goes on past the titles counted, on its rail, its tile,
  its own page and the filter's grid, on the site and the television, and the exact number when it
  does not; a tile's head carries `more` for it.

## [3.68.0] - 2026-10-24

### Changed
- IMDb's rating is the one a title is shown with everywhere: `rating` and `votes` on every view are
  IMDb's where the import holds the title and the film database's otherwise, `ratedBy` says whose,
  and `imdbRating` and `imdbVotes` are gone from the wire. A rating floor holds for the rating shown,
  the title page's score names its source, and About credits IMDb.

## [3.67.2] - 2026-10-24

### Fixed
- A Discover shelf sorted by rating or release date keeps its order as it scrolls: the shelf route
  takes `sort` and orders the whole shelf before paging it, so a page read later extends the grid
  and nothing already on it moves.

## [3.67.1] - 2026-10-24

### Fixed
- A question narrowed to one genre and one year ranks on the votes of the thirtieth most voted title it
  names rather than the hundredth, so a film a few hundred people rated no longer heads the best horror
  of a year, and documentaries filed under a genre are left out of its IMDb ranking unless asked for.

## [3.67.0] - 2026-10-24

### Added
- Discover reads what is typed as a question of the catalogue, in any order: counts and orders (`top
  10`, `best`, `newest`), kinds, years, ranges and decades, runtime bounds, genres and languages by the
  film database's own names, people (`with`, `starring`, `by`), subjects (`about`), likeness (`like`),
  streaming services (`on netflix`), awards, adaptations, places and subgenres. `GET
  /api/catalogue/ask` answers the reading as parts and the titles it found, or that the words are a
  title's name, which the name search answers as before.
- **Ask the assistant** on Discover when a search finds nothing worth showing: it opens the assistant
  with the words typed already sent.
- Discoveries, questions and shelves go on past their first page (`page`, `next`, at most 25 pages),
  and the shelf, filter and question grids load the next page as they end.
- IMDb's non-commercial datasets are imported daily (`imdb_titles`, the `Imdb` settings): a question
  or shelf ordered by rating ranks by IMDb's votes with a floor scaled to the question, widened by
  IMDb's own matches for a genre and years, and every title view carries `imdbRating` and `imdbVotes`
  beside the film database's `rating`.
- Wikidata (the `Wikidata` settings), asked in the background weekly and never while a page waits:
  shelves of award winners and nominees, adaptations by author, films set in or filmed in a place, and
  well-filled subgenres, in new Awards, Adaptations and Places sections; a film's `awards` and
  `basedOn`.
- A film or show carries its trailers, where it streams in `Tmdb:WatchRegion` (logos served from here),
  recommendations, reviews, its logo and tagline, a film's budget and revenue, and its keywords with
  their ids, all from the one description call. Discover has a Streaming section of the region's
  services.

### Changed
- `Tmdb:WatchRegion` names the country streaming is asked about, `Tmdb:Region` when empty, so
  certificates keep the ratings the shelves name. hotbox streams in Spain.
- A film's `keywords` are `{id, name}`.

## [3.66.0] - 2026-10-24

### Changed
- A transcode the hand-off stops part way (a restart, a crash, a reboot) is carried on by its next
  start from about ten seconds before where each track stopped, on the disk it was on, instead of
  being deleted and made again from the beginning in front of the rooms watching it. The resumed
  segments open on the same frames at the same times as an uninterrupted transcode's, so the rungs stay
  aligned and players carry on across the seam (`TrackResume`, `Fmp4`; `--resume` on the ingest CLI).
- The hand-off's unit stops with `KillMode=mixed`: only the hand-off is asked to stop, and it kills its
  ffmpeg processes outright, so a stop no longer ends the playlists of a film still being made.
- A transcode stopped with its process stays `transcoding` in its manifest rather than `failed`.

## [3.65.0] - 2026-10-23

### Added
- A player says what went wrong with its film (a fatal network or media error, a stuck playhead, a
  device store that did not answer, a film that never started, giving up) over the hub
  (`ReportTrouble`), written to the API's journal at most six lines a minute per connection, each
  naming the playback by the CMCD session id its requests carry into the delivery log.
- The API warns when a transcode an occupied room is watching begins again from nothing, and the
  hand-off says how much it deletes when it replaces a transcode's output.
- `web/site/verify/stalls.mjs`: a private file system that never answers, playlists missing for a
  while, and a transcode begun again from nothing under two viewers, in a real browser.

### Changed
- Buffer ahead is Standard on a desktop that chose nothing, and Dynamic on a phone, a tablet or a
  television.
- What the tracker offers for a film is asked once per ten minutes for availability and wants
  (`FilmOfferCache`); the wish sweep's answers are kept there too.
- `deploy-services.sh` restarts the hand-off only while it is making nothing; while it transcodes,
  a transient unit on hotbox restarts it once the last transcode ends.

### Fixed
- A player opening a film while its transcode had just begun again waits on the Preparing screen
  for good: loading always starts at the position the player names, never at the live edge hls.js
  picks for a playlist still being written, and a playhead that sits on nothing it can play while
  the server holds that stretch has the element attached afresh after eight seconds.
- A device store that stops answering no longer holds a film on the Preparing screen: the device has
  three seconds to answer before the film streams without it.
- A fatal network error is waited out at a doubling interval up to ten seconds rather than loaded
  again at once, which asked for a missing playlist hundreds of times a second.
- A growing playlist is served from one open copy of the file, so its length and its bytes always
  agree.
- A home page card asks for the poster of the kind its title is, not the film's and the show's both.

## [3.64.0] - 2026-10-23

### Changed
- The product is Cinema wherever a person reads its name: the site's title and home-screen name, its
  wordmark, the sign-in and offline screens, notifications and their pushes, the shared room's preview,
  the Discord bot's replies, the assistant's prompt and errors, and the phone and television apps.

## [3.63.0] - 2026-10-23

### Changed
- The site's splash, favicon and install artwork are the design system's (krystal 8.1.0), from the
  clapperboard in `web/site/identity.js`: the splash and the tab's icon take the theme the person chose,
  and the launch screens and the phone app's launcher icon are written by `npm run icons`.

## [3.62.0] - 2026-10-23

### Changed

- **The account menu every site shares** (krystal 8.0.0): the sidebar's account row is the avatar and
  the name, and its menu is Profile, Settings (the profile's settings tabs, from Notifications), a rule
  and Sign out. In the Discord Activity it has no Sign out, as before.

## [3.61.1] - 2026-10-23

### Fixed

- **Corners are the design system's two radii** (krystal 7.0.0): every surface of the site and the TV
  takes the 4px UI radius, the notification tray among them, and only pills, avatars and round marks
  take the pill. The palettes re-value only those two.
- The phone's bell sits at the end of the front page's header through its own class.

## [3.61.0] - 2026-10-23

### Changed

- **Notifications are the shared system's.** The API keeps and pushes them with
  `TheKrystalShip.Notifications` 1.0.0-dev.1: every notification is kept a year with no count limit, in
  `notifications.db` beside `moviebot.db`, and the push key pair is the library's `vapid.json`, so every
  browser subscribes again from Profile's Notifications. The routes are the library's under `/api/me`
  (`/notifications`, `/notifications/read`, `/notifications/kinds`, `/notifications/preferences`,
  `/push/key`, `/push/subscribe`, `/push/devices`) and the anonymous button route is
  `/api/push/actions/{handle}`. The old `/api/push/key` and `/api/me/push` are gone.
- **A kind a person switched off is neither kept nor pushed.** An invite and a plan cannot be switched
  off. The digest of titles to watch together is kept only for a person who switched it on, and is keyed
  by what it says, so a digest the person holds is not told again.
- **The bell and Profile's notification settings are the design system's** (krystal 6.0.0-dev.2): the
  `NotificationTray` and `NotificationList` in the sidebar, on the phone's front page and on the
  television's bar, and `NotificationSettings` in Profile. The service worker shows a push and opens its
  link through krystal's notifications worker script.

## [3.60.0] - 2026-10-23

### Added

- **Tools** in the chat's options menu: what the assistant can use on your behalf, each named and
  described (krystal 5.1.0).

## [3.59.0] - 2026-10-23

### Added

- **What the assistant writes down, where it happens.** A line under an answer for each memory the
  assistant wrote or forgot during it ("Remembered: …", "Forgot: …"), with Edit opening that memory.
- **Skills** in the chat's options menu: what the assistant knows how to do, each skill named and
  described (krystal 5.0.0).

## [3.58.1] - 2026-10-23

### Changed

- **The assistant lists its skills.** The plugin is on `TheKrystalShip.Agent.Plugin` 1.0.0-dev.20, whose
  `GET /skills` answers each skill's name and description for the chat's Skills page.

## [3.58.0] - 2026-10-23

### Added

- **The assistant's memory, in the chat.** The chat's options menu holds Memory: what the assistant
  has written down about you, across every product, listed in the chat's own panel, where each memory
  is edited or forgotten (Forget asks twice). Compact is in the composer's options.

### Fixed

- **The chat runs to both edges at phone width.** The scrolling column keeps no scrollbar room while
  it holds the chat, which scrolls inside itself, so its right gutter matches its left.

## [3.57.0] - 2026-10-22

### Added

- **Twelve more cinema themes**, in the picker under Appearance: Crimson Reel, Enchanted Navy, Amber
  Library, Fresh Green, Prime Night, Sunrise Violet, Streaming Gold, Big Screen, Film Diary, Popcorn
  (light), Grindhouse and Sci-Fi Bridge. Each is a mood of a streaming service or a screening room
  with no brand mark; all pass `npm run check:theme`.

## [3.56.0] - 2026-10-22

### Added

- **Six cinema themes.** Noir, Velvet, Drive-In, Technicolor, Video Store and Silent Film join the
  theme picker under Appearance. They are the site's own palettes, drawn in `src/themes.css` and
  registered in `src/themes.js`; `npm run check:theme` holds them to the design system's palette
  contract and its 4.5:1 ink rule.

## [3.55.0] - 2026-10-21

### Changed

- **Making a plan is laid out as the title's own page.** `/plans/new` opens with the title's hero
  (facts, score, story, credits) holding a line that says the plan so far, Make the plan and More info.
  When is a month to pick days from beside the chosen day's times every half hour, starting at the
  first one at least 5 minutes away, with Another time for any minute; the times picked are listed
  with their calendar tile, how far off they are and when the film ends. Who is everybody who can be
  asked as person cards to press, the people who want the title first and pressed already, with a
  search and Everyone. The site is on krystal 3.5.0, whose `Calendar`, `TimeGrid` and `TimeInput`
  draw it.
- **Every title page draws its hero from one component**, `TitleHero`, with `TitleStory` for the
  score, tagline and overview: a film's, a show's, a catalogue film's, a plan's and a new plan's.

## [3.54.1] - 2026-10-21

### Added

- **`Wants:Prefetch` switches the prefetcher's fetching of wants.** Off, nothing is fetched because
  somebody wants it, and the prefetcher fetches only the titles of fixed plans. It defaults on; hotbox
  sets it off.

## [3.54.0] - 2026-10-21

### Added

- **Anybody in a plan suggests another time** (`POST /api/plans/{id}/times`),
  and is in for it; everybody else is told who suggested what. A plan holds at most six times still
  ahead, each 5 minutes to 30 days out. The plan page's Suggest a time offers +30 min, +1 h, +2 h and
  Next day from the time the plan is on, or any time typed, and each time says who suggested it.
- **Anybody in a plan invites more people into it**: `POST /api/plans/{id}/people`
  with `{userId}` answers the delivery (`push` or `bell`) as a room's invite does. The person is told
  who asked them, and a person brought in once the room is open is let into the room. A plan still
  invites at most 20 besides its maker. The plan page's last people card, Invite people, opens the
  room's own Invite list (`components/InvitePeople.jsx`, which the player's Invite panel draws too),
  beside the card or as a sheet on a phone.

- **Plans starts a plan.** Plan a watch in the page's head opens the new-plan form, which finds a
  title by name when none is given (`components/PlanTitlePicker.jsx`). A person with no plans is
  offered what they could plan: Watch together, then the rest of Want to watch, each card planning its
  title, or the title search and Discover when they want nothing yet.

### Changed

- **A plan is kept only while it is going.** The maker cancelling it, or its room going after the
  picked time, deletes it with its times, people and answers, so Plans lists only plans still to watch
  and a cancelled or watched plan's page is a not-found page. The schema step that adds who suggested
  each time deletes every plan already done or cancelled. The `done` and `cancelled` states are gone.
- **Nothing about a plan locks.** Answers pick no time: the maker picks one and can
  move the pick to any other time ahead, every time and its answers staying, and everybody is told the
  plan moved. Answers change at any time, and saying in or maybe to the picked time once the
  room is open lets the person in. The maker cancels at any point, and a plan whose times
  have all passed is kept for a new one rather than ended. A pick and a reminder are said again for each
  time picked. `Plans:FixAt` is gone from the settings.
- **A plan's page is drawn as its title's page is.** The title's hero (poster, backdrop, year,
  certification, runtime) leads, with who planned it and how many are invited, the plan's steps
  (Proposed, Time picked, Room open), Join the room once it opens, More info opening the
  title's page, and Cancel plan beside it for the maker. Each time still ahead is a row: a calendar
  tile, the hour and how far off it is, the picked one marked, a bar of who is in and who might be, and
  the viewer's answer, flagged while they have not given one, with Pick this time or Move to this time
  for the maker. The people are cast cards (`PersonCard`, which the cast grid draws too): each name, the
  maker tagged Planned it, and what they said to the picked time. Site 0.99.0.

## [3.53.4] - 2026-10-20

### Changed

- **The site's sidebar is grouped by what each place is.** Home stands alone; Library holds Discover,
  Movies, TV Shows and Anime; Yours holds Want to watch, Plans, History and Following, the last two
  opening Profile on their tab. Site 0.98.3.

## [3.53.3] - 2026-10-20

### Changed

- **Home's Watch together cards are plain want cards**, with no faces of the others under them.
  Site 0.98.2.

## [3.53.2] - 2026-10-20

### Fixed

- **A title shows one rating everywhere.** Each kept answer carried the rating from when it was
  asked, so a new release read 6.2 on its page, 4.0 in a search and 8.3 on an actor's page. The
  newest rating the film database has given is kept once per title (`title_scores`, seeded from the
  descriptions and glances already kept) and every view serves it: film and show pages, search
  results, credits, Discover shelves, Popular, wants and the library's details. A rating with fewer
  than 25 votes behind it is shown as none. A film released within sixty days is described again
  after `Tmdb:RecentRefreshDays` (a day) instead of `Tmdb:RefreshDays`. Nothing new is asked of the
  film database.

## [3.53.1] - 2026-10-20

### Fixed

- **A title that is not out cannot be planned.** `POST /api/plans` answers `409` for a film the
  tracker shows is not out digitally, and for a show's episode the film database dates to a day not
  yet due; a tracker that does not answer, or an episode with no date, refuses nothing. The site's
  Plan a watch is disabled for such a title, the Want button no longer carries a "Waiting for a
  release" pill, and on a phone Want and Plan a watch share a row when there is no Watch. Site 0.98.1.

## [3.53.0] - 2026-10-20

### Added

- **Want to watch.** One signal, "I want to watch this", replaces Watch later, custom lists and
  wishes. A want is just a person and a title, visible to everybody by default with a per-title
  Just me switch; a title nothing has yet is asked of the tracker hourly and its wanters are told
  when it turns up, and finishing a title ends the want. The site has a Want button and ribbon on
  every card, the people who want a title drawn as faces, and `/wants`; the television has its own
  Want to watch page; the bot's notify and the assistant's `my_taste` read wants. Existing list
  entries and wish subscriptions come across as just-me wants.
- **Prefetch.** A wanted title the tracker offers is fetched ahead of time once the host is quiet:
  no room playing, no Watch now arriving, the disk budget Ok. The torrent carries `want:<imdbId>`
  and every wanter whose want may fetch is told when it is ready. A wanted, unwatched title is kept off
  `/api/library/due`.
- **Matching.** People who want the same title are shown to each other, and a daily digest names the
  titles to watch together.
- **Plans.** A plan is a title, up to three proposed times and the people invited. Answering in,
  maybe or can't settles a time (fixed at `Plans:FixAt` or by the maker); `Plans:OpenMinutes` before
  it the room is made, closed, with the people coming admitted and the title loaded, and a reminder
  goes out at `Plans:RemindMinutes`. The site has `/plans`, `/plans/new` and `/plans/<id>`, and
  Home's plans row. Site 0.98.0.

### Fixed

- The mobile harness's views for the footer, the Library tab and the Library's filter and sort
  menus select the design system's current classes, and its overlap check boxes each line of a
  wrapped link instead of the union of its lines.

## [3.52.1] - 2026-10-20

### Fixed

- **Declining a proposal in the assistant settles as declined.** The plugin is on
  `TheKrystalShip.Agent.Plugin` 1.0.0-dev.19, which answers a decline as the stream's result frame
  when the chat asks for a stream, so the dock no longer reports "The action ended without a result".

## [3.52.0] - 2026-10-19

### Added

- **The assistant hands-free.** The site pins `@thekrystalship/krystal` and the chat at 3.2.0: the
  dock's options offer *Hands-free* where the assistant can scan. The microphone stays open, and a
  request that opens with "hey assistant" is sent and read aloud; listening holds while the answer is
  written and read. Site 0.97.0.

## [3.51.5] - 2026-10-18

### Fixed

- **Home's carousel no longer keeps a phone busy.** The site pins `@thekrystalship/krystal` and the
  chat at 3.0.3, whose pager shows the shown slide's mark whole on a touch screen rather than filling
  it frame by frame. Site 0.96.2.

## [3.51.4] - 2026-10-18

### Fixed

- **The assistant's mark holds still on a phone.** The site pins `@thekrystalship/krystal` and the
  chat at 3.0.2, whose mark no longer animates forever on a touch screen. Site 0.96.1.

## [3.51.3] - 2026-10-18

### Changed

- **The phone app's system bars wear the site's colour** (phone app 1.1.3, tks-shell 1.2.0).

## [3.51.2] - 2026-10-18

### Added

- **The phone app saves files and picks them as a browser does** (phone app 1.1.2, tks-shell 1.1.0).

## [3.51.1] - 2026-10-18

### Changed

- **The phone app is built on tks-shell 1.0.0**, vendored into `phone/shell/` (phone app 1.1.1):
  `phone/` holds only Cinema's `shell.conf`, `res/` and version. Nothing a person sees changes.

## [3.51.0] - 2026-10-18

### Changed

- **The phone app is the TKS shell built as Cinema** (phone app 1.1.0): its native part names no app
  and no site, and the site hands it a save's tracks as one of its downloads, so the shell can carry
  other sites. A download whose grant ended is asked for again and handed over with a new one, with
  nothing shown. Site 0.96.0.

## [3.50.1] - 2026-10-18

### Fixed

- **The phone app looks like the site.** Its launcher icon is the site's clapperboard, as the
  installed web app shows it, with a themed variant. Phone app 1.0.1.
- **The phone app says what finished.** Each download that completes gets its own "Downloaded"
  notification; pausing one no longer says the downloads finished. Phone app 1.0.1.
- **A download in the phone app shows each file as far as it is.** The download sheet listed
  subtitles as still waiting while the app fetched the picture. Site 0.95.1.

## [3.50.0] - 2026-10-18

### Added

- **Krystal Cinema for Android** (`phone/`, version 1.0.0): the site in an app whose downloads run
  in a foreground service, so they carry on with the screen off, the app out of view and the phone
  in Doze, and are told to the API as complete with no page running. Download in the site hands a
  title's tracks to the app on the save's grant, and the player plays them from the app with no
  connection. The API serves it at `/phone.apk` (`Phone:App`), and the site's footer offers it on an
  Android phone's browser. Site 0.95.0.

### Fixed

- **A download no longer stalls for good on a dead connection.** A request that delivers nothing for
  30 seconds is dropped and asked for again from what is on the device, so a phone moving between
  networks, or a page the browser froze and thawed, carries on instead of waiting forever.
- **A link to a file opens the file.** The site's service worker answered every navigation that was
  not a page with the site's index; one answered with a file now goes through.

## [3.49.0] - 2026-10-18

### Added

- **A save's grant.** `POST /api/me/offline` answers a `grant` that opens the save's tracks and its
  `complete` route with no access token, carried in `X-Cinema-Save-Grant` and lasting the save's
  hold. It is what a phone's own download service fetches on for hours with no page running. It
  names the save's owner, so their transfer limits count it, and opens nothing once the save is
  forgotten.

### Changed

- **More transfers at once.** A person may fetch 6 tracks at once and the server sends 12
  (`Offline:MaxTransfersPerPerson`, `Offline:MaxTransfers`).

## [3.48.0] - 2026-10-18

### Changed

- **A source goes as soon as the tracker lets it go.** The hand-off removes a finished torrent and
  its files once it has seeded 60 hours (the tracker's 48 plus a margin), or reached a ratio of 1.1
  after 3 hours of seeding, whichever comes first. Settings: `Retention:TrackerMinimumHours`,
  `MarginHours`, `TrackerRatio`, `RatioMargin` and `RatioMinimumHours`.

## [3.47.1] - 2026-10-18

### Fixed

- **Download season no longer blanks the site.** The season's sheet asks for the episodes that
  remained when it opened, so the wants it creates no longer empty its list while it is still on
  screen. A season the server then refuses shows each episode as Not downloaded, with the reason.
  Site 0.94.1.

## [3.47.0] - 2026-10-17

### Added

- **Buffer ahead: Dynamic, the default.** Standard in memory, and as much more kept on the device as
  the connection calls for: ten minutes at a time while the network carries three times what the film
  needs or the link is Wi-Fi, at least ten minutes after a stall or a dead zone, two at a time on a
  comfortable connection and none on a tight one, up to the rest of the episode and the start of the
  next. Nothing more with the browser's data saver on. Standard where the browser has no private file
  system. Site 0.94.0.

## [3.46.0] - 2026-10-17

### Added

- **Buffer ahead: A lot.** The rest of the episode and the first five minutes of the next are kept on
  the device as they play, the lowest rung with the audio first and then the rung watched, and deleted
  once the playhead has passed them by two minutes; only the title playing and the next are kept. A
  segment on the device is read from it before the server is asked, and every file the player fetches
  is kept too. With no connection the player is held to the best rung the device has where it is
  fetching, so a stretch kept only at 480p plays at 480p rather than stopping. Offered where the
  browser has a private file system. Site 0.93.0.

## [3.45.0] - 2026-10-16

### Added

- **Buffer ahead, chosen per device**: under This device on the profile's Playback tab, Light fetches
  about 30 seconds ahead and Standard, the default, about 3 minutes, whatever the rung. It is kept in
  the browser, so a phone and a television choose apart.
- **A ready title's tracks carry their measured rate** (`rate: {averageKbps, peakKbps}` on each
  rendition and audio track), measured by the ingest as a title turns ready and by the hand-off's
  `RatesWorker` for every ready title that records none.

### Changed

- **The master states what a rung's segments measure**: `BANDWIDTH` is the rung's peak with the
  heaviest audio's, and `AVERAGE-BANDWIDTH` their averages; a rung not yet measured states the
  encoder's ceiling as before. A phone on a few megabits now plays 720p where it was held to 480p.
- **What hls.js trims of the buffer while switching rungs is given back** once a fragment lands,
  unless the browser said its buffer was full. Site 0.92.0.

## [3.44.0] - 2026-10-16

### Added

- **`Rejoin` on the hub**: a page coming back into its room says what its player did while the room
  could not hear it (`PlayerAccount`: the last state it applied, the film, its playhead, playing or
  paused). Alone in a room nobody changed since, the room carries on from that playhead and credits the
  stretch to the viewer; a room forgotten meanwhile is given the film back; a room with others in it,
  or changed, stands.

### Changed

- **A room nobody is in is held where it was**, as one whose every player waits is, so a viewer whose
  connection went comes back to where they were rather than to where the room ran to.
- **The player never stops trying to reach its room**: soon at first, then every ten seconds, and at
  once when the browser is online again or the page is in front of somebody again. "The room can't be
  reached" goes once the room is back.
- **Where a room's film got to is kept on the device while the room cannot hear the page**, in the log
  a film played alone writes, and sent once it can. Site 0.91.0.

## [3.43.3] - 2026-10-16

### Fixed

- **A release naming another language's dub is fetched last.** When the audio wanted is the title's
  original language, or none is asked for, a release whose name advertises a dub in another language
  (`Rus`, `Castellano`, `TRUEFRENCH`, `PLDUB`) comes after every release at the same quality that names
  none: Watch now, the next episode fetched ahead, a followed show's new episode, a device download and
  the Watch now panel's offers all take the clean release, a single episode before a foreign season
  pack, and the dub only when nothing else is there. Only tokens after the title and not marking
  subtitles are read, so a film called The Cat names no Catalan.

## [3.43.2] - 2026-10-16

### Fixed

- **A film starts on the viewer's audio, not the release's default.** The player chooses the audio
  track it starts on (the one picked for the film, else the viewer's audio language) while hls.js
  announces the renditions, and on the element once the browser plays HLS itself; a film whose
  default track is a dub, such as Grimm's second season with the Russian dub first, starts in the
  viewer's language. Site 0.90.1.

## [3.43.1] - 2026-10-16

### Changed

- The assistant plugin is on `TheKrystalShip.Agent.Plugin` 1.0.0-dev.18, which serves no site.

## [3.43.0] - 2026-10-16

### Changed

- **The dock on a wide screen is drawn as the phone's page**: the greeting alone in the middle, the
  suggestions as chips over a one-row composer, the privacy note under it, and no name over an answer.
- **Back starts a conversation.** Every conversation's header carries the back arrow to the empty one,
  and there is no New chat button.
- **The dock hides into the right of the screen** through a hide button with the sidebar's collapse
  icon mirrored. The `/chat` page has none.
- Design system 3.0.0; site 0.90.0.

## [3.42.0] - 2026-10-16

### Changed

- **The site is drawn by design system 2.0.0**, whose assistant is this site's: every other surface
  now draws its chat the same way. The composer's switches (Thinking, Read aloud) stand behind its
  options button at every width, the `/chat` page in the column's full width included.
- Site 0.89.0.

## [3.41.1] - 2026-10-16

### Fixed

- **The Discord Activity opens with the person's Cinema access.** The site learns which claim lists the
  Cinema actions wherever it reads `/api/config`, so the Activity's session, which the API mints and the
  site adopts, reads them too instead of drawing "Your account has no access to Krystal Cinema yet."

## [3.41.0] - 2026-10-16

### Changed

- **The site's sign-in is `@thekrystalship/auth-client`.** The session renews ahead of expiry, replays a
  refused call, holds through an outage or an unreachable provider, and settles races between tabs, all
  without a word on screen: a page draws only a session that has ended, as one screen with Sign in. The
  provider returns the browser to `/signed-in`; `/signin` goes Home. The television signs in through the
  client's device grant and keeps its stored session; website sessions end once, with the new redirect.
  The Activity holds its Discord-minted token through the client's `adopt`.

## [3.40.4] - 2026-10-16

### Changed

- **The site runs on Krystal 1.0.0.** A dimmed television card reads at 4.5:1 on the light palette.

## [3.40.3] - 2026-10-16

### Fixed

- **On a television, Down from the bar into Home's slides stays on the slide it landed on.** The slide's
  time starts over when focus arrives in the slides (krystal 0.43.4), so the slide no longer turns the
  moment focus lands on its first action.

### Changed

- **`verify:tv-focus` steps can expect `under`**: focus comes to rest on the card under or over the one
  it left, whichever index that is. Movies' walk expects Down from a row's fourth card to land under it.

## [3.40.2] - 2026-10-16

### Fixed

- **The television performance gate fails only on a regression.** `verify:tv-perf` judged one pass of
  each step against the baseline's median, so one to four steps failed at random on an unchanged site. It
  now discards a warm-up pass, takes each step's median over three passes (the lower quartile for the
  two-valued `answer` and `req`), measures a walk that holds a step past its limit four more times and
  fails only what is still past its limit over all seven, holds a key the baseline answers inside a frame
  to one slow frame at least, and keeps the machine's load in `report.json`. The baseline is saved again
  from seven passes.

## [3.40.1] - 2026-10-16

### Changed

- **The Library kind switch fills its row through `RadioGroup` `fill` alone** (Krystal 0.43.3).

## [3.40.0] - 2026-10-16

### Changed

- **No site sheet selects a package class.** The shelves take their card counts from Rail's `perView`;
  a download sheet's dialog takes `artOverlap` and its foot `footLayout`; a downloaded or downloading
  card's pill is the card's `corner` in the accent or monospace tone, with the progress track set through
  `--k-showcase-progress-track`; the profile's tabs and the tab bar disable their places through each
  item's `disabled`; Quality and Download quality are rows whose choice goes under the words on a phone
  (`controlsBelow`, indented to the label); the Discover filter is a framed, compact ToolbarFilters; the
  television's spotlights take `titleSize` and `overviewLines`, its cast portraits `imagePosition`, and
  its spotlit pages dim the other cards through `data-tv-dim`; the remote dock leaves its room through
  `--k-app-bottom-inset` and the toast tokens, and the column keeps its scrollbar's room through the
  shell's `mainClassName`. Cards drop their fact icons below 210px through `compactFacts`. Krystal and
  Krystal Chat 0.43.2; Stylelint's `.k-` selector rule is an error. Site 0.87.0.

## [3.39.1] - 2026-10-16

### Changed

- **The site's code names fewer package classes.** A download sheet's foot buttons fill the drawer through
  the Button's `wide`; the volume's mute button, the remote's idle state and switch, a list's rename field,
  the people sheet's person select, the offline page's Library button, the Watch buttons dimmed on a full
  disk, the player's menu rows, the television's profile avatar and Discover's search input each carry a
  class of the site's own that its sheet styles. A television film and show hand `TvSpotlight` their credit
  and actions, and the hero's actions sit in a wrapper the site names. The tab bar's and the profile tabs'
  offline disabling find their buttons by element and role, and the remote dock reads the tab bar's
  `--tabbar-h` from the bar itself. Krystal and Krystal Chat 0.42.1; Stylelint holds the package's `.k-`
  selector rule at a warning until the sheets reach zero. Site 0.86.1.

## [3.39.0] - 2026-10-16

### Changed

- **The notifications bell is a `NavItem`.** The sidebar row is `role="button"` with `aria-haspopup`,
  `aria-expanded` and an `aria-label` that carries the unread count, and its count is the package's pill.
- **The television's spotlights take their story and buttons as props.** Home, Discover, a film and a
  show hand `TvSpotlight` an `overview`, and the hero and Discover's best match hand it `actions`.
- **The television's headings are `TvHeading`s.** A browse page (a person, a genre, a category, a shelf)
  names itself through a large heading with its eyebrow, count and portrait lead, and Details is a heading
  of its own. A show's season pills are the `lead` of its episodes' `TvRow`, and Cast and the episodes set
  their track through `trackClassName`.
- **The assistant's page is a framed `Chat`** through its `framed` flag. Krystal and Krystal Chat 0.40.0.
  Site 0.86.0.

## [3.38.0] - 2026-10-16

### Changed

- **The page gutter is the frame's.** The site's pages, the trail above them and the Discord call bar
  keep only their vertical padding and their 1280px width; the frame's main column pads both sides equally
  by `--k-page-gutter` (24px, 16px at 768px and narrower, 12px at 480px and narrower). The assistant's
  page spans the column through a negative inline margin of the same variable. Krystal and Krystal Chat
  0.39.0.
- **The television's Discover search is a `SearchField`**, its first-focus mark on the input through
  `inputProps`. The best match is a `TvSpotlight` and Cast is a `TvRow`. The television's season and
  profile choices are bare `Button`s that take the package's focus look without a class of their own.
- **Portraits and account marks are `Avatar`s.** A cast member, a person in Discover and the television's
  account marks draw their picture or their initials through the package, so a name such as "Claude (agent)"
  reads "CA". The subtitle-height slider on a television gives Up and Down to spatial focus. Site 0.85.0.

## [3.37.1] - 2026-10-15

### Changed

- **The site's stylesheets hold no literal colour and no off-scale radius, and Stylelint fails the
  build on one.** Borders are `--edge*`, pill corners `--r-pill`, and the player's white and black
  veils are `color-mix()` over `--art-fg-white`, `--kc-black` and the dark palette's `--canvas`,
  `--surface-1` and `--krystal-teal`, which resolve to the same pixels as the literals they replace.
  The colours that hold in every theme (a QR code's black, the ink under a poster, the subtitle
  preview's sample frame) are named once in `src/fixed-colours.css`, the one sheet the lint config
  exempts from the colour rules. Four corners between two radius tokens keep their pixel value behind
  a commented `stylelint-disable-next-line`. Site 0.84.1.

## [3.37.0] - 2026-10-14

### Changed

- **The site draws its remaining controls through the Krystal design system** (`@thekrystalship/krystal`
  and `-chat` 0.37.1). The player's people are `Avatar`s (a tone per person, one letter, the sheet sets the
  diameter, a status dot for a viewer who is buffering). The player's menus are `Menu`s of `MenuRadioItem`s
  (with a second line, and read-only for a guest's chapters) and `Popover`s for the episodes and the
  subtitle style, drawn inside the player's frame so they show while it is full screen and hung from the
  control bar; on a television they are the column down the frame's right edge. The subtitle style's choices
  are `RadioGroup`s, its colours a swatch `RadioGroup` with the browser's picker. Discover, Notify me and
  the new list's name are `SearchField`s, and the television's Discover field is an `Input`. Every control
  the site's own class shapes (the player's chrome, the remote, the film and episode tiles, the download
  mark, the season chips, the television's profile and show pills) is a `Button variant="bare"`. The
  design system's lint rules are errors. Site 0.84.0.

## [3.36.1] - 2026-10-14

### Fixed

- **A film opened on a page left open past its media ticket's expiry plays.** The player holds a ticket
  until an hour before it expires and fetches a new one then, measuring its life from the reply's new
  `expiresIn` so a device clock hours off the server's does not matter, and a media request refused
  with `401` on its ticket is sent once more on a fresh one. A TV that kept the site open overnight
  handed the server the previous evening's ticket, the playlist was refused, and the player sat on
  its buffering screen. Site 0.83.1.

## [3.36.0] - 2026-10-14

### Changed

- **The site draws its sheets, menus, choices and sliders through the Krystal design system**
  (`@thekrystalship/krystal` and `-chat` 0.35.0). Every drawer is krystal's `Drawer` (the player's menus
  `contained` in the frame, the remote's picker, the download, replace and remove sheets, the Notifications
  and Save to lists), the Save and Notifications panels on a wide screen are `Popover`s, Following is a
  `Menu`, a list's note and people are `Dialog`s, the library's and profile's segmented choices are
  `RadioGroup`s, the volume and the subtitle height are `Slider`s, and the mute, Close, Back and Reset
  controls are `IconButton`s and `Button`s. The television's arrow-key navigation is krystal's
  `createSpatialFocus`, with the site keeping its pages' own first focus, Back and Menu. Site 0.83.0.

## [3.35.0] - 2026-10-14

### Changed

- **The site runs on the Krystal design system's two packages**: `@thekrystalship/krystal` 0.32.1 for
  the components and `@thekrystalship/krystal-chat` 0.32.1 for the assistant. Every class the system
  draws carries the `k-` prefix, and the site's rules and selectors follow
  the names. The site's document rules sit in cascade layers ordered around the system's, and ESLint
  and Stylelint read the system's lint configs as warnings (`npm run lint:css`). Site 0.82.0.

## [3.34.1] - 2026-10-14

### Changed

- **The site runs on krystal-ui 0.31.0**: fonts are WOFF2, popovers are placed by Floating UI, and a
  Modal or Dialog traps focus, returns it, makes the rest of the page inert and stacks Escape. Site
  0.81.4.

## [3.34.0] - 2026-10-14

### Changed

- **The site's sign-in screens, labels and people lists are krystal-ui's own** (krystal-ui 0.30.0):
  the waiting, no-access, Activity-problem and first-sign-in screens stand on `CardScreen`; the
  preparing and not-out-yet labels, keywords and the bell's count are `Pill`s; the profile's Saved row
  and a saved list's people are `ListRow`s. Native controls take the palette's scheme, a prefixed field
  draws one focus ring, and the closed assistant dock casts no shadow.

## [3.33.3] - 2026-10-14

### Changed

- **A file the assistant cannot read says what to send instead** (krystal-ui 0.29.4): a PDF, an office
  document, a recording, a video or an archive each with its own sentence, and anything else that the
  assistant cannot read it. Site 0.81.3.

## [3.33.2] - 2026-10-14

### Fixed

- **On an Android phone, *File* opens the phone's file browser** (krystal-ui 0.29.2): Chrome there shows
  the camera and photos for an ordinary file picker, and the file browser only for the File System
  Access picker, which *File* now is. Inside the Discord Activity it stays the ordinary picker. Site 0.81.2.

## [3.33.1] - 2026-10-14

### Fixed

- **A phone can attach a text file to a message.** `+` opens *Picture*, the phone's photos and camera,
  and *File*, its file browser; one picker for both offered only photos and the camera on Android
  (krystal-ui 0.29.1). Site 0.81.1.

## [3.33.0] - 2026-10-13

### Added

- **Pictures and text files in a message to the assistant** (krystal-ui 0.29.0). `+` at the composer's
  left opens the picker, and a file pasted into or dropped on the composer is taken too. A picture is
  shrunk in the browser to 2048 pixels on its longer side, under about 3 MB, and is never refused for
  its size; each file uploads as it is attached, with its progress on its chip. A sent message shows
  its pictures as thumbnails that open full size, and its files as chips.

### Fixed

- **Confirming what the assistant proposes** sends the proposal's `handle`, as the wire contract names
  it.
- **The assistant's lists arrive as lists**: the conversations kept on the assistant join the ones on
  this device, and the slash commands and the memory page fill.
- **A turn whose connection drops is picked up again** from the last frame it delivered, and a token
  the assistant refuses is renewed once and the call sent again where it may be sent twice.
- The chat page makes no `POST /assistant/events/attach`, which nothing here serves.

Site 0.81.0.

## [3.32.1] - 2026-10-13

### Changed

- **The assistant takes `TheKrystalShip.Agent.Plugin` 1.0.0-dev.17**, which reads an upload to 8 MB
  whatever the host's own request limit. The site will shrink every picture to 2048 pixels on its
  longer side before it uploads it, a megabyte or so. Assistant 3.5.1.

## [3.32.0] - 2026-10-13

### Added

- **The assistant takes pictures and text files attached to a message** (`TheKrystalShip.Agent.Plugin`
  1.0.0-dev.15): `/assistant/attachments` uploads one, `/assistant/attachments/limits` says what a
  message may carry, a turn names its uploads, and a conversation's attachments read back. Both nginx
  halves, and the release's example, let that one location take up to 8 MB; every other route keeps
  its 1 MB. The site offers nothing to attach yet. Assistant 3.5.0.

## [3.31.0] - 2026-10-13

### Changed

- **On a phone the assistant is a page, `/chat`, among the tab bar's.** It opens, leaves, reloads and
  answers Back as every page does, and fills the screen down to the tab bar or the remote dock. It is
  drawn in krystal-ui 0.27.0's page layout: on the page's own background, so a black theme is black
  edge to edge; a way back to an empty conversation in its header and the conversations in a sheet;
  the opening suggestions as chips over a one-row composer, with Thinking and Read aloud behind the
  options beside the microphone; and the tab bar and the remote dock giving their room to the
  keyboard while typing. A wide screen keeps the dock beside every page, and has the assistant as a
  page at `/chat` with no dock beside it. Site 0.80.0.
- **A sideways swipe neither opens nor closes the assistant.**

## [3.30.3] - 2026-10-13

### Fixed

- **The assistant's dock shows one line at each edge.** The chat in the dock draws no frame of its
  own (krystal-ui 0.26.6), so its bottom edge no longer doubles the remote dock's or the tab bar's
  line. Site 0.79.3.

## [3.30.2] - 2026-10-13

### Fixed

- **A phone's remote dock stands flush under the assistant.** The dock takes its place from the tab
  bar's measured height, so on a phone whose screen is a fractional number of CSS pixels tall no line
  of the page shows between the assistant and the collapsed remote. Site 0.79.2.

## [3.30.1] - 2026-10-13

### Fixed

- **A television draws no scrollbar.** The page's column, and the Profile panel's, scroll only as focus
  moves, so the WebView's own scrollbar no longer flashes down the right edge while a page loads or
  under the splash. Site 0.79.1.

## [3.30.0] - 2026-10-13

### Changed

- **A phone's remote is one dock that a finger grows and shrinks.** It stands on the tab bar at every
  height, and the tab bar stays uncovered and live while it is open. Pushing the collapsed row up raises
  the dock's top edge with the finger, the row fading into the open remote as it grows over a scrim that
  darkens with it; pulling the open remote's head down, or its body while scrolled to the top, lowers it
  the same way. Let go, it settles open or collapsed by distance or speed. A tab tapped while it is open
  goes to its page and collapses the dock. `npm run verify:dock` measures it under a finger. Site 0.79.0.

## [3.29.0] - 2026-10-13

### Changed

- **A phone's player answers a finger the way the big streaming apps do.** A tap anywhere on the film
  shows or hides the controls and never pauses it; play and pause is a large button in the middle of
  the film with the skips either side, and pressing it leaves the controls up a moment. The film moves
  from the scrub bar and by two taps at a side, no longer by a sweep across the picture. The row under
  the bar is Lock, Episodes, Audio & Subtitles (one menu for both), Settings (Chapters and Quality) and
  Next episode, with no volume button. The bar ends with the time left and thickens under the thumb,
  every control in the top bar is a thumb wide, and Invite is an icon. Lock puts the controls away and
  ignores touches until Unlock. Site 0.78.0.

## [3.28.0] - 2026-10-12

### Changed

- **A television's remote drives the player the way the big streaming apps do.** While the controls
  are away, OK pauses or plays and leaves focus on play, and Left or Right skips and brings the scrub
  bar up seeking, with a preview of where the skip lands, so the presses after go on skipping. With
  the controls up, focus on the scrub bar no longer moves the film: OK there starts seeking, Left and
  Right then skip, and OK, Back, Up or Down stop it. Down from the scrub bar goes back to the control
  focus came up from, or to play, rather than to whichever button sat under the bar's middle. A
  focused scrub bar lights its knob alone instead of an outline round the whole bar, and a seeking one
  grows it. Site 0.77.0.

## [3.27.2] - 2026-10-12

### Fixed

- **On a television, focus goes where the remote points.** The profile opens on its list of sections,
  on the one open, and Back from it returns to the bar's profile button it was opened from, without
  focus passing through the page being left on the way. Down out of the bar goes into the page where
  it was left, or where it starts, rather than onto whatever card sits under the bar's item. A row
  too short to scroll remembers its card as a long one does, so coming back into it lands on the card
  it was left on. Site 0.76.2.

## [3.27.1] - 2026-10-12

### Fixed

- **On a television the player's controls stay up while a remote walks them.** Every press of OK or
  an arrow among the controls holds them up afresh, and they wait six seconds before going rather
  than under three, so reaching the subtitles a press at a time no longer has them go mid-walk and
  turn the next Left or Right into a skip that moves the room. Site 0.76.1.

## [3.27.0] - 2026-10-12

### Changed

- **Download is offered on phones and tablets only.** A computer's browser shows no Download on a
  film's or a show's page, no Downloaded filter in the library and no Download quality in Playback:
  people new to the site took Download beside Watch for the way to watch a film. A phone or a tablet
  is told apart by its main pointer being a finger, so a tablet with a keyboard and trackpad keeps
  Download and a laptop with a touch screen does not. A television never offered it. Site 0.76.0.

## [3.26.5] - 2026-10-12

### Fixed

- **Carrying on with something from Home puts it on the television while the phone drives one.** The
  cards of Home's Continue watching row show the television's glyph and do what the remote's own row
  does, rather than starting the film on the phone. Site 0.75.5.

## [3.26.4] - 2026-10-12

### Changed

- **Moving down through Discover on a television stalls less.** The posters inside its mood, franchise
  and browse tiles are square-cornered there (krystal-ui 0.26.5): drawing an image with rounded
  corners at the panel's resolution was the most expensive part of the tiles. On the reference Fire TV
  the worst stall moving through Discover is 100 to 140 ms rather than 237 to 253. Site 0.75.4.

## [3.26.3] - 2026-10-12

### Changed

- **Moving between a television's page and its bar is smooth.** With focus in the bar the page steps
  back under a veil of the canvas that fades in, rather than the page itself fading, which carried
  the whole page as one layer at the panel's resolution through the fade; it looks the same. Every
  card in a row is rendered and asks for its picture a quarter of a screen ahead (krystal-ui 0.26.4).
  On the reference Fire TV the worst stall going into or out of the bar is 80 to 100 ms rather than
  220, the sweep's 26 moves miss a frame budget 22 to 26 times rather than 33 to 38, and opening
  Discover, Movies, TV Shows, Home, a film and coming back takes 2.9 s to focus rather than 3.9.
- **A television's Home holds its top's place while the top's slides are described,** so its rows
  never move under it as the page arrives, and none of them asks for pictures from where they first
  stood. Site 0.75.3.

## [3.26.2] - 2026-10-12

### Changed

- **Moving through the rows on a television is smooth.** Focus there is lit by the glow alone, drawn at
  once: a focused card, tile, button, face, list or phone keeps its lift and its glow without the
  drop shadow under it, which the television's GPU painted again on every frame of the lift
  (krystal-ui 0.26.2). On the reference Fire TV, Home's and Movies' rows miss a frame budget 31 to 41
  times over 28 moves rather than 99 to 103. Site 0.75.2.

## [3.26.1] - 2026-10-12

### Fixed

- **Moving between Home and Discover no longer shows both tops at once.** A page navigated to is
  prepared out of sight, and the slide shown at its top made itself visible inside it, so for a moment
  its words and actions were drawn over the page being left, and its actions could be reached with Tab.
  The page being prepared is transparent and `inert` as well as hidden, which nothing inside it can
  undo. `npm run verify:stage` records every frame of those moves on a desktop and a phone and fails on
  anything of the page being prepared that is seen or reachable. Site 0.75.1.

## [3.26.0] - 2026-10-12

### Added

- **The site opens on a splash.** The mark on the canvas is drawn with the page's first bytes, the same
  drawing as the iOS launch screens, the installed app's launch screen and the television app's
  background, so it takes over from them unseen. It stays while the session is restored and the first
  page is prepared beneath it, until the page's first reads are answered and the artwork on its first
  screen has decoded, then fades; it is gone three seconds after opening whatever is still coming. On the
  reference television, opening Home draws something at 288 ms rather than 1824, and the page is seen
  arriving once rather than rearranging six times.

### Changed

- **The top of Home and of Discover arrives with its page.** Its slides are chosen and described in the
  same pass as the page's other reads, not two drawings later, and the first slide is one of the reads a
  page waits for before it is shown. Every slide is laid out once it is described and turned to once its
  artwork has arrived, so a later slide no longer makes the top taller under the rows. The artwork of the
  slides after the first is fetched behind what is on screen.
- **A page navigated to waits only for the artwork it shows.** A row's cards past its right edge, which
  load only when the row is scrolled, no longer hold the page to the half-second cap.
- The dark screen before a session is known is the canvas colour rather than black.
- Site 0.75.0.

## [3.25.1] - 2026-10-12

### Changed

- **A television page's first focus is found without measuring the whole page.** Until a key is pressed, a
  page's focus follows its first control as its content arrives, and every change asked again: each time,
  every control on the page was measured for whether it is drawn. Now the controls are looked at in order
  and the first that qualifies is taken, and a page come back to measures only the controls that carry the
  name it was left on. Where focus lands is unchanged (`npm run verify:tv`). In `npm run verify:tv-perf`'s
  lab, opening Discover blocks the page for 956 ms rather than 1131 and loading it for 817 rather than 1113.
  Site 0.74.1.

## [3.25.0] - 2026-10-12

### Changed

- **Discover's first screen comes in one answer.** `/api/discover` carries `shelves`, the spotlight's shelf
  and the first three shelf rows' as their own route answers them, and `films`, the spotlight's films as
  `/api/films/{imdbId}` answers them, each when it answers within 400 ms; the page draws from them and asks
  for any other by its route. The spotlight's shelf, its films and the first rows were each asked for after
  the index arrived and drawn, and the spotlight's shelf twice; on a television each wave was drawn before
  the next was asked for. In `npm run verify:tv-perf`'s lab, opening Discover from the bar is ready at
  2.5 s rather than 5.1, with 37 requests and 3.0 MB rather than 80 and 6.4 MB, and its layout shift is
  0.27 rather than 0.57. On a computer the first rows draw at once in the same way. Site 0.74.0.

## [3.24.5] - 2026-10-12

### Changed

- **Discover's tiles fetch their posters within half a screen of being seen.** The site takes krystal-ui
  0.26.1. On the reference television the browser's own lazy loading reached nearly all of Discover, a page
  under four screens tall; tiles further down and along a rail now wait until they near the screen. Site
  0.73.5.

## [3.24.4] - 2026-10-12

### Changed

- **Discover on a television fetches the posters near the screen, not every tile's.** The mood, franchise
  and category tiles take krystal-ui 0.26.0, whose `CollectionTile` loads its posters lazily. Opening
  Discover from the bar in `npm run verify:tv-perf`'s lab fetches 80 files and 6.4 MB rather than 143 and
  11.8 MB, and the longest frozen frame is 783 ms rather than 1850. Site 0.73.4.

## [3.24.3] - 2026-10-12

### Changed

- **The remote's controls are two fixed rows.** Above: back ten seconds, previous episode, Play or Pause, next
  episode, forward ten seconds (a film has no episode steps, so three controls). Below: Episodes, Subtitles,
  Audio when the film has several audio tracks, Quality when it has several rungs. Each row is as wide as the
  remote's bottom block at every phone width, for films and episodes alike, its first and last control on the
  block's edges and the gaps between its controls equal; every control keeps its 44px size and Play its round
  shape. This replaces the grouping and wrapping of 3.24.2. Measured in `npm run verify:tv` (check `row`) at 360,
  390 and 412px for a film and an episode with several audio tracks; every remote view was run on three
  iPhones and a Galaxy (44 views, 0 faults). Site 0.73.3.

## [3.24.2] - 2026-10-12

### Fixed

- **The remote's control row no longer overflows a phone.** An episode of a film with several audio tracks
  and rungs has nine controls (Play, back and forward ten seconds, previous and next episode, Episodes,
  Subtitles, Audio and Quality), which at a thumb's 44px is wider than 360, 390 and 412px phones: the last
  buttons ran outside the drawer and the row scrolled sideways. The menu buttons now stand together and move
  to a second line when the row cannot hold them, never clipped or scrolled, each still 44px. Measured in
  `npm run verify:tv` (check `row`) at the three widths; every remote view was run on three iPhones and a
  Galaxy (44 views, 0 faults). Site 0.73.2.

## [3.24.1] - 2026-10-11

### Fixed

- **A stale echo no longer shows after a newer choice.** Choosing 720 then 1080 in a moment showed 720 again
  when the first write's push landed. Every write to `/api/me/preferences` and `/api/me/tracks/{titleId}`
  is now stamped with a `revision` that follows the order the database kept the writes in (answered and
  pushed), and a player or a phone's remote applies a push only when it is newer than what it holds, holding
  one that arrives during its own write until that write has answered. Measured in `npm run verify:tv` with
  the television's pushes delayed: the quality label showed 818, 546, 818, 546 before and 818, 546 after.
  Site 0.73.1.

## [3.24.0] - 2026-10-11

### Added

- **The phone's remote changes the television's own settings.** Subtitles, Audio (only for a film with more
  than one audio track) and Quality join the control row, each the player's own menu aimed at the television
  with a back arrow to the remote; Style opens the subtitle style editor, whose back arrow returns to the
  subtitle list. The labels and the checked rows read the person's records from the API and are kept current
  by a push, and where the person chose nothing for the film they show what the film starts on. Quality reads
  "Auto" or the fixed height, never the height playing. While style changes keep arriving the television
  shows its sample line and lets it lapse four seconds after the last. Site 0.73.0.
- **The subtitle and audio track are kept per person and film by the API**, the same on every device:
  `GET` and `PUT /api/me/tracks/{titleId}` (`FilmTracks`), in the new table `viewer_tracks`. The player
  writes them there and reads them when a film starts, so a track chosen on the phone, the television or
  the site is the one every device plays, and a film nobody chose tracks for starts on the person's
  languages. A guest, who has no account, and a film saved on a device played alone with no connection keep
  theirs in the browser.
- **A change to how a person watches is pushed to their connections** (`PreferencesChanged`,
  `TracksChanged`, to every connection of the person's with an account, never to the room): a playing player
  follows a track, a quality or a style chosen on another device, and a phone's open remote follows the
  television's own menus. The room's state and roster are untouched. Measured in `npm run verify:tv`.

### Changed

- **The API refuses what the film or the ranges do not have.** A track the title's manifest does not list
  (a subtitle that can be shown, an audio track) is a `400`; so is a quality that is neither Auto nor a height
  from 1 to 4320 and a subtitle style outside the ranges it keeps (`size` 50 to 200, `font` and `edge` among
  their sets, colours as `#rrggbb`, `backgroundOpacity` 0 to 1, `position` 0 to 30), which were bent into
  range before and are refused now. The player's own setters check what arrives the same way: an unknown
  subtitle id no longer turns the subtitles off and is no longer kept as the film's choice, and an unknown
  audio id is not kept.

### Upgrading

- The database gains the table `viewer_tracks`, declared with the others in `Data/Schema` and not added to a
  database that already exists: create it with the statement there before starting this version on one.
- Track choices made before this version, which each device kept for itself, are not carried over: a film
  starts on the person's languages until a track is chosen for it again.

## [3.23.3] - 2026-10-11

### Fixed

- **A phone's Assistant shows no resize handle.** The site takes krystal-ui 0.25.1, whose dock hides its
  resize tab at phone widths, where the dock is the whole screen and there is nothing to drag. Wider, the
  dock keeps the handle. Site 0.72.3.

## [3.23.2] - 2026-10-11

### Fixed

- **The collapsed remote no longer covers the assistant's input.** The strip takes room above the tab bar,
  so everything that leaves room for the bar leaves room for the strip too: the page column's bottom
  padding, the assistant dock's bottom edge and the toasts' all add `--remote-strip-inset`, the strip's
  measured height (plus the pixel by which the bar is taller than the inset krystal-ui reserves for it),
  set while the strip is shown and followed as it resizes. Measured with the strip let down: the assistant's
  input ends above the strip, and the last element of Home, Movies, Discover, Profile and a title page
  scrolls clear of it. Site 0.72.2.

## [3.23.1] - 2026-10-11

### Fixed

- **The collapsed remote stands flush on the tab bar.** Its bottom was the bar's height plus a pixel
  worked out in CSS, which a bar's border and a phone's fractional pixels moved; it is now placed from
  the bar's measured top (and follows its resizing). Measured at 1x, 2x, 2.625x and 3x from the rects
  and the screenshot's pixel rows.
- **The strip and the open drawer have equal space above and below their content.** The strip's row is
  12px from its top edge and from the bar; the open drawer's head and its controls are as far from the
  edges as each other.
- **A way back from every screen inside the remote.** The TV picker and the Episodes list carry the
  drawer's own back button ("Back to the remote"), as the subtitle style editor's does, and choosing a
  television returns to the remote. The back button's hit area is 44px.
- **A film is named without its year** on the strip, the drawer and the question before it is replaced
  (the year stands in the eyebrow, as the player's top bar has it). Site 0.72.1.

## [3.23.0] - 2026-10-11

### Added

- **A paused television shows a phone glyph while a remote drives it**, beside who is watching. The
  screen is told how many remotes drive it (`RemotesChanged`, and `Remotes` when it connects) on its own
  group, never in the room's roster. Measured in `npm run verify:tv`: it appears when the phone joins as a
  remote, goes while the film plays and when the phone leaves, and returns when it rejoins. Site 0.72.0.

## [3.22.0] - 2026-10-11

### Added

- **The phone's remote.** While remote mode is on and a paired television is connected, a drawer is let
  down above the tab bar as its strip (the still, the television's name, what plays, Play or Pause);
  tapping it or pushing it up opens it, pulling its handle down collapses it, and swiping it sideways
  turns remote mode off with Undo. Open, it names the television (which opens a picker of the paired ones,
  "Off" for one not connected), holds the Remote switch and shows the still with Skip intro, what plays, and
  the player's own scrub bar with its preview and its controls: Play or Pause, back and forward ten
  seconds, and for an episode previous, next and Episodes, the player's list aimed at the television. The
  still takes the player's touch handling (tap, double tap, sideways sweep; vertical sweeps off) and a
  gesture the television took vibrates the phone. With nothing playing it offers Continue watching, each
  card Watch on TV. Measured with real touch events (`npm run verify:tv`, check `remoteui`); a real
  thumb and iOS Safari are not.
- **Watch on TV.** With remote mode on a title's main button is Watch on TV, with Watch (phone glyph)
  beside it, which turns remote mode off. `WatchOnScreen(room, titleId, startSeconds)` on a remote's
  connection tells a television in no room to open the room, as the site's Watch writes the address, so
  its Back returns; a television in a room holding nothing is loaded, one already on the title is left
  alone, one mid-film is asked about first on the phone and one in the closing stretch is replaced at
  once. A title put on plays. A film the library lacks goes through Watch now with the television's room.
  The phone says the television did not answer when its next announcement does not show the room.

### Fixed

- **A paused room whose second seek arrived during the first stayed where the first left it.** The
  player dropped a seek that arrived while another was running, and a paused room has no drift
  correction. A paused playhead not where the room stopped is now seeked there. A skip pressed again
  before the room answers the last is measured from where that one was going. Site 0.71.0.

## [3.21.0] - 2026-10-11

### Changed

- **Every drawer on the site is pulled down by its head.** `Drawer`'s head, the tab, the title and
  its actions (or a `head` of its own, as the download sheet's), follows the finger down and, let go
  past 30% of the drawer's height (between 40px and 120px) or at 0.5px/ms, calls `onPull`, which
  closes it unless the drawer passes its own; shorter, it settles back open. The head holds
  `touch-action: none` and the drawer `overscroll-behavior: contain`, so a pull starts no browser
  scroll or pull to refresh and a body's scrolling passes nothing on. Reduced motion settles at once.
  Drawers have square corners, are fully opaque (they were 96%, letting the page show through), and
  wear a 40x6 tab along the inside of the top edge, `--border-strong` at rest and teal under a pointer
  or while held; the download sheet's drawer takes the same tab and corners. The assistant dock's
  resize handle is the same tab, standing, from krystal-ui 0.25.0. Measured with real touch events
  in Chromium (`npm run verify:pull`); the thumb's own speed and the phones' pull to refresh are not
  measured. Site 0.70.0.

## [3.20.0] - 2026-10-11

### Added

- **A paired phone drives its television's room as a remote.** `JoinAsRemote(screenId)` on the hub
  makes a phone's connection a remote of a screen it is paired with, in the room that screen is in,
  decided by the server from the token and the pairing and checked again on every act. It plays,
  pauses, seeks, loads a title and moves between episodes like any member, and is in nothing a room
  counts: not the roster, the head count, `/health`'s `watching`, the hold, the transcode plan or the
  watch record. `Buffering`, `ReportQuality`, the door and the other ways into a room refuse it
  (`RemoteGate`, `[RemoteAllowed]`). It follows its screen from room to room (`RemoteMoved`) and is in
  none while the screen is, and unpairing from either side drops it. An act names the person alone.
  Site 0.69.0: `remoteRoom.js` holds the connection and the room's commands, and `remote.js` drives the
  first connected paired screen while remote mode is on (`useRemoteMode`, `useRemoteRoom`,
  `chooseScreen`).
- `node verify/resume.mjs` measures how quickly a phone's remote is live again after its page was away,
  and `node verify/resumePhone.mjs` does the same on a real phone's Chrome over USB debugging.
  `npm run verify:tv` drives a room from a phone with nothing on screen.

### Changed

- **A page that comes back replaces its dead sockets at once.** A person connection (a phone's, a
  television's) that is not connected when the page becomes visible, is restored from the cache or the
  network returns is replaced immediately, and one that says it is connected is asked for the server's
  clock and replaced when two seconds pass unanswered. A phone offline for 45 s waited 26 s for
  SignalR's reconnect schedule once the network was back; it is live again in about a tenth of a second.
  Site 0.69.0.

## [3.19.1] - 2026-10-11

### Fixed

- **Unpairing a television's last phone leaves focus on Remote.** Focus that loses its element lands
  on the nearest control as before, and when that is in the side column it lands on the section's own
  place there, as entering the column does; it went to whichever place in the list sat nearest, and OK
  there opened another section. Measured on the Fire TV, where it landed on Requests. Site 0.68.1.

## [3.19.0] - 2026-10-11

### Added

- **A phone pairs with a television by scanning its QR code.** The television shows the code in
  Profile's Remote, with the phones paired with it and Unpair on each, and beside the paused player's
  card. The code opens `/p/<code>` on the phone, which signs in if it must, pairs that session with the
  television at once, turns remote mode on and goes Home with a toast carrying Undo. A phone of another
  person is refused. The code is signed rather than stored, names its screen, person and expiry, lives
  five minutes and is replaced every minute. Pairings are kept in the database by the phone's and the
  screen's anchor sessions, end when either side unpairs, and are forgotten once a session has gone
  unseen for longer than the provider keeps an unrenewed one (`Oidc:SessionLifetime`). Site 0.68.0.
- `GET`, `POST /api/me/screens` and `DELETE /api/me/screens/{id}` for a phone's pairings; `PairingCode`,
  `Phones` and `Unpair` on the hub, and `PhonesChanged` to a screen.

### Changed

- **A screen is heard by the phones paired with it, not by every connection of its person.**
  `ScreenArrived`, `ScreenChanged` and `ScreenLeft` go to the paired sessions alone, `Screens` answers
  the screens the caller's session is paired with, and `ScreenLeft` means the pairing ended.
- **Left and Right from a television's side column reach the page beside it** when nothing is on the
  line focus is on, so a section whose controls sit at its top is reached from any place in the column.

### Upgrading

- The database gains the table `screen_pairings`, declared with the others in `Data/Schema` and not
  added to a database that already exists: create it with the statement there before starting this
  version on one.

## [3.18.1] - 2026-10-11

### Fixed

- **A list's page on a television is headed by the list's name.** The page read the name from the
  wrong place in the API's answer and drew an empty heading; it reads the list from the answer's
  `list`, and the heading is a class of the site's own, in the size Profile's title has, instead of
  the spotlight's title class. A list with no titles shows its name and no count; the count of
  titles shows once there is one. `verify:tv` checks a list's name is visible and an empty list
  carries no count. Site 0.67.3.

## [3.18.0] - 2026-10-11

### Changed

- **An episode a room moves past stops downloading when only that room asked for it.** Every pick of
  a show's episode says on its torrent why it is fetched: the room waiting on it, or `keep:` for a
  follow, a device, the next episode fetched ahead or a download asked for outright. A room that
  moves on from an episode still arriving leaves `passed:` on the torrent, and the hand-off switches
  the file off unless another room waits on it, something kept it, or its transcode has begun. The
  episode asked for most recently is the one the torrent client fetches first.
- **Next, Previous and an episode picked from the menu take one step at a time.** The player offers
  no second press until the room has moved or the step was refused, and the server drops a step that
  arrives while another is being taken in the same room. Site 0.67.2.

### Fixed

- **An episode is started once the beginning of its file has arrived**, measured from the file's
  first byte, rather than once a share of it has arrived anywhere in it; the container's header is at
  the start, and waiting for it ran out on episodes whose share had arrived elsewhere.
- **A failure while an episode's file was still arriving is not final.** The episode is tried once
  more when its file is whole, and the torrent is not marked failed while any episode waits for that.
- **A torrent marked failed is tried again when somebody asks again.** Asking withdraws
  `ingest-failed` and the hand-off forgets that torrent's failures, where before the verdict stood
  until the hand-off restarted; and a room waiting on an episode that can play is loaded whatever the
  torrent says.

## [3.17.1] - 2026-10-11

### Fixed

- **An empty Saved, or an empty list, on a television is no longer a blank page.** An account with
  nothing saved, and a list with no titles, show the empty state the phone's list page uses, "Nothing
  saved yet" over a Discover button, centred under the bar. Discover takes initial focus, so the first
  key press lands on the page, and Up and Back still reach the bar from it. `npm run verify:tv` checks
  the three pages with people who have nothing saved. Site 0.67.1.

## [3.17.0] - 2026-10-11

### Added

- **The server knows each person's television screens.** A hub connection signed in through the
  television client with an anchor session is a screen; the server keeps its name, whether it is
  connected, the room it is in and what that room holds. A person's other connections ask for them
  (`Screens`) and hear a screen arrive, change and leave (`ScreenArrived`, `ScreenChanged`,
  `ScreenLeft`), and nobody else does. Every connection with an account joins its person's group; a
  guest's joins none. The television holds a connection of its own,
  in no room, for as long as it is signed in, so it is a screen outside a room too. Site 0.67.0.

## [3.16.0] - 2026-10-11

### Added

- **A television signs in under its own name.** The TV's code request carries the name its owner gave
  it, which the provider's approval page offers to keep or change; the name decided travels with the
  TV's session. Needs tks-auth 4.7.0 for the name to be used, and the TV app 1.1.0 to have one to send.
  Site 0.66.0.

## [3.15.0] - 2026-10-11

### Added

- **The player says what is playing.** An (i) at the end of its top bar, or `i`, opens the title's page
  beside the film: its heading, score, story, credits, cast and facts, an episode first and its show
  after it, with nothing in it that leaves the room. On a wide frame it is a pane down the right edge
  and the controls make room for it; on a phone held upright it is a sheet from the bottom. The film
  plays on, and it is the viewer's own: nobody else in the room sees it open.
- **A room left paused a few seconds says what is playing** over the left of the film: the name, an
  episode's place and name, the year, rating and length, the story and who is in it, gone as soon as
  the film plays. A guest sees it too, from what the room's film says of itself. Site 0.65.0.

## [3.14.1] - 2026-10-11

### Changed

- **A title page's poster fills its hero's height.** The hero is two columns, the poster and everything
  else: the text takes a width set by the hero alone, and the poster stands as tall as the text beside
  it at its own 2:3, as wide as that needs, held to the room the text leaves. A hero narrower than 900px
  keeps the poster at 220px wide, at the top. On a phone the poster spans the page with everything else
  under it. Site 0.64.1.

## [3.14.0] - 2026-10-11

### Added

- **A film's and a show's page carry everything the film database describes them with**: the tagline
  above the overview, the original title among the details when it differs from the title, and the
  keywords in a section of their own. Library films, films found in Discover and shows draw them alike.

### Changed

- **The actions on a film's page come in one order and one style**: Watch, then Save, then Download,
  whether the library holds the film or not. Download is drawn like Save and Follow, and takes the
  accent once the film is on the device, as Save does once saved. Site 0.64.0.

### Removed

- **Black and white is no longer a fact of its own.** `GET /api/films/{imdbId}` and
  `GET /api/shows/{imdbId}` drop `blackAndWhite`; the keyword stays in `keywords` like any other.
  The assistant's `title_details` no longer writes a black-and-white line, and `find_by_clues` matches
  a look against keywords and genres the same way for every word.

## [3.13.2] - 2026-10-11

### Changed

- **A film's page draws the same description whether the library holds it or not.** A film found in
  Discover shows its year, certification and runtime under its name, and a Details section with its
  release year, languages, countries and IMDb id, as a library film's page does; the library adds only
  what a held copy knows, its picture and when it was added. Both name an anime film Anime movie, the
  word its card uses. A runtime reads the same everywhere, as `2h 24m`, in a download sheet and on an
  episode's row too. Site 0.63.2.

## [3.13.1] - 2026-10-11

### Changed

- **A poster card names a film Movie**, and Anime movie on the anime shelves, the word the rest of the
  site uses. Site 0.63.1.

## [3.13.0] - 2026-10-11

### Added

- **A poster card says what it holds**: Film, or Series in the accent, on the poster above the name,
  Anime film or Anime series on the anime shelves, on every card the site draws: the library, Home,
  Discover, Saved, Notify and the history. A show's card in the library draws a short bar per season
  along the poster's foot, each filled as far as the person watched it, and gives its rating, seasons
  and episodes; a film's keeps its rating, year and length. Site 0.63.0, on krystal-ui 0.24.0.
- **`GET /api/shows` carries `seasonProgress`**: `[{season, episodes, watched}]` for each season the
  library holds, in order, with how many of its episodes the asking person watched through.

## [3.12.0] - 2026-10-10

### Added

- **Every page that sorts opens in the order the person last picked on it**, on the site, in the
  Activity and on a television alike: every list (Watch later among them) shares one order, Movies and
  TV Shows one each with their anime pages, every Discover shelf one, and the filter one, which an
  address's own `sort` overrides. The device keeps the last answer, so a page opens in that order
  before the API answers and with no connection. A television's list is drawn in the list order. Site
  0.62.0.
- **`GET /api/me/sorts` and `PUT /api/me/sorts/{page}`**: the order kept for each page, `{key,
  direction}`, by the site's name for the page, at most 32 pages a person.

### Changed

- **The database is at schema version 2**, which adds `viewer_sorts`. A file at version 1 is brought
  forward when the API starts, keeping everything it held.

## [3.11.0] - 2026-10-10

### Changed

- **A room is open from the moment it is made**, wherever it is made: the site, the Activity, the
  bot or Watch now. Anybody who may watch finds it in the listing and walks in, and somebody in it
  closes it to keep it to the people already let in. A room already closed stays closed.

## [3.10.0] - 2026-10-10

### Added

- **A poster card saves its title without opening it.** Every film and show card, in the library, in
  Discover and in the Anticipated row, carries a ribbon hanging from its top edge: a press on one not
  saved puts it on Watch later, and on one saved opens the person's lists beside it, the same lists the
  Save button opens. A saved title's ribbon is filled and always shown; one not saved shows on the card
  a mouse is over or that holds focus, and always on a touch screen. Every card, the Save button and
  the Saved page read one set of what the person saved, so a title saved anywhere is marked everywhere
  at once, and a save made on another device shows the next time the page is looked at. The ribbon is
  krystal-ui's `ShowcaseCard` `ribbon` (0.23.0). Site 0.61.0.
- **`GET /api/me/saved`**: the IMDb ids on every list the person can save to, each once.

## [3.9.0] - 2026-10-10

### Changed

- **Home opens on slides that turn by themselves**, as Discover's spotlight now does too, on a
  computer, a phone and a television: the film people are watching together, the newest film, the
  title most watched here lately, the film and the show most popular this week, and the film most
  waited on here (or else the next one out), each with its artwork, facts and story and what its own
  page offers. A slide turns every 8 seconds, the mark under it filling meanwhile; the slides hold
  still under a mouse, under keyboard focus, on a hidden page and while focus is in a television's
  rows, never turn by themselves under reduced motion, and turn by a swipe. The top keeps one height
  as it turns, and the artwork fades from slide to slide. With no connection, Home turns through the
  films downloaded last. The turning is krystal-ui's `useCarousel` and `CarouselPager` (0.22.0).
  Site 0.60.0.

## [3.8.0] - 2026-10-10

### Changed

- **Discover is a page to browse, the same on a computer, a phone and a television.** The mood chips,
  the row of buttons and the nine tabs give way to the page's own rows: a spotlight on a film from the
  season's shelf (Watch, More info, Surprise me), the shelves as rails of posters, the moods as a rail
  of tiles that fan three of each mood's posters, the franchises as tiles that lay each series' films
  out in order and follow the person through it (the films ahead dimmed, a segment per film, the next
  named), and the browse categories as a grid of tiles: Moods, Movies, TV Shows, Genres, Themes, World
  cinema, Franchises, Studios and Directors, each its own page at `/discover?browse=`. Filters sits
  beside the field and opens the filter; a shelf's page and the filter use the library's Toolbar, with
  Watched among the filters and Surprise me beside the count. The tiles are krystal-ui's
  `CollectionTile` (0.21.2). `/api/discover` answers the page's rows, the categories with their
  posters, and each franchise's progress for the person. API 3.5.0, site 0.59.0.

## [3.7.0] - 2026-10-10

### Added

- **A host chooses which disk a transcode is written to.** `Handoff.TranscodeDisk` is `Media` (the
  default) or `Cold`; the chosen disk takes the transcode when it has the room and the other one
  when it does not, and cold storage is used only while its volume is present. `Cold` keeps a
  transcode that runs alongside its download off the disk the download is landing on, and the film
  never has to settle. hotbox runs `Cold`. Hand-off 0.19.0.

## [3.6.0] - 2026-10-10

### Added

- **Discover is where to go not knowing what to watch.** With nothing typed it holds about 150
  shelves under nine tabs: For you (the season's shelf, Because you watched for the last two titles
  finished, trending today and this week, Popular here, a director a day, in cinemas, new releases,
  coming soon, Ready to watch), Moods (Sunday chill, Date night, Mind-benders, Epic weekend and
  more), Movies (top rated, hidden gems, classics, each decade's best), TV Shows (binge-worthy,
  miniseries, sitcoms, K-dramas, by network), Genres, Themes (heists, time travel, slashers,
  cyberpunk and more), World cinema (Spanish and Catalan among them), Franchises in release order,
  and Studios and directors. The moods are chips over the tabs; Surprise me opens a random title
  nobody watched; Filter narrows by kind, genre, decade, length, rating, language and order; Ask the
  assistant opens the dock; Hide watched is kept on the device. Each shelf is a rail of 20 asked for
  as it nears the screen, with See all for up to 40. `/api/discover` and
  `/api/discover/shelves/{id}` serve them, every shelf the same for everybody asked again each hour
  ahead of anybody (`Tmdb.ShelvesWarmMinutes`), and `/api/catalogue/discover` takes `minRuntime` and
  `maxRuntime`. API 3.4.0, site 0.58.0.

## [3.5.0] - 2026-10-10

### Changed

- **A room made through Discord is open.** A room whose maker came through the Activity or the bot
  (the token names `Oidc.ActivityClientId` or `Oidc.BotClientId` as the client acting for them) is
  open from the moment it is made, so everybody in a call is in the call's room without knocking, and
  the Activity's bar and its pull into the player reach all of them. Rooms made on the site stay
  private, and arriving through Discord opens no room that already stands. API 3.3.0.

### Fixed

- **Watch on a film while a room plays another switches the room.** Joining applied the room's state
  before reading the film the address named, which spent it, so the room went on with what it held.
  The film and where to start in it are read first. In the Activity this was every Watch after the
  first. Site 0.57.1.

## [3.4.0] - 2026-10-10

### Changed

- **The Discord Activity is the whole site.** It signs in through Discord as before and then shows
  Krystal Cinema itself: browsing, lists, shows, Discover, profile and the assistant, with the
  Discord sign-in held as the site's session. Its one room is the call's, so every Watch, Resume and
  Watch now plays there in place of whatever the call held, and the player's back arrow returns to
  the page the film was opened from while the film plays on for the call. A bar above every page names
  what the call is watching, with Watch; the call being given something else opens the player for
  everybody browsing. It opens on the player when the call is already watching, and on Home
  otherwise. Download, the service worker and other rooms are not offered there, and links off the
  site open outside Discord. Site 0.57.0.
- **A launch that moves a room holding another film starts it where the launch says**, as one
  filling an empty room does.

## [3.3.0] - 2026-10-10

### Added

- **Anticipated: the films people are waiting on, for everybody to see.** `GET /api/anticipated`
  lists every wish that is neither out nor in the library, most waited on first, with how many wait on
  it and whether the person asking is one of them; it names nobody and takes `cinema:wish`. The site's
  front page and the television's carry it as an Anticipated row after Recently added, each card
  leading to the film's page, where the person can wait on it too; the count reads as the film's page
  says it. The assistant answers "what are people waiting for" with an `anticipated` tool. API 3.2.0,
  Core 4.5.0, assistant 3.4.0, site 0.56.0.

## [3.2.0] - 2026-10-10

### Changed

- **The player's invite panel is one column of aligned sections**: the room row is a lock badge, the
  room's state and a switch that opens or closes it, with the room link beneath once open; Guests
  carries how many are in and until when on its heading, the guest link or the button that asks for
  it, and Stop guest access; Invite people lists everybody on the same gutter. Every control is a
  krystal-ui `Button` or `Toggle`, the room link and the guest link are the same row, and the panel
  scrolls within the frame. Site 0.55.0.

## [3.1.0] - 2026-10-10

### Removed

- **A title's shelf is the rule's alone.** `PUT /api/categories/{imdbId}`, the `title_categories`
  table and the Move to Anime / Move to Movies|TV Shows button on a film's and a show's page are gone:
  a title is anime when the film database files it under Animation and it was made in Japanese, and
  nobody shelves one by hand. API 3.1.0, site 0.54.0.

## [3.0.0] - 2026-10-10

### Changed

- **The API keeps everything in one SQLite database, `moviebot.db` in its state directory**, set by
  the new `Database:Path`. Every person's records (names, preferences, notification switches,
  progress and last plays, lists, follows and announced episodes, the bell, push subscriptions, saves
  and wants, wishes), the categories set by hand, the rooms, the issuer pin, the push key pair, the
  fetched subtitles' descriptors and pins, and every answer of the film database are rows; only
  served bytes stay files (fetched WebVTT, artwork). Each change is one transaction, so a rule that
  reads before it writes (a list's limit, a notification's key, the first poster pin) can no longer
  race, and nothing is rewritten whole on every change. The `.src` marker beside every kept image is
  a row in `images`, and poster pins are rows in place of `posters/`. Raw `Microsoft.Data.Sqlite`
  keeps the API Native-AOT; the native SQLite ships beside the binary. API 3.0.0.
- **Upgrading needs a hand-made carry-over.** The API reads no JSON state file: started against a
  state directory of `rooms.json`, `watch.json`, `lists.json` and the rest, it starts with an empty
  database beside them and none of what they held. The settings `People:*`, `Notifications:Inbox`,
  `Notifications:Devices`, `Notifications:Keys`, `Notifications:Preferences`, `Follow:Path`,
  `Lists:Path`, `Offline:Path`, `Notify:Path`, `Library:Categories`, `Rooms:Journal`,
  `Oidc:IssuerRecord` and `Tmdb:PostersDirectory` are gone; the `Tmdb:*Directory` settings name where
  artwork is kept and nothing else.

## [2.45.0] - 2026-10-10

### Added

- **Anime is a shelf of its own, beside Movies and TV Shows, across the whole site.** A title has a
  category as well as a kind: `anime` when the film database files it under Animation and it was made
  in Japanese, `general` otherwise, so an anime film is a film and an anime series a show, on the
  Anime shelf. Every API view that names a title carries `category`; somebody holding
  `cinema:download` shelves a title against the rule with `PUT /api/categories/{imdbId}`, kept by IMDb
  id in `Library:Categories` (`categories.json` in the state directory). `category=anime` narrows a
  genre's catalogue, a discovery, the released and upcoming lists and the library's additions. The
  site has Anime in the sidebar, the phone's library switch and the television's bar, at `/anime`
  (series) and `/anime/films`, with each anime title's page under it, an Anime row on Home, a Move to
  Anime or Move to Movies or TV Shows button on a title's page, and Anime on a search result's card. A
  download keeps its category, so the device's own library splits the same way. The assistant's
  `discover` and `whats_new` take `anime`, and a title's line says anime film or anime show. API
  2.21.0, assistant 3.3.0, site 0.53.0; needs moviebot-acquire 0.38.0.

## [2.44.0] - 2026-10-10

### Added

- **hotbox deploys with one script and no arguments**: `deploy/deploy.sh` for everything,
  `deploy/deploy-services.sh` for the four services (publish, rsync with the API's `wwwroot` left alone,
  settings, restart, wait for `/health`), and `deploy/deploy-site.sh` for the site alone, installed into
  the API's `wwwroot`, which the API reads on every request, so it deploys without a restart. `scripts/build-site.sh` builds the site; `scripts/build-player.sh` builds it and installs it
  into the API's `wwwroot` for a release.

## [2.43.0] - 2026-10-10

### Changed

- **A title keeps one poster from the search that found it to the shelf that holds it.** The library
  showed the title index's poster, or the source's own cover art once a download finished, where the
  search had shown the film database's, so a film changed face on arriving. `/media/{id}/poster.jpg` is
  now the film database's poster kept for the film, or for the show an episode belongs to, and the
  poster beside the title is served only for a title the database has none for; `/api/titles` names
  and versions it accordingly, so the site, the player, the TV app, Discord presence and the bot's
  launch cards all show it. Each title's database poster is pinned to the first one the API showed
  (`Tmdb:PostersDirectory`, `posters/` in the state directory), and the descriptions, search results
  and trending shelf fetch the pinned image even when the database later names another. A wish's
  `posterUrl` is the poster its page shows. API 2.20.0, bot 2.3.1.
- **The ingest no longer extracts a source's own cover art.** Ingest 1.18.0.

## [2.42.2] - 2026-10-10

### Changed

- **The assistant plugin takes `TheKrystalShip.Agent.Plugin` 1.0.0-dev.14,** which logs
  `System.Net.Http.HttpClient.*` at Warning by default, so the harness watch's poll leaves no line in the
  journal. Assistant 3.2.2.

## [2.42.1] - 2026-10-10

### Changed

- **The assistant plugin takes `TheKrystalShip.Agent.Plugin` 1.0.0-dev.13,** which reads only
  `prompt.md` and `skills/` from the profile directory. The profile the harness holds is the one it held.
  Assistant 3.2.1.

## [2.42.0] - 2026-10-10

### Added

- **Guest viewing.** Somebody in a room holding the new action `cinema:guests` hands out its guest
  link from the invite panel, private room or open, and anybody holding the link watches without an
  account: `/g/<code>` asks their name and opens the player with nothing that moves the room (no play,
  pause, seek, skip, episodes, chapters to jump to, invite or way back to the site) and their own
  volume, subtitles and their style, audio and quality, kept by their browser. The room sees each guest
  with a hollow mark. A room has one link, lasting `Rooms:GuestPassLifetime` (a day), until somebody
  stops guest access; guests are sent away once nobody with an account has been in the room for
  `Rooms:GuestGrace` (two minutes), and a room takes `Rooms:GuestsPerRoom` (ten) at once. A good guest
  link unfurls with its room's film.
- `POST` and `DELETE /api/rooms/{room}/guests`, `GET /api/guest/{code}`; the hub's `JoinAsGuest` and
  `GuestAccessEnded`; `RoomDoor.guests`, `Participant.guest`, `JoinReply.refused` and `JoinReply.you`.

### Changed

- **The door reaches only the people with an account in a room.** `DoorChanged` goes to a members'
  group a guest is never in.

## [2.41.1] - 2026-10-10

### Changed

- **A room is opened and closed with a button** in the invite panel, Open room or Close room,
  where it was a switch.

## [2.41.0] - 2026-10-09

### Added

- **Private rooms.** A room is private from the moment it is made: only whoever made it may enter,
  until somebody in it lets somebody else in, invites them, or opens the room from the invite panel.
  Closing an open room again lets in nobody new and shuts out nobody who has been in. Somebody
  holding a private room's link is shown that it is private and can ask to join; everybody in the
  room sees who is asking and anybody there can let them in or say no. Who was let in is kept with
  the room across a restart.
- Hub: `Knock`, `SetOpen` and `Answer`, and the events `DoorChanged` and `KnockAnswered`.
  `RoomSummary` carries `open`.

### Changed

- **`Join` answers `JoinReply`** (`{admitted, push?, door?}`) in place of `SessionStatePush`.
- **A private room says nothing about itself to anybody it does not admit.** Every
  `/api/sessions/{id}` route and Watch now naming the room are a `403`, a person's
  `GET /api/sessions` lists only the rooms they may enter (so Home's watching-now row and a film's
  "join the room" show nobody else's private room), and its `/r/` link unfurls as the site.
- **Inviting somebody lets them into the room**, and only somebody the room admits can invite.
- **The player hands out a room's link only while the room is open.** The invite button opens the
  panel on every frame size, and on a phone the open room's link goes to the share sheet. The
  Discord Activity's copy-link button is gone.
- **A Discord room is private to whoever started it**, like any other room: somebody else in the
  voice channel is at its door.

## [2.40.0] - 2026-10-09

### Changed

- **`person_credits` lists the credits worth reading and counts the rest.** A person with more than
  twenty-five credits is listed as every upcoming or announced credit with a named part or job, and the
  twenty released ones with a named part or job that the most people have voted on, still oldest first
  and with their exact dates. Credits with no named part, a part as oneself or an uncredited one, and
  the less known are counted in the result's header, and `all` lists every credit. A shorter list, such
  as a question about a few years, is listed whole. Karen Gillan's seventy-six credits came to a
  result of about a third of the size.

## [2.39.0] - 2026-10-09

### Changed

- **A discovery sorted by rating leads with titles many people rated well.** The film database orders by
  a plain average, which put a film rated 8.4 by 268 people, a children's cartoon and a TV special at the
  top of "really good horror after 2000". The sort now reads the five pages of the most voted matches and
  orders them by a rating weighted by votes. The API's answer and the assistant's `discover` carry the
  same fields as before, and a rating-sorted list drawn from a title's similar ones is ordered the same way.
- **A genre search leaves out animation and family titles it did not ask for.** `GET
  /api/catalogue/discover` takes `exclude` (further genres to leave out) and `includeFamily` (keep them),
  `resolved` gains `excluded`, and the assistant's `discover` tool passes `exclude` and `include_family`
  and says what it left out.

## [2.38.1] - 2026-10-09

### Changed

- **A character is traced to its actor with a search, never from memory.** When a person names a
  character rather than an actor, the assistant's first call is `web_search` for who played it, and no
  guessed name reaches `person_credits`. The rule is in the prompt, where every turn reads it, and in the
  find-a-forgotten-film skill. The suite case for a character traced to an actress and then to a film
  expects the search, holds the reply and caps the number of calls.

## [2.38.0] - 2026-10-09

### Changed

- **The assistant plugin is written on the plugin framework.** Each of the seven tools is one type: its
  name, class, description and argument record are declared once in C#, and the package's generator
  writes the schema and the reader of the model's arguments. The profile the harness holds is the same
  to the byte (a test holds it to the profile first registered), and a turn measures the same: nine of
  nine cases on the live harness, the same fixed prefix. An argument the model writes wrongly is
  answered with the argument and what it takes; a refusal of the API reaches the model in the API's own
  words, and any failure nobody foresaw is logged and answered as "could not finish". Guards and the
  speech vocabulary are declared on the plugin builder, the host is the package's, and the plugin's
  cards are a typed record serialized through a source-generated context.
- **The assistant's behavioural suite is a project of its own,** `tests/MovieBot.Assistant.Suite`, which
  runs `cases.json` through the package's runner against the deployed plugin as a signed-in person and
  reports pass rate, tool choice, turn time and prompt tokens.

### Removed

- **The Discord bot no longer puts speech to the assistant.** What the room verbs do not read is heard
  and left alone: the relay of a spoken request to the assistant, its proposals and their buttons, the
  spoken yes or no and the `Assistant` settings the bot read are gone, with the wire types only the
  relay used. The assistant answers in Krystal Cinema's chat alone.
- **The plugin's tool catalog, guard and speech files, its tool base, argument reader, result writer
  and API recorder** are replaced by the package's.

## [2.37.1] - 2026-10-09

### Fixed

- **A film still arriving is not abandoned when the torrent client drops a connection.** The read-ahead
  guard asks the client whether the download is whole and how far it has got, and a refused connection
  there failed the guard, which killed the transcode after the film had been announced as watchable. The
  transcode now pauses and asks again on the next tick, and the guard ends normally once the client
  answers.

## [2.37.0] - 2026-10-09

### Added

- **The assistant is a plugin to the assistant harness, written for finding films.** Seven read-only tools
  over the film database and the person's own records, through the API as that person: `search_titles`,
  `title_details`, `person_credits` (a person's work oldest first, each credit released, upcoming or
  announced), `discover` (by genre, years, people, keywords, language, rating and similarity, saying
  what each word matched and what to loosen when nothing fits), `find_by_clues` (a film someone half
  remembers: the model extracts the people, years, release status, look and plot words, and the tool
  searches, checks the best candidates against the database's page for them and ranks by how many clues
  each fits, saying which it fits and which it does not), `whats_new` and `my_taste`. Five skills:
  `find-a-forgotten-film`, `filmography`, `recommend`, `whats-on` and `taste-survey`. Every tool that
  names titles returns a `titles` card for the site to draw. The taste a survey asks for is kept in the
  harness's shared memory under `taste`, and every recommendation reads it. Assistant 3.0.0.
- **`Assistant:RecordApiTo`** appends every answer the API gives the plugin to a file, as a recording a
  test replays through the same tools.

- **Krystal Cinema's chat dock speaks to the plugin.** Replies stream, a question the assistant asks is
  a form of choices answered as the next message (the taste survey), the microphone transcribes through
  the plugin, and a title a reply names is a card with its poster, year and whether it is in the library
  or watched, leading to its page. The dock is krystal-ui's chat, whose client sends a recording to
  `/speech/transcribe`. Site 0.52.0.

### Removed

- **The assistant's own model, conversations and tools.** The language model, the conversation store,
  the room controls, the proposals for downloads and wishes, the notifications and the reply checks are
  the harness's or the site's now. Settings `Llm`, `LlmAgent`, `Assistant:OfferMinutes`,
  `Assistant:LibraryInContext` and `Assistant:IdleResetMinutes` are gone, with the tests and evals that
  measured the old assistant.
- **`moviebot-llm`**, the language model on hotbox's card for room turns, with `deploy/vulkan` that built it
  and `moviebot-llm.service`. `moviebot-assistant` keeps no state directory.

### Changed

- **`Assistant:HarnessAddress` and `Assistant:PluginKey`** say where the harness is and the key the plugin
  presents there; the key is set only in the environment.
  The unit reads `/etc/moviebot/assistant.env` beside `moviebot.env`.

## [2.36.0] - 2026-10-09

### Added

- **The API searches the film database.** `/api/catalogue/search` finds films and shows by name, with
  who made each and who stars in it; `/api/catalogue/people` finds a person by name and
  `/api/catalogue/people/{personId}/credits` lists every credit they have, cast and crew, films and
  shows, oldest first, each released, upcoming or announced, by year range, role and kind;
  `/api/catalogue/discover` finds titles by genre, years, people, keywords, language, a rating and
  vote floor and similarity to another title, naming what each word matched; `/api/catalogue/released`
  and `/api/catalogue/upcoming` list what is out and what is coming; `/api/catalogue/added` lists the
  library's newest arrivals. Every result says whether the library holds it and whether the asking
  person watched it. Answers are kept for a day or six hours (`Films/AnswerKeeper`), and what names a
  title is asked once and kept a week. Api 2.17.0.
- **A film and a show carry their keywords** and whether the database tags them black and white. A
  film also says which library title plays it and whether the asking person watched it. A description
  kept before the database was asked for keywords is asked for again at once.

## [2.35.0] - 2026-10-09

### Removed

- **`moviebot-speech`**, MovieBot's own whisper daemon, with its project, unit, whisper.cpp build
  and model. The organization has one speech path, tks-speech, and MovieBot's voice returns through
  the assistant harness.

### Changed

- **The bot's speech source is configured, never assumed.** `Speech.SocketPath` is blank by default
  and a blank path means no speech daemon: recognition and trigger scanning answer with an absence,
  as they do on a host whose daemon is not running. Bot 2.3.0.

## [2.34.0] - 2026-10-09

### Changed

- **The assistant runs on hotrod's shared model server**, gemma4 12B, the one the KGSM assistant
  uses, reached as `http://hotrod.home:8081` with a key of its own (`Llm__LlamaCpp__ApiKey` in the
  environment file). It answers Krystal Cinema's chat; the bot relays nothing said in a room to it
  and listens for no voice commands (`Assistant.Enabled` and `Voice.Enabled` off on hotbox), so
  `moviebot-llm` and `moviebot-speech` are stopped. Their code and units stay, and the two switches
  bring them back. Assistant 2.5.0.
- **The assistant warms the model for the turns that come**: a room's where the bot relays rooms to
  it, a person's otherwise. On a shared model server a request no turn repeats only takes a slot
  another assistant was keeping warm.

## [2.33.0] - 2026-10-07

### Changed

- **A room in which every player is waiting for the film is held where they stopped.** A viewer
  alone on a slow connection stalled, the room's clock ran on without them, and when the film
  arrived the player caught up by seeking ahead into what it had not fetched, stalling again: the
  film skipped forward after every stall and buffered more for it. Each player now says where its
  playhead stopped, and the server holds the room there, still playing with its clock stopped, while
  every player in it waits, letting it go from the same place when one plays again. One viewer
  waiting among others who are playing still holds nobody. The room's state carries `waiting`, and
  the hub's `Buffering` takes the playhead's position. Api 2.16.0, Core 4.3.0.
- **A chosen quality is the best a film may play at rather than the rung it is held on.** A viewer
  with 720p saved was held at 720p however little the connection carried; the player now chooses
  for the connection beneath the chosen rung, and the quality menu names the rung playing while it is
  beneath the choice. Site 0.51.0.

## [2.32.0] - 2026-10-07

### Added

- **The ingress records how every request travelled.** hotrod's nginx writes one JSON line per request
  for `movies.thekrystalship.com` to `/var/log/nginx/moviebot-delivery.log`: the kernel's round-trip
  time and its variance on the viewer's connection, the time the answer took and hotbox's share of it,
  bytes sent and the cache's answer. The address is kept as its /24 or /48. A slow evening for one
  viewer can be read back afterwards as slow on the route or slow on the server.
- **The player reports playback with every request it makes** (CMCD, CTA-5004), in `CMCD-*` request
  headers the delivery log records: buffer left, measured throughput, the rung playing, stalls, and a
  session id random per playback. Site 0.50.0.

## [2.31.0] - 2026-10-07

### Fixed

- **A show's episodes are named as their releases number them.** The film database sometimes numbers
  a season differently from every release of it: Superstore's second season opens there with a
  promotion, Olympics, which releases count as a special, so every name, still, score and date in that
  season was one episode off, a show page's row played the episode after the one it named, and the
  season ended one episode late. The API now matches the episode names release and file names carry
  against the database's, season by season, and serves every description in the releases' numbering;
  an episode only the database counts is a special. Api 2.15.0.

## [2.30.1] - 2026-10-07

### Fixed

- **An episode's OpenSubtitles results are that episode's.** The search asked by the show's IMDb id
  alone, which the index answers with subtitles from anywhere in the show, so an episode was offered
  another's, a special among them. It now asks by the show, the season and the episode. Api 2.14.1,
  moviebot-acquire 0.35.0.

## [2.30.0] - 2026-10-07

### Changed

- **A room holds one thing, and the API decides what it is.** Asking for something that is not here
  puts it into the room at once in place of what it held: the state has no `titleId` and
  `preparing` names it, an episode by `season` and `episode`. Every player, its address and its
  Episodes menu move to it immediately rather than when it arrives. A room given anything else stops
  waiting on what it asked for before, so two presses no longer load two episodes one after the
  other. What a room holds while it arrives is kept across a restart.
- **Next, previous and the Episodes menu are requests to the room.** The hub's `Next`, `Previous`
  and `PlayEpisode(season, number)` decide on the server whether an episode is loaded or fetched, at
  what quality, and whether the person may; `GET /api/sessions/{id}/around` names what the room
  holds and the episodes either side. The player keeps no episode rules of its own, and
  `/api/titles/{id}/neighbours` is gone.
- **The address is the room alone once it holds anything.** `?title=` and `at` are taken off rather
  than rewritten, on the site and in the Activity. Api 2.14.0, Core 4.2.0, Site 0.49.0.

## [2.29.0] - 2026-10-07

### Changed

- **The next episode is in Continue watching whether or not the library holds it.** When the
  episode after a finished one is not here, `GET /api/me/progress` lists it from the show's
  description with no `titleId`, once it has aired and to somebody who may download: its name
  (`episodeName`), still (`stillUrl`) and runtime. Its card on the site and on a television looks
  like the others, and choosing it is Watch now, which fetches it into a room. Every episode entry
  now carries `episodeName` and `stillUrl`. Api 2.13.0, Site 0.48.0.

## [2.28.0] - 2026-10-07

### Changed

- **Continue watching keeps a show in it until the show is watched through.** `GET /api/me/progress`
  lists each show once, by the episode the person watched last, wherever in the show they started:
  that episode while it is unfinished, and once it is finished the episode after it, the first of
  the next season after a season's last, from the start or from where they left it. It keeps the
  finished episode's place in the list, and the show leaves the list once its last episode is
  finished. The episode after is listed while the library holds it, which the fetch of the next
  episode during watching normally sees to. Films are listed as before. Api 2.12.0.

## [2.27.0] - 2026-10-07

### Changed

- **The player's Episodes menu is the season as the show's page lists it**, one shared row
  (`EpisodeRow`): still with how far the person got, name, score, length, and the one playing
  marked. Choosing a row plays it for the room; an episode the library lacks is fetched from the
  release the tracker offers, as on the show's page, and one it does not offer cannot be chosen.
- **An episode's row says nothing about where the episode stands** other than its download to this
  device: "Not here", "Still being prepared", "Could not be prepared" and "Not available yet" are
  gone from the player and the show's page, and the row's line is when it aired. Site 0.47.0.

## [2.26.0] - 2026-10-07

### Added

- **The player has an Episodes menu beside Subtitles and Audio** for an episode in a room: every
  episode of its season, the one playing marked, each with its length and where the viewer got to.
  Picking one the library holds puts it on for the whole room; one it does not hold is fetched into
  the room as next fetches it, for a viewer who may download; one not yet aired is not offered. It is
  read from `/api/shows/{imdbId}` each time it opens, and the tiny frame lists it in its overflow.

### Fixed

- **Reloading a room keeps it on the film it is showing.** The address kept the film the page was
  opened with, and a launch naming another film than the room's moves the room, so after a few
  nexts a reload sent everybody back to the first episode. The address now follows the room
  (`?title=` the film it shows, `at` dropped), in the Activity as well, and a room emptied under a
  viewer is given back the film they held rather than the one first launched. Site 0.46.0.

## [2.25.1] - 2026-10-07

### Fixed

- **Listing a person's offline wants no longer fails with a 500 when `offline.json` predates
  wants.** A saves file without a `wants` key (or without `people`) read that half as null rather
  than empty, so `GET /api/me/offline/wants`, and asking for a want, threw on every call. Api 2.11.1.

## [2.25.0] - 2026-10-07

### Changed

- **The dialogue-boost mix is off unless asked for.** It fills every core on hotbox for minutes after
  each title and is rarely done before a room has started watching, so a title now turns ready after
  the main pass and lists only its source's own audio tracks. `Handoff.DialogueBoost` (default
  `false`) turns it back on for the hand-off, and the ingest CLI's `--dialogue-boost` asks for it in
  place of `--no-dialogue-boost`. Titles already mixed keep their boosted tracks. Handoff 0.18.0,
  Ingest 1.17.0.

## [2.24.0] - 2026-10-07

### Added

- **Download of a title the library does not hold asks for a size, an audio and a subtitle language,
  not only a quality.** The sheet shows roughly what each quality takes (`~1.4GB`), from the film's
  or the episodes' runtime and the median the library's finished titles take a second at each
  height (`GET /api/offline/estimate`, measured in the background every six hours), checks it
  against the device's room, and puts it on the Download button. Audio is a language, the title's
  own first and the person's audio language picked; Subtitles is a language or Off, the person's
  picked. `POST /api/me/offline/wants` takes `audioLanguage` and `subtitleLanguage` and lists them
  back.
- **An audio language that is not the title's own prefers a release carrying it.** A film's
  release, and a season's pack, whose name names the language (`Castellano`, `TRUEFRENCH`, ...)
  comes first, then one marked `MULTI` or `DUAL`, at the quality and then at any; otherwise the pick
  is as before. The device then downloads the audio track in that language where the title has one.
- **A save can take a subtitle language** (`subtitleLanguage` on `POST /api/me/offline`, kept on the
  save): every text subtitle in it the title carries is added each time the package is asked for, so
  a growing title's subtitles, extracted once its source is whole, ride on a later stretch, and a
  title carrying none has the best candidate fetched from the subtitle index once its source is
  known, once per save and language, waiting at most 20 seconds.

### Changed

- **Fetching a subtitle is one service** (`SubtitleFetcher`) shared by the player's menu and
  Download, and a fetched subtitle carries the language it was searched in
  (`AddSubtitleRequest.language`, English when absent) rather than always English.
- **Language tags are matched through one table in Core** (`Languages`), which the ingest's subtitle
  filter now reads. Api 2.11.0, Core 4.1.0, Ingest 1.16.1, site 0.45.0.

## [2.23.0] - 2026-10-07

### Added

- **Download's progress is in a notification while the site is out of view.** With the page hidden
  (another app in front, the screen off), one notification says what is downloading and how far it
  has got (Downloading · 42% · 2 more, Queued, Waiting for a connection, or Preparing · 12% while
  the server prepares it), replaced silently in place at most every two seconds and closed when the
  page is in view again; a download that finishes meanwhile gets a Downloaded notification of its
  own. Tapping either opens the library. The first Download on a device asks the browser for leave
  to notify, once. A download still moves only while the browser runs the page, which a phone stops
  before long once it is hidden; the notification says how far it got. `OfflineWant` carries the
  film's or show's `name`, as the server has it described, for the notification to say. API 2.10.0,
  site 0.44.0.

## [2.22.0] - 2026-10-07

### Added

- **Download reaches anything the tracker offers, not only what the library already holds.** A film
  found in Discover or on its catalogue page, a film still being transcoded, every episode of a show
  the library holds or the tracker offers, and a whole season (Download season, or Download N more
  once some are on their way) are downloaded from a sheet that asks for the quality alone (1080p,
  720p or 480p; the person's Download quality, else 480p on a phone and 720p elsewhere). The server
  fetches the release (a film at 1080p, a season's episodes out of its pack at 720p, one torrent per
  release), and the control says Preparing with how much has arrived there; once the library can
  play it the device starts the download itself, with the default audio and subtitles, and pulls
  each track as the transcode writes it, finishing when the film is whole. New routes:
  `POST`/`GET /api/me/offline/wants` and `DELETE /api/me/offline/wants/{id}`, taking
  `cinema:download` as well as `cinema:offline`; wants are kept beside the saves and waited on for
  `Offline:WantDays` (14). API 2.9.0, site 0.43.0.

### Changed

- **A title still being transcoded can be saved, as it grows.** Its offer, package and tracks are
  marked `growing` and measured as far as the encoder has got; a growing track's `Content-Range`
  gives its length as `*`. A track's `ETag` now carries how many files it covers (`"<hash>-<n>"`),
  and `If-Range` resumes with any validator whose first files still hash the same, so a device holding
  a stretch of a growing track carries on from it. A validator without a count is read as the whole
  track's, as before. Only a title that failed is refused.
- **The download worker keeps bytes under their own validator.** Each track's file records the
  `ETag` its bytes arrived under; a new package no longer starts a track over because its `etag`
  changed, and a whole `200` to a resume drops the bytes held before the package is asked for again.
  Subtitles and artwork already on the device from the same address are not fetched again.

## [2.21.0] - 2026-10-07

### Changed

- **Saving a film for later without a connection is Download, and downloads live in the Library.**
  Everything a person reads says Download, Downloading · N%, Downloaded and Remove download?; Save
  stays the word for lists. The sheet is the phone's bottom drawer, or a dialog with the backdrop on
  a wide screen: quality, audio and subtitles as full-width rows (krystal-ui's `OptionList`), the
  device's space as a `Meter`, and Download · size; then the download's progress per part with Pause
  and Cancel, then its facts with Play and Remove. A film's page has Download beside Save, an
  episode row a button that turns into a progress ring and then a check, with the state under the
  episode's name. Library and Home cards carry a Downloaded or Downloading N% mark, and the Library's
  Filters gain Downloaded. Profile's Offline tab is gone. Profile › Playback's Download quality row
  no longer crushes its title on a phone. Site 0.42.0, krystal-ui 0.18.1.
- **With no connection the site is the same site, showing only what is downloaded, and says
  nothing about it.** Home's hero and rails, the Library, a film's page and a show's page are drawn
  from the device; a show is kept whole on the device (description, seasons, every episode and
  still, written by a worker so Safari keeps it too) once any of its episodes is downloaded, and its
  page dims the episodes that are not here. Continue watching reads the last progress the server
  gave beside what was watched on the device. Everything that needs the server is disabled without
  a word: Discover, the assistant, the bell, Saved and Notify, Profile's History, Requests,
  Following and Notifications, Save and Follow. Discover, the assistant and any other page the
  device cannot draw say only that it is not available offline, with a way to the Library. The
  offline banner and the "Needs a connection" pages are gone.

## [2.20.0] - 2026-10-07

### Added

- **Save offline keeps a film or an episode on a phone, a tablet or a computer.** A film's page and
  each ready episode's row have a control whose sheet picks the quality, audio and text subtitles,
  each with its exact size, against the free space on the device; a device without room is told how
  much it needs before anything moves. The transfer runs in a worker into the Origin Private File
  System, one track after another, resumes by `Range` and `If-Range` across reloads and crashes, can
  be paused, keeps the screen awake, and tells the API when it is complete. Profile's Offline tab
  lists every save with its progress, and Playback sets the quality a save picks by default. Not on a
  television or in the Activity. Site 0.41.0.
- **A saved film plays from the device, with or without a connection**: alone on a timeline of its
  own, starting with the person's kept preferences and resuming where they left off, its progress
  logged on the device and sent when the server can be reached. In a room, a film saved whole on the
  device plays from it while the hub keeps the timeline, and a room that cannot be reached offers to
  carry on alone from the device.
- **The installed site starts and gets around with no connection.** The service worker holds the
  whole build, and still no API answer and no media. A network failure, a gateway's error page or an
  unanswered probe of `/api/config` is offline, and an expired session stays signed in until the
  connection returns. A banner says so; Home shows the films on the device, a saved film's page is
  drawn from the device, Watch on a saved film plays it from there, and every page that needs the
  server says so. Coming back online forgets removals made offline and removes saves the API no
  longer lists. Signing out asks whether to remove the films saved on the device.
- **The API's half of Save offline** (`cinema:offline`, granted beside `cinema:watch`).
  `GET /api/titles/{id}/offline` answers what a title can be saved as with every track's bytes on
  disk; `POST/GET /api/me/offline`, `POST /api/me/offline/{id}/complete` and
  `DELETE /api/me/offline/{id}` keep each person's saves per device; `GET /media/{id}/{rendition}/track.mp4`
  answers a rendition's files joined, ranged with a strong `ETag`. A save still being made holds its
  title for `Offline:HoldHours`: the library's clock skips it and the hand-off, through
  `GET /api/library/saving`, neither settles, re-encodes, removes nor frees the source of it.
  Transfers are limited per person and per server and held to
  `Offline:MegabitsPerTransferWhileWatched` while a room has anybody in it. `POST /api/me/progress`
  takes what a device watched offline, the newer record winning, and `ViewerPreferences` gains
  `offlineQualityHeight`. Api 2.8.0, Handoff 0.17.0.

## [2.19.0] - 2026-10-07

### Added

- **Every film gains a third rung, `v2`, 854 wide.** It is what a weak connection streams and the
  copy a phone saves offline: encoded at quality 25 with a 1600 kbit/s target, it comes out at about
  half a gigabyte an hour and scores 94.5 to 98.9 under VMAF's phone model. `Ladder.StepDowns` lists
  the rungs under the top one, and the ingest, the re-encode, the hand-off's reservation and the
  transcode planner all read it, so a new film is made with all three. A film already in the library
  gains `v2` from `moviebot-handoff --reencode`, which makes only the rungs a film lacks, from its top
  rung, and moves them in beside the ones it has without touching them, whether or not a room is
  playing it. Core 4.0.0, Ingest 1.16.0, Handoff 0.16.0, Api 2.7.0.

## [2.18.0] - 2026-10-06

### Changed

- **A listed poster is kept by the browser instead of checked on every page.** `TitleSummary` carries
  `posterVersion`, when the poster file was last written, and the site asks for
  `/media/{id}/poster.jpg?v=…`, which is served `immutable` for a year; the bare address is still
  checked every time, so a rewritten poster reaches everybody under its new version. A library page
  of posters costs no requests once seen. Api 2.6.0, Site 0.40.1.
- **hotrod's ingress compresses text and holds its connections to hotbox open**
  (`deploy/nginx-moviebot-ingress.conf`). JSON, scripts, stylesheets, playlists and subtitles go out
  gzipped, and the hop to hotbox reuses up to 32 idle connections instead of a TLS handshake per
  request.

## [2.17.0] - 2026-10-06

### Changed

- **Watching is the one way to get a film, everywhere a person asks.** The site's chat proposes Watch
  for a film that has to be fetched: agreeing opens a room of the person's own, the site goes to it,
  and the film plays there while it is prepared (`ConfirmOutcome.Watch`, `MovieBotApiClient.WatchNowAsync`).
  The proposal names the film and no release. In a Discord room `watch_film` proposes into the room
  as it did. Assistant 2.4.0, MovieBot.Client 5.1.0.
- **Nothing on the site says where a film comes from.** "In the library" is gone from Discover's
  results, popular rows and best match, and from Saved's filters; Discover's results are one list,
  whatever the server already holds by that name among them. A film being prepared shows
  `preparing · N%` where it said downloading. Show episode rows say when an episode aired, not
  whether it is on the server, and the film and show pages drop their "On this server" fact (a film
  keeps its picture class, as Picture). A film page that cannot be opened is "Not found", pointing at
  Discover. Notifications about a wish and about something asked for say watched, not downloaded.

### Removed

- **Download on the site**: the film's Download button and its sheet, the show's episode picker
  ("Get episodes" / "Download"), "Download season" and "Download the other N", and Discover's "On the
  tracker" shelf of raw releases. A show's episodes are watched one by one from their rows, the next
  one fetched ahead as before. Site 0.40.0.
- **The bot's `/download` command.** `/watch` fetches what it plays; `/show` fetches a season. Bot
  2.2.0.
- **The assistant's `download_only` tool**, which fetched a film to wait in the library.

## [2.16.0] - 2026-10-06

### Removed

- **The library is no longer anything a person sees or tends.** Which films are on disk, how long
  each stays and when it goes are the server's alone: a film that has left is fetched again like any
  other, in minutes. Gone from every surface: the site's Leaving soon page, its Home row and nav
  entry, the hourglass and days-left status on film and show cards, the stay pill and Keep / Un-keep
  on a film's and a season's page, and the library's Stay filter; the `leaving` and `kept`
  notifications and their switches; the bot's `/keep` command and the "On disk" line on launch cards
  and download announcements; the assistant's `keep_film` and `let_go` tools and the stay line in
  what it says about a film. The API drops `GET /api/stays`, `GET /api/titles/{id}/stay`,
  `PUT`/`DELETE /api/titles/{id}/keep`, the `cinema:keep` action, the keep store and
  `Library:Keeps`. The library's clock itself is unchanged: a settled film or season leaves after
  `Library:LeaveAfterDays` without a play unless a room holds it, and the hand-off still deletes only
  what `/api/library/due` names. API 2.5.0, Bot 2.1.0, Assistant 2.3.0, MovieBot.Core 3.0.0
  (`TitleStay` is gone), MovieBot.Client 5.0.0.
- **Profile's Requests tab no longer lists downloads.** It showed the torrent client's view of each
  download a person started, release name and all, and is the waiting list alone. Site 0.39.0.

### Operational

- `keeps.json` in the API's state directory is no longer read. The films it held go back on the
  clock on the first sweep after the upgrade, so any that have gone their time without a play leave
  then.

## [2.15.1] - 2026-10-06

### Changed

- **The assistant's context window is its model server's.** On `TheKrystalShip.Llm` 2.5.0 a turn's
  usage is measured against the per-slot window llama-server reports in `GET /props`; `Llm.ContextWindow`
  stands only until the server first answers. Assistant 2.2.1.

## [2.15.0] - 2026-10-06

### Changed

- **The assistant runs on the organization's engine**: turns, the event stream, a person's
  conversations and the confirm channel are `TheKrystalShip.Agent.Service` 2.1.0, the code the KGSM
  assistant runs, over this service's seams (`Wire/CinemaTurns.cs`). The wire follows the contract's
  shapes: a streamed turn opens with `turn.attach`, which states the turn so far; a conversation
  reads back as the contract's history with its switches; a stream that falls behind is redrawn. What
  is MovieBot's own — a proposal, a film's card, `launch` — is written as before. Assistant 2.2.0.
- **MovieBot.Client 4.0.0**: the event-stream frames, conversation shapes and attach and feedback
  bodies are the organization's package's, so the client carries what is MovieBot's alone.

## [2.14.2] - 2026-10-06

### Fixed

- **Profile on a television keeps its title and its list on the screen.** A section longer than the
  screen (History, Subtitles, Appearance) scrolls inside its panel under the title; the whole page
  scrolled before, carrying the title off the top, and coming back up could leave it cut off.
  Site 0.38.2.

## [2.14.1] - 2026-10-06

### Fixed

- **Krystal Cinema on a television**: entering Home's rows and going back up into the bar no longer
  stall on a frame of over 100 ms followed by a restyle of the page on every frame of its fade; the
  press takes one frame of under 75 ms. About opens as Profile's section, with focus on its place in
  the list, where it opened as a page with nothing to focus. Site 0.38.1, on krystal-ui 0.17.2.

## [2.14.0] - 2026-10-06

### Added

- **Krystal Cinema for Android TV and Fire TV** (`tv/`, its own version clock, `tv/CHANGELOG.md`):
  the site in an app launched from a television's home screen, built with the Android SDK's own
  tools by `tv/build.sh`. The API serves the build made for its site at `/tv.apk`, from `Tv:App` or
  `tv.apk` in the state directory, for a TV's Downloader to fetch.

### Changed

- **No link leaves the site on a television**: Profile's account shows the provider's address as
  text, and About's TMDB logo is not a link. Site 0.38.0.

## [2.13.0] - 2026-10-06

### Added

- **A television signs in with a code approved on a phone.** On a TV with nothing held, Krystal
  Cinema shows a code, the address to enter it at, a QR code of the address with the code in it, and
  the time left, and signs in once somebody approves it at the provider: the device authorization
  grant (RFC 8628) as the new public client `cinema-tv`. The session renews like any other and
  survives restarting the app and the phone signing out. A code that runs out is replaced; a
  provider that cannot be reached, a refusal, and a client the provider will not sign in this way each
  have one screen. A session that ends while a page is open puts the code over the page, and approval
  returns to it. Signing out on a TV revokes its refresh token at the provider (RFC 7009).
  `/api/config` names the client as `tvClientId`, from `Oidc:TvClientId` (default `cinema-tv`); the
  provider needs that client registered with the device grant, and token revocation for a TV to sign
  out. Site 0.37.0.

## [2.12.1] - 2026-10-06

### Fixed

- **Krystal Cinema on a television**: every button in a row of actions (Watch, More info, Save,
  Follow, Resume, Restart) is one height. Save and Follow were drawn a size taller. Site 0.36.1.

## [2.12.0] - 2026-10-06

### Added

- **Krystal Cinema's own design on a television.** In TV mode the site draws pages of its own
  (`web/site/src/tv/`) instead of the desktop's: a bar across the top with Home, Discover, Movies,
  TV Shows and Saved, the bell and the person, and no sidebar or footer. Home opens on the newest film
  with Watch focused; below it, Continue watching, Watching now, Watch later, Recently added, Movies
  and TV Shows run past the right edge, and with focus in them the top of the screen is a spotlight on
  the focused card while the focused row holds one line. Movies and TV Shows are the library a row per
  genre, each ending in Discover more. A film's page offers Resume, Restart and Save with how far in
  and what remains, then the cast, each leading to their work, and the details; a show's adds seasons
  and its episodes, whose spotlight is the episode. Discover opens on its field, and answers a query
  with the best match large. Saved shows Watch later and the lists; Profile keeps its sections down
  the left, with About among them. The player's menus are a panel down the right and its subtitles
  start larger. Focus lifts what it is on. Site 0.36.0, on krystal-ui 0.17.1.

### Changed

- The TV navigator keeps Left and Right on the line focus is on, puts a focused row on a page's line,
  leaves an element already well in view where it is, and decides a move from geometry before reading
  any element's style, which held a key press to about 20 ms of script on the Fire TV.

## [2.11.0] - 2026-10-06

### Added

- **Discover by person.** `GET /api/catalogue/people/{personId}` answers a person by the film
  database's id for them, with up to 40 films and shows they are credited on, most voted on first, each
  with the part they played or the job they did; appearances as themselves and on talk, news and
  reality shows are left out. Their portrait is served at `/catalogue/people/{personId}.jpg`.
- **Discover by genre.** `GET /api/catalogue/genre?kind=film|show&name=` answers up to 40 of the most
  popular films or shows in a genre named as a description names it, in one call, so a shelf of a
  genre can lead on to the catalogue's.
- A film's and a show's cast carry each person's `personId`, and its directors, writers and creators
  their ids in `personIds`. A kept description without them is asked for again when it is next read,
  and the warmer brings every library title's within a pass.
- Both browses take `cinema:watch`, are kept under `catalogue/` in the state directory
  (`Tmdb:CatalogueDirectory`), a person's for a week and a genre's for a day, and serve each title's
  poster at `/glances/{imdbId}/poster.jpg`.
- **Krystal Cinema**: every cast member on a film's or show's page, and every director, writer and
  creator in its credits, leads to Discover's catalogue of their work at `/discover?person=<id>`.
  Discover shows a genre's most popular films or shows at `/discover?genre=<name>&kind=<film|show>`,
  and Movies and TV Shows end their grid in a Discover more tile there while a genre is picked.
  Site 0.35.0.

## [2.10.0] - 2026-10-06

### Added

- **Krystal Cinema driven by a television remote.** A page carrying the wrapper app's mark in its
  user agent, or opened with `?tv=1`, is in TV mode: the remote's arrows move focus to the nearest
  control that way, an open overlay holds focus and hands it back, a row scrolls to the focused card,
  a page opens with focus on its first control and returns to the one it was left on, and focus is
  never left on nothing. Back and Menu, which never reach a page, are handed over by the wrapper as
  `window.krystalTv.back()` and `.menu()`. The assistant is not drawn. Site 0.34.0, on krystal-ui
  0.16.0, whose focus ring reads from across a room on a television.
- **The player answers the remote**: the first press only brings the controls back, play has focus
  once the film is ready, presses of Left and Right are gathered into one skip and a skip gives the
  scrub bar focus, holding scrubs with the preview and moves the room once on release, the media keys
  act directly, Back closes a menu, hides the controls and then leaves, and Menu opens the subtitles.
  A television has no volume, full screen or invite in the player.
- `npm run verify:tv`: every page walked with the remote's keys alone at a Fire TV's size, and the
  overlays, Back and the player as a remote drives them.

## [2.9.0] - 2026-10-05

### Added

- **The assistant stands on the organization's shared tooling**, `TheKrystalShip.Agent.Service`
  1.1.0, so its commands, memory and notifications are the same code as the KGSM assistant's.
  Assistant 2.1.0.
- **Commands**: `/help`, `/tools`, `/compact`, `/new` and `/memory`, each needing `cinema:watch`.
  `/new` in a room, sent by the bot with the room in `X-Assistant-Room`, starts the room's
  conversation over.
- **Memory, on a person's own chats**: the assistant writes down what somebody asks it to remember,
  reads it back on every later turn, and the person reads, corrects and drops it through
  `/memories`. A room remembers nothing, being shared and starting over after a silence.
- **Notifications of a proposal waiting on the person who asked**, with the assistant's own key pair
  and device list, for a site served from the assistant's own origin. A notification can cancel a
  proposal and says that agreeing to one is done in the chat, where it still waits: a proposal is
  carried out with the approving person's token, which a notification's button does not carry.
  Krystal Cinema's dock does not subscribe to these, its origin being subscribed to the API's.

### Removed

- The assistant's own `new` and `help` and their records in `MovieBot.Client`, whose commands are
  the shared package's. Client 3.0.0.

## [2.8.1] - 2026-10-05

### Changed

- **Web Push comes from `TheKrystalShip.WebPush` 2.0.0**, the same library published under the
  organization's name. Nothing a person receives changes. Api 2.4.1.

## [2.8.0] - 2026-10-05

### Changed

- **The assistant's chat is redrawn**, on krystal-ui 0.14.0: your messages as bubbles, one line for
  the assistant's work, proper headings, lists and code in answers, one card per proposed action
  that changes in place to its outcome, and quiet one-line events. A proposal reads as what it does
  to the film ("Get notified about Dune: Part Two", "Fetch subtitles for …") while its button keeps
  the short verb. Site 0.33.0.

## [2.7.1] - 2026-10-05

### Fixed

- **The foot of every page clears the tab bar on a phone.** The page's room for the bar was added
  below a full screen's height, so the last of each page sat under it. The assistant's sheets now
  rise in as they open. Site 0.32.1, on krystal-ui 0.13.0.

## [2.7.0] - 2026-10-05

### Changed

- **Every button, field and switch on the site is krystal-ui's**, the ones the Control Panel and the
  standalone assistant draw: `Button` and `IconButton` in place of the site's own `kc-btn` family,
  `Input` and `Textarea` for naming, renaming and noting a list, and the class-drawn `Toggle`. The
  sizes are the design system's ladder, so the title page's actions are 46px, a page heading's and a
  sheet's 38px and a list's own actions 34px, and a filled button's type takes the theme's `--on-*`
  ink.
- **The assistant's spinners show.** Verifying an action, loading the chat's history and compacting
  a conversation each draw a spinner that krystal-ui styles itself. Site 0.32.0, on krystal-ui
  0.12.0.

## [2.6.0] - 2026-10-05

### Changed

- **Profile tabs move like pages.** The tab strip answers at once and the tab chosen is prepared
  under it, fading in once its first reads are answered, as a page navigated to does. A list still
  reading after half a second holds one row of placeholder cards, Requests one row for both of its
  shelves, and the scrolling column keeps its scrollbar's room, so nothing moves sideways.
- **Every profile tab is one column at the page's width**, Notifications and the settings tabs
  included.
- **Playback is three tabs:** Playback (audio, quality, skipping intros), Subtitles (the language a
  film starts in and how subtitles look) and Appearance (the theme). **Account** is a tab of its own,
  with who is signed in and a link to the account's page at the provider, in place of the button in
  the page's heading.
- **The scrollbar is krystal-ui's**, the one the Control Panel draws. Site 0.31.0, on krystal-ui
  0.11.0.

## [2.5.0] - 2026-10-05

### Changed

- **Every list of titles is poster cards.** Notify me, History, Requests (waiting on and downloads),
  Following, Leaving soon and each saved list draw a krystal-ui `ShowcaseCard` per title in the
  site's poster grid, with a skeleton of the grid while it loads. What a row did besides opening the
  title is on the card's tools: remove from history, stop waiting, unfollow, switch automatic
  downloads, keep or let go, write a note and take a title off a list; a note is written in a sheet
  over the page. History and downloads carry their progress along the poster's foot. Notify me finds
  a film with Discover's result cards, waited on from its page. Site 0.30.0, on krystal-ui 0.10.0.
- **`DownloadView` carries `progress`**, from 0 to 1, which a download's card draws. Core 2.1.0, Api 2.4.0.

## [2.4.0] - 2026-10-05

### Added

- **Discover's results carry the film database's rating, and the first answer what it is about.**
  Each title the title index answers with is looked up by its IMDb id in the film database, every
  title of a search at once; a title already described answers from its description. A lookup is
  kept on disk under `glances/` (`Tmdb:GlancesDirectory`), a year for a title out over a year and a
  week for anything newer, refreshed in the background so no search waits on a refresh, and a search
  waits for new lookups at most a second and a half. The index's answer for a name is held in memory
  for an hour. `FoundTitle` gains `rating`, `votes` and `overview`. Api 2.3.0, site 0.29.0.
- **Discover's first answer offers what its page offers.** A film not here asks the tracker as soon as
  it is shown and carries Watch, Download or Notify (`components/FilmActions.jsx`, which the film's
  page uses too); a show carries Watch and Resume from the library, or Watch its first episode and
  Download.

### Changed

- **A search result shows the poster its page shows.** `posterUrl` is the film database's poster,
  served from here: the described title's kept one, or one fetched once beside its glance and served
  at `/glances/{imdbId}/poster.jpg`. The title index's poster stands in only where the database has
  none.
- **The API keeps its connections to the title index and the film database open**, one handler each
  for the life of the process, HTTP/2 where they speak it, and a ping every thirty seconds, so a
  search asks over a connection already open. On moviebot-acquire 0.33.0.

## [2.3.0] - 2026-10-05

### Added

- **`GET /api/titles/find?q=`** answers films and shows together, in the title index's own order, for
  somebody who may download. Api 2.2.0.

### Changed

- **Discover's search results are posters.** The catalogue's first answer is drawn large, with its
  poster, year, cast, Watch when the library holds it ready, and Save; the rest follow as poster cards
  carrying their year, whether each is a film or a show, and their cast, and one the library holds is
  marked on its card and leads to its library page. Films and shows are one list in the catalogue's
  order, and "In the library" lists only what the library holds by that name that the catalogue did
  not answer with. A skeleton of both is drawn while the catalogue is asked. Site 0.28.0.
- **`/api/films/find` and `/api/shows/find` answer `FoundTitle`**: the same fields with `kind`,
  `starring` and `posterUrl` added.

### Removed

- **Discover's "See how to watch" button.** A result opens its page, where Watch, Download and Save are.

## [2.2.0] - 2026-10-05

### Added

- **A film's or show's page is drawn as its frame while its description is read**: the poster, the
  heading, the lines of text, the credits, the actions and the cast as krystal-ui `Skeleton` blocks
  where they will be, so a title the film database is being asked about for the first time no longer
  shows an empty page. Site 0.27.0, on krystal-ui 0.9.0.

### Changed

- **Home's Movies and TV Shows rows are dealt in a new order on every visit**, and keep it while the
  page is open.
- **A description the film database is asked for fetches its images side by side**, eight at a time,
  and a show's seasons the same way, so a page opened on a film nothing is kept for waits on the
  slowest image rather than the sum of them. Api 2.1.0.

## [2.1.0] - 2026-10-05

### Changed

- **Krystal Cinema has no sign-in page of its own.** A page opened with nothing held goes straight to
  the provider, and `/signin` is only where the provider sends the browser back with its code. A
  session that ends while a page is open is shown with a Sign in button and never followed; a sign-in
  the provider refuses and a provider that does not answer are shown the same way. Site 0.26.0.
- **Signing out is the provider's.** The site sends the browser to the provider's end-session endpoint,
  which ends the sign-in there and every session under it and comes back to the site.

## [2.0.0] - 2026-10-05

### Changed

- **People sign in at an OpenID Connect provider, and MovieBot signs nobody in.** The provider named
  by `Oidc:Issuer` (tks-auth on hotbox's deployment) mints ES256 access tokens for the audience
  `cinema`, listing the Cinema actions a person holds in `tks_actions`, and every service validates
  them: issuer, audience, algorithm, `at+jwt` and expiry. A token for any other audience is refused.
  Every record is keyed by the provider's `sub`. Api 2.0.0, Core 2.0.0.
- **Every route names the action it needs**: `cinema:watch` for the library, rooms, the hub, tickets
  and media; `cinema:download`, `cinema:wish` and `cinema:keep` for fetching, waiting on and keeping
  films; `cinema:profile.read` and `cinema:profile.write` for a person's own records. A route naming
  none admits nobody. `/.well-known/tks-actions.json` serves the declarations to the provider. Without
  a token the answer is `401` with `WWW-Authenticate: Bearer`, without the action `403`.
- **The person is always the token's.** No request names who asked: downloads, wishes, keeps, room
  controls, subtitles and list entries record the token's `sub` and `name`, and an `act` claim — a
  client acting for the person — is logged beside them. The hub's `Join` takes the room alone and reads
  the viewer from the connection's token, which the hub reads from `access_token` on the query string.
- **The API pins its issuer.** It records `Oidc:Issuer` in `Oidc:IssuerRecord` the first time it
  starts and refuses to start against another until `Oidc:AdoptIssuer` names it.
- **Krystal Cinema is the provider's public client** (`oidc-client-ts`, code with PKCE, refresh-token
  rotation, `Oidc:SiteClientId` `cinema-site`). The door is a button. Controls show to somebody holding
  the action they need, and an approved account without `cinema:watch` sees a page saying it has no
  access. Profile links to the account's page at the provider; signing out forgets this browser's
  tokens. Site 0.25.0.
- **The Activity signs in through the API.** `POST /api/auth/discord/callback` takes `{code}`, redeems
  it with Cinema's Discord application and exchanges the Discord access token at the provider (RFC
  8693) as the confidential client `Oidc:ActivityClientId`, answering `{accessToken, token, expiresIn}`;
  posting `{accessToken}` renews. A provider refusal is a `403` carrying its `accountStatus`.
- **The bot acts as the person who used it.** Each command, autocomplete and button exchanges the
  Discord user's id for their token at the provider as the confidential client `Oidc:BotClientId`,
  held until shortly before it expires, and the API decides what they may do. A refusal is worded per
  account status. The assistant gets the same token. Bot 2.0.0, Client 2.0.0.
- **The assistant validates the person's token and acts with it.** Its tools reach the API with the
  caller's token, a proposal is carried out with the token of whoever agrees, and a turn with an `act`
  claim is the bot relaying a room. Assistant 2.0.0.
- **A wish that came out stays on the person's list with the release found**, shown on the Notify
  page, until they take it off. Everybody waiting is told by a notification and a push.
- **`/api/config` answers the issuer, the site's client id and the actions claim.**
- `Auth:MediaTicketKey` signs media tickets; `Auth:ServiceKey` opens only machine work that names
  nobody: the hand-off's routes, reading the library and rooms, loading a room as MovieBot, the
  downloads list and a film's stay.
- An invite answers `delivery` `push` or `bell`.

### Removed

- Signing in with Discord's OAuth2 redirect, the server-membership check and its
  `guilds.members.read` scope, the API's own tokens and `Auth:SigningKey`, the room token, the
  `member` claim, `POST /api/auth/service-token`, the device list (`/api/me/sessions`, `devices.json`)
  and Profile's Sessions tab. `Discord:GuildIds` is the bot's command registration alone.
- Discord direct messages for notifications (`/api/notifications/direct`) and the bot's wish
  announcements (`/api/wishes/available`, `/api/wishes/{imdbId}/told`), with `X-MovieBot-Acting` and
  every `requestedBy` and `userId` a request carried.

### Upgrading

- Register Cinema at the provider before starting this version (`docs/deploying.md`, step 3b), and
  set `Oidc:Issuer`, `Oidc__ActivityClientSecret`, `Oidc__BotClientSecret` and
  `Auth__MediaTicketKey`.
- Records kept under Discord ids (wishes, keeps, lists, progress, follows, notifications, push
  devices) belong to no provider account; clear them before the first start.

## [1.93.0] - 2026-10-04

### Added

- `moviebot-handoff --reencode [id...]` encodes the library's settled films again at quality 23,
  from each film's own top rung, one at a time and only while no room is playing anything and no
  transcode is running. A film that has only its top quality gains the 720p step-down on the way.
  Matching the quality of a film encoded from its source at 25 takes 23 from the top rung, which
  is about 42% smaller than the rung it replaces. A film is swapped only when nobody is playing it,
  and its new segments carry new names so no cache mixes them with the old ones. `--now` skips the
  wait, for trying one film. Handoff 0.15.0, Ingest 1.15.0.
- Each quality in a film's manifest records the constant quality it was encoded at (`cq`), which
  is how a film already gone over is told from one that is owed. Core 1.25.0.

## [1.92.0] - 2026-10-04

### Changed

- Films are transcoded at about half the size, with no difference a viewer would notice. Every
  quality is encoded at constant quality 25. Scored against the source on clips of a grainy
  Blu-ray, a Hi10P one, a clean web release and an animated film, the picture stays within three
  VMAF points of what it was, and a clean web release no longer comes out larger than the file it
  was made from. Films already in the library keep their size. Ingest 1.14.0.

## [1.91.1] - 2026-10-04

### Fixed

- A film decoded on the card no longer flashes single frames from a third of a second ahead. The
  cuvid decoder's default surface pool is too small for a source with B-frames, so now and then a
  surface was reused before its frame was handed on and a later frame went out in its place, in
  bursts that every viewer saw as the picture hopping forward and back. The decoder now holds 32
  surfaces. Measured on the first 11.5 minutes of an x264 Blu-ray rip: 74 and 82 such frames on two
  runs before, none after. Ingest 1.13.1.

## [1.91.0] - 2026-10-04

### Added

- The API plans what the card should be transcoding. `GET /api/transcode/plan` ranks every unit of
  work owed in the library (each quality of a film still being made, its audio, and each quality a
  finished film lacks) by who is watching: a watched film short of its lead first, a film a room has
  just asked for next, backfill last and only while no room plays. It spends two budgets measured on
  the card, the encoder in 1080p-equivalents of realtime and the decoder in source decodes, and says
  of every unit whether to run, hold or park it, and of every finished film whether it may settle.
  `PUT /api/transcode/status` takes the executor's report. The plan is computed on every request and
  never stored. Api 1.28.0, Core 1.24.0.
- Each quality of a film being made carries its own head in the manifest, and so does its audio. The
  room's state carries them too (`rungHeads`, `audioHead`); a seek is clamped to the best quality's
  head, held back to the audio's, and the player only offers the qualities that have caught up to
  the playhead plus thirty seconds. Each player reports the quality it plays and the one it would
  choose to the server over the hub (`ReportQuality`), which plans by it and never shares it with
  the room.
- A transcode chooses its disk when it starts. Its size is estimated from the ladder and the running
  time and reserved against the media root's free space, shared with the downloads still arriving;
  a film that does not fit is written to the cold disk directly, and one that fits neither waits
  rather than running out of room partway. Handoff 0.14.0.

### Changed

- Each quality of a film is encoded by its own ffmpeg process, decoded and scaled on the card where
  the source allows, and the audio by two processes sharing the title's tracks. A 1080p film with
  audio transcodes about a fifth faster and an HDR one about three quarters faster; cancelling an
  ingest ends every ffmpeg it started. Ingest 1.13.0.
- A film settles onto the cold disk only when no room with somebody connected is playing it, and
  never while a transcode writes to the cold disk.
- The quality ladder (bitrates, the 720p step-down and the segment length) is defined once in Core,
  and the ingest's defaults are taken from it.

## [1.90.0] - 2026-10-04

### Changed

- The dialogue-boost mix runs at the lowest CPU priority. It fills every core on hotbox for minutes
  after a film turns playable, and while it did so at the same priority as everything else the API,
  the bot, speech and the model all slowed under it; now it takes only what they leave idle, and
  yields to the next film's main pass the same way. The mix sounds exactly as before. Ingest 1.12.0.

## [1.89.0] - 2026-10-04

### Changed

- What somebody does to a room — playing, pausing, moving, changing the film — is said over the film
  to everyone in it, the person who did it included, as "You paused" on their own screen and "Ada
  paused" on everybody else's. It reads the same on every screen size, on the site and in the Discord
  Activity.

## [1.88.0] - 2026-10-04

### Added

- Touch gestures on the player. A tap in the middle of the film plays or pauses it; a tap at either
  side shows or hides the controls; two taps at a side skip back or forward ten seconds, and each
  further tap adds ten more, sent to the room as one seek once the taps stop. A sweep sideways
  anywhere on the film scrubs it, the width of the frame standing for the whole film, previewed on
  the scrub bar and sent when the finger lifts. A sweep up or down at the right sets this viewer's
  volume, where the browser lets a page set one (not on iOS), and at the left dims the picture,
  which the browser remembers like volume. The subtitles stay at their own brightness.
- On a phone the player takes the whole screen and turns it sideways on the Join press or the first
  touch on the film, and takes both back on the next touch after leaving full screen. The Discord
  Activity asks Discord to hold its window in landscape. iPhones grant neither, so there the phone is
  turned by hand.

### Changed

- A phone's player has no full screen button, since it is always full screen.

## [1.87.1] - 2026-10-04

### Changed

- The front page shows Watch later after Watching now, so Continue watching and Watching now are the
  first two rows.

## [1.87.0] - 2026-10-04

### Added

- A trail above every page of the site but the front page, Home first and then each level of the
  address: Home / TV Shows / Breaking Bad / Season 3. Every level but the last is a link to the page it
  names, and a film's, a show's or a list's level carries its name once the page has it.
- A Not found page for an address the site does not have.
- `npm run verify:routes` in `web/site`: every address loaded cold, with what it lands on and the
  trail it shows, against the scratch site.

### Changed

- The site's addresses are hierarchical, each beginning with the address of the section it belongs
  to. A film in the library is at `/movies/<id>`, a show the library holds an episode of at
  `/tv/<imdbId>`, and what the library does not hold under Discover, at `/discover/film/<imdbId>` and
  `/discover/show/<imdbId>`. A page that finds the library holds it, or does not, moves to the right
  one. A season is `/season/<n>` on its show's address and a profile tab is `/profile/<tab>`, so both
  survive a reload and can be linked to.
- Addresses the site does not use in that form are rewritten to the one it does: `/title/`, `/show/`,
  `/film/` and `/profile?tab=`, which links already sent carry, a trailing slash or a segment too
  many, and a season or a tab the page does not have.
- Notifications link to the new addresses.
- The film and show pages have no Back button; the trail leads back up.

## [1.86.1] - 2026-10-04

### Changed

- Lists are called Saved, after the button that fills them, and are a page of their own at `/saved`
  beside Home and Discover in the sidebar. On a phone, Me leads to it.
- The front page has a Watch later row after Continue watching, each title with Watch when the
  library holds it, and See all opening the list.
- A list's rows no longer carry a mark for being in the library; the library filter still narrows by it.

## [1.86.0] - 2026-10-04

### Added

- Lists of films and shows to watch later. Everybody has a Watch later, and makes as many named lists
  as they like. Save on a film's or a show's page puts it on Watch later in one press; once it is
  saved, the same button opens every list the person can add to and makes a new one. Every entry
  keeps when it was first saved, who saved it, and an optional note.
- A Lists page in the sidebar and in the phone's Library switch. One list's page puts the newest saved
  first under Today, Yesterday, This week and each month. It can be searched by title and note,
  narrowed by kind, by whether the library holds the title and by whether the person watched it, and
  sorted by date added, name, year, rating or length.
- Sharing a list: its owner puts people on it as editors, who add to it and take off it, or viewers,
  who read it. Somebody put on a list is told with a `shared` notification, which has its own switch.
  A list is shown to nobody else.
- `/api/me/lists`, `/api/lists` and `/api/lists/{id}` with its entries and members
  (`docs/api-contract.md`, Lists). Lists are kept in `lists.json` in the state directory
  (`Lists:Path`).

## [1.85.1] - 2026-10-04

### Fixed

- The shelf loads without waiting on the disks. `/api/titles`, `/api/shows`, a show's page and a
  title's neighbours read a list of the library the API rebuilds every ten seconds in the background,
  so a title arriving, changing state or moving to the cold disk reaches them within ten seconds.

## [1.85.0] - 2026-10-04

### Added

- Discover, the site's page for finding anything to watch, here or not, under Home in the sidebar
  and as a tab on a phone. It opens on a large field and the films and shows popular this week, each
  marked when the library holds it and otherwise leading to its page, where whether and how it can be
  watched is asked. `/`, Ctrl+K or Cmd+K anywhere outside a text field opens it with the field focused.
- The Movies and TV Shows rows on the front page end in a tile that opens Discover.
- `GET /api/popular` answers the films and shows the film database counts as trending this week, and
  `/popular/{imdbId}.jpg` their posters. The API keeps the list and the posters under `popular/` in
  its state directory (`Tmdb:PopularDirectory`) and asks again every six hours.

### Changed

- Discover is at `/discover`, where the Search page was, and the sidebar no longer holds a search
  field. A search's tracker releases are asked for when their row is opened, and the row opens by
  itself when the catalogue knows no film or show by the name typed.
- The notice that a film somebody waited on is out opens the film's page.

## [1.84.1] - 2026-10-04

### Fixed

- A show's card labels its first year "Year", as a movie's does, on one line, so a rail's cards
  stand level.

## [1.84.0] - 2026-10-03

### Changed

- One drawer from the bottom edge, the full width of the screen, for every menu and list a phone
  opens: the player's subtitles, audio, chapters, quality and overflow, and the notifications from
  the front page's bell, which had a floating panel of its own. The subtitle menu, which kept the
  wide popover's fixed width as a drawer, spans the screen like the others.

## [1.83.2] - 2026-10-03

### Changed

- The sidebar's search is the Search page's field, smaller and without its button; on the collapsed
  rail it is the same box holding the icon, opening the Search page.

## [1.83.1] - 2026-10-03

### Fixed

- The site's words cover what the library holds, movies and shows alike, rather than films alone:
  the sidebar's search, the Search page's field, heading and empty state, the footer, the sign-in
  card, About, and the player's messages. Empty states say what is empty and no more. The Notify me
  page, which is for movies, keeps saying so.

## [1.83.0] - 2026-10-03

### Changed

- Moving between pages no longer shows the new page empty. The page navigated to is prepared out
  of sight while the one being left stays, and replaces it once its first reads are answered and the
  artwork in view has decoded (half a second at most), fading in as it rises a few pixels. The tab
  bar and the sidebar answer the press at once.

## [1.82.1] - 2026-10-03

### Fixed

- On a phone the library has its Movies | TV shows switch under the heading, as the mobile design
  draws it: the Library tab is the phone's one way in, and it only ever reached the movies.

## [1.82.0] - 2026-10-03

### Changed

- The site's sidebar is laid out as the Control Panel's: the search under the brand, then Home,
  Movies and TV Shows, and Notify me and Leaving soon, each ruled off and unlabelled, and at the
  bottom Notifications and the signed-in person as krystal-ui's account row, with Profile and Sign
  out in its menu. krystal-ui is at 0.7.0.

## [1.81.0] - 2026-10-03

### Added

- **Subtitles look the way each person chooses.** Size, font, weight, text colour, edge (outline,
  shadow, raised, depressed) and its colour, a box behind each line and its opacity and colour, and
  how high dialogue sits, under Style in the player's subtitle menu and in Profile's Playback tab with
  a preview. It is the person's `subtitleStyle` preference, so it follows them into the Activity.

### Changed

- The default subtitle look is white text with a black outline and no box, about a quarter smaller,
  sized to the picture rather than the frame, so a phone held upright no longer sizes it to the black
  above and below the film.
- The player's menus name no audience in their headers.

## [1.80.0] - 2026-10-03

### Added

- **Skip intro and skip recap.** An episode's title sequence and its "previously on" are written
  into its manifest as `markers` once it is ready: from a chapter that names them, else by matching
  its audio fingerprint (chromaprint's `fpcalc`, now a requirement beside ffmpeg) against the show's
  other episodes. While playback runs through one, the player offers Skip intro or Skip recap, which
  seeks the whole room past it. Profile's Playback tab has "Skip intros and recaps automatically",
  acted on when the viewer is alone in the room. A dialogue subtitle inside a found intro trims it
  or drops it, so a skip never takes speech. `moviebot-handoff --backfill-markers` marks the episodes
  already in the library.
- **Follow a show.** A show's page has Follow, and Profile a Following tab. When an episode after the
  ones out at follow time airs and the tracker offers it, its followers are told ("episode", a fifth
  notification switch), and those who switched on Download new episodes automatically have it
  fetched, once, at the best quality any of them watches the show at. The page shows the next
  episode's air date. `GET /api/me/follows`, `PUT`/`DELETE /api/me/follows/{imdbId}`; `ShowView`
  carries `followable`, `follow` and `nextEpisode`; a `Follow` settings section.

## [1.79.0] - 2026-10-03

### Added

- **Every episode the tracker has can be played.** On a show's page an episode not here that the
  tracker offers plays from its row: Watch now for that episode, out of its season's pack at 720p,
  playing in the room as it arrives. Only an episode the tracker does not have is marked so.
- **The next episode is ready before it is wanted.** While a room watches an episode, the API fetches
  the one after it, one ahead and no further, at the quality of the one being watched, out of the
  season's pack. The player's next, and the next offered at the end of an episode, fetch the one
  right after into the room when it is not here yet, showing it arrive over the film.
- **Download a season, first episode first.** Each season on a show's page has Download season (or
  the others not here yet); its episodes are fetched with the first at the torrent client's highest
  priority and the second at its next.
- What the tracker offers for a show is asked once per ten minutes, shared by the show page, Watch
  now and the prefetch. `/api/titles/{id}/neighbours` carries `fetchable`.

### Fixed

- Watch now on an episode out of a season's torrent waits on that episode: the room is loaded once
  its own title can play, rather than with whichever episode of the torrent was watchable first, and
  the panel's progress is that episode's file. It owes nobody a "ready" notification.

## [1.78.2] - 2026-10-03

### Fixed

- A room waiting on a Watch now names the film as the catalogue does, the name every page shows,
  rather than as its release does, which is often the film's name in another language.

## [1.78.1] - 2026-10-03

### Fixed

- A film the tracker holds from a digital source up, but only in categories, at sizes or with swarms
  the server turns down, is `notOffered` rather than `notOut`: its page says why it cannot be fetched
  instead of calling it not out digitally yet and offering to wait on it.

## [1.78.0] - 2026-10-03

### Added

- **Watch now.** `POST /api/watch` fetches a film that is not here and plays it in a room the moment
  it can be opened: a film already here is loaded at once, one already arriving has the room added to
  its download, and anything else is fetched at the release a page was offered or the best at 1080p
  (or 720p) from a digital source up. The answer comes at once with the room. The room's state carries
  what it waits on (`preparing`: finding, downloading with its progress, or failed and why), pushed to
  everybody in it, and `WatchNowLoader` loads and starts the film there once the hand-off marks it
  watchable. The download carries the room as a `watch:` tag, so a restart loses nothing. At most
  `Watch:MaxStreams` films (4) are watched while they arrive; past that the answer is `full`.
- `GET /api/films/{imdbId}/availability`: here, arriving, offered at 1080p and 720p with sizes, or not
  out digitally yet, with who waits on it and how many live slots are free. `GET /api/films/find?q=`
  names films from the catalogue.
- **Krystal Cinema's film page**, `/film/<imdbId>`, from the design: Watch and Download (a request
  sheet with 1080p and 720p and the size of each) for a film the tracker offers, its progress while it
  arrives, and Notify for one not out yet. Search lists the catalogue's films, each leading to its
  page; the Notify page links each wish to its page. A show with nothing here gets Watch, which plays
  its first episode while it arrives.
- **The player's preparing panel**: Finding a release, Downloading with its percentage, Starting
  playback, and "Keep downloading in background"; a film that will not arrive says why, and one that
  does starts on its own with "Playing while it finishes downloading."

## [1.77.0] - 2026-10-03

### Added

- **Notifications.** The API tells a person when a film they wait on can be downloaded, a download
  they started can be watched (or could not be made watchable), something they watched leaves within
  a day, somebody keeps or lets go of something they watched, and when somebody invites them into a
  room. Each is kept for the person's bell (`/api/me/notifications`), newest 50 for 30 days, said
  once per thing it is about.
- **Web Push.** A browser subscribes against this server's VAPID key (`/api/push/key`,
  `/api/me/push`), made once into `push-keys.json` in the state directory. Every notification of a
  kind the person allows (`/api/me/notifications/preferences`; all but keeps on by default) is pushed
  to each browser they subscribed; an invite always is. A browser the push service calls gone is
  forgotten.
- **Discord messages.** When no push reaches a person, the bot sends the notification as a direct
  message with an Open button, from `/api/notifications/direct`. Somebody already told in a channel,
  where the announcement mentions them, is not messaged as well.
- **Invites.** `/api/people` lists who can be asked into a room and when each was last seen;
  `POST /api/rooms/{room}/invite` pushes or messages them. The player's invite panel lists them, says
  whether the invite went out as a push or on Discord, and marks who is already watching.
- **Krystal Cinema's bell**, in the sidebar above the person and at the top right of the phone's front
  page, as the design draws it: each kind with its icon, Mark all read, the action, and a way to the
  settings. Profile has a Notifications tab: pushes on this browser, and the four switches. The
  service worker shows pushes and opens the page each names.

### Changed

- Whoever waits on a film without having asked in a channel is told through their notifications when
  it can be downloaded, and comes off the wish then.

## [1.76.1] - 2026-10-03

### Fixed

- The player's backdrop is the poster saved beside the title, or the API's copy for the film or the
  show, so the page loads no artwork from the title index's servers.

## [1.76.0] - 2026-10-03

### Added

- **Krystal Cinema has shows.** `/show/<imdbId>` is a show's page from the design, in the library or
  not: what it is, its seasons as chips flagged with how long each stays, each season's episodes with
  their stills, how far the person got and play, Resume where they left off, and Keep season. The
  request sheet picks seasons and episodes to download, 720p by default with 1080p offered, with what
  each costs on disk. `/tv` lists shows one card each; Home's TV Shows, Recently added and Leaving soon
  rows carry shows and seasons; Search finds shows in the catalogue; an episode's `/title/` forwards
  to its show.
- **The player shows chapters** as marks on the scrub bar, named under the pointer, and as a Chapters
  menu that moves the room. An episode has previous and next episode controls the whole time it
  plays, and next is offered over the film from its closing stretch; neither moves on by itself.
- **`/show`** in Discord fetches a season of a show, or the episodes named, at 720p unless 1080p is
  asked for; the assistant's **`download_show`** proposes the same. Both plan with `ShowPlan`, the
  site's own plan.
- `GET /api/shows/find?q=` names shows from the catalogue; `/api/titles/{id}/neighbours` carries each
  episode's name.

### Changed

- A library title that is an episode is named by its show and its place (`Severance S02E04`) in the
  bot.
- An announced season with no episodes yet is left off a show's page.

## [1.75.0] - 2026-10-03

### Added

- **Shows, episode by episode.** A show's download is ingested one title per picked episode
  (`<show>-s02e04`, `kind: "show"`), in order, each starting once enough of its own file has
  arrived, so the first is watchable while the rest download. The manifest carries `episode`
  (`{season, number}`) with the show in `film`, and is titled with the show's name and the
  episode's place. The torrent owes its transcode until every picked episode is in the library.
- **`/api/shows`** lists the library's shows one entry each, **`/api/shows/{imdbId}`** describes a
  show from TMDB with every season's episodes joined with the library and the asking person's
  progress and where they pick up, **`/api/titles/{id}/neighbours`** names the episodes either side
  of one, and **`/api/shows/{imdbId}/releases`** offers the tracker's packs and single episodes
  season by season. A show's artwork and its episodes' stills are kept by the API under `shows/`
  and served at `/shows/{imdbId}/…`.
- **Picking episodes:** `POST /api/downloads` takes `episodes` (`[{season, episode}]`) and fetches
  only those out of a show's release; picking more from a release already downloading switches them
  on. A `ReleaseOffer` says its resolution and, for a show's, where it sits, how many episodes it
  holds and their average size.
- `Selection__AllowedShowCategories__N` names the tracker's show categories (packaging, the guide).

### Changed

- **A season stays and leaves as one.** A title's stay is its unit's (`TitleStay.unit`): a film's own,
  or its season's. Keeping an episode keeps the season, the season's clock runs from the latest play
  and settle of any episode in it, and a room on one episode holds the season. `/api/stays` lists a
  season once.
- `TitleSummary` carries `episode` and `unit`; an episode's `details` are its show's. Progress and
  history entries name an episode by its show and carry `episode` and `showImdbId`.
- The tracker's clock keeps a show's torrent while a room holds any episode made from it.

## [1.74.0] - 2026-10-03

### Changed

- **Krystal Cinema on a phone gets around by a tab bar** along the bottom edge, as the mobile design
  draws it: Home, Library, Search, Assistant and Me (krystal-ui 0.6.0's `TabBar`). It replaces the
  drawer, its left-edge handle, which sat over page text, and the assistant's floating button, which
  sat over the drawer and the page. Assistant opens the dock above the bar, so the bar is the way back
  out of it. The tab of the page already open scrolls it to its top. Wide screens keep the sidebar.
- **The phone harness looks at the tab bar**: a view taps the Library tab, the assistant is opened
  from its tab, and a check fails a page whose end sits under the bar or an open dock that runs under
  it. Page text scrolling by under the bar is what a tab bar is for, and is not a collision.

## [1.73.0] - 2026-10-03

### Added

- **Krystal Cinema installs as an app.** A web app manifest (standalone, the canvas colour, a 192, a
  512 and a maskable icon, and no `orientation`, so the phone's rotation lock holds), Safari's
  home-screen tags and a launch screen for each iPhone and iPad size, and a service worker that keeps
  the shell and the build's files for offline and lets everything else, the API, the media and the
  hub, reach the network untouched. The Activity registers no worker. The icon is the Movies entry's
  clapperboard on the accent's tile, written by `web/site/scripts/make-icons.mjs`, and the site has a
  favicon of it.
- **`npm run verify:pwa [origin]`** asks a real Chromium whether the site is installable, decodes
  every icon and launch screen at its named size, and drives the API, media and hub routes through
  the controlled page to show the worker kept none of them.

## [1.72.0] - 2026-10-03

### Added

- **`npm run verify:mobile`**, a harness for Krystal Cinema on phones (`web/site/verify/mobile/`): every
  page and every tapped-open state, on iPhones drawn by WebKit and Galaxies drawn by Chromium, against
  a scratch site with a library and people of its own. Each view is measured for what reads as broken
  on a phone — sideways scrolling, something past the screen's edge, text cut off or running into other
  text, a tap that opens nothing, small or covered controls — and photographed, with a contact sheet
  per view putting every phone side by side.

### Fixed

- **A room on a phone has its way back.** The player called any frame 440px wide or narrower
  "popped out" and drew it as one row with no Back and no Invite, which every phone held upright is; a
  frame is now popped out only when it is small both ways, and a phone gets the compact player.
- **Sort and Filters fit on a phone** (krystal-ui 0.5.1): they span the toolbar instead of running off
  the right edge.
- **A film card two-up on a phone** keeps its rating, year and length apart by letting their icons go.

## [1.71.0] - 2026-10-03

### Changed

- **A film on a shelf is krystal-ui's `ShowcaseCard`**, the card the Control Panel's catalog shows a
  game with: the poster with the name on it, a "New" badge for the last two weeks, a caution chip
  when it leaves within three days, its rating, year and length, one status line (preparing, a room
  watching it, how much is left for this person, kept, days left, or its genres), and Watch, Resume
  or Join. It is the card on the home page's poster rows, Movies, TV Shows and Search; the shelves
  keep every card at about 190px or wider so none of it is clipped.
- **The footer links krystal-ui** among the source repositories, and its rooms-watching note has no
  dot.

## [1.70.0] - 2026-10-03

### Changed

- **The sidebar is Home, Movies and TV Shows**, then Notify me and Leaving soon. Search is the field
  at the top of the sidebar. `/movies` and `/tv` are each kind's shelf.
- **Movies and TV Shows use krystal-ui's `Toolbar`**, the bar the Control Panel's lists use: a
  search by name, one Filters popover (state, stay, genre, each option with its count) whose active
  filters show as chips, a sort with its direction, and how many of how many are shown.
- **The assistant dock can be pinned** beside the page or float over it, from the pin in the chat's
  header, as in the Control Panel.

### Removed

- The Downloads page and the home page's "On its way" row. A film being fetched shows on the shelf
  as preparing; a release started from Search says it is on its way.

## [1.69.0] - 2026-10-03

### Added

- **A title says whether it is a film or a show.** The manifest carries `kind` (`"film"` or
  `"show"`; a manifest that does not say is a film) and `/api/titles` carries it on every summary.
- **The home page has a Recently added row** (what came into the library in the last two weeks,
  newest first) and splits the shelf into **Movies** and **TV Shows**; a row with nothing in it is
  left out. **Leaving soon** lists every settled film nobody has kept, soonest first, with the ones
  three days or less from leaving in the warning colour.

## [1.68.0] - 2026-10-03

### Changed

- **The home page's rows are krystal-ui's `Rail`**, the shelf the Control Panel's dashboard uses: each
  row is a titled card with a count, scrolled freely by swipe, trackpad or shift-wheel and settling on
  a card, with arrows when it overflows, the next card peeking at the right edge, and a "View all" link
  where a page has the rest. How many cards a row shows follows the row's own width. The library row
  reaches every film rather than the first eighteen.

## [1.67.0] - 2026-10-03

### Changed

- **Krystal Cinema ends every page with the Krystal footer** (krystal-ui 0.4.0's `Footer`, the one the
  Control Panel uses): the cinema's mark and what it is, how many rooms are watching something right
  now with a live dot (or that it is self-hosted and open source when none are), links to the
  Library, Leaving soon, Notify me and About, and the source on GitHub. TMDB's credit is on the About
  page.

## [1.66.0] - 2026-10-03

### Changed

- **What a person watched is written from the room, as it changes.** The room's state is the one
  record of where a film is, so nothing samples the rooms any more: a pause, a seek, a film changed,
  somebody leaving or the server stopping ends a stretch of playing, and everybody who was watching it
  is credited with where it got to. What a room is playing right now is read from the room, so
  Continue watching, Resume and "last played" are to the second rather than up to half a minute old.

## [1.65.0] - 2026-10-03

### Added

- **What each person watches is recorded.** Every 30 seconds the API credits each person in a room
  that is playing a film with the room's position (`watch.json`); a paused or empty room records
  nothing. `GET /api/me/progress` and `GET /api/me/history` answer for the signed-in person,
  `DELETE /api/me/history/{titleId}` forgets one, and `/api/titles` carries the asking person's
  `progress` and the film's `lastPlayed {at, byId, byName}`. A directory of names (`people.json`),
  learned at every sign-in and join, turns an id into a person.
- **Resume.** A room link carries `&at=<seconds>`; a film loaded into an empty room starts there. The
  hub's `LoadTitle` takes the start as a second argument, null for none.
- **Signed-in devices.** Every token carries an id (`jti`) and is recorded with a short device name
  (`devices.json`). `GET /api/me/sessions` lists them; `DELETE /api/me/sessions/{id}` signs one out
  and `DELETE /api/me/sessions` signs out every other; a revoked token is refused, on the hub too.
- **Two retention clocks.** The tracker's clock removes a download's torrent and source files once its
  seeding window ends, and no longer the transcoded film. The library's clock removes a film settled on
  cold storage after `Library:LeaveAfterDays` (7) without a play, counted from the latest of its last
  play, when it settled and when plays began to be recorded; a kept film and one a room holds never
  leave. The API decides (`GET /api/library/due`), the hand-off deletes.
- **Keeps belong to the library**, by film, in `keeps.json`: `GET /api/titles/{id}/stay`,
  `GET /api/stays`, `PUT`/`DELETE /api/titles/{id}/keep`. The bot's `/keep` and the assistant's keep
  tools use them; a keep shows the keeper's name.
- `DownloadView.requestedBy`, so a person can be shown their own requests.
- Krystal Cinema: Continue watching on the home page; Resume with the time left, and "Played … by
  you" on a film's page; the stay pill and Keep / Un-keep on the library's clock; Leaving soon from the
  library's clock; Profile with History (each row removable), Requests, Playback (default subtitles,
  audio language, quality) and Sessions (sign out one device or every other).

### Changed

- `/api/keeps`, `/api/keeps/{hash}`, `/api/keeps/{hash}/release` and `/api/titles/{id}/retention` are
  replaced by the library's keep and stay routes. Nothing on the site mentions seeding.
- A room nobody is in is not shown as watching now, and a film's page offers to join a room only
  when somebody is in it.

### Fixed

- The player's stream module is in the repository: the ingest output's ignore rule also matched the
  player's own `media/` directory, so a fresh clone could not build the site.

## [1.64.0] - 2026-10-03

### Added

- **A room's link unfurls.** `/r/<room>` is served with Open Graph and Twitter tags, so a link pasted
  into Discord, iMessage, WhatsApp or Slack shows the film: "Watch <film> on Krystal Cinema", its year
  and how far in the room is, and its backdrop (or poster). The card never names or counts the people
  in the room, because whatever unfurls a link reads it without signing in. A room nobody has opened
  yet shows the film its link names.

## [1.63.0] - 2026-10-03

### Added

- **Films are described by TMDB.** The API asks TMDB about every film in the library and every film
  somebody is waiting on, keeps each description and its artwork (poster, backdrop, cast portraits)
  under `films/` in its state directory, serves its own copies, and asks again after
  `Tmdb:RefreshDays`. `GET /api/films/{imdbId}` answers for any film, in the library or not;
  artwork is at `/films/{imdbId}/…`, open like a poster. Set `Tmdb__ReadAccessToken` (TMDB's "API
  Read Access Token") in the environment to turn it on.
- `GET /api/titles` carries, per film, what TMDB said as far as a listing shows it (`details`: rating,
  votes, certificate, genres, runtime, overview, directors, poster, backdrop), its size on disk
  (`sizeBytes`) and when it arrived (`addedAt`).
- Krystal Cinema shows it to the design: a film's page has its certificate, TMDB's rating, genres,
  overview, director, writers and stars, the cast with their portraits and roles, and a details grid
  (released, language, country, what is on this server, when it was added, the IMDb id). The home
  page leads with the newest film; the library filters by genre and sorts by rating and by when a
  film arrived.
- An About page, and a footer on every page crediting TMDB as its terms ask.

### Changed

- The site fetches nothing from outside its own origin: posters a film lacks, and a wished film's
  poster, come from the API's kept copies.

## [1.62.0] - 2026-10-03

### Added

- **The player is Krystal Cinema's own**, drawn to the design: a room at `/r/<room>` on the site,
  full screen with a way back, and the Discord Activity at `/embed/`, the same page with no site
  around it. A wide frame has labelled controls and popovers, a compact one icons and sheets from the
  bottom edge, a popped-out one a single row with an overflow menu. The room's people are on screen
  with a mark on anyone waiting for the film; what somebody else does is said over the film with who
  did it; Invite hands out the room's link (the phone's share sheet on a phone, a copy in the
  Activity); a browser that wants a press before playing is shown who is watching and where, with
  Join; a film not yet in the element shows how far its transcode has got.
- **Quality**: Auto, or any rung of the film's ladder, offered where there is more than one.
- **Preferences that follow the person**: `GET`/`PUT /api/me/preferences` keeps a subtitle language,
  an audio language and a quality for whoever the token names. A film starts on them wherever the
  person opens it, and OpenSubtitles is searched in their language beside English.
- **Who is waiting for the film**: the hub's `Buffering(waiting)` puts a viewer's waiting on the
  roster (`Participant.buffering`), which every viewer sees.
- OpenSubtitles' rating rides on each subtitle on offer (`rating`), when anybody has voted on it.
- `npm run verify` in `web/site`: the player in a real browser against a running API.

### Changed

- `web/site` builds the whole of what the API serves; the separate player and its build are gone.
  Discord's launch at the root is taken to `/embed/` by the site's index, and the URL mapping is
  unchanged.
- The player's subtitles are drawn in one style sized against the frame, and its menus are the
  design's: subtitles with the film's own tracks and what OpenSubtitles offers, audio, quality.

## [1.61.0] - 2026-10-03

### Added

- **Krystal Cinema**, the site (`web/site`), served by the API at the root: the shelf, the rooms
  watching something now, what is downloading and leaving, a film's page with its tracks and how long
  it stays, search across the library and the tracker, the films a person waits on, keeping and
  letting go, and moviebot-assistant in a dock. Built on `@thekrystalship/krystal-ui` 0.3.1. Signing
  in goes through Discord and lets in members of the server alone; the redirect is
  `<origin>/signin`, registered with the Discord application.
- The API answers a path that is no route and names no file with the site's index, or with the
  player's under `/activity/`, so a page can be linked to and reloaded.
- A tool that puts a film on, or finds one to open, carries it as its card (`tool.result`'s
  `result`), and the conversation's history keeps it.

### Changed

- **The player is served under `/activity/`.** Discord still launches the Activity at the root, and
  the site's index sends that load on. In a browser, the player takes the site's signed-in session as
  its proof and its viewer's name, which is how the site opens a film or joins a voice channel's room.
- moviebot-assistant's conversation listing names a conversation by `id`, and its history is
  `entries`, each a turn with its prompt, reply, tools and cards: the shapes the package chat reads.
  A chat nobody has spoken in yet is answered as empty.
- A film put on with nothing written after it is answered with what was done.
- The release build installs the site with the player, reading the design system from GitHub
  Packages.

## [1.60.0] - 2026-10-03

### Added

- **Members of the server sign in on the site.** The OAuth2 callback, given a `redirectUri`, asks
  Discord whether the person is in one of `Discord.GuildIds` (`guilds.members.read`) and issues a
  token only to a member, carrying `member`. Somebody outside the server is refused. The Activity's
  sign-in, which sends no redirect, is unchanged.
- **A member may fetch films as themselves.** Release search, downloads, wishes and keeps answer a
  member's token as well as the service key; who asked is taken from the token, and a member's
  download or wish names no Discord channel. Announcing wishes stays the bot's.

### Changed

- moviebot-assistant answers a person only on a member's token.

## [1.59.0] - 2026-10-03

### Changed

- **The bot is a client of moviebot-assistant, with no engine of its own.** A spoken request the room
  verbs do not cover is a `POST /turn` naming the room and the speaker; a `launch` frame becomes the
  launch card `/watch` posts; the buttons and a spoken yes carry the service's token to
  `POST /confirm`, as whoever agreed; and a room verb is written into the room's conversation with
  `POST /rooms/record`. With the assistant unreachable a request is told so in one sentence, and the
  room verbs, slash commands and announcements are unaffected.
- A confirmed download, wish or keep is posted in the same words and embeds as the slash command
  (`DownloadCommand.Describe`, `NotifyCommand.Describe`, `KeepCommand.Describe`), and a download gets
  its progress message as before.
- The bot no longer carries the agent loop, the prompts, the conversation store or the model client.
  `Assistant.BaseUrl` names the service; `Assistant.ConfirmWindowSeconds` stays the bot's.
- The assistant wire's shapes are in `MovieBot.Client`, so the bot builds without the assistant's
  project. A proposal frame carries `describes` and `askedBy`, and a speaker's name in
  `X-MovieBot-Acting` is percent-encoded.

## [1.58.0] - 2026-10-03

### Added

- **moviebot-assistant**, a service of its own (`src/MovieBot.Assistant`). It holds the agent loop,
  the prompts and tool catalog, the conversations, the proposals waiting to be agreed to and the
  reply checks, and serves the assistant wire contract 2.1: `POST /turn` as server-sent events or one
  JSON answer, `POST /confirm`, the caller's conversations, `GET /events` with attaching to a chat,
  and the `new` and `help` commands. It adds one frame, `launch`, for a film put on or found to open.
  Every act its tools take is the API's, through the service key.
- A person is recognised by the token the API gave them at Discord sign-in, checked with
  `Auth:SigningKey`; a service proves itself with `Auth:ServiceKey` and names the person in
  `X-MovieBot-Acting`. Only a service names a room, which is what gives a turn the room's tools and
  conversation. A person's turn gets the library and fetching tools, with `site.md` as its
  instructions, and a proposal it makes is theirs alone to agree to.
- `POST /rooms/record` writes a room verb the bot carried out into the room's conversation.
- `deploy/moviebot-assistant.service` and `packaging/systemd/moviebot-assistant.service`, listening
  on `Assistant.Listen` (`127.0.0.1:8191`), proxied at `/assistant/` by both nginx configurations.
  Releases carry it; it needs a llama.cpp server the release does not include.
- `src/MovieBot.Client`: the API client, `TitleMatcher` and `FilmClock`, shared by the bot and the
  assistant.

### Changed

- `RoomToken` is in MovieBot.Core, so the assistant checks the API's tokens with the same code that
  issues them.
- moviebot-llm replays the assistant's warm-up request, from `/var/lib/moviebot-assistant`.

## [1.57.0] - 2026-10-03

### Changed

- **MovieBot.Api fetches films.** Downloads, wishes and keeps are served under `/api` to the service
  key alone (`docs/api-contract.md`, "Fetching films"): release search and the picker's suggestions,
  starting and listing downloads, the wish list with its hourly sweep, and keeping or releasing a
  film. Each act names the person a service vouches for, and each outcome is a word and its facts,
  so every client words it its own way. The API reads the tracker, qBittorrent, download, selection,
  retention and notify settings, and refuses to start without the tracker's categories, as the bot
  does.
- **The bot is a client of those routes.** `/download`, `/notify`, `/keep`, the tracker rows of the
  `/watch` picker and the assistant's tools ask the API; the bot still reads qBittorrent for the
  progress messages and the ready announcement. `WishWatcher` posts the wishes the API's sweep found
  and reports which channels it told.
- **The wish list lives in the API's state directory**, `/var/lib/moviebot/wishes.json`. A host that
  had wishes in `/var/lib/moviebot-bot/wishes.json` moves that file across once, with both services
  stopped.
- `Notify` is declared in `moviebot.settings.json`.

## [1.56.0] - 2026-10-03

### Fixed

- **The disk budget measures the download directory's own volume.** moviebot-acquire read the free
  space of `/` whatever the download directory was mounted on, so a download volume could fill while
  the budget saw the system disk's room.

### Changed

- **Downloads leave room for their transcode.** `Download.ReservedGiB` (default 30) is declared in
  `moviebot.settings.json`: moviebot-acquire's disk budget keeps that much free on the download
  volume once a download lands, so the transcode written beside it on a shared volume finishes
  instead of stopping with the disk full. The budget now also warns and reads full against the
  volume when the ceiling sits above the volume's size.
- **Hi10P releases rank last in a search.** 10-bit H.264 decodes on the processor rather than the
  GPU, so its transcode runs at under half the usual pace; a menu shows it marked `Hi10P` and below
  every other release, and still offers it when it is the only one.

## [1.55.1] - 2026-09-25

### Fixed

- **Stopping the API after its container is disposed no longer throws.** `SessionKeeper` disposed
  its cancellation source on `Dispose`, and a host that stops it after that, as a test host stopped
  twice does, failed in `StopAsync` with `ObjectDisposedException`. The source is left undisposed,
  since it holds nothing disposal frees. It surfaced as an intermittent class cleanup failure in CI.

## [1.55.0] - 2026-09-25

### Changed

- **Settings come from one file, `moviebot.settings.json`**, shared by the API, the bot, the
  hand-off and the speech host. `src/moviebot.settings.json` declares every setting with its default
  and ships beside each binary; a host's own copy in its XDG configuration directory
  (`$XDG_CONFIG_HOME/moviebot/`, else `moviebot/` under each of `$XDG_CONFIG_DIRS`) overrides it key
  by key, and the environment overrides both. Each service logs which files it read when it starts.
  The per-service `appsettings.json` files are gone.
- **The environment file holds secrets only**: the bot token, the OAuth2 client secret, the signing
  and service keys, the tracker account and its category names. Everything else the release's units
  and hotbox's units set with `Environment=` is in the settings file.
- **The API takes its OAuth2 client id from `Discord:ApplicationId`** when `Discord:ClientId` is
  unset, so the settings file names the application once for the bot and the API.
- The release's `install.sh` puts the settings file at
  `/srv/moviebot/.config/moviebot/moviebot.settings.json`, the XDG configuration file of the
  `moviebot` account, and keeps it across upgrades. `deploy/hotbox.settings.json` is hotbox's copy,
  installed as `/home/heisen/.config/moviebot/moviebot.settings.json`.
- The deployment guide splits configuration into the settings file and the secrets file.

## [1.54.0] - 2026-09-25

### Added

- **Releases.** Pushing a tag `v<version>` that matches the newest CHANGELOG entry builds
  `moviebot-linux-x64.tar.gz` and publishes it, with its checksum, as a GitHub release. The archive
  holds the API with the player, the bot, the hand-off and the ingest CLI, all published for
  linux-x64 so a host needs no .NET runtime, together with the packaging kit.
- **The packaging kit in `packaging/`**: `install.sh`, which creates the `moviebot` account and the
  library directories, installs the programs to `/opt/moviebot` and the systemd units, and writes
  `/etc/moviebot/moviebot.env` with fresh internal keys; the three units; an env file naming every
  setting a deployer fills in; an nginx site for certbot to complete; and qBittorrent's settings.
- **`docs/deploying.md`**, a step-by-step guide from downloading a release to watching a film.
- **CI** builds the player, the solution and the tests on every push to `main` and every pull
  request, with moviebot-acquire checked out beside this repository.
- `scripts/version.sh` prints the repository's version, and `scripts/package-release.sh` builds the
  release archive locally exactly as the release workflow does.

## [1.53.1] - 2026-09-25

### Changed

- **The tracker's category names come from `/etc/moviebot/moviebot.env`**, as
  `Selection__AllowedCategories__0`, `__1` and so on, through moviebot-acquire 0.18.0. The bot and
  the hand-off refuse to start without them. `deploy/CLAUDE.md` names everything the env file holds.

## [1.53.0] - 2026-09-25

### Changed

- **The bot and the hand-off read the tracker from the `Tracker` configuration section**, through
  moviebot-acquire 0.17.0's `TrackerClient`, and the tracker's address is part of it. Nothing in this
  repository names the site. Operational: `/etc/moviebot/moviebot.env` carries `Tracker__BaseUrl`,
  `Tracker__Username` and `Tracker__Passkey` before this version starts.

## [1.52.0] - 2026-09-23

### Added

- **Every film gains a dialogue-boost mix of each feature track.** Films are mixed for a cinema,
  and folded down to stereo by the standard matrix the dialogue lands about 20 dB under the
  loudest scenes, so a room watching on laptops and headphones rides the volume all night. The
  dialogue sits in the centre channel, which is its own signal only until the downmix, so the
  second mix weights it above the fronts and surrounds there, then compresses and normalises the
  result. It is listed as "English — Dialogue boost" beside the original, which stays the default,
  and choosing it is each viewer's own, like any audio track. The weights were chosen by ear on
  Road House (2024) in a live room between two strengths: the milder took the dialogue-to-peak gap
  from 20.1 dB to 8.8 and was kept, the stronger's 5.8 flattened the action. A layout with more
  speakers per role shares each weight at equal power; a source with no centre channel gets the
  ordinary downmix and the dynamics alone.

  The mix is a pass of its own after the video, before the title turns ready. `loudnorm`
  oversamples to 192 kHz for true peaks, which puts the chain at 8x realtime on hotbox's Athlon
  against 27x without it, below the main pass's rate, so inside the main pass it would hold back
  the head every viewer waits on. A slow second compressor and `dynaudnorm` were measured in its
  place and each landed on a different sound from the one chosen. A film is therefore playable
  exactly as soon as before, and the boosted track appears in the menu about a quarter of an
  hour after the transcode of a two-hour film ends. The master playlist is now written through a
  temp file and a rename, since it is rewritten under players already fetching it.
  `--no-dialogue-boost` skips the mix. Ingest 1.9.0, Core 1.12.0, Handoff 0.9.0.

## [1.51.0] - 2026-09-20

### Added

- **A film can be fetched without taking the room that is watching something else.** Every door
  into a download was the means to watching now: `/watch`'s tracker pick and the assistant's
  proposal both wrote the asker's voice channel onto the torrent, so a room halfway through a film
  that asked for the next one had the next one loaded over it the moment it became watchable.
  `/download film:<name>` fetches a film and stops there, and the assistant's `download_only`
  does the same by voice — "get Inception ready for after this one" leaves the room playing what
  it is playing, where "put Inception on" still takes it. The pipeline already carried a download
  with no room, so nothing about the hand-off, the API or the player moved; what moved is that
  two of the three doors no longer force one. Fetching is its own tool rather than a flag on
  `watch_film` because which was meant is the whole question and this model picks between two
  names more reliably than it sets an argument; `AssistantRoutingTests` measures both directions
  against the real model. `/download` searches the tracker alone and refuses a film the library
  already holds or is already fetching, reading the film the tracker names rather than the release
  name, and skipping either check rather than refusing when it cannot be made. Bot 0.21.0.

## [1.50.0] - 2026-09-18

### Fixed

- **A viewer whose client cannot decode the film no longer sends the room back to 0:00.** The
  player's play and pause published intent from any media element event, including those of an
  element that never loaded and sat at zero, so a click on the poster paused or started the whole
  room at the opening titles. They now publish only from a player holding the film, and a key seek
  from one that is not is measured from the room's position. Player 0.8.0.

- **A client with no H.264 decoder is told so, once, and stops trying.** Discord's desktop client
  decodes H.264 only on the graphics card, so with hardware acceleration off every append failed
  and the player recovered from each failure without limit: the element reset about ten times a
  second, the opening of the film was fetched again each time, and video.js's error dialog flashed
  too fast to read over a poster that never gave way to the film. Reproduced in a browser without
  the decoder: fatal `bufferAddCodecError` every ~100 ms, indefinitely. The player now checks
  `MediaSource.isTypeSupported` before fetching anything and names the setting to change; a fatal
  media error is recovered from twice, then the source is torn down with a lasting message and the
  room's state is no longer applied to it. video.js's error dialog is off. Player 0.8.0.

## [1.49.0] - 2026-09-17

### Added

- **A film is encoded at two sizes, and a player can drop to the smaller one.** A viewer whose
  connection dipped had nowhere to go: one rendition at 9 Mbps meant hls.js could only re-fetch the
  same five-megabyte fragment over the same path, so playback stopped for good at whatever second
  the buffer ran dry while everyone else watched on. Measured on Evil Dead Burn, a viewer who had
  held 9–11 Mbps at a perfect fifteen-segments-a-minute cadence for half an hour was served
  4,151,808 of a 4,895,827-byte segment over sixty seconds and never recovered, across two attempts.
  The ingest now writes `v0` at the source's size and `v1` at 1280 wide and 4 Mbps, cut on the same
  keyframe expression so segment *n* of one rung starts on the same frame as segment *n* of the
  other. A source no wider than the step-down keeps one rung, and `--step-down 0` writes one.
  Ingest 1.8.0, Core 1.11.0.

- **A media ticket opens a film's bytes, in place of who is asking** (`GET
  /api/titles/{id}/ticket`, carried as `?mt=`). A shared cache will not answer a request that
  carried an `Authorization` header, which is why 6,973 requests at the edge were 6,973 requests at
  the origin: every viewer pulled every byte of the film the whole way, and a room watching together
  multiplied that by however many people were in it. A ticket names one title and an hour and
  nothing else, and every viewer of a film is handed the same string, so their requests are one URL
  one cached copy answers. The player sends the ticket or the header, never both. API 1.10.0.

### Changed

- **Segments are two seconds rather than four.** A segment is the unit a stall is measured in: at
  the top rung four seconds is around five megabytes, which a path that slows for a moment cannot
  finish inside the time a player waits before abandoning it. Half the duration is half the bytes.
  The cost is a keyframe twice as often.

- **hotrod's nginx holds segments** (`deploy/nginx-moviebot-ingress.conf`), so a room costs one copy
  across the switch instead of one per person; `proxy_cache_lock` collapses the simultaneous misses
  a synchronised room produces into a single fetch from hotbox. Segments get their own location
  because caching needs buffering on and a growing playlist needs it off. A request carrying an
  `Authorization` header is never stored and never answered from store: that header is not part of
  the cache key, so keeping its response would hand the film to the next request for that URL
  whether or not it proved anything. A ticket is safe in that position because it is in the URL.

- **A subtitle search that fails at the index answers with an explanation** rather than reaching
  Kestrel as an unhandled exception and leaving the picker spinning. The index row that caused it is
  fixed in moviebot-acquire 0.16.2.

### Fixed

- A rung advertises the size it is encoded at rather than the source's, in the master playlist the
  ingest writes and the one the player composes. A manifest written when a film had one rung carries
  no size on it and is read as the source's, so the sixteen films already on the disk keep loading
  without being re-encoded.

## [1.48.0] - 2026-09-17

### Changed

- The bot (0.20.0) answers to "okay computer". "Hey MovieBot" went unheard often enough that people
  repeated simple commands several times: whisper reads the one-syllable "hey" as "A", "Pay" or "K"
  inside a three-second scan window, and the coined name as "movie box" or "movie bought". Measured on
  hotrod's recogniser (the same small.en model as hotbox) with fourteen kokoro voices, each command
  inside open-mic chat and scanned as the listener scans it:

  | trigger | commands found, clean | with room noise | windows found, with noise |
  |---|---|---|---|
  | hey moviebot | 12/14 | 10/14 | 35/70 |
  | okay moviebot | 12/14 | 10/14 | 42/70 |
  | hey computer | 13/14 | 11/14 | 35/70 |
  | hey jarvis | 13/14 | 12/14 | 36/70 |
  | okay popcorn | 14/14 | 13/14 | 55/70 |
  | **okay computer** | **14/14** | **14/14** | **64/70** |

  Seven near-miss sentences ("okay, come on", "my computer crashed, okay", "okay, compute the total")
  in every voice, clean and noisy, triggered only where the sentence names the Radiohead album. The
  command read got all 56 test requests right after the new trigger, primed or not.

## [1.47.0] - 2026-09-17

### Changed

- "Hey MovieBot, pause" works while the room keeps talking, which is how people watch a film together.
  A request was a sentence ended by silence, and open microphones are never silent. On hotbox, a
  pause said inside 12.7 s of unbroken talk got no tone and landed only after the talk ended. The bot
  (0.19.0, `TheKrystalShip.Discord.Voice` 3.0.0) now reads each person's last three seconds for the
  trigger every half second, on a scan lane in moviebot-speech (0.2.0, `TheKrystalShip.Speech.Engine`
  2.6.0). Finding it plays the tone at once and starts taking the request from the word before the
  trigger. `RoomVerbCompleteness` tells the pipeline a request is whole as soon as two readings agree
  that everything up to a full stop is a room verb, so the film moves before the speaker stops talking.
  A request that is not a verb ends when the speaker goes quiet, or at seven seconds, under
  moviebot-speech's eight-second window.
- Measured with two bots in an empty channel against hotbox's recogniser, three rounds each:
  - The trigger five seconds into fourteen of unbroken talk, then "pause it" and more talk: tone at
    +200 to +395 ms, and "pause it" acted on at +1.35 to +1.74 s while the talk ran on for another nine
    seconds.
  - The same with another speaker holding the recogniser: tone at +400 to +660 ms, "pause it" at +1.1 to
    +1.4 s.
  - Seventeen seconds of chat: no false trigger.
- `Voice__SilenceGapMs` no longer exists. `Voice__MaxCommandSeconds`, `Voice__CommandQuietMs`,
  `Voice__ScanWindowMs` and `Voice__ScanStrideMs` are in the bot's README.

## [1.46.0] - 2026-09-17

### Fixed

- The listening tone after "hey MovieBot" plays whole. The reading of the finished sentence found
  the trigger again and stopped the bot talking, and the tone its opening had started was what it
  stopped.
- Saying "hey MovieBot" while the recogniser is busy with somebody else still gets the tone, a
  little later, instead of none. The opening of the sentence is read again when the recogniser
  frees up.

### Changed

- The listening tone is a short notification sound that starts at once.

## [1.45.0] - 2026-09-17

### Changed

- Asking the assistant for a film is one tool, `watch_film`, in place of `load_title`,
  `search_library`, `search_catalogue` and `search_tracker`. It puts the film on when the library has
  it, and otherwise proposes downloading the best release the tracker offers, which plays in the room
  once somebody confirms. When the tracker has no release it gives the id a wish takes, and other
  films the words could mean are named. A room asking for Twilight and Step Up was told only that
  they were not in the library.
- A film named by its place in a series — "the second Pirates of the Caribbean", "step up 2",
  "part two" — is counted by the bot in the order the films came out, whether it is in the library or
  has to be downloaded.
- The assistant's tool for carrying on a paused film is `resume`. Named `play`, it was what the model
  called for "play" followed by a film's name, and the room went on with the film it already held.

## [1.44.2] - 2026-09-16

### Fixed

- `/voice leave` takes the bot out of the voice channel. It reported leaving while Discord kept the
  bot listed in the channel, because the connection was closed without telling Discord.
- A voice session that cannot decrypt anybody's speech rejoins by itself. The silence every client
  sends after speaking passes through undecrypted, and it kept such a session looking healthy for as
  long as people talked into it. Both through `TheKrystalShip.Discord.Voice` 1.4.1.
- The bot hears every speaker in a voice channel. Discord.Net's libdave binding passes user ids
  without a terminating NUL, so a speaker whose id is shorter than one formatted just before it was
  read by libdave as a different user, and every frame they sent failed to decrypt while the rest of
  the room was heard. The bot pins `Discord.Net.Dave` 3.20.2-tks.1, built with the terminator.

## [1.44.1] - 2026-09-16

### Fixed

- Asking the assistant for a film that is in the library puts it on, even after the room's
  conversation has taught the model to retype the film's id. In the live room the model asked for
  `pirates-of-the-caribbean_the_curse_of_the-black-pearl-2004` and similar for the 2003 film, and
  every one was refused, including when the film was named in full. The matcher now ignores
  punctuation, and a film named whole under a year nobody said is taken as that film. When somebody
  did say a number the year may be what picks the film, so it is offered by name instead.
- A film the assistant cannot pick is answered for the model rather than with `/watch`'s wording:
  the films it could have been, by name and earliest first, or that it is not in the library. The
  same list is how a `/watch` for an ambiguous name is now ordered.
- "Loading Pirates of the Caribbean..." on a turn that put nothing on is sent back to the model and
  corrected if it happens again (`RoomLoadingClaim`).

## [1.44.0] - 2026-09-16

### Changed

- A room's conversation with the assistant starts over once the room has been silent for
  `Assistant:IdleResetMinutes`, 15 by default. The model was replaying every turn since the room's
  first request, which in the live room was more than half of each request by the next morning and
  handed it last night's replies to copy. Every turn stays in `assistant.db`; the model is simply
  not shown what came before the silence. A room verb after the gap starts the conversation over
  too, so the pause it records is still there for "why did it stop?".

## [1.43.1] - 2026-09-16

### Fixed

- A download asked for in a room whose conversation had once been answered "I need the IMDb ID" is
  searched for instead of answered the same way again. The model copies its own earlier replies,
  so the request routed in a fresh conversation and failed in the room's real one. The instructions
  tell it to find things out with the tools rather than ask, and a reply asking for an IMDb or
  torrent id is sent back to the model to call them (`RoomIdRequest`).

## [1.43.0] - 2026-09-16

### Fixed

- A transcode no longer runs the host out of memory. The main pass read the source through one
  demuxer feeding the video and every audio track, which runs at the pace of the fastest consumer
  and queues the rest without bound: 9 MB a second on a 1080p rip with four audio tracks, so
  hotbox killed the transcode a third of the way into the film and the hand-off started it again
  from the beginning, every time. Each rendition now opens the source itself, which holds flat at a
  few hundred megabytes and runs faster. `ReadAheadGuard` takes the furthest of the open
  descriptors as the read position.
- A pause after "hey moviebot," no longer hands the next thing said to the bot. At a 500 ms silence
  gap that pause ended the sentence, leaving the trigger alone and the door open for whatever came
  after it. The gap is 800 ms.
- Remarks made while the bot waits for a yes or no to a proposal are no longer taken as answers and
  asked about again. Anything that is neither spends the window silently; the buttons stay.
- Requests longer than four seconds are no longer mangled. moviebot-speech encodes eight seconds of
  audio (220 ms an utterance on the P2000, against 114 ms for four), and whisper given a clip
  longer than its window repeated phrases instead of stopping.
- A trigger heard in the opening of a sentence and misheard in the whole of it is answered, through
  `TheKrystalShip.Discord.Voice` 1.4.0. The tone had played and nothing answered.
- "Download the second Pirates of the Caribbean movie" downloads Dead Man's Chest. `search_catalogue`
  lists films in the order they came out, where the index orders them by popularity and the model
  took the second row, Dead Men Tell No Tales.

### Changed

- Transcripts of everything heard are logged on hotbox while the triggers are tuned
  (`Voice__LogTranscripts` in the unit).

## [1.42.1] - 2026-09-15

### Fixed

- Sitting silent in a voice channel no longer sets the bot listening. The recogniser was primed with
  the trigger phrases, and whisper answers a breath, a keyboard or line hiss with the sentence it was
  primed with, so noise came back as "Hey moviebot.": a tone, ten seconds of listening without the
  trigger, and the next noise put to the assistant as a request, often opening the door again. It is
  primed with the name alone now, which invents no trigger from noise and still hears a spoken one.

## [1.42.0] - 2026-09-15

### Added

- The assistant's replies are held to what their turn did. A reply that says it loaded, paused or
  downloaded something on a turn that did none of it is sent back to the model once, and posted with
  a correction if it says so again; the same goes for a figure of four digits or more that nothing the
  turn was given contains. A reply that proposes something without mentioning it gets a line saying
  it waits for confirmation.

### Changed

- The assistant's prompt reading, tool catalog, proposal tokens and conversation compaction come from
  `TheKrystalShip.Agent`, shared with kgsm-llm's assistant, rather than from copies of its own. A
  catalog that disagrees with the tools is refused naming both directions in one message.

## [1.41.0] - 2026-09-15

### Added

- MovieBot answers. Anything said to it in a voice channel that is not one of the room verbs goes
  to the model on hotbox's card, with the room and the library in front of it, and the answer is
  posted in the voice channel's chat. "Go back a bit, I missed that", "jump to an hour in", "put on
  Collateral instead", "carry on", "what are we watching?" and "why did it stop?" all work, and so
  does "download Inception", which searches the catalogue and the tracker and proposes the release.
  The room verbs still go through the gate first, with no model involved, and act exactly as fast.

  Moving the room happens at once, as the person who asked. A download, a fetched subtitle, a wish
  and a keep are proposals, posted with two buttons; a spoken yes or no straight afterwards is the
  same answer, and whichever comes first acts. Each is carried out by the same code as `/watch`,
  `/notify` and `/keep`, so a download started this way reports its progress and starts in the
  room when it can be watched.

  A room verb is written into the room's conversation, so "why did it stop?" is answered with who
  paused it. The bot warms the model with its real instructions and catalog when it starts, and
  leaves that request for the model's unit to replay when it restarts.

  `Assistant__Enabled` switches it on, and the unit sets it.

### Changed

- The subtitle search and fetch shapes are part of the shared contract library, so the bot reads
  the same types the API writes. Nothing on the wire changed.

## [1.40.1] - 2026-09-15

### Changed

- A spoken sentence counts as finished after 500 ms of silence rather than 800. That silence is most
  of the wait between saying "pause" and the film stopping — at 800 ms it was 1.07 to 1.26 s end to
  end on hotbox — and the room verbs are short enough that the shorter pause does not cut them off.

### Fixed

- libdave no longer fills the bot's journal. It wrote every skipped silent frame straight to standard
  output, several a second, and on the first voice session that was 363 of 519 lines. Its messages go
  through the bot's logging now, where only its warnings and errors are shown by default.

## [1.40.0] - 2026-09-15

### Added

- MovieBot listens. `/voice join` brings the bot into the voice channel you are in, and "hey
  MovieBot, pause" stops the film for the room — as do "resume", "back fifteen" and "skip forward a
  minute". There is no model in the bot: what is heard goes to moviebot-speech for words, and the
  words go to a gate that knows a handful of verbs and reads the whole utterance. "Should we pause?"
  contains the word and is not the verb. A phrasing that looks like a verb and cannot be read safely
  — "go back" with no amount, "rewind a bit", "go back 1:30" — is not guessed at, because a misread
  verb moves the film for everybody.

  The act is silent, because the film stopping is the acknowledgement and the player already says
  who did it. It is recorded under the speaker's own account, it needs the room to be holding a
  film, and it is logged with the milliseconds from the moment the speaker stopped talking to the
  moment the room changed.

  Joining posts a notice in the channel saying the bot is listening, because that notice is the only
  way anyone but the person who ran the command learns they are heard. If it cannot be posted the
  bot leaves again.

### Changed

- `RoomChanged` is in the shared contract rather than the API, so the bot reads the type the API
  writes.
- The invite the bot logs asks for Connect and Speak as well. A bot already in a server keeps the
  role it was given.

### Fixed

- The test host journals its rooms somewhere of its own. It had been writing `rooms.json` beside the
  test binary, and the API restores every room in its journal on start, so one run's rooms turned up
  in the next.
- A room-control test listened for the play before the load's own push had arrived, and over long
  polling the load could land second and be read as the play.
- The repository names its package feeds. It had been restoring the speech packages through the
  user-level configuration, which also lists a local folder that could satisfy a package the real
  feed does not have.

## [1.39.0] - 2026-09-14

### Added

- A room can be driven from outside it. `play`, `pause`, `seek` and `seek-relative` are POSTs
  beside the title endpoint that was already there, so the bot — and, next, something somebody
  said out loud — can stop a film without holding a connection to the room.

  **A position is optional, and leaving it out means "wherever the room is".** A player knows
  where its own playhead sits and says so; a caller that is not watching does not, and a missing
  position read as zero would pause the film *and* send the room back to the opening titles. That
  failure looks like success, because the film stops either way. The position is resolved where
  the change is applied, under the same lock.

  **`seek-relative` is its own act because the film is moving.** Reading the position and then
  seeking to it minus fifteen spends a round trip in between, and in a playing room that round
  trip comes out of the fifteen. Before the start is the start; past the transcode head is clamped
  like any other seek, and the clamp comes back in the response body — over the hub it goes to the
  caller alone, and an HTTP caller is the one thing a broadcast cannot reach.

### Changed

- Every change to a room goes through `RoomControls`, whichever door it arrived by. Recording who
  did it and pushing the new state to everybody watching are the same act for a player on the hub
  and for the bot over HTTP, and the title endpoint was already carrying its own copy of half of
  it. One implementation cannot let a change reach one door and miss the other.

## [1.38.0] - 2026-09-14

### Added

- MovieBot hears. `moviebot-speech` holds one whisper model on hotbox's card and answers a unix
  socket, so what is said in a voice channel can become text without anything leaving the
  machine. It listens and never speaks: no synthesiser is registered, which keeps a gigabyte of
  models and the runtime behind them off this host entirely, and a surface that asks it to speak
  is told there is nothing to ask — an absence the protocol has always had a word for.

  It runs its own build of whisper rather than a prebuilt one, and both ways a prebuilt fails are
  invisible until they are expensive. The CUDA build carries no PTX for a Pascal card and would
  drop to the processor at a fortieth of the speed without a word; the Vulkan build loads on this
  machine, enumerates the card, and then core-dumps on the first transcription, because its CPU
  backend is compiled for AVX2 and this Athlon has none. The build in `deploy/vulkan` is made for
  this processor and links Vulkan as a hard dependency, so it either works or the service refuses
  to start.

  It is resident and warm before anyone speaks. The model loads at startup rather than on first
  use, and the daemon puts one request through itself, because a loaded model is not a ready one:
  the card compiles its compute pipelines the first time a graph actually runs. Whisper also
  encodes a thirty-second window whatever it is given, so the window is set to four seconds — the
  difference between 870 ms and 183 ms for the same clip. Measured through the socket on hotbox:
  285-309 ms for an utterance, and 429 ms for the first one after a restart.

## [1.37.0] - 2026-09-14

### Added

- The two pieces of inference a spoken command needs run on hotbox's card: whisper to turn what
  somebody said into text, gemma to turn that text into a room command. Both are built from
  source by `deploy/vulkan/build.sh` and installed to `/opt/moviebot` by `install.sh`, and
  `moviebot-llm.service` holds the model resident so nobody pays a load in front of a room.
  They are built rather than installed from a repository for three reasons that each cost a
  measurement to find. hotbox's Athlon has neither AVX2 nor BMI2 while the machine that builds
  for it has both, and ggml compiles for the builder by default, so the obvious build produces
  something that dies on the first request that reaches the wrong kernel; the build now pins the
  instruction set and refuses to ship an object containing an opcode that machine cannot run.
  The distribution builds ggml's backends as modules loaded from a path compiled into the
  library, where a backend that is not found is not an error but a silent fall back to the
  processor at a fortieth of the speed; linked as an ordinary dependency it either resolves at
  exec or the service does not start. And each build carries the ggml it was built against,
  rather than meeting a different one at runtime.

  The model is warm before the service is called active. The port opens before the card can
  compute — Vulkan builds its compute pipelines on the first request that needs them, and the
  prompt prefix is only cached once something has been prefilled through it — so a warm-up runs
  as `ExecStartPost` and systemd waits for it. It warms only the shape it sends: replaying its
  own request takes 241 ms while a first request carrying a different prompt and catalog takes
  1,093 ms, so the body it sends lives in `warmup.json` and is the catalog the assistant
  actually asks with.

## [1.36.0] - 2026-09-12

### Added

- A viewer sets how subtitles are drawn for themselves: text colour, the band behind it and how
  opaque it is, size, how far off the bottom of the frame they sit, an outline or a shadow, the
  family and the weight. It is a panel behind the same cog as the tracks, answered by a sample cue
  drawn by the function that draws the ones over the film, and it is one viewer's own — held once
  per browser rather than per title, because it is about the eyes reading it, and never sent.
  Untouched it is white on a black band at the size the frame gives it, which is what a cue is
  drawn as with nothing said about it.

### Changed

- Cues are drawn by the player library on every browser, `nativeTextTracks` off. A cue the browser
  draws takes the operating system's caption settings and nothing the page can say about it, so
  the one code path holds everywhere rather than everywhere except an iPhone.

### Fixed

- The player checks reach the menus through the cog that holds them, and read an unavailable
  track's reason off the mark that carries it.

## [1.35.3] - 2026-09-08

### Fixed

- One spinner stands in the middle of the frame. video.js draws its own whenever the element
  waits for data, in its own design, and the player's own answers three questions rather than
  that one — a film being loaded, a playhead being moved to where the room is, and a stall that
  has lasted long enough to be worth showing — so the library's is off and the player's is what
  a person sees.
- The control bar appears when the film does. Its styling says how the bar looks and no longer
  what its display is, so when it arrives is the library's answer again: on screen once the film
  has started, which is the moment a control can answer a press rather than sit over a frame
  holding nothing.

## [1.35.2] - 2026-09-08

### Fixed

- The player drives the media element video.js ends up with rather than the tag it was handed.
  Where a media element cannot be moved into the frame the library builds — iOS — the library
  clones the tag, disposes the original and plays the clone, and everything here reaches the
  element directly: the transcode is attached to it, the room's timeline is applied to it, and
  the events it raises are what tell the room somebody pressed play. Holding the tag pointed all
  of that at an element no longer in the document, leaving an iPhone on the film's poster with
  controls that answered, a play button that changed shape and a film that never arrived, while
  every other browser played it.

## [1.35.1] - 2026-09-07

### Fixed

- A settled copy of a film that is being made again is cleared by the pass that can see both
  disks, and not only by the ingest. The ingest clears it as the transcode starts, which does
  nothing while the cold volume is away, and a copy that survived that would be resolved ahead of
  the film being made for the whole of its transcode: a room that asked for the new release would
  watch the old one until it finished. Only a manifest saying the film is being made counts as
  evidence — a directory carrying none is not a reason to delete the one finished copy of a film.

## [1.35.0] - 2026-09-07

### Added

- Films are made on one disk and kept on another. `Media__ColdRoot` and `Handoff__ColdRoot` name
  a second volume; a finished title moves there and the library is the two roots together, with
  the cold one resolved first. A title is under exactly one root, which disk it is under changes
  no URL, and nothing about it appears on the wire.
- The move is a copy, ordered so that every failure before the last step leaves the film where it
  was: assembled under `.incoming` on the cold root, checked against what was read, committed with
  one `sync -f`, renamed into place, and only then deleted from the media root. Timestamps are
  carried across, so artwork that is served asking to be revalidated is not refetched wholesale.
- `Media__ColdRoot` is used only while a `.moviebot-cold` marker sits at its root, made by hand on
  the volume. A mount point is an ordinary directory when nothing is mounted on it, and a cold root
  that is really a directory on the disk being drained would take films off that disk and put them
  straight back on it.
- `/health` carries `coldStorage`, which names what is wrong with that volume and is absent when
  nothing is.

### Changed

- Both services start whether or not the cold volume is there, report the fault and serve
  everything on the media root. Nothing is pruned while it is missing: a film on a volume a pass
  cannot see looks exactly like one already deleted.

## [1.34.0] - 2026-09-06

### Changed

- The three services run on hotbox, which holds the media root, the state directories, the
  torrent client and the GPU that transcodes. They are built on hotrod, which holds the
  checkouts and the toolchain, and published across by rsync.
- `movies.thekrystalship.com` is served in two halves on two machines. There is one public
  address on this network and hotbox does not hold it, so hotrod publishes the name and routes
  it across the LAN over an https hop made under hotbox's own name: the certificate on the far
  side is chosen by that SNI while the server block is chosen by the Host header. The two nginx
  files are `deploy/nginx-moviebot-ingress.conf` and `deploy/nginx-moviebot.conf`.
- The hand-off reaches ffmpeg through its unit's own `PATH`. Where a card's driver branch is
  older than the NVENC API the distribution's ffmpeg is built against, that ffmpeg still lists
  `h264_nvenc` and cannot run it, so a matching build is named by the unit and kept off the
  global PATH.
- `Handoff__MaxConcurrentIngests` is set per host, against the aggregate rate the card actually
  saturates at rather than a figure carried over from another one.

## [1.33.0] - 2026-09-06

### Changed

- A film opens at the position the room is already at, rather than at its beginning. The player
  is handed that position as it loads the source, so the first segments it fetches are the ones
  being watched instead of the opening it would otherwise have to seek away from.
- The controls stay held, with the spinner up, until that first seek lands. A play issued in
  that window fires with whatever position the video element is holding, which is zero, and
  takes the whole room back to the start of the film.

## [1.32.0] - 2026-09-04

### Changed

- The API is a native binary, for the same reason the hand-off is: what it held after a day was
  the JIT's code and the type data built around it, not the rooms, and a process compiled ahead
  of time has neither. Its heap is collected on one thread rather than one per core, since it
  belongs to a handful of rooms and the film itself never touches it, and the globalization data
  is not loaded, since every comparison is ordinal.
- Everything the API puts on the wire is named in a serializer context, and the two contexts are
  the only resolver the hub and the endpoints use. The bodies that were anonymous objects are
  records now, with the same fields under the same names, and a type left out of the contexts
  fails a test rather than a request.

## [1.31.0] - 2026-09-04

### Changed

- The hand-off is a native binary. What a long-running .NET process holds is the code the JIT
  has written and the type data the runtime builds around it, and a process compiled ahead of
  time has neither: its code and its type system are data in the binary, paged in from the file.
  Every comparison in the pipeline is ordinal or invariant, so the globalization data is not
  loaded either. Nothing about what it does changes; it idles at a quarter of what it did.

## [1.30.1] - 2026-09-04

### Changed

- The three services hold less of the JIT. What each process holds is not its heap, which is a
  few megabytes in every one of them, but the code the JIT has compiled and the type data behind
  it, and tiered PGO keeps that growing for as long as a service runs: every method it finds hot is
  held as three copies with counters beside them. PGO is off in all three. The bot and the hand-off
  also compile each method once, at full optimisation, since a worker that runs for weeks has no
  startup to protect; the API keeps tiering, so a restart mid-film answers quickly.

## [1.30.0] - 2026-09-04

### Fixed

- An Activity invite lasts the film it was made for. Discord counts an invite's life from the
  moment it is made rather than from the last person through it, so a flat half hour shut the door
  on every film longer than one: everybody already watching carried on, and the card they came
  through stopped letting anybody else in, with nothing on either side to say so. The invite is
  now given the film's running time plus `Launch:InviteGraceSeconds`, which defaults to the half
  hour the API waits before forgetting an empty room, so the invite and the room behind it run out
  together. An age of zero reads as never expiring to Discord, so a floor stands under whatever is
  configured.

### Added

- A launch card says when it has stopped being a way in. The rooms are read from the API every
  half minute, and a card whose room has closed — or whose room has moved on to another film — is
  edited where it stands: it goes grey, the button and the link on its title go, and the
  description says what happened and that `/watch` in a voice channel starts the film again. The
  film, the poster and the fields stay, because scrolling back to what an evening watched is worth
  being able to do. A room that closed and a room watching something else are told apart, since
  one of them is still going.
- The invite behind an expired card is revoked in the same pass, so a link copied out of a card
  stops working when the card says it has.
- The cards still standing are written to `launches.json` in the state directory, configurable
  with `Launch:CardsPath`. Which message hands out which invite into which room exists in Discord
  and nowhere else — the API has never heard of a Discord message — so a bot that did not write it
  down would leave every card it had posted looking live for good. A pass that cannot reach the
  API does nothing at all: rooms it could not read are not rooms that closed.

## [1.29.0] - 2026-09-03

### Fixed

- `/watch` finds a film released abroad under another name. The tracker names a film the way the
  country that made it named it, so searching it for an English title matched no release at all:
  "The furious 2025" found 45 rows and offered none, while the same film by its id returns nine,
  every one named `Huo.zhe.yan`. The menu now identifies the film in the title index and asks the
  tracker for it by id. Pasting an IMDb link works for the same reason, being a film already
  named.
- The menu fills in as somebody types. The filter that keeps a search for "Heat" from answering
  with "Dead Heat" was applied to half a title, which is never equal to one, so every keystroke
  before the last offered nothing: "eurotri" offered none of six where "eurotrip" offered five.
- A row saying why nothing is on offer, where the menu came back empty and read exactly like a
  tracker that had not answered. Picking that row submits the text as typed.

### Changed

- A row shows the film's name beside the name the release carries when the two differ, so
  `Huo zhe yan` under a search for "The Furious" reads as the film that was asked for.

## [1.28.1] - 2026-09-03

### Fixed

- A channel Discord refuses is given up on, rather than asked again every ten seconds. A film
  announced into a channel the bot cannot see, and a progress message in one, met the same refusal
  on every pass for as long as the download was kept: thousands of them in an evening, each
  spending the channel's rate allowance to be told the same thing, and the announcement never
  arrived either way. A refusal over permission, or about something that is gone, now retires the
  tag that asked for it and says once which channel was refused and what the bot needs there.

## [1.28.0] - 2026-09-03

### Added

- A downloaded film is let go after a week of seeding, unless somebody keeps it. The hand-off
  prunes: the title's directory first, then the torrent and its files through the client. The
  week is measured on the torrent client's own seeding clock, which counts only while this
  machine is on and the torrent is active, and nothing is removed under the tracker's minimum plus
  a margin whatever the window is set to. A film a room holds waits until the room lets go, a film
  still owing its transcode is left to the log, and a title directory is deleted only when its
  manifest names the source this download arrived as, so an older release's turn to go cannot take
  a newer one's transcode with it. `Retention__SeedDays`, `Retention__TrackerMinimumHours` and
  `Retention__MarginHours` set the figures, and the hand-off reads `Api__ServiceKey` to ask the API
  which films the rooms hold.
- `/keep add` keeps a film on disk until somebody runs `/keep remove`, and `/keep list` shows every
  film on disk with who keeps it or how much more seeding it has before it leaves. The keep is a
  tag on the torrent and names who set it; anyone may keep a film and anyone may let it go.
- A launch and a ready announcement say how long the film stays, or who keeps it.

## [1.27.0] - 2026-09-02

### Added

- The up and down arrows step the volume by five points, and so does the wheel turned over the
  volume control. Each step flashes where the volume landed, with the step beside it, in the
  middle of the screen where a seek from the keyboard flashes what it did.
- What somebody else did to the room is said in the same place, to everyone in it: who paused,
  who resumed, who moved the film and to where, for long enough to read. The person who did it
  is not told, since they pressed the key. The line under the player uses the same words.

### Fixed

- The subtitle menu's search results followed the room from one film to the next. Changing the
  film with `/watch` left the OpenSubtitles list showing what it had found for the previous film;
  the list is forgotten with the film now, and a search that lands after the change is dropped.
- Subtitles a film carries are shown available once they are, for a viewer who opened the film
  while it was still transcoding. The film's own tracks are read again every time the subtitle
  panel is opened, so the menu is right whatever reached the page while it was closed.

## [1.26.0] - 2026-09-02

### Changed

- `/watch` fetches a film that is not here yet, so there is one command. Its menu lists the
  library while anything in it matches, and once nothing does the same menu shows the tracker's
  ranked results, with the release's quality, size and seeds beside each name. Picking one starts
  the download and, because the film is transcoded as it arrives, loads it into the voice channel
  the person was standing in the moment it can be watched, posting the launch as a new message
  that mentions them. The reply to the pick is the download's progress message, which names the
  room the film will play in. A pick from outside a voice channel downloads and announces, as
  before.
- The hand-off writes the id a film goes under in the library onto its torrent before the
  transcode starts, so the bot opens the film by the id it was actually given rather than by a
  second parse of the release name.
- The download watcher acts on a film becoming watchable wherever the download has got to. It
  waited for the download to finish first, which held the announcement of a playable film for the
  length of the download, and it now looks every ten seconds rather than every thirty, since
  somebody is waiting in a voice channel for it.
- A download's progress message says when the film is already watchable.
- `/notify` points at `/watch` where it pointed at `/find`.

### Removed

- `/find`. Everything it did is reached through `/watch`.

## [1.25.0] - 2026-09-02

### Added

- `/notify` tells a person when a film that cannot be downloaded yet can be. `/notify add` takes
  the film's name, autocompleted from the catalogue, or a pasted link to its IMDb page; `/notify
  list` shows what a person is waiting on and `/notify cancel` takes them off it. The bot asks the
  tracker about every film on the list once an hour and posts a new message, mentioning exactly
  the people who asked in that channel, naming the release it found and pointing at `/find`. A
  film counts as available only once a web encode or better is offered: a film in cinemas has
  camcorder recordings on the tracker within days, and those are not what anybody is waiting for.
- Asking is refused, with the reason, when the film is already in the library, already
  downloading, or already on the tracker, so nobody waits on something that is here.
- The wish list is the one thing the bot writes down, in the state directory systemd hands it.
  The unit now carries `StateDirectory=moviebot-bot`.

## [1.24.0] - 2026-09-02

### Added

- What a film gains while people are watching it reaches them without a reload. The API watches
  the manifests of the titles occupied rooms hold and pushes the fresh copy over the hub as
  `TitleChanged`; the player adopts the head, the preview sheet and the subtitle list from it.
  A subtitle fetched or confirmed by one person is announced the same way, so the whole room's
  menu changes at once. The player's five-second manifest poll is gone.

### Fixed

- Preview thumbnails appear for a film opened while it was still transcoding. The sheet is written
  after the main pass and the manifest names it only once the film is ready, and the player took
  nothing but the head from a fresh manifest, so everyone who opened a film early scrubbed a bar
  that previewed nothing for the whole film. The subtitles a source that was still downloading
  gains at the same point were lost the same way.

## [1.23.0] - 2026-09-02

### Changed

- `/watch` changes the film for a room that is already watching one, and the room keeps playing.
  The new film is loaded from its start and whether the room is playing is left as it was, so a
  room mid-film goes straight on into the next one rather than sitting paused until somebody
  presses play. The reply says it switched, and names the film it replaced.
- Asking for the film the room already holds no longer restarts it. The room is left exactly
  where it is and the reply hands over the way in.
- The hub's `LoadTitle` follows the same rule: from the start, playing if the room was playing.

## [1.22.0] - 2026-09-02

### Added

- Discord says what is being watched. The bot's status reads "Watching Dune (2021)" while one room
  is watching a film, counts them when several are, and shows nothing when none is. The line under
  each room's voice channel names the film and where it has got to, to the minute, or says it is
  paused, and is cleared when the room empties — including a line left behind by a bot that died
  mid-film, which it recognises as its own on the next start. Both are read from a new room listing
  on the API on every pass, so the bot remembers nothing. The bot needs Set Voice Channel Status
  and Manage Channels for the second, and the invite it logs carries them.
- Each viewer's own presence comes from the Activity: the film's name and poster beside their
  name, a bar running from where the room is to the end of the film while it plays, "Paused" when
  it is not, and how many others are in the room. This is the one place Discord gives real rich
  presence to, and it costs one more OAuth scope, so the Activity asks once more for consent the
  first time it is opened after this.
- `/api/config` carries the API's public address, from `Api__PublicBaseUrl`, which is where
  Discord's servers fetch a poster from.

## [1.21.0] - 2026-09-02

### Fixed

- Films no longer wait a whole transcode each for their turn. The hand-off took them strictly in
  turn, so the second and third films anybody asked for were invisible until the ones ahead
  finished — not in `/watch`, not announced, not openable, however much had downloaded. Since a
  film is watchable seconds after its own transcode starts, that cost exactly what transcoding
  alongside the download was for. Up to three now run at once, which still leaves each of them
  running several times faster than anyone can watch.
- An interrupted transcode is picked up again. Becoming watchable cleared the tag that recorded
  the work as owed, so anything stopping the hand-off after that point — a crash, a deploy — left
  the film marked failed with nothing to retry it, while the log claimed it stayed owed. The two
  facts are separate tags now: what is owed survives until the transcode finishes, and what is
  watchable is its own mark.
- A poster is on disk before the manifest names one. The manifest advertised artwork from the
  moment it was written and the file only arrived when the download completed, so every film
  ingested while downloading served a 404 for its whole download — and a surface that fetches
  artwork on its own servers caches that.

## [1.20.0] - 2026-09-02

### Added

- Rooms survive a restart. What a room is watching and where it has got to lived in memory and
  nowhere else, so restarting the API took the film out from under everybody in it: the player
  carried on playing against a server that no longer knew what it was, the screen said nothing was
  playing, and seeking stopped working. Every room is now written to the state directory and read
  back before the server begins listening, keeping its epoch and its revision — so it is the same
  run of the same room, a connected client goes on applying pushes rather than discarding them,
  and nothing about the restart reaches a person watching. Position needs no special handling: it
  is a place at an instant rather than a number that ticks, so a room that was playing is still
  playing, at the right place, however long the restart took.
- `/health` reports how many rooms are occupied and by how many people, so a restart is something
  that can be looked at first rather than found out about afterwards. No names: the route is open.

### Fixed

- A client that finds its room holding nothing offers the film it is already showing. It offered
  the one named in the page's address, which an Activity never has — it is launched from an invite
  that names nothing — so the recovery was inert in the only place it was needed.

## [1.19.1] - 2026-09-02

### Fixed

- Scrub previews are the frames they claim to be. The sheet was built by asking the decoder to
  skip everything but keyframes, which is several times faster and silently wrong on some files:
  on the one HEVC source here it returned smeared frames belonging to no moment in the film, which
  reached the scrub bar looking like a corrupt encode of a film that plays perfectly. Every frame
  is decoded now, on the card where there is one — thirteen seconds per ten minutes of film
  against thirty-five, and a thirtieth of the processor time, which is time the transcode wants.
  A machine with no card, or one whose driver refuses the file, decodes it itself.
- Artwork is checked rather than trusted for a year. A poster and a preview sheet keep their names
  and are rewritten in place, so serving them as immutable left whoever had already looked at a
  broken one holding it until the file name changed, which it never does. Segments, which really
  never change, are untouched.

### Added

- `moviebot-ingest <source> --thumbnails` rebuilds the previews for a title already in the
  library. Everything a preview is made from is in the source file, so correcting a sheet costs
  four minutes rather than re-encoding the film behind it.

## [1.19.0] - 2026-09-02

### Added

- Films are named by the catalogue rather than by the file they arrived in. The hand-off resolves
  the film before the transcode starts — by the IMDb id the tracker supplies, or by the name and
  year parsed out of the release — and keeps its name, year, top billing and poster in the
  manifest. Before the transcode rather than after it, because a film is watchable and announced
  within seconds of the first segments landing, and metadata added at the end arrives a quarter of
  an hour after every message that would have shown it.
- Every film has artwork. A source that shipped its own cover art keeps it; every other film gets
  the catalogue's poster, fetched at the width it will be seen at and written beside the film.
- `moviebot-handoff --backfill` gives films already in the library what a film ingested from now
  on gets on the way in. The four already here are done.

### Changed

- Every message and link about a film shows the film's name. The launch card leads with it and
  carries the billing, the poster and a link to the film's page; the announcement that a download
  is ready names the film rather than the release; picking a film by name matches either. The
  release name is still shown beside the name, because which encode arrived is worth knowing — it
  is just not what the film is called.
- The two places that still lead with the release name are the search results and a download in
  progress. Neither has been through the catalogue, and both are about a file rather than a film.
- The poster is an embed's small image rather than its large one. Posters are portrait, and a
  large one fills a message with artwork nobody asked to look at.

## [1.18.0] - 2026-09-02

### Fixed

- A film paused and resumed no longer jumps forward by however long it was paused, and seeking no
  longer stops working afterwards. Rooms live in memory, so a room the server builds again —
  restarted, or swept for being empty — counts revisions from zero; a client that kept comparing
  those against the run before discarded every push it was sent for as long as its page stayed
  open. It then drove the film from a state nothing could correct: drift correction extrapolated
  the old anchor across the whole pause and hard-seeked the playhead forward the moment playback
  resumed, and every seek it published was accepted by the server and thrown away on arrival, so
  the scrub bar and the arrow keys moved nothing. Each state now carries the run of the room it
  belongs to, and a client that sees a new one starts counting again.
- A room the server has built again is told which film it holds, so a restart leaves the player
  on the film rather than on an empty room it can no longer be moved off.
- Nothing is derived forward from a room that cannot be heard from. The anchor runs whether or
  not the film does, so correcting to it while disconnected is a guess about where a film that may
  have been stopped has got to.
- A derived position is clamped to the length of the film. A room left playing derives one that
  keeps growing, and seeking a media element past the end of what it holds never completes —
  which blocks every seek made after it.
- Opening a film still being transcoded no longer drags the room to the transcode head. The media
  element seeks for reasons of its own, and the start position a player picks for a playlist still
  being written is the end of it; a seek is published by the control that made it and never by the
  element reporting one.
- A reconnect resynchronises. What `Join` hands back answers where the room is rather than
  announcing that it moved, so it is applied whatever revision it carries, and coming back to a
  backgrounded tab asks the same question.

## [1.17.0] - 2026-09-02

### Added

- A zoom lane on the scrub bar. Resting a pointer on the bar for a moment opens a panel above it
  holding the three minutes of film around that point as a strip of frames, ruled every ten
  seconds and named every thirty, with the chapters and the playhead that fall inside those
  minutes drawn where they belong. It is a control of its own: clicking or dragging inside it
  seeks. Two hours across a bar puts several seconds under every pixel, which finds a scene and
  cannot find a moment inside one; the lane is worth about thirty times that, so a second is a
  distance rather than a rounding error — and the frames are what make it a magnifier rather than
  a ruler, since only a picture says whether the second under the pointer is the wanted one.
- It draws as many frames as the sheet actually holds for those minutes, so none is repeated and
  none thrown away, and the strip's own shape sets how tall the lane is.
- The bar below never changes what a pixel is worth while the lane is open, so a long move is
  never trapped behind a precise one. A caret in the lane says which part of the window the coarse
  bar is pointing at, the window moves when the pointer leaves it, and pointing away from both
  closes it.

### Changed

- The control bar is held up by a count of what is being read on it rather than a flag, so a menu
  closing no longer drops the bar out from under the zoom lane, or the other way round.

## [1.16.0] - 2026-09-02

### Added

- A seek from the keyboard says so. An arrow key puts a round badge in the middle of the film
  reading how far the playhead moved, and takes it away again inside half a second. Five seconds
  inside a scene routinely passes without a visible cut, so before this a key that worked and a
  key that never registered looked the same. Presses inside one window are one gesture and are
  summed, so a held key reads as a single growing number.

### Changed

- Glass is the player's one surface. The control bar, every menu, the scrub preview, notices, the
  connection pill, the name card and the badge above are all the same blurred, translucent panel
  from the same handful of tokens, so the film stays visible under whatever is being read and a
  new panel is the right thing by default rather than by being written out again.

## [1.15.1] - 2026-09-02

### Changed

- The settings menu is the same glass as the control bar it rises out of, rather than a panel in
  front of the film.
- Its rows have room. Everything in them is a size somebody can read from across a room rather
  than from arm's length, and the space between them is the difference between a list and a wall
  of text.

## [1.15.0] - 2026-09-02

### Changed

- Scrub previews are twice the size. It is the thing somebody is looking at when they seek, so it
  is sized to be looked at. How many frames a sheet holds is now a pixel budget rather than a
  count, since a browser holds four bytes for every pixel of it the whole time a film is open, and
  larger frames mean fewer of them: a two-hour film gets one every twenty-five seconds or so
  rather than every seventeen.
- No focus ring around the film itself. The frame takes focus because the library makes it
  focusable, not because it is a control, and a ring around the whole picture tells nobody
  anything. The controls inside it keep theirs, and only when a keyboard put it there.

## [1.14.3] - 2026-09-02

### Fixed

- Playback starts on the first click and stays started. A click was being answered twice: once by
  the library, which has toggled playback on a click all along, and once more by a timer behind it
  waiting to find out whether a double click was coming. The film played for the length of that
  wait and then stopped.
- Double clicking does nothing. It asked for fullscreen, and the player already is the screen.
- The scrub bar's preview shows the film rather than a grey box. A background image is fetched by
  the browser, which carries none of this Activity's token, so the sheet of frames was answering
  401. It is fetched with the token and drawn from what comes back, the same way subtitles are.

## [1.14.2] - 2026-09-02

### Fixed

- Pressing play no longer stops the film a moment later. A play interrupted by a seek is rejected
  and the element goes back to paused, and the pause it then reports was published to the room as
  though somebody had decided to stop it. Playback the room wants now waits for the playhead to
  arrive instead of racing it, and a pause raised by a seek in flight is machinery rather than
  anybody's decision.
- The controls sit on one line. Everything in the bar is centred by the box it is in, rather than
  by giving each glyph a line height equal to the bar — which lines up only while those two numbers
  agree and drops a button half a row when they stop.
- A setting's value sits at the far side of its row rather than against the name it belongs to.

### Added

- Every intent the room receives is logged with who sent it and which connection it came down. A
  room that pauses itself is either a client publishing a pause or a player stopping without saying
  so, and those have nothing in common but the symptom.

## [1.14.1] - 2026-09-02

### Fixed

- Starting a film no longer needs anybody to fight it. A play interrupted by one of this client's
  own corrective seeks was being read as the browser refusing to play without a gesture, so a
  button appeared asking for one — and pressing it started the same race again, which is what made
  playback take several attempts to begin.
- Nothing is corrected while the film cannot play forward. A playhead waiting for data drifts from
  the room by definition, and seeking it to catch up throws away the buffer it was waiting for,
  which is the same stall again and further behind.
- A seek is not issued while one is already running. Abandoning a seek mid-flight during the
  opening buffer restarts the load.
- "Nothing playing yet" appears only once the room is known to hold no film. Whether it holds one
  takes a round trip to answer, and the message was being shown while that was still in the air —
  telling everybody arriving to a film that there wasn't one.
- There is no play button before there is something to play. Waiting is shown as waiting, and the
  only button a person is asked to press is the one that appears when the browser genuinely wants
  a gesture.

## [1.14.0] - 2026-09-02

### Changed

- Audio and subtitles sit behind one settings cog rather than a control each. The lists inside it
  render lists and nothing else: opening, closing, the lock that holds the control bar open and the
  way back out belong to the menu hosting them.
- The control bar is glass over the film rather than a panel beneath it, with a hairline along its
  top edge.
- Everything is a quarter larger, and the scrub bar and volume slider are grabbed by targets far
  taller than the lines they draw. Aiming at a six-pixel bar is a test of precision; the target
  around it is nearer thirty.
- The mute control is an icon rather than an emoji.

### Fixed

- The scrub bar's preview bubble disappears when the pointer leaves it. It never did: the hidden
  attribute works through a user agent display rule, and the one that laid the bubble out beat it,
  so the bubble stayed until the whole control bar faded.

## [1.13.0] - 2026-09-02

### Added

- The scrub bar previews the frame under the pointer, from a sheet written at ingest. One image
  holds every frame, so a preview appears the instant a pointer lands rather than after a fetch.
- Chapter ticks on the bar, with the chapter's name in the preview. Nothing is generated or
  guessed: a disc already records these, and a release that carries none simply has none.
- The keys a player is expected to answer — space and k to play, the arrows and j and l to seek,
  f, m and c for fullscreen, mute and subtitles. Every one of them moves the whole room, so
  nothing is bound that a hand resting on a keyboard could trigger.
- A click on the film plays or pauses it and a double click goes fullscreen, where the browser
  grants fullscreen at all.
- A spinner once playback has been waiting long enough that it is not a stutter. Playback can
  outrun a transcode here, and a stall otherwise looks like nothing happening.
- The total time can be clicked to show what is left instead.

## [1.12.0] - 2026-09-02

### Added

- The scrub bar shows the time under the pointer before a seek is made. It matters more here than
  in a player watched alone: a seek moves the whole room, so reading the moment first is the
  difference between choosing one and discovering one. Past the transcode head it says the film is
  not ready there, which is otherwise only learned by trying and being refused.

## [1.11.1] - 2026-09-02

### Changed

- The subtitle picker fits without scrolling. Six candidates are offered rather than twelve, since
  the list is ranked and a longer one only adds worse answers below the good ones, and the rows are
  tighter.
- Rows carry a mark rather than a sentence. The words are on the mark, where a pointer and a screen
  reader both reach them, and the whole state of a row is on the row itself. The confirm control is
  the confirmation as well as the action, so nothing says it twice.

## [1.11.0] - 2026-09-02

### Added

- Only the subtitle languages a room reads are extracted from a download. A disc carries thirty
  and the three rows anybody wants cannot be found among forty-eight, so the hand-off keeps English
  and the ingest run by hand still keeps everything.
- What was left behind is named in one line rather than thirty rows, so a language missing from a
  film that plainly has it is answered instead of reading as a fault.

## [1.10.0] - 2026-09-02

### Added

- The subtitle picker. What the room already has is listed first and renders at once; what the
  index offers is fetched underneath it, and choosing one both adds it for everyone and switches
  the person who asked to it.
- A track can be confirmed by whoever watched with it. That is the only ground truth there is —
  frame rate, release name and hash are all proxies for whether a subtitle looks right on screen,
  and a person watching is that question answered directly. A confirmation outranks every
  measurement, including one of our own that disagrees with it.
- A confirmation records how much of the film had been watched. Drift only shows up late, so a
  track confirmed near the end has been cleared of it where one confirmed two minutes in has not,
  and the menu says which.
- A film with a confirmed track does not search the index when its menu is opened. There is
  nothing worth interrupting anyone for once somebody has settled it, so the search waits to be
  asked for.

## [1.9.0] - 2026-09-02

### Added

- Subtitles can be fetched from an outside index when the ones a film shipped with do not fit it.
  Searching is free and only fetching spends the day's allowance, so every candidate is judged
  first and what was compared is shown rather than summarised: whether it was indexed against this
  exact file, whether its frame rate matches, and whether it came off the same kind of source.
- A fetched subtitle is measured against a track that came out of the film itself, and the offset
  that comes back is applied before it is written. What lands on disk already fits.
- A title records which film it is. Kept apart from the fingerprint of the file it was made from,
  because an id for the film stays true across every copy of it where a hash describes only one.

### Changed

- One person fetching a subtitle adds it for the whole room; which track each viewer selects stays
  their own choice. They are stored outside the media root, so a re-ingest that replaces a title's
  directory cannot take with it the one thing in the pipeline that is not regenerable for free.

## [1.8.0] - 2026-09-02

### Added

- A title records what it was made from: the release name, the file size, its frame rate and the
  hash subtitles are uploaded against. It is what tells a viewer whether a subtitle found elsewhere
  was timed against this exact release or against a different one, and it outlives the source file
  itself.

## [1.7.0] - 2026-09-02

### Added

- The English subtitle track is checked once it has been repaired, and a track that is still wrong
  says so at ingest instead of forty minutes into a film. The check is that two non-ASCII
  characters do not stand next to each other in English, which catches mangling through encodings
  the repair does not reverse.

### Changed

- English is repaired with the benefit of the doubt on the one case that cannot be decided by
  decoding: a closing quote whose last byte the encoder discarded is restored even with nothing in
  the document to prove that is what it was. Every other language keeps the strict reading, where
  a run that cannot be proven is left as it is.

### Fixed

- Text mangled through Latin-1 is repaired alongside text mangled through Windows-1252. The two
  encodings differ only in the block the corruption passes through, and the reversal now carries
  that block back to its bytes rather than refusing it.

## [1.6.0] - 2026-09-02

### Added

- Subtitle tracks are repaired on extraction when the release shipped them written as UTF-8, read
  back as Windows-1252 and written again, which turns a right single quote into three characters.
  Nothing downstream can spot it, because the file is valid UTF-8 and valid WebVTT and every byte
  survives the transcode. The repair works one run of non-ASCII characters at a time and keeps only
  what decodes strictly, so the same characters occurring legitimately in Portuguese, Romanian or
  Vietnamese are left exactly as they are.

## [1.5.0] - 2026-09-02

### Added

- A download's message shows where it has got to: a bar, a percentage, a speed and an estimate,
  and the number of seeds it is connected to. A slow download is visible as slow rather than
  indistinguishable from a broken one, and none connected says why.

### Changed

- Every message about a download is built from one definition, used by the command that starts
  one, the updater that keeps it current and the announcement when it is ready.

## [1.4.0] - 2026-09-02

### Added

- The person who asked for a film is mentioned when it arrives, so nobody has to watch a channel
  waiting for it. Only that one account can be notified by the message: it is named by id rather
  than by allowing mentions generally, so nothing composed from what somebody typed can ping
  anyone.

## [1.3.0] - 2026-09-02

### Added

- A film starts transcoding while it is still downloading, so it is watchable within seconds of
  being asked for rather than after the download. Measured on a slow torrent: the transcode began
  at 7 percent downloaded.
- The transcoder is held behind the part of the download that has arrived, and stopped when it
  catches up. On that same torrent it was held back seventeen times; without it the film would
  have been encoded partly from unwritten file, with nothing to show anything was wrong.

### Changed

- Subtitles are extracted after the main pass when the source is still arriving, since they have
  to be demuxed from a whole file to be complete. A track is advertised only once its file is
  whole, which is the same rule as before reached from the other side.
- A film is named for what it is rather than for how it was encoded. A container's title tag is
  routinely the release name again, so the name comes from the same parse that builds the id.

## [1.2.0] - 2026-09-02

### Added

- A finished download is turned into something the player can open, without anybody running the
  ingest by hand. It runs as its own service: it writes where the bot may not, it takes minutes
  where a bot restart would abandon it, and it holds the GPU.
- A film is announced the moment it becomes playable rather than when its transcode ends, which
  on a feature is a quarter of an hour earlier.
- A download that arrives but cannot be prepared says so, rather than being indistinguishable
  from one still being worked on.

## [1.1.0] - 2026-09-02

### Added

- `/find` searches a tracker for a film that is not in the library yet and starts it downloading,
  with the results offered as you type. It is answered by the repository beside this one, which is
  referenced as a library.
- A film is announced in the channel it was asked for once it has finished downloading. What to
  announce and where is kept on the torrent rather than in the bot, so a restart in the middle of
  a long download still announces it.

### Changed

- Both slash commands autocomplete a title and mean opposite things by it, so the autocomplete
  handler routes on the command name. `/watch` searches films already on disk; `/find` searches
  for ones that by definition are not.
- The bot unit reads `/home` rather than having it masked. The disk budget is measured off the
  filesystem, and a masked directory reads as zero used — a ceiling that never stops anything.

## [1.0.0] - 2026-09-01

### Security

- Discord is the only way in. The library, the films, the rooms and the hub were reachable by
  anyone who knew the hostname: `/api/titles` listed the collection, `/media` served the segments,
  and a stranger could read any room and change what it was playing. All of it now requires a
  token, and a token is issued only after Discord has confirmed who somebody is.
- The API refuses to start without `Auth:SigningKey`, because a missing key would otherwise mean
  a server that quietly lets everybody in.
- The bot proves itself with `Auth:ServiceKey`, having no Discord user of its own to be.
- A plain browser says where to go rather than failing one request at a time against a black
  screen. Cover art stays open: Discord's servers fetch it to render an embed and carry no token,
  and a poster is not the film.

## [0.9.0] - 2026-09-01

### Fixed

- The volume slider is proportional to loudness. `HTMLMediaElement.volume` is a linear amplitude
  multiplier and hearing is not linear in amplitude, so half the slider was about -6 dB and
  sounded closer to two thirds as loud. The slider now holds the loudness asked for and the media
  element is given its 5/3 power, which puts half the slider at -10 dB — half as loud. It replaces
  the player library's volume panel, which has no way to hold a position different from the
  amplitude it sets.

### Added

- Rooms are forgotten after 30 minutes with nobody in them, so a link handed out for one evening
  stops being a way back into it. The window starts when the last person leaves, so a film
  playing to a full room is never at risk however long it runs, and an old link opens a room with
  nothing in it rather than resuming what was playing. Configured by `Rooms:IdleTimeout`.
- The Discord invite an Activity launch produces expires on the same schedule. It gates joining
  only, so people already watching are unaffected when it lapses.

## [0.8.0] - 2026-09-01

### Fixed

- The film chosen with the slash command reaches the Activity. The bot named the room after the
  voice channel and the Activity named it after the SDK instance, so the two were never in the
  same room and the choice arrived nowhere. Both now key on the channel, and the bot loads the
  title through `POST /api/sessions/{id}/title` — an Activity opens at a URL the bot never wrote,
  so a query string cannot carry it the way a browser link does.

### Changed

- The page is the player. The library, the participant list, the panel and its toggle are gone,
  and the frame takes the whole viewport, which is what maximising was for. A room with no film
  says how to fill one instead of showing a library to pick from.
- The browser checks reach a film the way the product does — named at launch — rather than by
  clicking a library that no longer exists.

### Removed

- The forty-second test clip is out of the media library, so it is never offered by autocomplete.
  It stays as a fixture for the browser checks, which run against their own media root.

## [0.7.0] - 2026-09-01

### Changed

- The page gives its room to the film. The top banner and the strip under the player are gone;
  the film's name and who last touched playback moved into the side panel, and the connection
  state became a marker on the frame that shows itself only when something is wrong.
- The side panel collapses, and stays collapsed — the choice is a viewer's own, kept like volume.

### Added

- A control that fills the frame with the film. Discord's iframe withholds the Fullscreen API, so
  a real fullscreen button cannot work inside an Activity and is offered only on a plain page;
  what can be reclaimed is the page around the player. Escape gives it back.

## [0.6.0] - 2026-09-01

### Added

- `POST /api/auth/discord/callback`: the Activity's sign-in exchange. The client secret redeems
  the code here because a secret shipped to a browser is not a secret; the player handles only
  the code and the token that comes back, which is its own.
- `GET /api/config`: serves the application id, which is public, so the player needs no
  build-time configuration and no rebuild when it changes.
- `discordEnvironment.ts`: the player as an Activity. Two things differ from the browser page and
  they are the two the environment seam exists to hold — the room is the SDK's instance and the
  viewer is who Discord says they are. The API, media and hub are the same relative paths, since
  a root mapping forwards them.
- `ActivityLaunchPresenter`: the bot opens the film in the voice channel. When it cannot — no
  application id, a channel it cannot see, a missing Create Instant Invite — it says which and
  falls back to the browser link rather than failing quietly.

### Changed

- Playback starts at half volume. A film mastered for a cinema is punishing at full on laptop
  speakers. It is a starting point, not a shared setting: anyone who has set their own keeps it.

## [0.5.0] - 2026-09-01

### Added

- The API serves the player from `wwwroot`, so one origin carries the page, the API, the media
  and the hub — a Discord Activity then needs one URL mapping rather than several.
- `scripts/build-player.sh` builds the player and installs it there.
- `deploy/`: systemd units for the API and the bot, and the nginx vhost. Secrets come from a
  mode-600 `EnvironmentFile`, because systemd does not read `/etc/environment` and that file is
  world-readable.
- A name gate: the plain link carries no identity, so the player asks once and remembers.

### Fixed

- A launch link naming a film the room is already showing no longer reloads it. Loading a title
  resets the position, so a friend clicking the link mid-film sent everyone back to the start.
- The invite URL the bot logs omitted `CREATE_INSTANT_INVITE`. Installing from it produced a bot
  that could not create Activity invites, failing later as though Discord were at fault.

## [0.4.0] - 2026-09-01

### Added

- `MovieBot.Bot`: a Discord.Net bot with a slash command that resolves a film, opens the room's
  session and replies with a launch link and a poster embed. It holds no state: the session is
  named by the voice channel, so two people asking in the same channel reach the same room with
  nothing remembering that the first one asked.
- `ILaunchPresenter`, the seam between resolving a film and handing someone into the player. The
  link presenter is what exists; an Activity presenter replaces it without the command changing.
- A guild allow-list, because a verified application cannot stop being addable.
- The poster is attached only when an address Discord's own servers can reach is configured. An
  unreachable image renders as a broken embed, which reads as a broken bot.

## [0.3.0] - 2026-09-01

### Added

- `web/activity`: the player, as a plain page. video.js and hls.js, audio and subtitle menus
  built from the manifest and split into feature and commentary, per-viewer preferences in
  `localStorage`, and a scrub bar drawn at the film's true duration with the transcoded region
  shaded — the player library only knows what the playlist advertises, which is wrong for a
  title still being written.
- Shared play, pause and seek over the hub, with revision gating, position derived from the
  anchor against a server-clock offset, echo suppression by comparison, and graduated drift
  correction that nudges the rate for a small gap and seeks only for a large one.
- A browser verification suite: it plays the film, reads the menus, drives two viewers through
  play, pause and seek from either side, knocks one out of step to watch it recover, and edits
  the fixture manifest to a transcoding state to exercise the seek clamp.

## [0.2.1] - 2026-09-01

### Added

- `MasterPlaylist`: ingest writes an HLS master playlist binding the audio renditions to the
  video, and the manifest points at it. Without one a player handed a bare video rendition plays
  the film silently with no audio track to switch to.

### Fixed

- `docs/api-contract.md` described echo suppression as a flag cleared when the expected media
  event arrives. A seek interrupted by a second seek raises no `seeked` at all, so such a flag
  stays set and swallows the next thing the viewer does. The rule is now to compare against the
  position that was applied.

## [0.2.0] - 2026-09-01

### Added

- `MovieBot.Api`: the library, media and session surface. Serves manifests and HLS with the
  content types hls.js requires, holds shared session state, and pushes it over SignalR.
- Server-authoritative sessions with monotonic revisions and a server-clock timestamp on every
  push, so reordered or duplicated pushes and skewed client clocks are all harmless.
- Head-aware seek clamping. A seek past what has been transcoded is granted short of the head
  and answered with `SeekClamped` to the caller alone.
- `no-store` on playlists for a title still transcoding; segments served immutable.
- `MovieBot.Tests`: two real SignalR clients against the app in-process, covering propagation,
  revision ordering, clamping, participant broadcast and position derivation.

## [0.1.0] - 2026-09-01

### Added

- `MovieBot.Core`: the manifest contract — title, duration, transcode status and head, video
  rendition, audio tracks and subtitle tracks — with a source-generated camelCase serializer and
  an atomic writer, so a reader polling during a transcode never sees a half-written document.
- `MovieBot.Ingest`: the ingest CLI. Probes a source with ffprobe, extracts every text subtitle
  track to WebVTT and the container's cover art, then transcodes to HLS with `h264_nvenc`,
  writing `EVENT` playlists so the film is playable within seconds of the transcode starting.
- NVDEC decoding via `-hwaccel cuda`. Software-decoding a 16 Mbps HEVC Main 10 source costs
  35 s of CPU per 40 s of film against 5.5 s on the GPU, and saturates every core for the
  length of a feature while the GPU sits half idle.
- HDR detection from the source transfer function, with `tonemap_opencl` converting PQ and HLG
  sources to BT.709 and SDR sources left untouched. `--tonemap` overrides the detection.
- Subtitle classification into text and bitmap codecs. Bitmap tracks are listed as unavailable
  with a reason rather than dropped.
- Track labelling from the container's title tags, with commentary split out by ffmpeg's
  `comment` disposition.
- `--dry-run`, which prints the manifest a source would produce without transcoding it.

[1.0.0]: https://github.com/TheKrystalShip/MovieBot/releases/tag/v1.0.0
[0.9.0]: https://github.com/TheKrystalShip/MovieBot/releases/tag/v0.9.0
[0.8.0]: https://github.com/TheKrystalShip/MovieBot/releases/tag/v0.8.0
[0.7.0]: https://github.com/TheKrystalShip/MovieBot/releases/tag/v0.7.0
[0.6.0]: https://github.com/TheKrystalShip/MovieBot/releases/tag/v0.6.0
[0.5.0]: https://github.com/TheKrystalShip/MovieBot/releases/tag/v0.5.0
[0.4.0]: https://github.com/TheKrystalShip/MovieBot/releases/tag/v0.4.0
[0.3.0]: https://github.com/TheKrystalShip/MovieBot/releases/tag/v0.3.0
[0.2.1]: https://github.com/TheKrystalShip/MovieBot/releases/tag/v0.2.1
[0.2.0]: https://github.com/TheKrystalShip/MovieBot/releases/tag/v0.2.0
[0.1.0]: https://github.com/TheKrystalShip/MovieBot/releases/tag/v0.1.0
