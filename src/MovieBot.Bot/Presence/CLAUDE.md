# Saying what a room is watching

Discord shows it in three places, and each is a different surface with a different reach. Two are the
bot's and live here; each viewer's own rich presence comes from the Activity (`web/activity/CLAUDE.md`).

- **The bot's status names a film only while exactly one room is watching one.** There is one status
  for the whole bot, so two rooms are counted rather than named, and no room at all is no status at
  all: a placeholder beside the name never changes, which reads as never having worked. Discord lets a
  bot set nothing beyond a name and a type here, so the second line stays empty.
- **The line under a voice channel is per room, and only an occupied room gets one.** It is the one
  surface the whole server sees without opening anything. The position is written to the minute and
  every distinct line is one request, so the clock sets the pace of the writes: a playing film changes
  its line once a minute and a paused one never does. Discord requires Manage Channels as well as Set
  Voice Channel Status from a bot that is not itself connected to the channel, and both are in the
  invite the bot logs.
- **The bot recognises its own lines and clears only those.** It keeps no record of the channels it
  wrote under across a restart, and a line left standing after the room behind it ended would
  otherwise stay until the next film in that channel. A person's own channel status has a different
  shape and is never touched.
- **What the bot says is read from the API on every pass, never remembered.** The listing carries what
  each room is watching and how many are in it, and no names: who is in a room is the room's business.
