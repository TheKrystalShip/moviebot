# `/notify`: waiting on a film

A wish is the API's (`src/MovieBot.Api/Acquisition/CLAUDE.md`): it decides whether somebody waits,
keeps the list and sweeps the tracker. This directory asks it and tells the channel.

- **The film is picked from the catalogue.** The command autocompletes from the IMDb index and accepts
  a pasted link; what is sent is the id, or what was typed when nothing was picked.
- **The announcement is a new message and names exactly who asked.** An edit notifies nobody. The
  mentions go in the message text, because a mention inside an embed renders and pings no one, and the
  allowed mentions are those ids alone.
- **`DirectMessages` sends a person the notifications no push reached** (`/api/notifications/direct`).
  The API decides who is owed one and words it; this sends the text as it is, with the page as a link
  button, and reports each. Discord refusing a message outright (no shared server, messages closed) is
  reported as refused; any other failure is not reported, and the next pass tries again.
- **`WishWatcher` reports the channels it told, and only those.** Whoever asked in a channel the
  message could not be sent to stays on the list for the next pass. A channel the bot can no longer see
  is reported as told, because retrying it would never work.
