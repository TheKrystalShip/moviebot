/**
 * What this browser remembers for its viewer.
 *
 * Volume, brightness and the track picked for each film are this device's and never reach the hub:
 * shared volume is a way for one person to deafen everyone else, and one person needing subtitles
 * should not put them on five other screens. Volume and brightness are held here because they are
 * about the speakers and the screen in front of the person; a track choice is held per title because a track id only means anything inside the
 * film it came from. The person's defaults — languages and quality — are the API's to keep, so they
 * follow them from the site into the Activity.
 */
const Key = 'moviebot.prefs.v1';

interface TitlePrefs {
  audioTrackId?: string;
  subtitleTrackId?: string | null;
}

interface Prefs {
  volume: number;
  muted: boolean;
  /** How bright the picture is drawn, from 1 (as the film was made) down to `MinBrightness`. */
  brightness: number;
  byTitle: Record<string, TitlePrefs>;
}

function empty(): Prefs {
  // Half volume to start. A film mastered for a cinema is punishing at full on laptop speakers,
  // and the first thing a new viewer would otherwise do is scramble for the control. Anyone who
  // has already set their own volume keeps it: this is only the starting point.
  return { volume: 0.5, muted: false, brightness: 1, byTitle: {} };
}

function read(): Prefs {
  try {
    const raw = window.localStorage.getItem(Key);
    if (!raw) return empty();
    const parsed = JSON.parse(raw) as Partial<Prefs>;
    return {
      volume: typeof parsed.volume === 'number' ? parsed.volume : empty().volume,
      muted: parsed.muted === true,
      brightness: typeof parsed.brightness === 'number' ? clampBrightness(parsed.brightness) : 1,
      byTitle: parsed.byTitle ?? {}
    };
  } catch {
    return empty();
  }
}

function write(prefs: Prefs): void {
  try {
    window.localStorage.setItem(Key, JSON.stringify(prefs));
  } catch {
    // A viewer with storage denied still gets a working player, just no memory between visits.
  }
}

/** The darkest the picture is drawn: dim enough for a dark room, never so dark it is gone. */
export const MinBrightness = 0.2;

export const clampBrightness = (value: number): number => Math.min(1, Math.max(MinBrightness, value));

export const prefs = {
  volume: () => read().volume,
  muted: () => read().muted,

  setVolume(volume: number, muted: boolean): void {
    const next = read();
    next.volume = volume;
    next.muted = muted;
    write(next);
  },

  brightness: () => read().brightness,

  setBrightness(brightness: number): void {
    const next = read();
    next.brightness = clampBrightness(brightness);
    write(next);
  },

  forTitle: (titleId: string): TitlePrefs => read().byTitle[titleId] ?? {},

  setAudioTrack(titleId: string, audioTrackId: string): void {
    const next = read();
    next.byTitle[titleId] = { ...next.byTitle[titleId], audioTrackId };
    write(next);
  },

  setSubtitleTrack(titleId: string, subtitleTrackId: string | null): void {
    const next = read();
    next.byTitle[titleId] = { ...next.byTitle[titleId], subtitleTrackId };
    write(next);
  }
};
