# Cinema for Android phones

Cinema's site as an Android app, for one thing a browser cannot do: download films that keep
downloading with the screen off and the app out of view. It is the TKS shell built as Cinema. What the
shell does and how an app is built on it is the shell's `README.md`, vendored here as `shell/`
(`shell/VENDORED` names its version); how a site becomes an app on it, and the bridge the site talks
to, are `kgsm-docs`' `systems/shell/README.md` and `reference/shell-bridge.md`.

## What is Cinema's

- **`shell.conf`**: the app id every install was made under, which never changes; the mark
  `KrystalCinemaApp`, by which the site names the device "Cinema on Android" on the API's
  records; the site; the artifact name (`krystal-cinema-phone`, also the key directory); and the
  `downloads` feature.
- **`res/`**: the name, and the site's icon, Lucide's clapperboard on the PWA icon's ground
  (`web/site/public/icons/icon.svg`), as an adaptive vector icon with a themed variant. `debug/res/`
  names the debug build.
- **`CHANGELOG.md`**: the app's version, a clock of its own tagged `phone-v<version>`.
- **Downloads are saves.** The site hands a save's tracks to the shell as one download, its id the
  save's, the save's grant (`docs/api-contract.md`, Save offline) as the header, and the save's
  `complete` route as `done`, and plays them back from the shell. The site's half is
  `web/site/src/offline/shell.js` (`web/site/src/offline/CLAUDE.md`).

## Building

```bash
phone/build.sh                                     # release, for https://movies.thekrystalship.com
phone/build.sh --origin https://movies.example.com # release, for another host
phone/build.sh --debug --install --open            # debug build, installed and opened on the phone adb reaches
```

`build.sh` hands this directory to the shell's build (`shell/build.sh`), which says what each option
does and what the toolchain is. The APK lands in `phone/out/`. A release is signed with the key in
`~/.config/krystal-cinema-phone/`, which is never in a repository and has to be kept: Android installs
an update only under the key the app was installed with. The debug build is
`com.thekrystalship.cinema.phone.debug`, installed beside a release one; its downloads are its own
files (`files/downloads/`), read with `adb shell run-as com.thekrystalship.cinema.phone.debug`.

The shell is carried into this directory from `tks-shell` with its `vendor.sh`, and never edited here.

## Getting it onto a phone

- **From this machine**: with USB debugging allowed, `phone/build.sh --install`.
- **Anybody else**: the site serves the build made for it at `/phone.apk` (`docs/api-contract.md`), and
  its footer offers it on an Android phone's browser. Opening that address on the phone downloads it;
  installing asks once to allow the browser to install apps. A phone does not update an app installed
  this way; fetching the same address again does.
