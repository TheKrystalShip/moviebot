# The player (Discord Activity)

`README.md` here is the authority for the player. Builds: `npm run check` (types only),
`npm run build` (→ `dist/`), `npm run verify` (a real browser against a running API and page). It
reaches people only through the API: `scripts/build-player.sh` installs the build into
`src/MovieBot.Api/wwwroot`.

## Starting playback

- **A rejected play is two different things and they are not treated alike.** The browser refusing one
  for want of a gesture is a person's problem to solve; a play abandoned because this client seeked
  underneath it is this client's. Asking somebody to press a button for the second leaves them
  pressing it repeatedly, each press starting the same race again.
- **Nothing is corrected while the film cannot play forward.** A playhead waiting for data drifts from
  the room by definition. Seeking it to catch up throws away the buffer it was waiting for, so the
  correction produces the stall it was correcting, further behind each time. Waiting is the
  correction.
- **A play is never issued in the same breath as a seek.** One interrupts the other, the element
  returns to paused, and the pause it reports is indistinguishable from a person stopping the film.
  Playback the room wants waits for the playhead to arrive instead.
- **A pause raised while a seek is in flight is machinery, not a decision.** Publishing it stops the
  room every time one person's playhead moves.
- **A seek is never issued while one is running.** The second abandons the first, and during the
  opening buffer that restarts the load.
- **The library answers a click on the film, and nothing else does.** It toggles playback on a click,
  and it knows not to when the click was on a control. A second handler behind it toggles the film
  back, which reads as playback refusing to start.
- **Everything except the poster needs the token, including anything CSS fetches.** A background image
  is fetched by the browser and carries no header of ours, so it is fetched in code and drawn from a
  blob. The poster is open because Discord's servers draw embeds with it; a sheet of frames from the
  film is not that.
- **No control offers a press before anything can answer one.** Waiting is shown as waiting. The only
  button a person is asked for is the one that appears when the browser wants a gesture.
- **A message that answers a question waits until the question is answered.** Whether the room holds a
  film takes a round trip, and saying it holds none while that is in the air tells everybody arriving
  to a film that there isn't one.
- **Nothing in the player changes for a film switch.** A state naming a different title is the same
  push the library click produces, and the player follows it: the old source is torn down, the new one
  loaded, and the room's state applied once the film is in the element.

## The controls

- **Every control moves the whole room.** There is one playhead and no host, so a space bar pauses the
  film for everybody rather than for whoever pressed it. That is the premise, not a hazard, but it is
  why nothing is bound that a hand resting on a keyboard could trigger and why the scrub bar shows
  where a seek would land before it is made.
- **A control that already answers a key keeps it.** The scrub bar and the volume slider each handle
  the arrows themselves when focused; taking them globally as well moves the film, or the volume,
  twice for one press.
- **Volume is the one key that moves nobody else, and it is still shown in the middle of the screen.**
  Up and down step this viewer's loudness by five points, as does a wheel turned over the volume
  control, and each step flashes where the volume landed in the same place a seek flashes what it did.
  The slider is on a bar nobody is looking at while the film plays, and one step is not something an
  ear can be sure it heard.
- **What somebody else did to the room is said on screen, to everyone but them.** A pause looks like a
  stall and a seek inside the scene looks like nothing, so the bubble names who did what and, for a
  seek, where the film went. Only a state that advanced the room's revision counts: a resync hands back
  a state already seen, and announcing it would report an act nobody took. One function,
  `describeChange`, words it for the bubble and for the line under the player.
- **Fullscreen exists only where the browser grants it.** Inside Discord's iframe the API is not given
  to an Activity, so the key and the double click do nothing there rather than failing — and the
  player already fills the frame, which is what they would have been for.
- **A preview sheet is bounded by pixels, not by a count of frames.** A browser holds four bytes for
  every pixel of it for as long as the film is open, so that is the real limit rather than the size of
  the file — and it means larger frames buy themselves fewer of them. Both of the sheet's dimensions
  stay inside four thousand pixels, which is what older hardware will hold as one texture.
- **Previews come from one sheet, not one file each.** A preview is wanted the instant a pointer lands
  on the bar, and a request per frame would spend the whole hover fetching. Only keyframes are decoded
  to build it, which is what keeps it bounded by how fast the file reads rather than by the length of
  the film.
- **A chapter title that is only a timestamp is no title.** Muxers write the chapter's own start time
  into its name routinely, and shown beside the time under the pointer that reads as a second clock
  disagreeing with the first.
- **The spinner waits before it appears.** A stall shorter than a moment is a stutter, and flashing at
  one is worse than ignoring it.

## The subtitle menu

What a fetched or confirmed subtitle is: `src/MovieBot.Api/Subtitles/CLAUDE.md`.

- **The subtitle menu belongs to one film.** What the index offered is forgotten when the film
  changes, and a search that lands after the change is dropped, or the menu shows the last film's
  subtitles under the next one. The film's own tracks are read again every time the panel is opened:
  what a film gains is pushed to the room as it lands, and the re-read is the guarantee behind the
  push, so a track that became available is shown available whatever reached the page in between.
- **A confirmed track stops the menu searching the index.** There is nothing to interrupt anyone for
  once somebody has settled it, so the search waits to be asked for.
- **Silence is the ordinary answer in the picker.** Most uploads declare nothing useful. Marking every
  one of them leaves the whole list marked and the marks meaning nothing, so only a strong claim or a
  real problem earns one.
- **A row carries a mark, not a sentence.** Rows already hold release names, and a phrase beside each
  one pushes the list past the height of the menu. The glyphs are typography rather than pictures, so
  they take the row's colour and size, and what they mean rides on the mark itself where a pointer and
  a screen reader both reach it. Colour agrees with the glyph rather than carrying the meaning alone.
- **The menu has to fit without being scrolled.** The candidate list is ranked, so showing more of it
  only adds worse answers underneath the good ones while pushing the good ones off screen.

## Each viewer's own presence

The Activity is the only surface Discord gives real rich presence to: the film's name, a poster, and a
bar that runs from where the room is to the end of the film. The bar is two instants and Discord draws
the rest, so nothing is sent on a timer; a paused film says so and carries no clock. It needs the
`rpc.activities.write` scope, which is the second of exactly two the Activity asks for. The poster is
fetched by Discord's servers, from the public address the API publishes on `/api/config`.
