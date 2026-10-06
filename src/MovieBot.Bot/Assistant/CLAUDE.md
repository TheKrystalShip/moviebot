# Asking the assistant

Anything said in a voice channel that the room-verb gate does not read is put to moviebot-assistant as
a turn in that room, as the person who said it, and answered in the voice channel's chat. The turn,
the tools, the conversation and the proposals are the service's (`src/MovieBot.Assistant/CLAUDE.md`);
`../README.md` is the authority for the surface. These are the rules the bot's half rests on.

- **The bot is a client, with no engine of its own.** It names the room and carries the speaker's
  access token, the one it exchanged for their Discord account (`Api/PersonTokens.cs`), and nothing
  else: which tools a turn gets and what they may do follow from that. With the service down, a
  request gets one sentence saying so, and the room verbs, slash commands and announcements go on
  exactly as before. `Assistant:Enabled` off means nothing is asked at all.
- **What stays here is Discord.** The heard line, the launch card for a `launch` frame, the buttons
  under a proposal, and what a confirmed act posts: the same sentence and embed the slash command
  posts (`DownloadCommand.Describe`, `NotifyCommand.Describe`), and for a download the progress
  message the watcher keeps current. One wording for one act, whoever asked.
- **The buttons carry the service's token, and so does a spoken yes.** It redeems once, at the
  service, so whichever arrives first carries the act out. The bot redeems it with the token of
  whoever agreed, and the act is done as them: they are the one spending it, and the API decides
  whether they may. A dropped proposal loses its buttons here and expires at the service.
- **Room verbs are written into the room's conversation** (`POST /rooms/record`) as the tool call
  they stand for, never waited on, so "why did it stop?" is answered by an assistant that knows.
