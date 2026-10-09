# Voice commands

What can be said out loud to a bot listening in a voice channel, with examples. `/voice join`
and `src/MovieBot.Bot/README.md` are the authority for the mechanism; this is a showcase of what
it accepts.

## Getting the bot listening

```
/voice join     Joins the voice channel you are standing in and starts listening
/voice status   Whether the bot is listening here, and whether it is hearing anything
/voice leave    Stops listening and leaves
```

Joining posts a notice in the text channel the command ran in — that notice is the only way
anyone but the person who ran it learns they are being heard.

## Addressing it

Nothing is heard as a request until a trigger phrase opens it. The default triggers are
**"okay computer"** and **"ok computer"** (each also matches with a trailing "s": "okay
computers"), configurable per deployment with `Voice__Triggers`. The trigger is picked out of
a person's last few seconds of speech while the room keeps talking — nobody watching a film with
friends goes quiet first — and everything from the word before it is taken as the request.

```
"Hey, okay computer, pause."
"Okay computer, skip forward a minute."
```

## Room verbs — acted on instantly

A closed set of playback commands is read by a plain phrase matcher, so they
land the moment they are heard and are worded identically every time. They need a room already
holding a film; said to an empty room, they do nothing.

**Pause**

```
"okay computer, pause"
"okay computer, stop"
"okay computer, pause the movie"
```

**Play / resume**

```
"okay computer, play"
"okay computer, resume"
"okay computer, unpause"
"okay computer, continue"
```

**Move back**

```
"okay computer, back fifteen"
"okay computer, go back a minute"
"okay computer, skip back thirty seconds"
"okay computer, rewind"
```

**Move forward**

```
"okay computer, skip forward a minute"
"okay computer, fast forward"
"okay computer, jump ahead thirty seconds"
"okay computer, skip"
```

An amount can be a bare number, a word ("fifteen", "a minute", "half a minute"), or a compound
("forty five seconds"). A direction with no amount ("go back"), a vague one ("rewind a bit"), or
a time written with punctuation ("back 1:30") is heard as *shaped like* a verb but not carried
out — a missed verb just costs a slower answer, where a misread one would move the film for
everyone. A question or a remark that merely contains the word — "should we pause?", "don't
pause it" — is not read as the verb at all.

## What is not a voice command

`/watch`, `/show` and `/notify` are typed slash commands, not spoken ones — see
`src/MovieBot.Bot/README.md` for their syntax. A `/watch` from inside a voice channel is what
opens the way back into a room whose launch card has expired; nothing spoken re-issues an
invite.
