# The player

The site's player, at `/r/<room>` on the site and at `/embed/` as the Discord Activity: the same React
page in both, drawn to the Krystal Cinema design. `engine/` is framework-free TypeScript (the hub, the
sync controller, the stream, the room) and the components draw what `engine/room.ts` says.
`npm run check` type-checks the engine; `npm run verify` drives it in a real browser
(`web/site/README.md`).

## The seam between front doors

Everything that differs between the site and the Activity is behind `Environment`
(`engine/environment.ts`): where the API is, how a manifest-relative media path becomes a URL, which
room this is, which film the launch named and where in it to start, who the viewer is, the link that hands the room to someone
else, and where their presence is shown. Nothing else reads `location` or builds a URL. The site's
room is the address and its viewer whoever signed in; the Activity's room is the voice channel and its
viewer whoever Discord says, so `engine/discordEnvironment.ts` is the other implementation and nothing
more.

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
  decides when this viewer is reported to the room as waiting, which is the mark on their avatar.

- **A room waiting on a Watch now shows what it waits on, from the room.** The room's state carries
  `preparing`, so every viewer sees the same three steps (finding, downloading, starting) and the film
  starts on its own when the server loads it; a film that will not arrive says why. Leaving does not
  stop the download.

## The controls

- **Every control that moves the film moves the whole room.** There is one playhead and no host, so a
  space bar pauses the film for everybody. That is the premise, which is why nothing is bound that a
  hand resting on a keyboard could trigger, and why a drag on the scrub bar says where letting go takes
  everybody before it is let go.
- **Every menu holds this viewer's own choice, except Chapters.** Volume, subtitles, audio and
  quality never reach the hub. Chapters moves the room like the scrub bar; a chapter's start is also
  a mark on the bar, and pointing names the chapter. No menu explains itself: its header is its name.
- **Moving between episodes is a button, never automatic.** An episode has previous and next
  controls the whole time it plays, from `/api/titles/{id}/neighbours` (the episodes either side among
  those the library holds), and from its closing stretch — the last chapter when that is the credits,
  else the last two minutes — next is offered over the film. Either one puts the other episode on for
  the whole room, as picking a film does. When the episode right after is not here but the tracker
  has it (`fetchable`), next fetches it into the room at the quality of the one playing, and the room
  shows it arriving over the film; the server has usually fetched it ahead already. The Activity
  spends nothing, so there next is the next episode here.
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
- **What somebody else did to the room is said over the film, to everyone but them**, with their mark
  beside it. A pause looks like a stall and a seek inside the scene looks like nothing. Only a state
  that advanced the room's revision counts: a resync hands back a state already seen.
- **The frame decides the layout, not the device.** A wide frame has labelled controls and popovers; a
  compact one (an Activity at its default size, a phone) has icons and menus as sheets from the bottom
  edge; a tiny one (a popped-out Activity, small in both directions) is one row with an overflow menu.
  Width alone does not make a frame tiny: a phone held upright is as narrow as a popped-out view, and
  is compact, keeping its way back and its invite. A mouse click on the film
  plays or pauses it; a touch shows or hides the controls, because a thumb reaching for the screen is
  reaching for them.
- **Fullscreen exists only where the browser grants it.** Inside Discord's iframe it is not given to an
  Activity, and the player already fills the frame there.
- **Quality is offered only where there is a choice.** A film with one rung has nothing to choose
  between, and where the browser plays HLS itself it chooses for itself and says nothing about it.

## What it fetches

- **Nothing outside this server.** The artwork behind the frame is the poster saved beside the title,
  else the copy the API keeps for the film or the show (`/films/<imdb>/poster.jpg`,
  `/shows/<imdb>/poster.jpg`). The manifest's `posterUrl` records where the ingest took its poster
  from and is never loaded.

- **Everything except the poster needs the token or the film's ticket, including anything CSS
  fetches.** A background image is fetched by the browser and carries no header of ours, so the preview
  sheet is fetched in code and drawn from a blob.
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
  because a track id means nothing outside the film it came from. Volume stays in the browser: it is
  about the speakers in front of the person.

## Each viewer's own presence

The Activity is the only surface Discord gives real rich presence to: the film's name, a poster, and a
bar that runs from where the room is to the end of the film. The bar is two instants and Discord draws
the rest, so nothing is sent on a timer; a paused film says so and carries no clock. It needs the
`rpc.activities.write` scope, the second of exactly two the Activity asks for. The poster is fetched by
Discord's servers, from the public address the API publishes on `/api/config`, which is also where the
Activity's room link points.
