# Rooms

## Every change to a room goes through one place

A player sends intent down the hub; the bot and a spoken command post it over HTTP. Recording who did
it and pushing the new state to the room are the same in both cases and are written once, in
`RoomControls` — a second spelling would not disagree loudly, it would just let a change reach one
door and miss the other.

- **A position omitted means "wherever the room is", and is resolved where the change is applied.** A
  player knows where its own playhead sits; a caller that is not watching does not. A missing position
  read as zero pauses the film *and* sends the room back to the opening titles, which looks like it
  worked because the film stops. For the same reason "back fifteen" is its own act rather than a read
  followed by a seek: in a playing room the round trip between the two comes out of the fifteen.
- **Clamp seeks on the server, never in the client.** A client's head is always slightly stale, so
  client-side clamping yields a seek half the room accepts and half rejects.
- **`SeekClamped` reaches the caller alone.** Broadcasting it would show the whole room an error
  nobody else triggered. Down the hub that is a message to the caller; over HTTP it rides in the
  response body, because the caller that asked is the one thing a broadcast cannot reach.

## Keeping a room

A room is the only thing in the system that exists nowhere else. The films are on disk and the
subtitles are on disk; what a room is watching and where it has got to would otherwise live in memory
alone, and restarting the API would take the film out from under everybody in it.

- **A room is written down, and put back.** `SessionJournal` holds every room in the state directory
  systemd hands over. It is read before the server begins listening — a room restored after the first
  client has joined is a room that client was already told did not exist.
- **A restored room keeps its epoch and its revision.** It is the same run of the same room rather
  than a new one wearing its name, so a client connected across the restart goes on applying pushes
  instead of discarding every one of them, and its reconnect resync hands back what it already had.
  Nothing about the restart reaches a person watching.
- **Position survives for free**, because it is a place at an instant rather than a number that
  ticks. A room that was playing when the server stopped is playing, at the right place, when it
  starts again — however long that took.
- **Nobody is restored into a room.** Membership is a live connection and every one of them died with
  the server. Restoring the names would leave a room reporting people who are connected to nothing,
  which the reaper would then never forget.
- **The journal does not resurrect what the reaper would have forgotten.** A room idle past the
  window is dropped on the way back in, because a link into a room is supposed to stop working.
- **Writing is on a two-second delay.** A drag along the scrub bar is a burst of changes and each
  would be a whole file. A planned stop writes on the way out and loses nothing.
- **`/health` says how many rooms are occupied and by how many people, and no names.** It is what
  makes restarting something that can be looked at first rather than found out about afterwards.
- **The room listing carries what each room is watching and how many are in it, and no names**: who
  is in a room is the room's business. The bot's presence surfaces read it on every pass.
