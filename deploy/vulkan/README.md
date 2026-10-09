# The GPU service the assistant's model runs on

gemma turns text into a room command. It runs on hotbox's Quadro P2000 through Vulkan, and
llama.cpp's server is built here rather than installed from a repository.

```
./build.sh          # on hotrod: build it, assemble into stage/
./install.sh        # rsync stage/ to hotbox:/opt/moviebot, fetch models there
```

`build.sh` is the authority on versions and flags. `install.sh` needs no privilege —
`/opt/moviebot` is owned by the user that runs the services. Installing the unit files does
need root, and is the one step that has to be asked for.

## Why these are built and not installed

Arch ships it, and using the package is the obvious answer. Three things make a source build the
cheaper one.

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

**The versions have to agree.** The distribution's llama.cpp is built against one ggml release
and run against whatever is installed, which is an ABI coupling that holds until it doesn't.
The build here carries its own.

## The shape of an installed tree

```
/opt/moviebot/llm/   bin/llama-server  bin/warm-llm.sh  warmup.json  lib/  models/
```

The tree is self-contained. Every binary carries an RPATH of `$ORIGIN/../lib`, so there is no
`LD_LIBRARY_PATH` anywhere, no wrapper script, and the tree can be moved or run by hand.

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

So the warm-up is the assistant's own request. Every time moviebot-assistant starts, it writes its
instructions and tool catalog as a llama-server request to
`/var/lib/moviebot-assistant/llm-warmup.json` and sends it through the model; `warm-llm.sh` runs as
this unit's `ExecStartPost` and replays that file, so systemd does not report the service active until
a request of that shape has been all the way through. The assistant is the only thing that knows the
shape, which is why it writes the file. `warmup.json` beside the script stands in on a host where the
assistant has never run; a warm-up of the wrong shape leaves the first real request cold and looks
like it worked.

## Measured on hotbox

Quadro P2000, 5 GB, Vulkan, model resident at 1,397 MiB.

| | |
|---|---|
| tool call, warm | 242–276 ms |
| model load to first token, from cold start | ~14 s |
