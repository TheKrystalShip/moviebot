# Remote mode: follow-up ideas

A draft of ideas, not a plan. Nothing here is scoped, investigated or decided. Each idea is taken
through a deep dive and its decisions are made with the user before it becomes a plan of its own. How
the remote works is canon in `web/site/README.md` ("Driving a television"), `docs/api-contract.md`
("Screens") and `src/MovieBot.Api/Sessions/CLAUDE.md` ("Screens", "Remotes").

## Ideas

- **Matching by network.** A phone on the same public address as a screen is offered it without a
  scan. Home Wi-Fi clients reach the site through the router's public address, so a phone at home and
  the TV look the same to the server; a phone on mobile data or a VPN does not.
- **Friends on the sofa.** A QR that brings another person's phone into the TV's room as a remote,
  driving the room but not the TV's own settings, and crediting them as watching.
- **A room's playlist.** What plays after the current film, for everybody in the room. Canvas board 11
  (replacing what is playing) is kept for it.
- **A desktop tab as a remote.** The same messages under the desktop's layout.
- **Saying which session a scan paired.** A pairing belongs to the session that scanned. When the
  browser that opens a scanned link is not the one the app was installed from (a phone whose camera
  hands links to Chrome, with the app installed from Samsung Internet, or iOS, where a scan always opens
  Safari), the app is not the paired one. The scan's landing page could tell when it runs in a browser
  tab rather than the installed app and say so.

## Measured only on real devices

What the headless runs cannot say and no real device has shown yet:

- The paused player's smaller code read by a phone camera from the sofa (Profile's code is read).
- A scan on iOS, and on an Android phone with the app installed from Chrome rather than Samsung
  Internet.
- A phone's remote coming back after hours in a pocket (`web/site/verify/resumePhone.mjs` measures it).
- iOS Safari's handling of the dock's push and pull and its sideways swipe, and whether a vibration
  is felt.
- A television that sleeps on its own, or whose app Fire OS ends, while a phone drives it.
- `Settings.Global.DEVICE_NAME` on an API 31 or later Google TV.
- The remote's control row on a narrow phone when Episodes, Subtitles, Audio and Quality all show: the
  menus take a second line.
