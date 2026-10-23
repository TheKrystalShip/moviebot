# MovieBot.Api invariants

`docs/api-contract.md` is the authority for what this serves on the wire. Rooms are
`Sessions/CLAUDE.md`; subtitles fetched from outside are `Subtitles/CLAUDE.md`; downloads, wishes
and the library's clock are `Acquisition/CLAUDE.md`; the plan the hand-off transcodes by is
`Transcode/CLAUDE.md`; notifications, pushes and invites are `Notifications/CLAUDE.md`; saving a
film onto a device is `Offline/CLAUDE.md`.

- **The server is authoritative and there is no host.** Clients send intent; the store decides,
  stamps the server clock and assigns the next revision. A client never pushes state.
- **Every type on the wire is named in a serializer context, and the two contexts are the only
  resolver.** `ApiJsonContext` holds this service's own shapes and `ManifestJsonContext` the Core
  library's, and nothing falls back to reflection, in the JIT build the tests run as much as in the
  native one, so an anonymous object or an unregistered type fails a test rather than a request. A
  list is looked up by the type the endpoint declares and written by the type it holds, so both are
  registered, and a list bound for the wire is built as a `List<T>`: a collection expression yields a
  type of the compiler's own that nothing has registered.
- **Everything the API keeps is one SQLite database, `Data/CinemaDatabase`** (`moviebot.db` in the
  state directory, or `Database:Path`; the tables are `Data/Schema`). Each read takes a pooled
  connection and each change is one immediate transaction, after every change before it, so a store
  that reads before it writes (a want's flags, a notification's key, the first poster pin) decides
  inside the transaction it writes in. A store keeps nothing in memory but what a listing reads on
  every row (`PersonDirectory`, the film database caches), written through. What
  the film database said is kept as its own JSON, one value per row beside the columns it is looked
  up and aged by; what a person did is columns. Served bytes are files and nothing else is: artwork
  under the caches' directories, with what each image came from in `images`
  (`Films/KeptImages`), and fetched WebVTT under `Subtitles:Root`. The file is `0600`, since it holds
  the push key pair. A file at an earlier schema version is brought forward by `Schema`'s steps, each taking
  the file from one version to the next in one transaction and carrying across the records a changed
  table held, and a file at a version this build does not know stops the API from starting.
- **Resolve configuration inside the DI factory, not at the top of `Program.cs`.** Reading
  `builder.Configuration` while the builder is still being assembled misses sources added later — a
  test host's media root, for one — and the service silently points somewhere else.
- **Playlists for a transcoding title are `no-store`, and served from one open file.** A cached
  growing playlist makes the film appear to end early, which looks exactly like a broken transcode.
  ffmpeg replaces the playlist by renaming a fresh copy over it, so one served by path takes its
  length from one copy and its bytes from the next, and breaks off mid-playlist.
- **MovieBot signs nobody in; every route names the action it needs** (`Auth/`; the token rules, the
  service key, the guest link, the media ticket and the issuer pin are `docs/api-contract.md`, "Signing
  in", and `../../../kgsm-docs/systems/moviebot/sign-in.md`). `CinemaActions` declares the actions once:
  the checks name them and `/.well-known/tks-actions.json` is written from the same list. A route
  without `RequireAction`, `RequireActionOrService`, `RequireService` or `AllowAnonymous` admits
  nobody, because the fallback policy is that. The person is always the token's — a request body
  never names who is asking.
- **The service key and a guest link name nobody.** A route that acts for a person never takes the
  service key; a guest link is authenticated only by the routes that name it (`Sessions/CLAUDE.md`,
  Guests).
- **A film's bytes are opened by a media ticket, never by the header and the ticket together.** The
  ticket is the same string for every viewer of a film, which makes their requests one cacheable URL; a
  shared cache refuses to store a response to a request carrying `Authorization`.
- **Media answers HEAD as well as GET.** Browsers probe a URL before fetching it, and a 405 there
  reads as the file being unavailable.
- **Which disk a film is on changes no URL.** `/media/{id}/…` resolves the id per request (see
  `src/MovieBot.Core/CLAUDE.md`), which is also what makes it safe to move a film out from under a
  room that is watching it.
- **CORS reflects the origin rather than enumerating one.** The Activity is served from Discord's
  proxy under an origin that is not known ahead of time, and a failed preflight is invisible: no
  status, no log, just a request that never happens.
- **A path that is no route and names no file is a page.** Cinema names its pages and rooms by
  path, so the fallback answers any of them with the site's index; the Activity is the same page at
  `/embed/`. It is the fallback rather than the default-files middleware that does this, because an
  endpoint matched ahead of the static files means they never see the request. Paths under `/api`,
  `/media` and `/hub` stay a 404.
- **The film database is reached by this service alone, and asked once per film.** `Films/FilmStore`
  keeps each description and, under one directory per film, every image it names; it serves its own
  copies at `/films/{imdbId}/…`, and asks again only once a copy is
  `Tmdb:RefreshDays` old; a database that cannot be reached then costs freshness and nothing else.
  `FilmWarmer` keeps every library film, every wished film and every wanted title described, so a
  listing reads only what is kept and never waits on the network. No page talks to the database or its image store.
  A show is kept the same way by `Shows/ShowStore`, its artwork under `shows/`, its description and every season's
  episodes asked for together with their stills, and served at `/shows/{imdbId}/…`; the warmer keeps
  every show the library holds described. An episode's show is never asked of the film side.
- **An episode is numbered as its release numbers it, everywhere** (`Shows/EpisodeNumbering`). The
  tracker, the torrent's files, the library and the subtitle index agree on it; the film database
  sometimes does not, most often by counting a special or a promotion as a season's first episode.
  `ShowStore` hands out every description already put into the releases' numbering, so no reader
  translates. The evidence is the episode names release and file names carry after their marker,
  from the library and from the show's torrents in the client (`EpisodeNumberingWarmer`, every ten
  minutes): each name matching the database's episode of the same name in that season anchors the
  two, and the offset holds until the next anchor. A database episode before the first release
  episode is a special and leaves the season. A still is asked for by the release's numbering and
  resolved to the database's file. A season with no names to match keeps the database's numbering.
  `Films/PopularNow` keeps the week's trending films and shows, the list and one poster per entry
  (under `popular/`) and nothing more, asked for every six hours and served at `/api/popular` and
  `/popular/{imdbId}.jpg`; an entry's page asks for the rest of it when it is opened.
  A search result's rating and overview are `Films/TitleGlances`, one lookup by IMDb id per title
  kept: a title already described answers from its description, a search waits
  for new lookups at most a second and a half, and a kept one is refreshed in the background, after
  a year for a title out over a year and after a week for anything newer. A result's poster is the
  one its page shows: the described title's kept poster, or the glance's, fetched once under
  `glances/` and served at `/glances/{imdbId}/poster.jpg`. `Films/Catalogue` browses the database by
  a person and by a genre, keeping each answer, a person's credits for a week and a genre's most
  popular titles for a day, and offers every title it lists to the glances, so its poster is served
  the same way; a person's portrait is kept with their credits, under `catalogue/`. A description whose cast carries no
  person id is due at once, since that id is what leads from a cast member to their work.
  `Acquisition/TitleSearch` holds the title index's answer for a name in memory for an hour.
- **A title has one poster, the first the API showed it with.** A person finds a title by its poster,
  and the database's answers name different posters for one title (search, trending and a description
  can each differ, and a refresh can name another). `Films/PosterPins` pins each IMDb id to the image
  path its poster was first shown from, and `FilmStore`, `ShowStore`, `TitleGlances`
  and `PopularNow` all fetch the pinned path. In the library, `Library/TitlePosters` serves that poster
  at `/media/{id}/poster.jpg` and names it on `/api/titles`; the poster the hand-off put beside the
  title (the title index's) is served only for a title the database holds none for. A source's own
  cover art is never extracted (`src/MovieBot.Handoff/CLAUDE.md`).
- **A title has one rating, the newest the film database has given.** Every answer the API keeps
  carries the rating from when it was asked, and a new release's moves daily, so each surface would
  show its own. `Films/TitleScores` holds one rating and vote count per database id and kind
  (`title_scores`), in memory and written through, and every place that receives a rating from the
  database records it as an observation stamped with when its answer was asked, kept only if newer
  than what is held: `AnswerKeeper` for every kept answer it serves (credits, searches, discoveries,
  charts, lists), and `FilmStore`, `ShowStore`, `TitleGlances` and `PopularNow` for theirs, each when
  it fetches and when it first reads a row. Every view takes `rating` and `votes` from
  `TitleScores.Serve`, falling back to the answer's own for a title nothing is held for, and a rating
  with fewer than `CatalogueSearch.RatedVotes` votes is served as none. A film released within sixty
  days is described again after `Tmdb:RecentRefreshDays`, since the observations only move as fast as
  the database is asked.
- **Finding is `Films/CatalogueSearch`, and every answer is kept by `Films/AnswerKeeper`.** A search by
  name, a person by name and every dated credit they have, a discovery by genre, years, people,
  keywords, language, a rating floor and similarity to another title, and the lists of what is out and
  what is coming are asked of the film database once and kept (a question somebody typed is named
  under `queries/` and trimmed after thirty days unasked), for a day or for six hours, and a database
  that cannot be reached costs freshness and nothing else. What names a title (its IMDb id, makers and
  top-billed actors) is one call kept a week, so a title in many answers is asked about once. Every word
  that names a genre, a person, a keyword or a language is resolved to the database's and reported in
  `resolved`; a word the database does not know is refused (`404`) rather than dropped, because a
  discovery that ignores a filter answers a question nobody asked. The library join (`LibraryShelf`) and
  whether the asking person watched are made when an answer is read and are never kept with it. A title
  the database holds no IMDb id for is listed by its own id with no page and no poster. The Discover page's shelves are `Films/DiscoverShelves`: each shelf the same for everybody
  is a fixed question by the database's own ids (genres, keywords, companies, networks, collections,
  people), answered and kept through `CatalogueSearch`, and `Films/DiscoverWarmer` asks every one of
  them again every `Tmdb:ShelvesWarmMinutes`, which asks the database only for what is due, so a page
  never waits on it. A tile's posters and a series' films are read from each shelf's last answer,
  held in memory and renewed by every warmer pass, so `/api/discover` asks nothing of the database once
  the warmer has run. The shelves that follow from a person read the watch record and the library as
  listed, and "Popular here" names nobody and no count.
- **The shelf is listed from memory.** `Library/LibraryListing` rebuilds the library's list every
  ten seconds in the background, and `/api/titles`, `/api/shows`, `/api/shows/{imdbId}` and a title's
  neighbours read that list. Building it asks the filesystem about every title, and on a host short
  of memory while a transcode runs, the kernel forgets those answers within seconds and the cold
  root is a spinning disk: built per request, a listing costs a second. A change on disk reaches the
  shelf within ten seconds. What must be exact reads `TitleLibrary` directly: a title's own
  manifest, a room's head, and whether a film is here before anything is fetched.
- **A title's category is `Library/TitleCategories`, beside its kind.** The kind is how a title is
  built and the category where people look for it: anime is what the film database files under
  Animation and was made in Japanese, read from the kept description, so a listing still never waits
  on the network, and from the genres and language a catalogue answer carries. Every view naming a
  title carries it (`docs/api-contract.md`, Categories).
- **An episode is a title, and a show is its episodes grouped.** Every route that serves a title
  serves an episode unchanged; `/api/shows` and `/api/shows/{imdbId}` group the library's episodes by
  the show their manifest names and join them with the show's description, and an episode's
  `details` on `/api/titles` are its show's.
- **A person's own settings are asked for by token, never by id.** `/api/me/preferences`,
  `/api/me/tracks/{titleId}` and `/api/me/sorts` answer for whoever the token names, so there is no route by which anybody reads or
  writes anybody else's; the service key names nobody and is refused there. Progress, history, follows and wants
  (`/api/me/progress`, `/api/me/history`, `/api/me/follows`, `/api/me/wants`) are asked for the same
  way.
- **How a person watches is one record the API keeps, checks and pushes** (`People/Preferences`,
  `People/FilmTracks`, `People/ViewerPush`). Subtitle style and quality are the account's
  preferences; the subtitle and audio track are kept per person and film in `viewer_tracks`, so every
  device the person watches a film on, and a phone driving a television, read the same one. The API
  refuses what the film or the ranges do not have rather than bending it: a track the title's manifest
  does not list (a subtitle that can be shown, an audio track), a quality that is neither Auto nor a
  height, a style outside the ranges `SubtitleStyle` keeps. A quality is the person's default for every
  film, so it is held to what a picture can be, never to one film's rungs. A change is pushed to
  `PersonGroups.Everything(person)` (`PreferencesChanged`, `TracksChanged`): the person's every connection
  with an account, a playing player's and a phone's remote among them, and never the room's groups, so no
  room state, roster or `StateChanged` moves for it. Each write is stamped with a `revision` inside the
  transaction that keeps it (`People/Revisions`), one stream per person for preferences and one per person
  and film for tracks, so a device can drop a push older than what it holds. A guest has no account and keeps their choices in
  their own browser.
- **A followed show is watched by `Shows/FollowWatcher`, and what it announces is kept.** A follow
  starts from a baseline, the latest episode already due, so only later episodes are news. An episode
  is due from the day after its air date, an air date being a date read against UTC, and is looked for
  until `Follow:GiveUpDays` past it; only a show with an episode due is asked of the tracker, once per
  `Follow:AskMinutes` per episode. An offered episode is fetched once for every follower who wants it,
  at the best quality any of them watches at, and what was announced is kept beside the follows so a
  restart never tells anybody twice. A followed show's description is asked of
  the film database again after `Follow:RefreshHours`, which is what brings a new episode into view.
  The rules for who is told what are in `Notifications/CLAUDE.md`.
- **A want is keyed by the person and the title's IMDb id, never by a library id** (`Wants/`). The
  library is what comes and goes; a film wanted outlives leaving it and is the same want when it comes
  back, and a title that was never here is wanted from its catalogue page the same way. Whether it is
  here, whether the asking person watched it, where a show's person is and how many others want it are
  joined in when the want is read, from the library as listed, the watch record and the other wants,
  and never stored. Wanting takes `cinema:watch`; what the server may spend on a want is captured from
  the token each time it is written (`may_fetch` from `cinema:download`, `may_wish` from `cinema:wish`),
  as a follow's auto-download is. A want is named to others only while it is visible, and a just-me one
  is counted by nobody else. It ends when its person removes it or finishes the title:
  `WatchRecorder` sees the finishing, for a film and for a show's last aired episode. The wish for a
  film nothing has yet is kept while some want on it has `may_wish`, and goes with the last, in the
  transaction that ends that want (`Wants/WantStore`).
- **What a person watched comes from the room, the one source of where a film is** (`People/`). A
  room's state is a position at an instant and everything between two changes follows from it, so
  nothing polls the rooms. `SessionStore` raises `Watched` when a stretch of playing ends — a pause,
  a seek, a film changed, somebody leaving, the server stopping, a player coming back alone carrying
  the room on from its own playhead — and `WatchRecorder` credits each person who was watching with
  where it got to. What a room is playing right now is newer than any
  record and is read from the room (`SessionStore.Viewing`), so progress and "last played" never
  lag the room. A paused room records nothing: the library's clock reads each film's last play to
  decide when an unwatched film leaves, and a room left paused overnight must not look like a play. A person forgetting a film removes
  their own entry and progress and leaves the film's last play. `PersonDirectory` is what turns an
  account into a name, learned from every token validated; a record never shows an id where a name
  belongs. When somebody was last seen is held in memory alone.
- **The API publishes what a page needs to sign in on `/api/config`**, including its public address:
  the Activity's origin is Discord's proxy, reachable only from inside the Activity, and the poster a
  viewer's presence shows is fetched by Discord's servers, so the page builds the poster URL from that
  address. The Activity signs in through `Discord/ActivitySignIn.cs` (`docs/api-contract.md`).

## What a film gains while people are watching it is pushed to them (`Library/TitleChanges.cs`)

The preview sheet is written after the main pass, and so are the subtitles of a source that was still
arriving, so the manifest that marks a film ready is the first to name them. The API watches the
manifests of the titles occupied rooms hold and sends the fresh copy down the hub as `TitleChanged`,
and the player adopts what changed: the head, the sheet, the subtitle list. There is no poll in the
player; a page that could not be heard from asks once on the way back. It goes over the hub rather
than a second channel because every viewer already holds an authenticated connection, and a subtitle
fetched from outside is announced by its route, since the file lives beside the manifest and the
manifest's timestamp says nothing happened.
