# Changelog

Krystal Cinema for Android phones, the app in this directory. Its version clock is its own, tagged
`phone-v<version>`; the newest heading here is the version `build.sh` builds.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.2] - 2026-10-18

### Added

- **Files, as a browser handles them** (tks-shell 1.1.0): a file the site saves lands in the phone's
  Downloads, and a file input opens the system's picker.

## [1.1.1] - 2026-10-18

### Changed

- **Built on tks-shell 1.0.0**, vendored in `shell/`: the app is its `shell.conf`, `res/` and this
  file, and `build.sh` hands them to the shell's build. Nothing a person sees changes.

## [1.1.0] - 2026-10-18

### Changed

- **The TKS shell**: what the app does natively is a shell that names no app and no site, in
  `com.thekrystalship.shell`, built as Cinema by its app id, mark, name and icon. The page finds it
  as `window.TksShell`, and hands it downloads as files with the headers they carry and where to say
  they are whole; a failure comes back as a reason (`credential`, `gone`, `http-<status>`) the site
  words. Downloads are kept under `downloads/` and served at `/__shell/downloads/`.

## [1.0.1] - 2026-10-18

### Fixed

- **The site's icon**: Lucide's clapperboard on the PWA icon's ground, as an adaptive vector icon
  with a themed variant.
- **A notification per finished download**, "Downloaded" under its name; a pause no longer says
  the downloads finished.

## [1.0.0] - 2026-10-18

### Added

- **Krystal Cinema on a phone**: the site in a WebView, with downloads the app runs itself in a
  foreground service, which carry on with the screen off and the app out of view, and the player
  reading what the app downloaded.
