/**
 * Per-viewer preferences.
 *
 * Volume, subtitle choice and audio track are this browser's alone and never reach the hub:
 * shared volume is a way for one person to deafen everyone else, and one person needing
 * subtitles should not put them on five other screens. Track choices are held per title
 * because a track id only means anything inside the film it came from.
 */
const Key = 'moviebot.prefs.v1';

interface TitlePrefs {
  audioTrackId?: string;
  subtitleTrackId?: string | null;
}

interface Prefs {
  volume: number;
  sideOpen: boolean;
  muted: boolean;
  byTitle: Record<string, TitlePrefs>;
}

// Half volume to start. A film mastered for a cinema is punishing at full on laptop speakers,
// and the first thing a new viewer would otherwise do is scramble for the control. Anyone who
// has already set their own volume keeps it: this is only the starting point.
const fallback: Prefs = { volume: 0.5, muted: false, sideOpen: true, byTitle: {} };

function read(): Prefs {
  try {
    const raw = window.localStorage.getItem(Key);
    if (!raw) return { ...fallback, byTitle: {} };
    const parsed = JSON.parse(raw) as Partial<Prefs>;
    return {
      volume: typeof parsed.volume === 'number' ? parsed.volume : fallback.volume,
      sideOpen: typeof parsed.sideOpen === 'boolean' ? parsed.sideOpen : fallback.sideOpen,
      muted: parsed.muted === true,
      byTitle: parsed.byTitle ?? {}
    };
  } catch {
    return { ...fallback, byTitle: {} };
  }
}

function write(prefs: Prefs): void {
  try {
    window.localStorage.setItem(Key, JSON.stringify(prefs));
  } catch {
    // A viewer with storage denied still gets a working player, just no memory between visits.
  }
}

export const prefs = {
  volume: () => read().volume,
  sideOpen: () => read().sideOpen,
  setSideOpen(open: boolean): void {
    const next = read();
    next.sideOpen = open;
    write(next);
  },
  muted: () => read().muted,

  setVolume(volume: number, muted: boolean): void {
    const next = read();
    next.volume = volume;
    next.muted = muted;
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
