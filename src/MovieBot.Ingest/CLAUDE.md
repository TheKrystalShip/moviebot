# Ingest invariants

These are measured against real Blu-ray rips, not assumed. Changing one means re-measuring.

```bash
dotnet run --project src/MovieBot.Ingest -c Release -- "<file>" --dry-run      # inspect, no GPU spent
dotnet run --project src/MovieBot.Ingest -c Release -- "<file>" --out ./media  # ingest for real
```

## Probing and subtitles

- **Probe with `-probesize 200M -analyzeduration 200M`.** With the defaults, ffprobe cannot size PGS
  subtitle streams: it prints `Could not find codec parameters` once per stream and may return
  incomplete data.
- **Subtitles and the poster are extracted before the main pass, never alongside it.** A monolithic
  `.vtt` being appended to while a player fetches it once gives subtitles that stop partway through the
  film.
- **Subtitles are repaired against double-encoding as they are extracted.** Releases ship tracks that
  were written as UTF-8, read back as Windows-1252 and written again, so a right single quote arrives
  as three characters. Nothing downstream can detect it: the file is valid UTF-8, valid WebVTT, and
  every byte survives the transcode intact. The repair works one run of non-ASCII characters at a time
  and keeps only what decodes strictly, which is what lets it leave a Portuguese `NÃO` or a Romanian
  diacritic alone — legitimate text almost never forms valid UTF-8 when re-encoded that way, and the
  same characters are exactly what the corruption produces.
- **English is the track that gets checked.** It is the one anybody here selects, so it is repaired
  with the benefit of the doubt on the single case decoding cannot decide — a closing quote whose last
  byte the encoder discarded is restored even with nothing in the document to prove that is what it
  was — and it is inspected afterwards. The inspection is adjacency: two non-ASCII characters do not
  stand next to each other in English, where quotes, dashes and the accented letters of names and
  loanwords all appear singly between ASCII, while every form of this corruption produces two or three
  in a row. That catches mangling through an encoding the repair does not reverse, which a list of
  known-bad sequences would not.
- **The other languages keep the strict reading.** Relaxing it would gain nothing, because the strict
  pass already repairs everything a looser one would, and it would cost the Portuguese and Romanian
  tracks it leaves untouched: the characters a looser pass would act on are the ones those languages
  are spelled with.
- **The source is fingerprinted once it is whole, never while it is arriving.** Its release name,
  size, frame rate and OpenSubtitles hash are what an externally found subtitle is judged against, and
  the hash covers the last 64 KiB of the file. Space for a download is reserved before the bytes land,
  so hashing an arriving source reads zeroes and returns a confident wrong answer that nothing
  downstream can question. It is computed where the subtitles are, after the file is complete.
- **Only the languages a room reads are extracted, and what is left behind is named.** A disc carries
  thirty subtitle languages and the three anybody wants cannot be found among forty-eight rows. They
  are not a resource problem — every track comes out of a single demux pass either way — so this is a
  menu decision. The languages skipped are recorded on the manifest, because one that is simply absent
  from a film that plainly has it reads as a fault.
- **Matching a language accepts every spelling of it.** A container writes Romanian as `rum`, `ron` or
  `ro` depending on who muxed it, and the same holds for twenty-odd others. Matching one spelling and
  not another keeps nothing, which produces a film with no subtitles and no error.
- **A track with no language tag is always kept.** Releases ship one untagged subtitle often enough
  and it is usually the one worth having; dropping it on a filter leaves a film with none and nothing
  anywhere to explain why.

## Track labelling (`Probe/TrackClassifier.cs`)

Driven entirely by what the container says, because the sample film makes every shortcut fail:

- **Label from the `title` tag, not the language code.** Three tracks tagged `chi`, two `spa`, two
  `por`, two `fre`, two `eng`. Code-only labels produce identical menu rows.
- **Split on the `comment` disposition.** Thirteen of forty-eight subtitle tracks are commentary.
- Inside the commentary group the word "Commentary" is stripped, since the group is already named —
  what remains qualifies the language. A title long enough to name *who* is speaking is kept whole.
- **Bitmap subtitles are listed, not dropped**, with `available: false` and a reason. A language that
  is simply absent looks like a bug.

## Playlists, rungs and segments

- **Playlists are `EVENT`, not `VOD`.** That is the whole mechanism behind playback starting seconds
  in. ffmpeg appends `#EXT-X-ENDLIST` on completion, so the playlist becomes a normal VOD by itself.
- **A film is written at two sizes, and every rung is cut on the same keyframe expression.** A player
  can only drop to a rung that exists, and a viewer who cannot sustain the top bitrate stops for good
  at whatever second the buffer empties. `v0` is the source's own size and `v1` is 1280 wide, and both
  are forced to a keyframe on each segment boundary so segment *n* of one opens on the same frame as
  segment *n* of the other, which is what makes a switch mid-film seamless. A source no wider than the
  step-down keeps one rung, because upscaling spends a second copy of the film on no more picture. The
  ladder stops at two: every rung is a full copy on a disk that holds the whole library.
- **A rung advertises the size it is encoded at**, because a player sizes its buffer from what it is
  told. A film with a single rung states no size on it, and that rung is read as the source's own size
  — so a manifest never has to be rewritten to say what it already implies.
- **Segments are two seconds.** A segment is the unit a stall is measured in: a player abandons one
  that does not arrive in time and refetches the whole thing, so the length caps how much a slowed
  path has to carry in one go — around two megabytes at the top rung. Changing it means re-measuring
  against a real rip, because the GOP is pinned to match and it is also how often a keyframe is paid
  for.
- **The GOP is pinned to the segment length** and `-force_key_frames` guarantees a keyframe on every
  boundary whatever the frame rate, so a seek lands on the frame it asked for.
- **`headSeconds` is the shortest of the written playlists, read from the `#EXTINF` sums** — not
  ffmpeg's own progress. A frame the encoder has read is not watchable until its segment is closed and
  listed, and video that exists without its audio is not playable at all.
- **The manifest is written through a temp file and an atomic move.** The API polls it during a
  transcode and must never observe a partial document.

## Decoding, tone-mapping and the card

- **Tone-mapping is auto-detected from the transfer function** and must stay that way. Tone-mapping an
  SDR source washes it out exactly as failing to tone-map an HDR one does.
- **`libplacebo` fails to initialise its filter graph on the ffmpeg the transcode runs against**, under
  every option combination tried. `tonemap_opencl` is the working path, and it is measured on the card
  that does the work rather than assumed. If a future build fixes libplacebo it is the better filter,
  but verify before switching.
- **Decode on NVDEC (`-hwaccel cuda`), always.** Software decode of a 16 Mbps HEVC Main 10 source
  costs 35 s of CPU per 40 s of film against 5.5 s on the GPU, and pins every core for the length of a
  feature. Frames land in system memory, which is where `tonemap_opencl` wants them.
- **Every rung and every audio rendition reads the source through its own demuxer.** One demuxer
  shared between outputs runs at the pace of the fastest consumer and queues packets for the slower
  ones in memory without bound — measured at 9 MB a second on a 1080p rip with four audio tracks,
  which exhausts hotbox a third of the way into a feature and gets the transcode killed.
  `ReadAheadGuard` takes the furthest of the descriptors as the read position.
- **The concurrency ceiling belongs to the card, and is measured rather than reasoned about.**
  `Handoff.MaxConcurrentIngests` is set per host in its settings file, because a GPU saturates at an aggregate rate and
  jobs past that point only divide the same throughput into thinner slices. What matters is not the
  aggregate but the per-film rate, which has to stay well ahead of one playhead.

## Dialogue boost (`Pipeline/DialogueBoost.cs`)

- **Every feature track gains a dialogue-boost mix, made after the main pass.** A film's dialogue
  lives in the centre channel, which exists as its own signal only until the downmix, so the mix
  weights it above the fronts and surrounds there, then compresses and runs `loudnorm`. Measured on a
  5.1 feature, the gap between the dialogue and the loudest ten seconds goes from 20 dB to 9. The
  weights in `DialogueBoost` were chosen by ear against a room's own playback; a layout with more
  speakers per role shares the weight at equal power so the balance holds.
- **The boost is its own pass because `loudnorm` is slow.** It oversamples to 192 kHz for true peaks,
  which puts the chain at 8x realtime on hotbox's CPU against 27x without it, slower than the video.
  Inside the main pass it would be the laggard every viewer's head waits on. Cheaper riders were
  measured in its place and each lands on a different sound. The pass runs before the status turns
  ready, because settling moves the directory it writes into, and a track is listed only once its
  playlist is whole. The boosted renditions are numbered after the source's own tracks, so a viewer's
  saved track id names the same track either way.

## Reading a source that is still arriving (`Ffmpeg/ReadAheadGuard.cs`)

Transcoding a file while it downloads is only safe while the reader stays behind the writer, and
nothing enforces that on its own. `IngestOptions.Availability` is supplied only while a source is
still arriving, so a whole file takes the ordinary path, measured against it, and nothing about the
CLI changes.

- **Reading past what has arrived returns zeros, not an error and not the end of the file.** Space for
  the whole file is claimed when the download starts. ffmpeg encodes the zeros, and the result is a
  film with stillness and silence in it and nothing anywhere to say so.
- **`ReadAheadGuard` watches rather than trusts.** The reader's position comes from the kernel
  (`/proc/<pid>/fdinfo`), the arrived length from whoever is fetching the file. When the gap closes the
  process is stopped and resumed when it opens.
- **The guard fails closed.** If it cannot find the descriptor, it refuses rather than running on: a
  position it cannot read pauses, and one it never found at all aborts the transcode. Running unguarded
  produces corruption that looks like success, which is worse than a failure that says so.
- **A guard that cannot do its job kills the transcode.** It is awaited after the transcode so its
  message replaces ffmpeg's, which would only report that it was killed.
- **Subtitles wait for the whole file.** They have to be demuxed from a complete one, so where a
  source is still arriving they are extracted after the main pass, and a track is advertised in the
  manifest only once its own file is whole. A `.vtt` that is still being written is never served.
