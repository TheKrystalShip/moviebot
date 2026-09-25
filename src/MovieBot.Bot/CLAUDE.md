# MovieBot.Bot

`README.md` here is the authority for the Discord surface; the `CLAUDE.md` files in each
subdirectory hold the rules its parts rest on (`Watch/`, `Download/`, `Notify/`, `Keep/`, `Launch/`,
`Presence/`, `Voice/`, `Assistant/`).

- **Discord.Net types are written `global::Discord.X`.** The voice pipeline is
  `TheKrystalShip.Discord.Voice` (shared with kgsm-bot, published from tks-agent), and inside any
  `TheKrystalShip.*` namespace a bare `Discord.X` resolves to `TheKrystalShip.Discord` first and fails.
- The bot holds the media root read-only; only the hand-off writes it.

## Showing a film's name

The catalogue resolves a film's name before its transcode (`src/MovieBot.Handoff/CLAUDE.md`); what the
bot shows follows from that.

- **There is one spelling of a name and a year written together**, `Release.Display`, because three
  of them is how the message that starts a download and the message that says it is ready end up
  disagreeing about what film they are talking about.
- **A release name is still shown, beside the name and never instead of it.** Which encode arrived is
  worth knowing; it is just not what the film is called.
- **The two places that keep the release name as the headline are the search results and a download
  in progress.** Neither has been through the catalogue, and both are about a file rather than about a
  film.
- **The poster is the small image in an embed, not the large one.** Posters are portrait, and a large
  one fills a message with artwork nobody asked to look at.
