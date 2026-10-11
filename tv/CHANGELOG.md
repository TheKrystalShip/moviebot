# Changelog

Krystal Cinema for Android TV and Fire TV, the app in this directory. Its version clock is its own,
tagged `tv-v<version>`; the newest heading here is the version `build.sh` builds.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.0] - 2026-10-11

### Added

- **The TV's name for the page**: `window.KrystalCinemaTV.deviceName()` answers the name the TV's
  owner set in its settings, or `null`, so the site can offer it when it signs the TV in.

## [1.0.0] - 2026-10-06

### Added

- **Krystal Cinema on a television**: the site in a full-screen WebView on its own origin, launched
  from the Android TV and Fire TV home screens, marking itself as a television so the site draws its
  TV design, and handing the page Back and Menu. The session is kept across launches. Links off the
  site are refused, and a site that cannot be reached is shown with Retry. Built with the plain SDK
  tools by `build.sh`, with a debug build for working against a scratch site.
