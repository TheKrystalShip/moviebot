# Subtitles fetched from outside, and confirming one fits

## Fetching

- **One person fetches, the whole room gets it.** A fetched subtitle is stored against the film, not
  the viewer, and appears in everyone's menu. Which track each viewer selects stays their own choice.
- **They live outside the media root.** Everything under that root is regenerable from the source
  file and these are not: each cost one of a limited daily allowance. A re-ingest replaces a title's
  directory wholesale, the transcode rewrites the manifest inside it every few seconds, and the API
  holds that root read-only. All three point the same way.
- **The manifest on disk never mentions them.** They are folded in as it is served, so a subtitle
  fetched while a manifest sits unchanged in the cache still appears without a restart.
- **The offset is applied before the file is written, not by the viewer.** Where the film carries a
  track that came out of it, a fetched subtitle is measured against that and shifted to match, so
  what lands on disk already fits.
- **The reference has to be a track that came out of the file.** It fits by construction; a
  previously fetched one might be wrong, and measuring against a wrong one propagates its error
  confidently.
- **A menu row names the release, not the language.** Three fetched subtitles otherwise give three
  rows all reading "English" and no way to choose between them, which is the same failure the track
  labelling rules avoid for embedded tracks.
- **Fetched subtitles and confirmations outlive the film.** They are keyed by the library id, a film
  fetched again lands under the same id and gets them back, and each cost a daily allowance to obtain.
- The source's release name, size, frame rate and OpenSubtitles hash are what a fetched subtitle is
  judged against; the ingest computes them once the file is whole (`src/MovieBot.Ingest/CLAUDE.md`).

## Confirming

- **A person watching is the only ground truth.** Frame rate, release name and hash are proxies for
  whether a subtitle looks right on screen. Somebody confirming one answers that directly, so a
  confirmation outranks every measurement — including a later one of ours that disagrees, which is why
  a confirmed track is never re-fetched or re-shifted.
- **How far in it was confirmed is recorded, because drift only shows up late.** A track confirmed two
  minutes in has not been cleared of drift; one confirmed near the end has. Without the position the
  weaker claim reads as the stronger one.
- **Anyone may confirm and anyone may undo it, and the row says who did.** A wrong confirmation is
  fixed by one click from whoever notices, which beats deciding who is allowed to.
- **A confirmation that names a track the film no longer has is dropped, not honoured.** Ids come from
  stream indices and a re-ingest can hand the same number to a different track.
- **Nothing is claimed about a track that came out of the film.** There is no frame rate to disagree
  with and no release to mismatch, so an unconfirmed embedded track says nothing at all. A quality
  mark there would be inventing the one answer that matters.

How the picker shows all this: `web/activity/CLAUDE.md`.
