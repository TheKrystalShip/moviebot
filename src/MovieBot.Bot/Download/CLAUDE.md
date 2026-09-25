# Telling somebody about a download

- **Editing a message notifies nobody.** Discord sends no notification for an edit, so a progress
  message that becomes "ready" reaches only whoever is already looking at it. The announcement is
  therefore a separate, new message — that is what pings.
- **Discord meters message edits per channel, not per message.** Several downloads in one channel
  share one allowance, and their timers landing together is what would spend it, so every edit goes
  through one paced queue. Discord asks that limits not be hard coded and be read from its response
  headers instead; the floor here sits well under the observed allowance and the library holds the
  real one.
- **An edit that would change nothing is not sent.** A download stalled at a third for ten minutes is
  otherwise sixty identical edits, each spending the channel's allowance to say the same thing.
- **The progress message is fetched through its channel, never through the interaction.** An
  interaction token lasts fifteen minutes and a download does not, so editing through the interaction
  stops working partway through a long film.
- **One definition of every embed.** `DownloadEmbed` is used by the command, the updater and the
  announcement. A second spelling would not disagree loudly, it would just produce two messages about
  one film that look like they came from different programs.
