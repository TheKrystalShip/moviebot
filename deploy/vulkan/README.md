# The GPU services the voice surface runs on

Two pieces of inference stand behind a spoken command: whisper turns what somebody said into
text, and gemma turns that text into a room command. Both run on hotbox's Quadro P2000 through
Vulkan, and both are built here rather than installed from a repository.

```
./build.sh          # on hotrod: build both, assemble into stage/
./install.sh        # rsync stage/ to hotbox:/opt/moviebot, fetch models there
```

`build.sh` is the authority on versions and flags. `install.sh` needs no privilege —
`/opt/moviebot` is owned by the user that runs the services. Installing the unit files does
need root, and is the one step that has to be asked for.

## Why these are built and not installed

Arch ships both, and using the packages was the obvious first answer. Three things make a
source build the cheaper one.

**hotbox cannot execute an AVX2 instruction.** Its Athlon X4 860K has AVX, FMA and F16C but
neither AVX2 nor BMI2; hotrod's Ryzen has all of them. ggml's default is `GGML_NATIVE=ON`,
which compiles for the machine doing the building, so the natural build on hotrod produces
something that dies with SIGILL on hotbox — and not at startup, but on whichever request first
reaches the offending kernel. The flags in `build.sh` pin the instruction set to what hotbox
actually has, and the script fails the build if an AVX2 or BMI2 opcode appears in anything it
is about to ship.

**A backend that is not found is not an error.** Arch builds ggml with `GGML_BACKEND_DL=ON`,
which makes the Vulkan and CPU backends modules dlopen'd at runtime from a directory compiled
into the library as `/usr/lib/ggml`. Populate that directory and it works; get it wrong and
llama.cpp starts, reports no devices, and runs every token on four Athlon cores — no error, no
warning, forty times slower. Built from source the default is `OFF`: the Vulkan backend is a
`NEEDED` entry on the binary, so it either resolves at exec or the process does not start.
It also means nothing has to be written under `/usr/lib`, so nothing here needs root.

**The versions have to agree.** The distribution's whisper is built against one ggml release
and run against whatever is installed, which is an ABI coupling that holds until it doesn't.
Each build here carries its own.

whisper.cpp is pinned to the release Whisper.net binds against, because the speech daemon
reaches whisper through P/Invoke rather than through `whisper-server`. The library ABI is the
contract there, so a newer whisper is a worse match rather than a better one.

## The shape of an installed tree

```
/opt/moviebot/llm/      bin/llama-server  bin/warm-llm.sh  warmup.json  lib/  models/
/opt/moviebot/whisper/  bin/whisper-cli   bin/whisper-server           lib/  models/
```

Each tree is self-contained. Every binary carries an RPATH of `$ORIGIN/../lib`, so there is no
`LD_LIBRARY_PATH` anywhere, no wrapper script, and a tree can be moved or run by hand.

`whisper-cli` and `whisper-server` are there to verify the tree and to measure it. The speech
daemon does not run either: it loads `lib/libwhisper.so` in its own process.

## Warming, and why a token request will not do

The port opens before the card can compute. Vulkan builds its compute pipelines on the first
request that needs them, and the prompt prefix is only cached once something has been prefilled
through it. Both are one-off, both cost about a second, and both are otherwise paid by whoever
speaks first.

The warm-up only warms the shape it is sent. Measured on hotbox after a restart:

| request | time |
|---|---|
| the warm-up's own shape, replayed | 241 ms |
| a different prompt and catalog, first time | 1,093 ms |
| the same, second time | 244 ms |

So the warm-up is the bot's own request. Every time moviebot-bot starts with the assistant on, it
writes its instructions and tool catalog as a llama-server request to
`/var/lib/moviebot-bot/llm-warmup.json` and sends it through the model; `warm-llm.sh` runs as this
unit's `ExecStartPost` and replays that file, so systemd does not report the service active until a
request of that shape has been all the way through. The bot is the only thing that knows the shape,
which is why it writes the file. `warmup.json` beside the script stands in on a host where the bot
has never run with the assistant on; a warm-up of the wrong shape leaves the first real request cold
and looks like it worked.

## Measured on hotbox

Quadro P2000, 5 GB, Vulkan, model resident at 1,397 MiB.

| | |
|---|---|
| whisper, encode + decode, 1–3 s clips | 149–223 ms |
| whisper encode alone, any clip length | ~108 ms |
| tool call, warm | 242–276 ms |
| model load to first token, from cold start | ~14 s |

Whisper's encode is flat with utterance length because `-ac 200` caps the audio context at four
seconds. Left at the default it pads every clip to thirty and the encode costs the same as a
thirty-second one, which on this CPU is the difference between a fifth of a second and twenty.
