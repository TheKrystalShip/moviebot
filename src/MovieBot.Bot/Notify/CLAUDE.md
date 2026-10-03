# `/notify`: waiting on a film

A wish is the API's (`src/MovieBot.Api/Acquisition/CLAUDE.md`): it decides whether somebody waits,
keeps the list and sweeps the tracker. This directory asks it and tells the channel.

- **The film is picked from the catalogue.** The command autocompletes from the IMDb index and accepts
  a pasted link; what is sent is the id, or what was typed when nothing was picked.
- **The announcement is a new message and names exactly who asked.** An edit notifies nobody. The
  mentions go in the message text, because a mention inside an embed renders and pings no one, and the
  allowed mentions are those ids alone.
- **`WishWatcher` reports the channels it told, and only those.** Whoever asked in a channel the
  message could not be sent to stays on the list for the next pass. A channel the bot can no longer see
  is reported as told, because retrying it would never work.
