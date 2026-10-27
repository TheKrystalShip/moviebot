# The player

The site's player, at `/r/<room>` on the site and in the Discord Activity, which is the site: the same
React page in both, drawn to the Cinema design. `engine/` is framework-free TypeScript (the hub, the
sync controller, the stream, the room) and the components draw what `engine/room.ts` says.
`npm run check` type-checks the engine; `npm run verify` drives it in a real browser
(`web/site/README.md`).

## The seam between front doors

Everything that differs between the site and the Activity is behind `Environment`
(`engine/environment.ts`): where the API is, how a manifest-relative media path becomes a URL, which
room this is, which film the launch named and where in it to start, forgetting the launch once the
room holds something (`forgetLaunch`), whether it is a television, who the viewer is, a good access token
(`freshToken`) and which Cinema actions it holds (`holds`), the link that hands the room to someone
else, where their presence is shown, how the film is held sideways on a phone, and what the device
keeps for the viewer (`solo`, `deviceCopy`, `keptPreferences`, `online`, `resumeAt`,
`recordProgress`, `sendProgress`, `aheadStore`, below). Nothing else reads `location` or builds a URL. The site's room is the address and its viewer whoever signed in; the
Activity's room is the channel it runs in and its viewer the site's session, which there is the token
the API exchanged for its Discord sign-in, so `engine/discordEnvironment.ts` is the site's environment
with the room, the presence and the phone's orientation of its own, and nothing more. The
viewer's id and name are the token's `sub` and `name` on both, and the hub takes them from the
connection's token rather than from anything the page sends. A room's guest link is the third
(`engine/guestEnvironment.ts`, `Environment.guest`): the room is the one the link opens, the viewer is
nobody, the link stands where the token would, and the room gives the guest their id when they join.

## Starting playback

- **A rejected play is two different things and they are not treated alike.** The browser refusing one
  for want of a gesture is a person's problem to solve, and the join gate asks them for the press; a
  play abandoned because this client seeked underneath it is this client's. Asking somebody to press a
  button for the second leaves them pressing it repeatedly, each press starting the same race again.
- **Nothing is corrected while the film cannot play forward.** A playhead waiting for data drifts from
  the room by definition. Seeking it to catch up throws away the buffer it was waiting for, so the
  correction produces the stall it was correcting. Waiting is the correction.
- **A play is never issued in the same breath as a seek, and a seek never while one is running.** One
  interrupts the other, the element returns to paused, and the pause it reports is indistinguishable
  from a person stopping the film; during the opening buffer the second seek restarts the load.
- **A pause raised while a seek is in flight is machinery, not a decision.** Publishing it stops the
  room every time one person's playhead moves.
- **Only a player holding the film speaks for anybody.** Play and pause publish intent only once the
  element holds the film. One that never loaded, or that a recovery has just emptied, sits at zero, and
  a relative seek from it is measured from the room's position instead.
- **No control offers a press before anything can answer one.** Waiting is shown as waiting; the only
  button a person is asked for is the gate's.
- **A message that answers a question waits until the question is answered.** Whether the room holds a
  film takes a round trip, and "nothing playing" said while it is in the air tells everybody arriving
  to a film that there isn't one.
- **The spinner waits before it appears.** A stall shorter than a moment is a stutter. The same grace
  decides when this viewer is reported to the room as waiting, with where the playhead stopped, which
  is the mark on their avatar.
- **A room every player is waiting in is held where they stopped** (`waiting` in the room's state).
  The server holds it, its clock still, at the furthest-behind playhead reported, and lets it go when
  one plays again, so a stall ends where it began, on what this player has already fetched. A player handed a held room while it is itself
  playing says so again, since the room stays held for as long as that goes unsaid. One viewer
  waiting among others who are playing holds nobody.

- **The room holds one thing, and the player draws what the room's state says it is.** That is a
  title, or something arriving (`preparing`, an episode by its season and number), never both: the
  moment somebody asks for something not here, the room holds it and every player drops what it was
  showing. The player keeps no rule of its own about films or shows. The launch only fills an empty
  room: a launch that names a film puts it into a room holding nothing, and moves a room holding
  something else once, on arrival, both from where the launch says to start; once the room holds anything the address is the room alone
  (`Environment.forgetLaunch`), so a reload rejoins it wherever it is.
- **What a room holds while it arrives is shown from the room.** Every viewer sees the same three
  steps (finding, downloading, starting) and it starts on its own when the server loads it; one that
  will not arrive says why. Leaving does not stop the download.

## Who may come in

- **A room is private until somebody in it opens it**, and the server decides who is in
  (`src/MovieBot.Api/Sessions/CLAUDE.md`). The room's `entry` says where this viewer stands: in, or at
  the door of a room private to them, which is drawn instead of the film with Ask to join; asking
  waits for an answer, and somebody saying no offers asking again. Once let in, the page enters
  the room exactly as one that walked in does.
- **Only an open room has a link.** The invite panel's room row carries the switch that opens and
  closes the room, and the people to invite sit below it; a private room has no link anywhere in the
  player, since a link to a private room would lead whoever is given it to a door. Inviting somebody
  lets them in. On a phone the open room's link goes to the phone's share sheet.
- **Somebody asking is shown to everybody in the room**, below the announcements, and anybody in it
  may let them in or say no, as anybody may move the film.
- **A room's guest link is in the invite panel, private room or open**, for somebody holding
  `cinema:guests`: asked for, it is shown, and copied or shared where the browser still allows it
  after the round trip; the row says how many guests are in and until when the link lasts, and Stop
  guest access takes it away. Somebody in the room who may not hand it out is not shown the link. A
  guest's mark is hollow.

## A guest

A guest on the room's link watches it (`view.viewOnly`). The API holds them to that whatever the page
does (`src/MovieBot.Api/Sessions/CLAUDE.md`, Guests); the page draws nothing that would be refused,
and does nothing that would be.

- **Nothing that moves the room is drawn**: no play, skip or episode buttons, no next-up or skip-intro
  offer, no episodes menu, no invite, no way back. The scrub bar shows where the film is and previews
  what it points at, and moves nothing; Chapters lists the chapters, the playing one marked, and picks
  none. Volume, subtitles and their style, audio, quality, full screen and brightness are the guest's
  own, as they are anybody's, and kept by this browser. A room somebody paused says Paused over the
  film, since a guest has no play button to say it.
- **Nothing that moves the room is answered.** A click or a tap on the film shows or hides the
  controls, sideways sweeps and double taps do nothing, and the room's own acts (`togglePlay`, `seek`,
  the episodes, the door) are no-ops in `engine/room.ts`. The player sends the room nothing but
  `JoinAsGuest`, its quality, what went wrong with its film and the clock probe: not even that it is
  waiting, which would hold nobody.
- **The element is put back, never published** (`SyncController`'s `viewOnly`). A play or pause the
  page did not apply itself (the browser's own controls, a headset's button, the system's media keys)
  is answered by applying the room's state again, at most once a second so a hand on those controls
  is never fought press for press.
- **Being sent away ends the player.** The server says why (`GuestAccessEnded`) before it closes the
  connection, and the hub stops itself so it does not reconnect into a room that refused it. The
  link ended is final; nobody in the room, or the room full, offers to try again.

## The controls

- **Every control that moves the film moves the whole room.** There is one playhead and no host, so a
  space bar pauses the film for everybody. That is the premise, which is why nothing is bound that a
  hand resting on a keyboard could trigger, and why a drag on the scrub bar says where letting go takes
  everybody before it is let go.
- **Every menu holds this viewer's own choice, except Chapters and Episodes.** Volume, subtitles,
  audio and quality are never shared with the room. Volume, subtitles and audio never leave the
  browser; a viewer's quality is reported up to the server alone (below). Chapters moves the room
  like the scrub bar; a chapter's start is also a mark on the bar, and pointing names the chapter.
  Episodes asks the room for an episode, as next does. No menu explains itself: its header is its name.
- **An episode's menu is its season, drawn as the show's page draws it** (`components/EpisodeRow.jsx`,
  from `/api/me/shows/{imdbId}`, read again each time it opens): still, name and length, the one
  the room's state holds marked and the list opened on it. Choosing a row is all it takes: it asks the
  room for that episode (`PlayEpisode`), and one the library lacks can be chosen by a viewer holding
  `cinema:download`. A row says nothing about where an episode stands except its download to this
  device, where the site offers Download.
- **Moving between episodes is a button, never automatic, and the server decides where it goes.** An
  episode has previous and next controls the whole time it plays, named from
  `/api/sessions/{room}/around`, asked again whenever the room holds something else and every half
  minute while the next is not here, and from its closing stretch — its credits where the manifest
  marks them, else the last chapter when that is the credits, else the last two minutes — next is
  offered over the film, unless a skip is offered there; a next the library cannot play yet is
  offered as not ready yet, and pressing it waits on it in the room. Each sends intent (`Next`, `Previous`) and the server puts the episode on, or fetches it
  into the room at the quality of the one playing for somebody who may download; a refusal is said
  over the film. **One episode is asked for at a time** (`view.stepping`): from a
  press of Next, Previous or an episode in the menu, the buttons offer no second press until the
  room's state moves on or the step is refused, since each press of one fetches what is not here.
- **Skipping an intro, a recap or the credits is a button over the film, and it moves the room.**
  The manifest's `markers` name the stretches, and while the playhead is inside one the button asks
  the room to seek to its end, as the scrub bar does, so everyone skips together. It is offered only
  when playback entered the stretch by playing through: a seek that lands more than a couple of
  seconds past its start says the person already chose where to be. A stretch that was skipped is not
  offered again. Credits are offered only when something follows them, a preview or a scene; credits
  that run to the end of the episode skip to nothing, and there the offer is the next episode. With
  the viewer's `skipIntros` preference on and nobody else in the room, entering an intro or a recap
  skips it without a press; skipping on its own for somebody who is watching with others would move
  their film too. The credits are never skipped without a press, since what follows them is the
  episode's own. No key is bound to it, for the reason above.
- **What is playing is told to the system** (`mediaSession.js`, the Media Session API): the lock
  screen, the notification, a headset's buttons, the keyboard's media keys and the browser's own
  media controls show an episode by its place and name under its show and a film by its name and
  year, with the artwork behind the frame and where the film is, told again only on a pause, a seek
  or another title, since the system runs the clock on itself. Their play, pause, skips and seek move
  the room as the player's own controls do, the skips by the buttons' step unless the system names
  one, and a seek dragged along the notification's bar goes to the room once, when it settles.
  Previous and next track are the episodes either side, offered only while the room names one and is
  not already moving. A guest is told what is playing and given no controls, and a television's keys
  are its remote's.
- **A control that already answers a key keeps it.** The scrub bar and the volume slider handle the
  arrows when focused; taking them globally as well moves the film, or the volume, twice for one press.
- **What anybody did to the room is said over the film, to everyone in it, them included**, with
  their mark beside it: "Ada paused" for the others and "You paused" for Ada, on every frame size
  and every front door. A pause looks like a stall and a seek inside the scene looks like nothing,
  and the person who did it sees the same confirmation the room does. Only a state that advanced
  the room's revision counts: a resync hands back a state already seen.
- **The frame decides the layout, not the device.** A wide frame has labelled controls and popovers; a
  compact one (an Activity at its default size, a phone) has icons and menus as sheets from the bottom
  edge; a tiny one (a popped-out Activity, small in both directions) is one row with an overflow menu.
  Width alone does not make a frame tiny: a phone held upright is as narrow as a popped-out view, and
  is compact, keeping its way back and its invite. The one thing the device decides is a touch screen's
  own controls (below). A mouse click on the film plays or pauses it.
- **A finger on the film is read the way the big streaming apps read it** (`Gestures.jsx`). A tap
  anywhere shows or hides the controls, at a side after a moment, to learn whether it is the first of
  two. Two taps at a side skip back or forward by the same step as the buttons, and each further tap
  while the skip is gathering adds a step; the skip goes to the room once, when the taps stop, because a
  second seek asked before the first is answered is measured from a playhead that has not moved. The
  film is moved from the scrub bar and by those taps, never by a sweep across the picture, which a
  hand resting on the screen makes without meaning to. A sweep up or down at the right is this
  viewer's volume, and at the left how bright the picture is drawn. The click a browser makes of a
  touch on the bare film is cancelled, since it would land on whatever the touch brought up under the
  finger. Before the film is in the element a touch only shows or hides what is over the frame.
  The phone's remote reads gestures on its still too, where a tap in the middle plays or pauses and a
  sweep sideways scrubs, there being no controls over it to reach; it passes `levels: false`, which
  makes the vertical sweeps nothing, and shares `stretches.js` (the closing stretch, the skip offer)
  with the player.
- **A phone's controls** (a touch screen, `is-touch`: a television's WebView answers a pointer query as
  a phone does, so the frame is marked rather than queried). Play and pause is a large button in the
  middle of the film with the skips either side of it, and pressing it holds the controls up a moment.
  The row under the bar is Lock, then Episodes, Audio & Subtitles (the audio tracks over the
  subtitles), Settings (Chapters and Quality, each opening its own menu) and Next episode; there is no
  volume button, the phone's own keys being the volume. The bar ends with the time left and thickens
  under the thumb, every control is a thumb wide, and Invite is an icon. Lock puts every control away
  and reads no touch on the film: a tap shows only Unlock.
- **A remote on a television** (`engine/media/remote.ts`, `Environment.tv`), answered the way the big
  streaming apps answer one. While the controls are away, OK plays or pauses and leaves focus on the
  play button, Up and Down only bring the controls back, and Left and Right skip by the buttons' step
  and bring the controls back with the scrub bar seeking, so presses however slow go on skipping. With
  the controls up the arrows move focus among them, and Down from the scrub bar goes back to the control
  focus came up from, or to play. Focus on the scrub bar moves nothing: OK there starts seeking, Left and
  Right then skip, and OK, Back, Up or Down stop it, sending a skip still being gathered at once. A
  focused bar lights its knob alone, and a seeking one grows it; a skip being gathered previews where
  it lands on the bar, frame and time. The play button holds focus once the film is ready, so OK plays
  from the first press. Presses are gathered into one skip as taps are, for the same reason. Every press
  among the controls holds them up afresh, and they wait longer on a television than anywhere else
  before going, since a remote reaches a control one press at a time.
  Holding Left or Right scrubs: the bar previews where letting go
  lands, faster the longer the key is held, and one seek is sent when it comes up, as a drag sends one.
  A held key is a keydown for a key that has not come up, since a Fire TV marks no repeat as one.
  Rewind and Fast-forward skip wherever focus is, and the media keys act directly. Back closes a
  menu, then stops the scrub bar seeking, then puts the controls away, then leaves the room; Menu opens the subtitles. A television
  has no volume, full screen, invite, about or back button in the player, and none of a phone's
  landscape or gestures. There a menu is a panel down the right edge, a focused control is a light pill, and
  subtitles nobody has sized are drawn large enough to read from across a room.
- **What is playing is told beside the film, and the telling is this viewer's alone** (`About.jsx`).
  The (i) at the top bar's end, or `i`, opens what the title's page says without what the page does:
  its heading, score, story, credits, cast and facts, and no action. On a wide frame it is a pane down
  the right edge and the bars end where it begins, drawing as a compact frame's do while that leaves
  them less than a compact frame's width; on a compact frame it is a sheet from the bottom. The film
  plays on beside it, and no name in it leads anywhere, since following one would leave the room. An
  episode leads with itself (its still, name and story) and its show follows. A tiny frame and a
  television have no (i). The words are the title's catalogue page (`/api/catalogue/titles/{imdbId}`,
  and for an episode `/api/me/shows/{imdbId}` beside it, read once a title), else what the manifest
  carries: a guest, whose link opens the room's film and nothing else, and a film played alone out of
  reach of the server are told the name, year and stars, and what a saved copy keeps of the title's
  card.
- **A room left paused says what is playing.** Four seconds into a pause, the left of the frame holds
  the name, an episode's place and name, the facts, the story and who is in it, until the film plays
  again or anything opens over it; on a frame as short as a phone held sideways the story is two
  lines and the stars are left out. It answers no press, so a tap or a click goes through to the
  film. For a guest it is the room's Paused, said with more.
- **Brightness is how dark the picture is drawn, not the screen's.** A page has no hold on the
  screen's backlight, so the left sweep fades a black layer over the film, from none to most of the
  way. The subtitles sit above it at the brightness they were styled at. Like volume it is the
  browser's to remember, since it is about the screen in front of the person.
- **Volume is offered only where a page can set it.** iOS plays a page's media at the level the
  hardware buttons set and ignores what the page asks, so there the right sweep does nothing.
  Elsewhere it moves the player's own level, which the phone's media volume caps.
- **A phone watches sideways and full screen, and has no button for either.** The front door holds
  the film in landscape (`Environment.landscape`) while the player is open. A browser grants the
  whole screen and its orientation only in answer to a press, so the site takes both on the Join
  press or the next touch on the film, full screen first because Chrome locks an orientation only
  for a page that is; leaving full screen gives both up until the next touch takes them back. A
  browser that grants neither, which is every browser on an iPhone, leaves the turning to the
  person. The Activity asks Discord, which turns its own window and needs no press. The manifest
  names no orientation, so the rest of the installed site follows the phone's rotation lock.
- **Fullscreen is a button only where the pointer is a mouse and the browser grants it.** Inside
  Discord's iframe it is not given to an Activity, and the player already fills the frame there.
- **Quality is offered only where there is a choice.** A film with one rung has nothing to choose
  between, and where the browser plays HLS itself it chooses for itself and says nothing about it.
- **A chosen quality is the best the film may play at, never a rung it is held on.** hls.js chooses
  for the connection beneath it, so a connection that cannot carry the chosen rung steps down rather
  than stopping the film; the menu names the choice and, while playback is beneath it, the rung
  playing. Auto is no ceiling at all. Only a film played from the device is held on one rung.

## How far ahead the film is fetched (`engine/media/bufferAhead.ts`, `stream.ts`)

- **It is the device's choice, not the person's.** It is about the connection in front of it, a phone
  on mobile data wanting what a television on a fixed line does not, so it is kept in this browser
  (`cinema.bufferAhead`) like volume and brightness, and chosen under This device on the profile's
  Playback tab, with or without a connection. Light is about 30 seconds ahead and Standard about 3
  minutes; A lot is Standard in memory and the rest of the episode and the start of the next on the
  device; Dynamic is Standard in memory and as much on the device as the connection calls for
  (below). A lot and Dynamic are offered only where the browser has a private file system, and are
  Standard where it has none. Each says roughly what it costs in prose, not in figures the API serves.
- **A device that chose nothing is on Standard if it is a desktop and on Dynamic otherwise**
  (`defaultBufferAhead`). A desktop, a browser whose pointer is a mouse, sits on a fixed line and
  streams as it plays; keeping the film on its disk as well fetches the whole film at the line's
  speed, from a host that may still be transcoding it. A phone, a tablet or a television keeps the
  film ahead for a connection that comes and goes. A television's WebView answers the pointer query
  as a phone does and carries its wrapper's mark besides.
- **Dynamic grows how far ahead the device keeps the film from what the connection does**
  (`engine/media/aheadHorizon.ts`). It starts at nothing past the player's own minutes in memory and,
  looked at no more than every thirty seconds, grows ten minutes while the network carries three times
  the rate of the rung watched or the browser calls the link Wi-Fi or a wire, at least to ten minutes
  and five more while a stall or a server that could not be reached is under ten minutes old, two
  minutes while the network carries one and a half times the rate, and not at all on a tighter
  connection, where filling would compete with playing. It never shrinks within a title, does not grow
  with the browser's data saver on, and reaches the next episode only once it covers the rest of this
  one. The network's rate is measured from the files fetched over it alone (`Reach.measured`): one read
  from the device arrives at the disk's speed, and hls.js's own estimate counts those.
- **A lot keeps the film on the device as it plays** (`engine/media/ahead.ts`, the store
  `src/offline/ahead.js` through `Environment.aheadStore`). The fetcher takes one file at a time:
  the playlists, then the lowest rung with the chosen audio from the playhead to the end, then the
  first five minutes of the next episode the same way (the room asks `/around` for it once a title
  loads, and its audio is the one it would start on), then the rung watched for each. Every file the
  player fetches from the server is kept as well, so nothing is fetched twice. What the playhead has
  passed by more than two minutes is deleted, the device keeps only the title playing and the next,
  and fetching stops while the browser would let the site keep less than half a gigabyte more. A
  growing playlist is read again every minute, so a title still transcoding is kept as far as it is
  written.
- **The device answers before the server, and the server is asked first only for a playlist**
  (`aheadLoader`): a segment the device holds is read from it, and a playlist is the server's, since
  one still being written grows, and the device's when the server cannot be reached.
- **The device is never waited on** (`DeviceAnswerMs`). A private file system can stop answering
  altogether, and the server holds every byte the device does: a store that cannot say what it holds
  of the title in time leaves the film to stream without it, and a segment it does not hand over in
  time is fetched from the server instead.
- **With no connection, the player is held to what the device has** (`FilmStream.holdToDevice`).
  While a request to the server has just failed, or the browser says it is offline, hls.js is capped
  at the best rung the device holds where the buffer ends, so a stretch kept only at 480p plays at
  480p. hls.js waits on a fragment that failed for want of a connection until the browser is online
  again, whatever the device holds, so when the player is first held, or held to another rung,
  loading starts again from where it was. Back online, the cap goes, and the rung watched returns
  once what was buffered from the device has played.
- **The level is seconds, whatever the rung.** hls.js otherwise sizes its buffer by a byte budget over
  the rung's stated peak, so a heavier rung would be fetched less far ahead; the budget is nothing and
  the seconds are the only measure.
- **What hls.js trims, it is given back.** It shortens how far ahead it fetches whenever it meets a
  fragment still being appended, which a switch between rungs makes, and never lengthens it again: a
  trip of switches would halve it time after time. The player puts the level back once a fragment
  lands, unless the browser said its buffer was full, which is the one limit the device really has.

## Quality while a film is still being written (`engine/media/heads.ts`, `stream.ts`)

- **The player stays on rungs written past the playhead.** While a title transcodes, its manifest
  gives each rung's head. hls.js is capped, for its own choice and for the rung a saved film holds, to the
  rungs whose head is at least `CapMarginSeconds` past the playhead, and the cap rises as a rung
  catches up. A rung the playhead has outrun is left for one that covers it before the buffer runs
  dry: switching to a rung behind the playhead stalls the film until that rung arrives. When no rung
  covers the playhead the one furthest along is used. Nothing is capped for a ready title, a manifest
  that records one head, or a browser that plays HLS itself.
- **A viewer's quality is never shared with the room.** It is not in the room's state and not in any
  broadcast. The player tells the server which rung it plays and which rung it wants, when either
  changes and on a slow heartbeat, over the hub connection it already holds (`ReportQuality`). In Auto
  the wanted rung is the one hls.js's own choice would pick with nothing holding it back (its bandwidth
  estimate against the advertised bitrates); a viewer who chose a quality wants no more than it, so it
  is the lower of that rung and the connection's pick.
  The server keeps it per connection, in memory, for its own planning. A browser that plays HLS itself
  reports nothing and is taken to want the tallest rung. Segment requests cannot say this: a shared
  cache answers a whole room from one fetch.
- **The scrub bar's shaded edge is the best rung's head**, the same limit the server holds seeks to.
- **Loading always starts where the player says.** A transcoding film's playlist has no end yet, so
  hls.js reads it as a live broadcast and, told nothing, loads at its newest segment; a player that
  starts there buffers film on either side of a playhead the room then seeks back to, and waits on it
  for good. Every restart of loading (after a network error, onto the device's rung) names the
  playhead, or before the film has played the position it was asked to start at.
- **A playhead sitting on nothing it can play is started again** (`FilmStream.unstick`). Once it has
  held no buffered film for `StuckMs`, while the server holds that stretch, the browser has a connection
  and no fetch of it is under way, the element is attached afresh, as a media error's recovery does, and
  loading starts from the playhead. hls.js keeps its own account of what it appended, and a flush made
  while the playlists could not be read (a transcode begun again from nothing deletes them) leaves it
  counting film as buffered that the element no longer holds; attaching afresh empties both.

## What it fetches

- **Nothing outside this server.** The artwork behind the frame is the title's poster
  (`/media/<id>/poster.jpg`, the one poster the API shows the title by everywhere), else the copy the
  catalogue keeps for the film or the show (`/images/titles/<imdb>/poster`). The
  manifest's `posterUrl` records where the ingest took its poster from and is never loaded.

- **Everything except the poster needs the token or the film's ticket, including anything CSS
  fetches.** A background image is fetched by the browser and carries no header of ours, so the preview
  sheet is fetched in code and drawn from a blob.
- **A fatal network error is waited out, longer each time** (`NetworkRetryFirstMs` doubling to
  `NetworkRetryMaxMs`, back to the first once a playlist or a fragment lands). A playlist that is
  missing or empty fails the instant it is asked for, and loading again at once asks for it hundreds
  of times a second, every restart aborting the requests in flight, from every viewer at the same
  moment.
- **Every request hls.js makes says how playback is going** (CMCD, CTA-5004): the buffer left, the
  throughput measured, the rung playing, a stall, and the playback's id, random per load and the one
  its trouble reports name (`FilmStream.onTrouble`, below). It travels in
  `CMCD-*` request headers, which the ingress writes to its delivery log (`deploy/CLAUDE.md`), never in
  the query string, where it would split a segment's cache entry per viewer. It names no person.
- **What went wrong with the film is told to the server, for its log and nothing else**
  (`FilmStream.onTrouble`, `ReportTrouble`): a fatal network or media error, the playhead started again
  from nothing it could play, a device store that did not answer in time, a film that never reached the
  element, and the player giving up, each with the playhead, a short detail and the playback's id. A
  player says each kind at most once every 30 seconds, and the server writes at most six lines a minute
  per connection, so a player stuck in a loop costs the log a line, not a flood.
- **Only the chosen subtitle is fetched.** A rip carries dozens; loading them all to show one is a
  megabyte of nothing. It is attached hidden and the page draws its cues itself, sized against the
  frame: a cue the browser draws takes the operating system's caption settings and nothing this page
  can say about it.
- **Each person's subtitles look the way they chose** (`subtitleStyle.js`, `SubtitleStyleEditor.jsx`).
  Size, font, weight, text colour, edge and its colour, a box behind each line and its opacity, and
  how high dialogue sits are the person's `subtitleStyle` preference, so the look follows them into the
  Activity, which has no profile page; the editor is under Style in the subtitle menu and on the
  profile's Playback tab. A stored style holds only what differs from `DEFAULT_STYLE`, so the default
  can be retuned for everybody who never changed it. The default is white letters with a black
  outline and no box, which reads over any picture and hides none of it. Sizes are fractions of the
  frame's height, so a size reads alike on a phone and a television. While the editor is open in the
  player and the film is saying nothing, a sample line stands in, so the choice is seen over the film
  it is for, and the choosing is saved once it pauses.
- **The subtitle menu belongs to one film.** What OpenSubtitles offered is forgotten when the film
  changes and a search that lands after the change is dropped. The film's own tracks are read again
  whenever the menu opens: what a film gains is pushed as it lands, and the re-read is the guarantee
  behind the push. The search asks for the person's own language and English beside it.
- **How a person watches is the API's record, and a playing player follows it.** Subtitle language,
  audio language, quality, how subtitles look and whether stretches are skipped automatically come from
  `/api/me/preferences`; the subtitle and audio track picked for a film come from
  `/api/me/tracks/{titleId}`. Both follow the person between devices, so a phone driving a television
  writes the record the television plays from. The player's menus write the API and read it when a film
  starts, falling back to the person's languages (`startingTracks`) for a film they chose no track for.
  A guest has no account, and a film saved on the device played alone with no connection has no API to
  ask: both keep a track choice in `localStorage` per title (`engine/prefs.ts`), because a track id
  means nothing outside the film it came from. Volume and brightness stay in the browser: they are
  about the speakers and the screen in front of the person.
- **A change to the record is pushed, and the room applies it.** The room's own hub connection hears
  `PreferencesChanged` and `TracksChanged` (the API sends them to every connection of the person's with
  an account, never into the room's groups) and applies them through the setters' own checks: a
  subtitle id that is not a track the film lists as one that can be shown, an audio id it does not
  list, and a quality that is neither Auto nor a positive height are refused, and a refused id is never
  kept as the film's choice nor turns subtitles off. A track another device just fetched may not have
  reached this copy of the film's list, so the subtitle list is read again once before a subtitle id is
  refused. A push for another film is ignored (that film reads the record when it starts), a style
  being chosen on this device is left alone until it is saved, a push older than the last record this
  device applied or was answered with is dropped and one that arrives during its own write is held until
  that write answers (`session/ordered.ts`, on the `revision` the API stamps), and a style that arrives from another
  device shows the sample line over the film for four seconds after the last one. The stream keeps the
  track choices it was given and moves them with every choice after, so one made while the film is
  loading is the one the load ends on.

## Playback stats (`PlaybackStats.jsx`, `engine/media/playbackStats.ts`)

What happens to a film between this browser's decoder and its speakers never reaches the server, so
the player measures it and shows it to the person watching: the counterpart of the big streaming
players' stats overlays.

- **It shows this viewer's own measurements and nothing else**, and every value is measured or says
  `unknown`: a browser that does not offer a measurement is never given a zero in its place.
- **Sound against picture is the first thing it says.** Each presented frame
  (`requestVideoFrameCallback`) gives its media time and when it reaches the screen; the element's
  `currentTime` follows the audio clock, so the frame's time against where the sound will be when it is
  shown is how far the picture is ahead of the sound the decoder makes, shown as a median over a
  second. `AudioContext.outputLatency` is what the default output device adds after that, which is
  where a wireless headset's delay sits, and the two together are how far the sound reaching the
  person trails the picture. The audio context is never connected to the film's sound.
- **The room against this player is a graph.** Each of the sync controller's checks (`onDrift`) is the
  room's position minus the playhead over the last five minutes, drawn with the band where drift is
  left alone and the lines where it is corrected with a seek. Each correction (`onCorrection`), the
  page hidden and shown, the hub reconnecting, a stall, a rung switch and a fatal error is a mark on its
  time axis. Beside it: the element's rate against the room's, the server clock's offset and round
  trip, and the connection.
- **The network and the film**: hls.js's bandwidth estimate and the rate measured over the network
  alone (`Reach`), seconds ahead in the picture's buffer and the sound's separately (`BUFFER_APPENDED`),
  the last fetch's first byte and total time, failed and fatal fetches, the rung playing and wanted,
  the codecs read from the init segments (`BUFFER_CODECS`, since the playlists declare none), the
  audio track, frames dropped, and stalls as the spinner's grace counts them. The playback id, the
  title, the build (`VITE_CINEMA_BUILD`, the CHANGELOG's version and the commit), the front door, the
  buffer-ahead choice and the user agent close it.
- **What accumulates runs from the moment a film loads; what costs something runs only while it is
  open.** The graph and the counters are reset by each load (`StreamEvents.onPlayback`), so opening the
  panel shows what led up to it. The frame callback and the audio context are made when it opens and
  let go when it closes. An audio context made without a press reports nothing until one comes, so a
  press anywhere while it is open wakes it.
- **The person opens it, and this browser keeps it open** (`cinema.playbackStats`), so a reload made
  to try again comes back with it up. Ctrl+Alt+Shift+D toggles it, read by the key's code since Alt and
  Shift change the character on a Mac; Ctrl+Shift+D alone is the browser's own. Settings has a
  Playback stats row everywhere: a phone's Settings among its own rows, the gear at the end of a wide
  frame's row and of a television's, and a tiny frame's overflow. A guest has it as anybody does,
  since it moves nothing.
- **It sits over the top left of the film, under the top bar and above the controls**, scrolling
  inside itself on a short frame. A press on it stays on it; the film around it answers as it always
  does. Close, Copy and Send are its controls.
- **Send writes the snapshot to the API's log** (`ReportStats`, `docs/api-contract.md`):
  the JSON the panel shows, cut to 8192 characters by dropping the oldest of the graph first
  (`fitSnapshot`), at most one every ten seconds a connection, held to both by the page and by the
  server. The button says whether it was sent. Copy puts the whole snapshot on the clipboard. A film
  played alone has no server, so no Send.

## When the room cannot hear the page

A phone on the move loses its connection for seconds or minutes and plays on from what it fetched.

- **The connection never stops trying to come back** (`buildHubConnection`): soon at first, then every
  ten seconds for as long as it takes, and at once when the browser is online again or the page is in
  front of somebody again (`SessionHub.reconnect`).
- **Coming back says what the player did** (`Rejoin`, `SyncController.account`): the last state it
  applied, the film, where its playhead is and whether it plays. Alone in a room nobody changed, the
  room carries on from there, and a room forgotten meanwhile is given the film back
  (`src/MovieBot.Api/Sessions/CLAUDE.md`); with others in it, or changed, the room's state stands and
  the player goes to it. The answer is applied before the page counts itself connected, since a
  player live first would correct toward the last state it heard, which ran on while it was away.
  Whether the player is waiting for the film is said again with the rest.
- **Where the film got to is kept on the device meanwhile** (`recordProgress`), from where it was when
  the room was lost, every ten seconds and on a pause, a seek and the end, and sent once the room can
  hear the page again. A room that can hear the page records it itself.

## Playing from the device

A film saved whole on the device (`src/offline/CLAUDE.md`) plays from it, alone or in a room.
`deviceCopy.js` reads the save for the signed-in account into a `DeviceCopy`
(`engine/media/deviceLoader.ts`): the saved manifest, each track's file with its segment index, and
the subtitles, the preview sheet and the poster by the path each stands for. A save under way, or a
track whose file is not the length the package says, is no copy at all.

- **The device answers before the server.** `engine/media/devicePlaylist.ts` builds each saved
  track's playlist from its index: one VOD playlist of byte ranges of `track.mp4`, the init segment at
  offset 0 and each segment at the init segment's length plus every segment before it. The loader
  (`deviceLoader`) answers those playlists and each range with a slice of the track's `File`, which
  reads that range and nothing else, at the very addresses the server's playlists sit at, and hands
  any other address to the ticketed loader. The master is composed from the manifest, so nothing at
  all is asked of the server for what the device holds. The film is held on the saved rung, which is
  the only one the quality menu offers. Only hls.js can be fed this way: where the browser plays HLS
  itself a film is never played from the device.
- **Alone, the timeline is the film's own** (`engine/session/local.ts`, `Environment.solo`, the
  address's `offline=1`). It answers play, pause and seek as the hub does, with the state each leads
  to, and the sync controller applies it exactly as it applies a room's: the rules above hold
  unchanged, and none of them is written twice. A seek is granted whole, since the film is whole;
  nothing is sent anywhere; no ticket is fetched; the controller is never told the timeline is live,
  so it corrects no drift, and a film waiting on the disk waits rather than skipping. The room's
  viewers, the invite, the announcements, episodes either side and OpenSubtitles are not drawn, the
  join gate's press is Play, and the poster behind the frame is the saved one.
- **Alone, the person is who the device last heard of.** Preferences are the API's while it can be
  reached and `keptPreferences` otherwise, and a stored track choice stands only where the saved
  copy has that track. A film named with no position starts from `resumeAt`: the newer of the
  device's log and the API's record, from the beginning once most of it was watched.
- **Alone, where the film got to is written down** (`recordProgress`, `src/offline/progress.js`):
  every ten seconds while it plays, and on a pause, a seek, the end and leaving. A room's film is
  written down the same way while the room cannot hear the page (below). The log is one entry
  a title, sent with `POST /api/me/progress` whenever the site can reach the server, and an entry
  goes once the API has answered for it.
- **In a room, the hub keeps the timeline and the device the bytes.** Nothing about the other viewers
  changes. The viewer reports the saved rung as the one it plays and the one it wants, since it asks
  the server for none of the picture.
- **A room lost is said over the film**, once the hub has been reconnecting past a moment or at once
  when the site knows the server cannot be reached. When the film is saved here, the same line offers
  to carry on alone from the device, from where this viewer's playhead is, which is where the room
  was when it was last heard: the address becomes the film's own, with `at` that position.

## Each viewer's own presence

The Activity is the only surface Discord gives real rich presence to: the film's name, a poster, and a
bar that runs from where the room is to the end of the film. The bar is two instants and Discord draws
the rest, so nothing is sent on a timer; a paused film says so and carries no clock. It needs the
`rpc.activities.write` scope, the second of exactly two the Activity asks for. The poster is fetched by
Discord's servers, from the public address the API publishes on `/api/config`, which is also where the
Activity's room link points.
