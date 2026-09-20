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
"Okay computer, put on Heat."
```

## Room verbs — acted on instantly, no model involved

A closed set of playback commands is read by a plain phrase matcher, not the assistant, so they
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

## Talking to the assistant

Anything the room verbs do not read is a turn of the assistant, which answers in the channel's
chat rather than out loud (the film's sound already plays in every viewer's browser). It can
call on the bot's own commands, so the same things `/watch`, `/download`, `/notify` and `/keep` do
from a text channel can be asked for by voice, alongside questions about what is happening.

**Watching a film**

```
"okay computer, put on Heat"
"okay computer, let's watch The Matrix"
"okay computer, watch the second Pirates of the Caribbean"
```

If the library already has it, it plays. If not, the assistant searches the tracker and proposes
the best release; confirming starts the download and the film plays here as soon as it is
watchable. A film with no release yet gets offered as something to wait on instead (see "waiting
on a film" below).

**Getting the next film ready without stopping this one**

```
"okay computer, download Inception for later"
"okay computer, get The Matrix ready for after this one"
"okay computer, grab Heat for tomorrow, don't stop this"
```

The film is fetched and the room carries on with what it is watching. Confirming starts the
download; nothing is loaded, and the film waits in the library until somebody asks to watch it —
at which point `/watch`, or "put it on", starts it. Asking to *watch* a film that is not here is
the other tool and still switches the room to it once it arrives, so what separates the two is
whether a film was asked for now or for later.

A film fetched this way is on the same clock as any other download: ask to keep it if it is for
a weekend away.

**Downloading a specific release**

```
"okay computer, get the other one instead, the smaller file"
```

(spoken after `watch_film` has already listed releases to choose from — a torrent id it did not
just offer is refused).

**Asking about a film**

```
"okay computer, how long is this?"
"okay computer, does Heat have Spanish subtitles?"
"okay computer, who's in this movie?"
```

**Checking on things**

```
"okay computer, how's the download going?"
"okay computer, what's everyone else watching?"
"okay computer, what are we waiting on?"
"okay computer, why did it stop?"
```

**Subtitles**

```
"okay computer, get subtitles for this"
```

**Waiting on a film that is not out yet**

```
"okay computer, let me know when Dune Part Three comes out"
```

**Keeping a film around, or not**

```
"okay computer, keep this one"
"okay computer, let this one go"
```

## Confirming

Watching a film that has to be downloaded, fetching one for later, downloading a specific
release, fetching subtitles, waiting on a film, and keeping or un-keeping one are all proposed
rather than acted on straight away. Each proposal is a message with two buttons under it in the text channel, and answering out
loud works the same:

```
"okay computer, yes"
"okay computer, no"
```

Anything else said in the confirmation window is read as unrelated to the proposal — a remark
about the film, say — and spends the window without touching it. A proposal otherwise expires on
its own after `Assistant__OfferMinutes`.

## What is not a voice command

`/watch`, `/download`, `/notify` and `/keep` are typed slash commands, not spoken ones — see
`src/MovieBot.Bot/README.md` for their syntax. A `/watch` from inside a voice channel is what
opens the way back into a room whose launch card has expired; nothing spoken re-issues an
invite.
