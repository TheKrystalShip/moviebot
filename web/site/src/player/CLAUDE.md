# The player

The site's player, at `/r/<room>` on the site and at `/embed/` as the Discord Activity: the same React
page in both, drawn to the Krystal Cinema design. `engine/` is framework-free TypeScript (the hub, the
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
`recordProgress`, below). Nothing else reads `location` or builds a URL. The site's room is the address and its viewer whoever signed in; the
Activity's room is the voice channel and its viewer whoever the token the API exchanged for its Discord
sign-in names, so `engine/discordEnvironment.ts` is the other implementation and nothing more. The
viewer's id and name are the token's `sub` and `name` on both, and the hub takes them from the
connection's token rather than from anything the page sends.

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
  something else once, on arrival; once the room holds anything the address is the room alone
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
- **Only an open room has a link.** A private room's invite panel has the switch that opens it and
  the people to invite, and no link anywhere in the player: a link to a private room would lead
  whoever is given it to a door. Inviting somebody lets them in. On a phone the open room's link goes
  to the phone's share sheet.
- **Somebody asking is shown to everybody in the room**, below the announcements, and anybody in it
  may let them in or say no, as anybody may move the film.

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
  from `/api/shows/{imdbId}`, read again each time it opens): still, name, score and length, the one
  the room's state holds marked and the list opened on it. Choosing a row is all it takes: it asks the
  room for that episode (`PlayEpisode`), and one the library lacks can be chosen by a viewer holding
  `cinema:download`. A row says nothing about where an episode stands except its download to this
  device, where the site offers Download.
- **Moving between episodes is a button, never automatic, and the server decides where it goes.** An
  episode has previous and next controls the whole time it plays, named from
  `/api/sessions/{room}/around`, asked again whenever the room holds something else, and from its
  closing stretch — the last chapter when that is the credits, else the last two minutes — next is
  offered over the film. Each sends intent (`Next`, `Previous`) and the server puts the episode on, or
  fetches it into the room at the quality of the one playing for somebody who may download; a
  refusal is said over the film.
- **Skipping an intro or a recap is a button over the film, and it moves the room.** The manifest's
  `markers` name the stretches, and while the playhead is inside one the button asks the room to seek
  to its end, as the scrub bar does, so everyone skips together. It is offered only when playback
  entered the stretch by playing through: a seek that lands more than a couple of seconds past its
  start says the person already chose where to be. A stretch that was skipped is not offered again.
  With the viewer's `skipIntros` preference on and nobody else in the room, entering a stretch skips
  it without a press; skipping on its own for somebody who is watching with others would move their
  film too. No key is bound to it, for the reason above.
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
  is compact, keeping its way back and its invite. A mouse click on the film plays or pauses it.
- **A finger on the film is read as gestures** (`Gestures.jsx`), in three columns. A tap in the
  middle plays or pauses the room; a tap at either side shows or hides the controls, and waits a
  moment first to learn whether it is the first of two. Two taps at a side skip back or forward by
  the same step as the buttons, and each further tap while the skip is gathering adds a step; the
  skip goes to the room once, when the taps stop, because a second seek asked before the first is
  answered is measured from a playhead that has not moved. A sweep sideways anywhere is the scrub
  bar under the thumb: the width of the frame is the length of the film, the bar previews where
  letting go lands, and nothing moves until it does. A sweep up or down at the right is this
  viewer's volume, and at the left how bright the picture is drawn. Before the film is in the
  element a touch only shows or hides what is over the frame.
- **A remote on a television** (`engine/media/remote.ts`, `Environment.tv`). The first press of OK,
  Up or Down while the controls are away only brings them back, since waking the screen should not
  move the room; the play button holds focus once the film is ready, so OK plays from the first
  press after. Left and Right skip by the buttons' step while the controls are away or the scrub bar
  holds focus, and a skip gives the bar focus, so presses however slow go on skipping; with the
  controls up anywhere else the arrows move focus among them. Presses are gathered into one skip as
  taps are, for the same reason. Holding Left or Right scrubs: the bar previews where letting go
  lands, faster the longer the key is held, and one seek is sent when it comes up, as a drag sends one.
  A held key is a keydown for a key that has not come up, since a Fire TV marks no repeat as one.
  Rewind and Fast-forward skip wherever focus is, and the media keys act directly. Back closes a
  menu, then puts the controls away, then leaves the room; Menu opens the subtitles. A television
  has no volume, full screen, invite or back button in the player, and none of a phone's landscape
  or gestures. There a menu is a panel down the right edge, a focused control is a light pill, and
  subtitles nobody has sized are drawn large enough to read from across a room.
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

## What it fetches

- **Nothing outside this server.** The artwork behind the frame is the poster saved beside the title,
  else the copy the API keeps for the film or the show (`/films/<imdb>/poster.jpg`,
  `/shows/<imdb>/poster.jpg`). The manifest's `posterUrl` records where the ingest took its poster
  from and is never loaded.

- **Everything except the poster needs the token or the film's ticket, including anything CSS
  fetches.** A background image is fetched by the browser and carries no header of ours, so the preview
  sheet is fetched in code and drawn from a blob.
- **Every request hls.js makes says how playback is going** (CMCD, CTA-5004): the buffer left, the
  throughput measured, the rung playing, a stall, and a session id random per playback. It travels in
  `CMCD-*` request headers, which the ingress writes to its delivery log (`deploy/CLAUDE.md`), never in
  the query string, where it would split a segment's cache entry per viewer. It names no person.
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
- **A person's defaults are the API's, and a choice made for one film is the browser's.** Subtitle
  language, audio language, quality, how subtitles look and whether stretches are skipped
  automatically come from `/api/me/preferences`, so they follow the person
  between the site and the Activity; the track picked for a film is held in `localStorage` per title,
  because a track id means nothing outside the film it came from. Volume and brightness stay in the
  browser: they are about the speakers and the screen in front of the person.

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
  every ten seconds while it plays, and on a pause, a seek, the end and leaving. The log is one entry
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
