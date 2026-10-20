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
- **The clamp is the best rung's head, held back to the audio's.** A transcoding title's manifest
  records each rung's head (`TranscodeHeads`), and a viewer is on one rung, so a lagging rung never
  clamps the room. The room's state carries `transcodeHead` (the shortest playlist), `rungHeads` and
  `audioHead`, and the player chooses among the rungs that cover its playhead.
- **A viewer's quality never enters a room's state.** `ViewerQuality` holds what each connection's
  player reports (`ReportQuality`) in memory, keyed by connection, for the transcode planner to read
  as the wanted rung heights per room (`WantedHeights`, `WantedByRoom`). A viewer who has said
  nothing wants the tallest rung of the room's film. It is dropped when the connection leaves, and no
  state, push or broadcast carries it.
- **A room is held only while every player in it is waiting for the film, or nobody is in it**
  (`SessionStore.Hold`). One slow connection never stops the film for the others. A room nobody can
  play would otherwise run on for nobody, and each player, once it has the film again, would be told
  the room is somewhere it has not fetched, seek there, and stall again. A room whose last viewer's
  connection went is the same room: their phone plays on from what it fetched, unheard, and a room
  that ran on would be somewhere they never saw. Each player reports where its playhead stopped
  (`Buffering`), and the room is held at the furthest behind of them, never ahead of where it had got
  to: a player put back seeks into what it already holds. The held state is playing with `Waiting`
  set, so its position does not advance, and names no actor. It is let go, from the same place, when
  one player plays, somebody arrives, or the waiting are left alone by somebody leaving; a change
  somebody makes to a held room leaves it held from wherever that put it. `RoomControls.HoldAsync` is
  the push, and the log says when a room was held and let go.
- **A player coming back alone carries the room on from its own playhead** (`Rejoin`,
  `SessionStore.Resume`). A page whose connection came back says what its player did meanwhile: the
  last state it applied (epoch and revision), the film, where its playhead is and whether it plays.
  The room takes that as its state when nobody else with an account is in it and either it holds that
  film with nothing done to it since that revision (`ActRevision`: holds, a download's progress and
  resumes are the server's own doing, not acts), or it holds nothing because it was forgotten while
  the player was away. Otherwise the room stands and the player takes it as it is, which is what
  keeps one viewer coming back from moving a room others went on watching. The server still decides:
  the player says what it did and the store judges it. The state names no actor, so no player
  announces it, and where the playhead moved, the stretch is credited to the viewer, since the held
  room recorded none of it.
- **`SeekClamped` reaches the caller alone.** Broadcasting it would show the whole room an error
  nobody else triggered. Down the hub that is a message to the caller; over HTTP it rides in the
  response body, because the caller that asked is the one thing a broadcast cannot reach.

## Who may come in

A room is open from the moment it is made, and closing it, and letting somebody into it once closed,
is something a person in it does. The rules live in `RoomAdmission`, one per room under the room's lock; `SessionStore.Enter` is
the one question every door asks, and `RoomDoors` is the one place a change at the door is made and
told.

- **A room is open from the moment it is made**, whichever door its maker came through. Friends find
  each other's rooms in the listing and walk in; a room that wants a door has somebody in it close
  it. Only making a room opens it: arriving at a room somebody closed, through Discord or anywhere
  else, leaves it closed.
- **Whoever makes a room is let in by making it.** `Enter` on a room there is none of makes it with
  that person let in, whichever door they came through: the hub's `Join`, a room route
  (`RoomEntry.RequireRoomEntry`), Watch now. A room made by machine work (`GetOrCreate`) lets nobody
  in.
- **Entering is what lets somebody in for good.** Anybody who comes in while a room is open stays let
  in, so closing it shuts out nobody who has been in and lets in nobody new. Closing a room is
  locking its door, not emptying it; a page reloaded is a person leaving and coming back.
- **Somebody outside is let in by somebody inside**: answering them at the door (`Knock`, `Answer`),
  inviting them, or opening the room, which lets in everybody asking. Anybody in the room may do any
  of it, as anybody in it may move the film: there is no host.
- **Asking belongs to a page.** A knock is kept by connection and ends with it; the room sees each
  person asking once. It is never written down, since a restart has dropped every page anyway.
- **A room private to somebody tells them nothing about itself.** `Join` answers that it is private
  and nothing else, a route is a `403`, the listing leaves it out, and its link unfurls as the site.
  The door goes to the room's group and the answer to the asking page alone, so somebody outside
  never learns who else is asking or who is in.
- **Who was let in is kept with the room.** A restart that opened every closed room would be the
  worst way for one to stop being private, and one that forgot who was let in would shut everybody
  out of their own room.
- **Machine work is let into every room.** The service key names nobody; what it does to a room is
  the room's own business arriving, and the listing it reads is every room, with whether each is open.

## Guests

A room's guest link lets somebody with no account watch it. A guest is nobody: a connection with a
name it gave itself, holding the link, and no record anywhere is keyed by one.

- **The link is a capability, not an account.** `GuestPass` is one per room, kept with the room in
  `RoomAdmission` and kept with it, and the link's code is its id signed with a key derived
  from `Auth:MediaTicketKey` (`GuestCodes`), so nothing secret is written down and everybody in the
  room who asks is handed the same link. Somebody in the room holding `cinema:guests` gives it and
  takes it away; it runs out after `Rooms:GuestPassLifetime`, and goes with the room when the reaper
  forgets it. A guest comes in on it and never through the door, so it is no change to who is let in.
- **A guest can change nothing about the room, and that holds by construction, three times over.**
  A guest's credential is authenticated only by the routes whose policy names the guest scheme
  (`RequireActionOrGuest`, `RequireActionServiceOrGuest`), which are the hub and the film the room
  holds, its manifest and its ticket (`GuestReach`); everywhere else it is no credential at all, and
  the fallback policy admits nobody, so a route added without opening it to guests is closed to them.
  On the hub, `GuestGate` refuses a guest every method not marked `[GuestAllowed]` (`JoinAsGuest`,
  `ReportQuality`, `ServerTime`), so a method added to the hub is closed to them too. And every method
  that moves the room needs the person a token names or the actor `Join` records for one, which a
  guest's connection never has, so each refuses a guest on its own as well. The tests walk every route
  and every hub method rather than a list of them.
- **A guest watches, and nothing they do reaches the room.** They are in the room's group for its
  state, its film and who is in it, and not in the members' group the door goes to
  (`RoomGroups`), since the door names who is asking. They hold nobody (`Hold` counts only people with
  an account), are never a viewer anything is recorded for (`Viewers`), and their quality report is
  planning like anybody's.
- **Guests stay only while somebody with an account is in the room.** The room keeps when the last of
  them left (`MembersGoneSince`, set too when it is made or put back); past `Rooms:GuestGrace` its
  guests are told why (`GuestAccessEnded`) and their connections closed by `GuestKeeper`, which also
  sends away the guests of a link that ran out or is no longer the room's. A grace rather than at once,
  because a reload or a restart is somebody leaving and coming back. A link left running with nobody
  in the room would be the library streamed to whoever holds it. A room takes `Rooms:GuestsPerRoom`
  guests at once.

## Plan rooms

A plan (`Plans/`, `docs/api-contract.md` "Plans") makes its room ahead of its time. `PlanKeeper` is the
only thing that does, and it goes through the same places every room change goes through.

- **The room is made closed with the people coming let in** (`SessionStore.CreateClosed`), the creation
  beside first-entry creation (`Enter`): the plan's id is the room's id, so nobody else holds it. A room
  that is already there (somebody entered it ahead of time) is closed and has the people coming let in
  through `RoomDoors`, the one place the door changes. The title is loaded paused at the start through
  `RoomControls.ApplyAsync`, as machine work (`WatchNow.Server`), once the library holds it.
- **The reaper leaves a plan's room alone until `Rooms:IdleTimeout` past the fixed time**
  (`SessionStore.Sweep` takes the rooms `PlanStore.RoomsHeld` names), then reaps it as any room. The
  plan is `done` once its room is gone after that. A room lost before then (a restart past its idle
  window drops it on the way back in) is made again.

## Screens

A screen is a hub connection signed in through the television client (`Oidc:TvClientId`) with a `sid`
in its token (`ScreenIdentity`); a phone is any other connection or request of a session
(`SessionClaims.SidOf`). `ScreenPresence` keeps screens by person and `sid`, in memory, and
`ScreenPairings` keeps which phones are paired with which screens in the database; the wire is
`docs/api-contract.md`, "Screens".

- **The token decides what a screen is, what a phone is, and whose.** The person is the token's `sub`,
  the screen or the phone the `sid`, the screen's name `tks_device_name`; a page names none of them.
  A guest's connection has no account and joins no person's group, and a token of no session (the
  Activity's) is no phone.
- **A screen is heard by the phones paired with it and nobody else.** Every message about a screen goes
  to each paired phone's group (`PersonGroups.Phone`, the screen's own person and the phone's `sid`) and
  every message about its phones to the screen's group (`PersonGroups.Screen`); `Screens`, `Phones` and
  `/api/me/screens` answer for the caller's own account and session. A new message about a screen takes
  its groups from the screen's own person and its pairings, never from an argument, and every
  `ScreenPairings` read and write names the person.
- **A pairing is made by a code only the screen was shown, and the code is a capability, not a
  record.** `PairingCodes` signs the expiry, a tag of the screen and the screen's person with a key
  derived from `Auth:MediaTicketKey` under a label of its own, so nothing is stored and a code is
  checked against the screens known now: an unknown or altered code, one that ran out, and a screen of
  another person's are told apart, and only the last needs the signature to hold. `PairingCode`,
  `Phones` and a screen's `Unpair` refuse anything but a screen.
- **Connected, room and contents are measured, not remembered.** Connected is whether a hub connection
  of the screen is open; the room is the room of its newest `Join`; the title and paused state are read
  from the room when a view is built. `RoomControls` calls `ScreenPresence.RoomChangedAsync` after every
  push of a room's state, so a change made through HTTP reaches the paired phones like one made on the
  hub.
- **Forgetting a screen loses nothing; ending a pairing is for good.** A screen not connected is
  dropped from memory when a screen of the same name connects under another session or after
  `ScreenPresence.Retention`, and a screen that connects is known again; a paired phone lists one not
  in memory under its kept name. A pairing ends when either side unpairs, when a television of the
  same name signs in again while its old screen is not connected, or when a session of it has gone
  unseen for longer than `Oidc:SessionLifetime`, the provider's own bound on a session nobody renews.
  Cinema hears no revocation, and that bound is what lets it say a session has ended without guessing.

## Remotes

A remote is a phone's hub connection that drives the room one of its paired screens is in
(`JoinAsRemote`, `RoomRemotes.cs`). It moves the film like any member, through `RoomControls`, and is a
participant of no kind.

- **The server decides it, from the token and the pairing.** The connection must be a phone (a `sid`,
  not a screen), the page names only the screen it wants, and `ScreenPairings.Paired` must hold for the
  token's person, the phone's `sid` and that screen. The pairing is read again on every act, so one that
  ended is refused at once, whatever the connection still holds.
- **A remote binds to the screen, so its room is asked, never held.** `ScreenPresence.RoomOfRemote`
  answers the room the screen is in now, and every method that moves a room resolves it through
  `SessionHub.SessionId`. `ScreenPresence` moves a remote between the rooms' groups in the same place,
  and under the same gate, that it works out a screen's room (`FollowAsync`, called ahead of every
  announcement), and tells the remote with `RemoteMoved`. A move cannot be missed by a push in flight,
  because the room a remote acts on is looked up per act and the group it hears is changed under the gate.
- **A remote is absent from what a room counts, by construction.** It is never added to `Participants`,
  `ViewerQuality` or the roster, so nothing filters it out and no new counter has to remember to. What
  its being there would corrupt:
  - *the hold* (`SessionStore.Hold`): a member who never waits keeps a room from being held, and one
    that waits holds it for nobody;
  - *the transcode plan* (`WantedHeights`): a viewer who has reported nothing wants the tallest rung,
    so a phone that plays nothing would raise the room's rung;
  - *the roster, the head count, `/health`'s `watching` and the presence the bot draws*: they would
    count somebody who is watching nothing, and `Attended` would keep a guest link alive on one;
  - *the watch record* (`Viewers`, `Watched`): it would credit a person with a film they only paused
    from a sofa.
  Buffering and quality are hub methods alone (no route takes either), and `RemoteGate` refuses them,
  the door (`Knock`, `SetOpen`, `Answer`) and the other ways in (`Join`, `JoinAsGuest`) to a remote. It
  refuses every method not marked `[RemoteAllowed]`, as `GuestGate` does for guests, and the tests walk
  the hub's methods for the list.
- **An act of a remote names the person alone.** `Actor` is the person's id and name, which is what
  the room shows ("You paused" on the screen of the person it is); the phone is named in the log
  (`via <connection> remote`) and nowhere in shared state. A device in `Actor` would be written into
  every push and every kept room, and shown to every other member, for a distinction nothing in a room
  uses.
- **The screen is told how many remotes drive it, and the room is not.** `RemotesChanged` (and the
  screen-only `Remotes` for the count when it connects) goes to the screen's own group whenever a remote
  joins, leaves, disconnects or is dropped; the television draws a phone glyph from it while paused. It is
  the screen's business, so it never rides on `ParticipantsChanged`, which a remote stays out of.
- **A remote opens a room on a screen that is in none, and nothing else does it** (`WatchOnScreen`,
  `ScreenPresence.WatchAsync`). The screen is the remote's binding, the person the pairing's, and both
  are read again on the call: the page names a room and a title and nobody. It is refused unless the
  screen is connected and in no room, since a screen in one is moved through that room, where the hold,
  the watch record and the roster already see it. The message goes to the screen's own group and carries
  no state: what the screen shows is the room it then joins.
- **A remote changes the screen's own settings through the API, never through the room.** The subtitle
  track, how subtitles look, the audio track and quality are the person's (`/api/me/preferences`,
  `/api/me/tracks/{titleId}`), and a phone writes them as any device of the person's does. The API checks
  them against the film and pushes the change to the person's group (`PersonGroups.Everything`), which
  holds the television's room connection and its person connection and the phone's, so the television
  that is playing the film applies it and the phone's labels follow, with the television reporting
  nothing back. Nothing about it is a hub method of the remote's, goes through `RoomControls`, is in
  `SessionState`, `Actor` or any room group, or names a screen: the record is the person's, not the
  screen's.
- **A pairing ending ends the remote.** `ScreenPresence` drops the remotes of a pairing it ends
  (unpaired from either side, or a television signed in again under the same name) out of the room's
  group and forgets them; a disconnect forgets the connection's.

## Keeping a room

A room is the only thing in the system that exists nowhere else. The films are on disk and the
subtitles are kept; what a room is watching and where it has got to would otherwise live in memory
alone, and restarting the API would take the film out from under everybody in it.

- **A room is kept, and put back.** `RoomStore` keeps every room in the database, its state in the
  shape it is pushed to the room. It is read before the server begins listening — a room restored
  after the first client has joined is a room that client was already told did not exist. A room
  whose kept state no longer reads as one starts again empty.
- **A restored room keeps its epoch and its revision.** It is the same run of the same room rather
  than a new one wearing its name, so a client connected across the restart goes on applying pushes
  instead of discarding every one of them, and its rejoin carries on from its own playhead, since
  nothing was done to the room while the server was down. Nothing about the restart reaches a person
  watching.
- **Position survives for free**, because it is a place at an instant rather than a number that
  ticks. A room that was playing when the server stopped is playing, at the right place, when it
  starts again — however long that took.
- **Nobody is restored into a room.** Membership is a live connection and every one of them died with
  the server. Restoring the names would leave a room reporting people who are connected to nothing,
  which the reaper would then never forget.
- **What is kept does not resurrect what the reaper would have forgotten.** A room idle past the
  window is dropped on the way back in, because a link into a room is supposed to stop working.
- **Keeping is on a two-second delay.** A drag along the scrub bar is a burst of changes and each
  would be every room written again. A planned stop keeps them on the way out and loses nothing.
- **`/health` says how many rooms are occupied and by how many people, and no names.** It is what
  makes restarting something that can be looked at first rather than found out about afterwards.
- **The room listing carries what each room is watching and how many are in it, and no names**: who
  is in a room is the room's business. A person is listed only the rooms they may enter. The bot's
  presence surfaces read it on every pass.
- **A room is the record of what is being watched.** Every change ends the stretch of playing before
  it, and the store says so (`Watched`) with who was in the room and where the film got to; somebody
  leaving ends it for them, and the server stopping ends it for everyone. Nothing else keeps its own
  idea of a film's position.
- **An open room's link unfurls with the film, never with the people** (`RoomPreview`). `/r/<room>` is
  answered with Open Graph tags in the page itself, since a crawler runs no script: the film's name,
  its year, how far in the room is, and its backdrop or poster at an absolute public address. Whatever
  unfurls the link fetches it without a token, so nothing there is private, and it is stricter than the
  listing: no names and no count either. A private room unfurls as the site and names not even its film. A room
  nobody has opened yet shows the film its link names. A good guest link (`/g/<code>`) unfurls with its
  room's film, open or not: whoever holds it was invited to watch it.
