# `/notify`: waiting on a film

A wish is the API's (`src/MovieBot.Api/Acquisition/CLAUDE.md`): it decides whether somebody waits,
keeps the list, sweeps the tracker and tells the person when a film is out. This directory asks it as
the person who ran the command and tells the channel what it answered.

- **The film is picked from the catalogue.** The command autocompletes from the IMDb index and accepts
  a pasted link; what is sent is the id, or what was typed when nothing was picked.
- **Nothing about a wish is said on Discord afterwards.** A person waiting on a film is told it is out
  on Krystal Cinema, in their notifications and by a push to their devices, and the wish shows the
  release beside it there. The bot needs no Discord id to remember for anybody, and keeps none.
