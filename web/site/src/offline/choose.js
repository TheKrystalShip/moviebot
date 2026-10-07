// What Download picks before the person changes anything, and what this device is called on the
// API's record of a save.
//
// The rung: the person's own Download quality where they set one (`offlineQualityHeight`), else
// the smallest on a phone or a tablet, which have the least room and the smallest screens, and the
// step-down anywhere else, a laptop's screen showing little of what the top rung adds over it. Audio:
// the person's language where the title carries it, else the title's own default. Subtitles: every
// text subtitle in the person's subtitle language, and the forced ones in each picked audio language,
// which carry the lines a film leaves untranslated.

const DEVICE_KEY = "cinema.offline.device";

/**
 * A language's primary code in its canonical spelling: a title's tracks carry the three-letter codes
 * its container wrote (`eng`, `ger`) and a person's preference the two-letter ones.
 */
function primaryLanguage(code) {
  try {
    return (Intl.getCanonicalLocales(code)[0] || code).split("-")[0].toLowerCase();
  } catch {
    return String(code).split("-")[0].toLowerCase();
  }
}

/** Whether a track's language is the one asked for: `eng` is `en`, and `es` matches `es-419`. */
export function sameLanguage(track, wanted) {
  return !!track && !!wanted && primaryLanguage(track) === primaryLanguage(wanted);
}

/** Whether this is a phone or a tablet: a touch screen and no mouse. */
export function handheld() {
  if (typeof window === "undefined" || !window.matchMedia) return false;
  return window.matchMedia("(pointer: coarse)").matches && !window.matchMedia("(any-pointer: fine)").matches;
}

/**
 * The width a quality height stands for at 16:9, which is how a rung is matched to it: a scope film's
 * 1080p is 1920 wide and 804 tall, and a 4:3 film's 720p is 960 tall, so a height alone would put
 * both on the wrong side of the line.
 */
const widthOf = (height) => Math.round((height * 16) / 9);

/** The rung a quality height picks from `rungs` (tallest first): the widest not wider than it, else the smallest. */
export function rungFor(rungs, height) {
  if (!rungs.length) return null;
  const fits = rungs.filter((r) => r.width <= widthOf(height) + 8);
  return fits.length ? fits[0] : rungs[rungs.length - 1];
}

/** The rung picked by default from an offer's `rungs` (tallest first). */
export function defaultRung(rungs, preferences) {
  if (!rungs.length) return null;
  const height = preferences && preferences.offlineQualityHeight;
  if (height) return rungFor(rungs, height);
  if (handheld()) return rungs[rungs.length - 1];
  return rungs[Math.min(1, rungs.length - 1)];
}

/** The audio picked by default: the person's language, else the title's default, else its first track. */
export function defaultAudio(audio, preferences) {
  const wanted = preferences && preferences.audioLanguage;
  const pick = (wanted && audio.find((a) => a.kind === "feature" && sameLanguage(a.language, wanted)))
    || audio.find((a) => a.default) || audio[0];
  return pick ? [pick.rendition] : [];
}

/** The subtitles picked by default for the audio picked. */
export function defaultSubtitles(subtitles, audioPicked, preferences) {
  const wanted = preferences && preferences.subtitleLanguage;
  return subtitles
    .filter((s) => (wanted && sameLanguage(s.language, wanted))
      || (s.forced && audioPicked.some((a) => sameLanguage(s.language, a.language))))
    .map((s) => s.id);
}

/**
 * What this device is called on the API's records: the browser and the system it runs on, and a mark
 * of this browser's own, so two phones of one kind are two devices. Saving a title again from the same
 * device replaces that device's save, so the name has to stay the same from one save to the next.
 */
export function deviceName() {
  let mark = null;
  try {
    mark = localStorage.getItem(DEVICE_KEY);
    if (!mark) {
      mark = crypto.randomUUID().slice(0, 4);
      localStorage.setItem(DEVICE_KEY, mark);
    }
  } catch {
    mark = mark || "here";
  }
  return `${browserName()} on ${systemName()} (${mark})`;
}

function browserName() {
  const ua = navigator.userAgent;
  if (/Edg\//.test(ua)) return "Edge";
  if (/Firefox\//.test(ua)) return "Firefox";
  if (/SamsungBrowser\//.test(ua)) return "Samsung Internet";
  if (/Chrome\/|CriOS\//.test(ua)) return "Chrome";
  if (/Safari\//.test(ua)) return "Safari";
  return "A browser";
}

function systemName() {
  const ua = navigator.userAgent;
  if (/iPhone/.test(ua)) return "iPhone";
  if (/iPad/.test(ua) || (/Macintosh/.test(ua) && navigator.maxTouchPoints > 1)) return "iPad";
  if (/Android/.test(ua)) return "Android";
  if (/Macintosh/.test(ua)) return "Mac";
  if (/Windows/.test(ua)) return "Windows";
  if (/CrOS/.test(ua)) return "ChromeOS";
  if (/Linux/.test(ua)) return "Linux";
  return "a device";
}

/**
 * Whether saved films here are at the browser's mercy after a week unvisited: Safari on an iPhone or
 * an iPad, in a tab rather than from the Home Screen.
 */
export function evictedUnvisited() {
  const ua = navigator.userAgent;
  const apple = /iPhone|iPad/.test(ua) || (/Macintosh/.test(ua) && navigator.maxTouchPoints > 1);
  const installed = (window.matchMedia && window.matchMedia("(display-mode: standalone)").matches) || navigator.standalone === true;
  return apple && !installed;
}
