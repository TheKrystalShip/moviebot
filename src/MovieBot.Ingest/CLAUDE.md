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
- **A film is written at up to three sizes, and every rung is cut on the same keyframe expression.**
  A player can only drop to a rung that exists, and a viewer who cannot sustain the top bitrate stops
  for good at whatever second the buffer empties. `v0` is the source's own size, `v1` is 1280 wide and
  `v2` is 854 wide (`Ladder.StepDowns`), and every rung is forced to a keyframe on each segment
  boundary so segment *n* of one opens on the same frame as segment *n* of another, which is what
  makes a switch mid-film seamless. Each rung is its own process and the expression is the only thing
  tying them together; the first frame of segment *n* has the same timestamp in all of them, on SDR
  and on tone-mapped HDR, and is a keyframe. A step-down joins only a source wider than it, because
  upscaling spends another copy of the film on no more picture, so a 720p episode has `v0` and `v2`.
  The ladder stops at three: every rung is a full copy on a disk that holds the whole library.
- **`v2` is the copy a phone keeps and what a weak connection streams.** It is encoded like every
  rung, at quality 25 with 1600 kbit/s as its target. Scored with VMAF against the source on
  two-minute clips of five films, it holds 94.5 to 98.9 under the phone model and 80 to 88 under the
  television model, which is what 480p scaled to a 1080p screen looks like. It comes out at 0.8 to
  1.1 Mbit/s, about half a gigabyte an hour, so the target bounds only the grainiest film.
- **Every rung is encoded at constant quality 25** (`IngestOptions.DefaultCq`), with the rung's
  bitrate as the target and four thirds of it as the cap. The quality decides a film's size, and
  the cap only bounds the grainiest. Scored with VMAF against the source on two-minute clips of four
  kinds of source (grainy Blu-ray, Hi10P, a clean web release, animation), 25 keeps the mean between
  93.6 and 96.8, within three points of 19 on each, at roughly half of 19's size; a difference a viewer notices is around
  six. At 19 a clean web release comes out larger than its source. Spatial and temporal adaptive
  quantization with B-frames made each clip about 30% larger at the same score.
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
- **Each rung and the audio carry a head of their own beside it.** Every rendition's `headSeconds` and
  the manifest's `audioHeadSeconds` (the shortest audio playlist) are read from the same sums on the
  same poll, so the overall head is the minimum of them. All of them are absent once the title is
  ready; a manifest without them reads as every rung being at the overall head.
- **The manifest is written through a temp file and an atomic move.** The API polls it during a
  transcode and must never observe a partial document.

## Decoding, tone-mapping and the card

- **Tone-mapping is auto-detected from the transfer function** and must stay that way. Tone-mapping an
  SDR source washes it out exactly as failing to tone-map an HDR one does.
- **`libplacebo` fails to initialise its filter graph on the ffmpeg the transcode runs against**, under
  every option combination tried. `tonemap_opencl` is the working path, and it is measured on the card
  that does the work rather than assumed. If a future build fixes libplacebo it is the better filter,
  but verify before switching.
- **The main pass is one ffmpeg process per video rung and two for the audio**
  (`Ffmpeg/TranscodeUnit.cs`, command lines from `Pipeline/MainPassArguments.cs`), running side by
  side. A unit owns its process, its progress and its guard, so it can be started, watched and ended
  without the others, and each carries the group it makes: a rung's directory, or `audio`, which
  all of a title's audio units share, so whatever holds or resumes a title's audio addresses them
  together. The first unit to fail ends the rest and its error is the one reported: a film missing
  any rendition cannot be handed to a player, and a guard's message replaces ffmpeg's report that it
  was killed. Measured on 300 s of 8-bit H.264 1920x1040 with two audio tracks, the main pass runs
  at 6.2x to 6.7x against 5.2x to 5.5x as one process. The two video units finish together at about
  8.5x with the card to themselves.
- **Audio renditions are dealt out to two processes, a track at a time** (`AudioGroups`), and a
  process holds a demuxer per rendition. ffmpeg decodes and encodes every rendition of a process on
  one thread (18 s per 300 s for two tracks at 160k and 96k, 27 s at the 256k the renditions are made
  at), so one process makes a film's audio the last unit to finish. Splitting it was measured on
  hotbox's four cores with one, two, three and five audio processes: Two Towers (five 7.x tracks)
  runs at 4.3x to 4.6x whichever, End of Watch (two tracks) at 5.6x to 6.7x with one and 6.1x to
  6.4x with two. The cores are already full, with a load of 8 to 10, so the split does not buy
  speed; what it changes is which unit finishes last. `headSeconds` is still the shortest playlist
  across all units, audio playlists included.
- **Decode on NVDEC, always, and scale in the decoder where there is one.** Software decode of a
  16 Mbps HEVC Main 10 source costs 35 s of CPU per 40 s of film against 5.5 s on the GPU, and pins
  every core for the length of a feature. A source whose codec has a `*_cuvid` decoder here (H.264
  8-bit 4:2:0, HEVC 4:2:0 at 8 or 10 bits, VP9 8-bit; `MainPassArguments.CuvidDecoder`) is decoded
  with it, and each step-down is produced at its size by `-resize`, whose height is the one
  `VideoLadder` advertises. The 1280 step-down alone runs at 19.6x that way against 9.1x with a CPU
  scale behind `-hwaccel cuda`. Every rung decodes the source for itself, so on hotbox's P2000 a
  1080p H.264 main pass is bound by NVDEC: measured on two-minute clips of two Blu-ray rips, the
  video of the pass runs at 9.4x to 10.2x with two rungs and 7.4x to 8.2x with three, the decoder at
  100% and the encoder at 85%. Frames land in system memory, which is where `tonemap_opencl` wants them;
  keeping them on the card from decoder to encoder was measured and is slower (7.8x for the pair
  against 8.6x), so the `format` filter stays.
- **A decoder that cannot open the source gives way to the generic path.** A cuvid unit that fails
  having written nothing runs again with `-hwaccel cuda` and a CPU `scale`; one that fails after
  writing has failed. H.264 High 10 is not decodable by NVDEC on this card's generation and is
  decoded in software with no hardware attempt. Two software decodes run at 2.5x on a 5-minute
  Hi10P clip; one decode feeding both rungs through `split` ran at 4.4x video only, which is what a
  library of Hi10P sources would want in place of a process per rung.
- **Tone-mapping runs after the decoder's scale**, so the step-down is mapped from frames already
  at its size and is tagged bt709 like the top rung. On the 1080p Dolby Vision HEVC clip measured,
  the main pass runs at 4.6x against 2.6x, the mean luma of both rungs agrees with the previous
  output to within one level, and each video process holds 580 to 650 MB of RAM and 350 to 390 MiB
  of GPU memory against 390 to 470 MB and 220 to 270 MiB on SDR. A stopped process keeps all of it.
- **Every audio rendition reads the source through its own demuxer.** One demuxer shared between
  outputs runs at the pace of the fastest consumer and queues packets for the slower ones in memory
  without bound — measured at 9 MB a second on a 1080p rip with four audio tracks, which exhausts
  hotbox a third of the way into a feature and gets the transcode killed. A rung is a process with
  one demuxer of its own, so it holds the same property.
- **The concurrency ceiling belongs to the card, and is measured rather than reasoned about.**
  `Handoff.MaxConcurrentIngests` is set per host in its settings file, because a GPU saturates at an aggregate rate and
  jobs past that point only divide the same throughput into thinner slices. What matters is not the
  aggregate but the per-film rate, which has to stay well ahead of one playhead.

## Dialogue boost (`Pipeline/DialogueBoost.cs`)

- **The boost is off unless asked for** (`Handoff.DialogueBoost`, `--dialogue-boost` on the CLI). It
  fills every core for longer than a room waits for a film, so the track it adds is rarely there by
  the time anybody would pick it.
- **With it on, every feature track gains a dialogue-boost mix, made after the main pass.** A film's dialogue
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
- **The boost runs at nice 19.** It fills every core for minutes on a host whose services rooms are
  using — the API, the bot, speech and the model — and nobody waits on it, since the film is already
  playable. `nice` ranks it against those services because all of them sit in hotbox's
  `system.slice`, which does not split CPU between its units: setting `CPUWeight` on any unit there
  gives each its own share, and from then on a niced thread yields only to its own unit's threads.

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
  process is stopped and resumed when it opens. Every process of the main pass has a guard of its own,
  because each reads the source through descriptors of its own; the audio process has one descriptor
  per rendition and the furthest is its position.
- **The guard fails closed.** If it cannot find the descriptor, it refuses rather than running on: a
  position it cannot read pauses, and one it never found at all aborts the transcode. Running unguarded
  produces corruption that looks like success, which is worse than a failure that says so. A question
  the source cannot answer, such as the torrent client refusing a connection, pauses the reader and is
  asked again on the next tick; it is not a verdict on the file, so it never aborts the transcode.
- **A guard that cannot do its job kills its process, and with it the pass.** It is awaited after the
  process so its message replaces ffmpeg's, which would only report that it was killed, and the
  other units are ended like any failed unit's siblings.
- **Subtitles wait for the whole file.** They have to be demuxed from a complete one, so where a
  source is still arriving they are extracted after the main pass, and a track is advertised in the
  manifest only once its own file is whole. A `.vtt` that is still being written is never served.

## Intro and recap markers (`Markers/`)

Measured on three real episodes of one show against their container chapters; the hand-off decides
when it runs and where fingerprints are kept (`src/MovieBot.Handoff/CLAUDE.md`).

- **A chapter that names the stretch is exact and is believed whole or not at all.** The whole
  normalised title has to be a name for a title sequence (`Intro`, `Opening Credits`, `Main Title`,
  `OP1`) or a recap (`Recap`, `Previously On ...`); "Opening Heist" and "Chapter 3" are acts. A chapter
  runs to the next chapter's start, and one longer than 150 s as an intro, 300 s as a recap, or shorter
  than 5 s, is not one.
- **Everything else is chromaprint run on the transcoded audio**, `fpcalc -raw -length 0` over the
  first rendition's playlist: one 32-bit value per 4096/3 samples at 11025 Hz, about 0.124 s, each
  describing about 1.9 s of sound. It reads the HLS playlist directly, so nothing is decoded twice and
  the source need not exist. A whole episode takes seconds.
- **Two fingerprints are aligned by voting, then verified bit by bit** (`RunFinder`). Each half of
  every value votes for the offset between the two fingerprints; the six best offsets are walked, and
  a run is a stretch whose values differ in at most 8 bits, bridging gaps of up to four values, with a
  mean under 6.5 bits. Unrelated audio differs in about 16. Values that occur more than six times in a
  fingerprint are ignored, because silence and a held tone produce one value hundreds of times and
  would vote for every offset; a run that is mostly one value is rejected for the same reason.
- **The title sequence is the longest stretch the openings of two episodes share**, an opening being
  the first quarter of an episode capped at ten minutes, and it counts only between 15 s and 120 s.
  Longer is a replayed scene and shorter is a sting. A longest stretch outside the range is no answer;
  the next longest is not tried. The cold open's length varies, so nothing is assumed about where it is.
- **The audio's edges are the titles' edges; a chapter can be earlier.** Measured by comparing the
  frames of two episodes at the aligned offset, the title sequence of the episodes tested runs on the
  same frames, 0.0 to 0.3 mean difference on a 64x36 grey frame, until the show's name card ends
  and the next scene's differ by about 90. The sound ends within a second of that frame. The
  container's `Intro` chapter ends eleven seconds before it, before the writing credits and the name
  card, so a chapter marks where the chapter is and not always where the titles end.
- **A run's edges are placed from its first and last matching value.** A value describes about
  1.9 s and still matches while about half of it is shared, so the run begins 0.9 s before the shared
  sound and its last value starts 0.9 s before the sound ends (`Fpcalc.EdgeSeconds`, measured against
  those frames). The start is placed 0.9 s after the first value; the end 0.5 s after the last, early
  on purpose, since a skip that leaves a moment of the titles costs nothing and one that lands late
  takes the scene.
- **Speech inside a title sequence cuts it short (`IntroGuard`).** A sequence has music and nobody
  talking, so a spoken cue that starts inside a marker, whether a chapter named it or the audio found
  it, ends the marker 0.5 s before the cue; with under 15 s left the marker is dropped. Cues are the
  episode's first available English text track that is not forced, otherwise the first available;
  a cue made only of music marks (a line with `♪`, `♫`, `♬` or a leading `#`), or wholly of bracketed
  or parenthesised sound descriptions, is not speech. An episode with no readable subtitles keeps its
  markers as found. Cues only ever shorten an intro and never set where it ends: the scene after the
  titles can be silent for twenty seconds (the first line after the titles of the episodes measured is
  23 s past them), and a skip to the line would take it. A recap is not guarded, since a recap is
  speech from earlier scenes. No cue falls inside the title sequences of the three episodes measured,
  so the guard leaves them as the audio placed them.
- **A recap is found by what it is made of.** The opening of an episode, up to the title sequence or
  the first four minutes, is compared with the *bodies* of the episodes before it, nearest first. A
  clip is a shared run of at least 2.5 s; clips from several earlier episodes are merged. It is a
  recap only when there are at least two clips adding to at least 15 s, the first starts within 45 s,
  and the clips cover at least 40% of the span from the first to the last. The earlier episodes'
  openings are never searched, because their own title sequence is there. The theme is played again
  in the bodies of the episodes measured, so an episode's own title sequence matches them too, which
  is why the sequence, once placed, ends the search. A recap whose
  first clip is within 10 s of the start begins at 0, one that ends within 10 s of the title sequence
  runs up to it.
- **A false recap skips story and a missing one skips nothing**, so every threshold above leans toward
  saying nothing, and the recap is searched only after the title sequence is placed so that the
  sequence bounds it. Nothing is a recap that the earlier episodes do not contain.
