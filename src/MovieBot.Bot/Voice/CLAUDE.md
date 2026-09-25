# Listening in a voice channel

`/voice join` brings the bot into a voice channel to listen, and "okay computer, pause" stops the
film. `../README.md` is the authority for the surface; these are the rules it rests on.

- **The trigger is heard mid-conversation, and a verb acts before the room goes quiet.** Each speaker
  is scanned for the trigger in three-second windows on moviebot-speech's scan lane, because people
  watching together never pause for the bot. `RoomVerbCompleteness` tells the pipeline a request is
  whole as soon as two readings agree it is a room verb, so "pause" stops the film while the room keeps
  talking. Commands are cut at seven seconds, under moviebot-speech's eight-second command window.
- **The gate comes before the model.** What is heard goes to moviebot-speech for words and the words go
  to `RoomVerbs`, a gate that knows a handful of verbs. A component that moves the room for everyone in
  it should read the same words the same way every time, so the common verbs never depend on a model's
  judgement. Only what the gate does not read goes to the assistant (`../Assistant/CLAUDE.md`).
- **The gate matches the whole request and has three answers.** "Should we pause?" contains the word
  and is not the verb. A phrasing that looks like a verb and cannot be read safely — no amount, a vague
  one, two acts, or digits joined by punctuation that flattening would fuse — is ambiguous and not
  guessed at, because a misread verb moves the film for everybody.
- **A verb needs a room holding a film.** Every write to a room creates one, so the room is read first
  and a verb in a channel watching nothing does nothing.
- **A play or a pause from the bot sends no position.** The API resolves "wherever the room is" under
  the lock that applies the change; a position guessed by something not watching would move the film
  as well as stopping it.
- **Listening is announced in the channel, or it does not happen.** The notice is the only way anyone
  but the person who ran `/voice join` learns they are heard.
