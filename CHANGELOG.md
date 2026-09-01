# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0] - 2026-09-01

### Added

- `MovieBot.Api`: the library, media and session surface. Serves manifests and HLS with the
  content types hls.js requires, holds shared session state, and pushes it over SignalR.
- Server-authoritative sessions with monotonic revisions and a server-clock timestamp on every
  push, so reordered or duplicated pushes and skewed client clocks are all harmless.
- Head-aware seek clamping. A seek past what has been transcoded is granted short of the head
  and answered with `SeekClamped` to the caller alone.
- `no-store` on playlists for a title still transcoding; segments served immutable.
- `MovieBot.Tests`: two real SignalR clients against the app in-process, covering propagation,
  revision ordering, clamping, participant broadcast and position derivation.

## [0.1.0] - 2026-09-01

### Added

- `MovieBot.Core`: the manifest contract — title, duration, transcode status and head, video
  rendition, audio tracks and subtitle tracks — with a source-generated camelCase serializer and
  an atomic writer, so a reader polling during a transcode never sees a half-written document.
- `MovieBot.Ingest`: the ingest CLI. Probes a source with ffprobe, extracts every text subtitle
  track to WebVTT and the container's cover art, then transcodes to HLS with `h264_nvenc`,
  writing `EVENT` playlists so the film is playable within seconds of the transcode starting.
- NVDEC decoding via `-hwaccel cuda`. Software-decoding a 16 Mbps HEVC Main 10 source costs
  35 s of CPU per 40 s of film against 5.5 s on the GPU, and saturates every core for the
  length of a feature while the GPU sits half idle.
- HDR detection from the source transfer function, with `tonemap_opencl` converting PQ and HLG
  sources to BT.709 and SDR sources left untouched. `--tonemap` overrides the detection.
- Subtitle classification into text and bitmap codecs. Bitmap tracks are listed as unavailable
  with a reason rather than dropped.
- Track labelling from the container's title tags, with commentary split out by ffmpeg's
  `comment` disposition.
- `--dry-run`, which prints the manifest a source would produce without transcoding it.

[0.2.0]: https://github.com/TheKrystalShip/MovieBot/releases/tag/v0.2.0
[0.1.0]: https://github.com/TheKrystalShip/MovieBot/releases/tag/v0.1.0
