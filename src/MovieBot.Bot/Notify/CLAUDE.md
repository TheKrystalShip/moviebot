# `/notify`: waiting on a film

A want is the API's (`src/MovieBot.Api/Wants/`, `src/MovieBot.Api/Acquisition/CLAUDE.md`): it keeps
the person's want of the film, decides whether anything has to be waited for, keeps the wish, sweeps
the tracker and tells the person when a film is out. This directory asks it as the person who ran the
command and tells the channel what it answered.

- **The film is picked from the catalogue.** The command autocompletes from the IMDb index and accepts
  a pasted link; the id is read from what was sent here, and what names no film is refused before the
  API is asked.
- **The want is made visible, as the site's is.** `/notify add` is the same act as wanting the film on
  Krystal Cinema, so the person is named to others who want it there.
- **Nothing about a wish is said on Discord afterwards.** A person waiting on a film is told it is out
  on Krystal Cinema, in their notifications and by a push to their devices, and the want shows the
  release beside it there. The bot needs no Discord id to remember for anybody, and keeps none.
