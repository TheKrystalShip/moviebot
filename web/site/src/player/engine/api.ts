import { environment } from './environment';
import type {
  GuestLink, Manifest, Participant, Person, RoomAround, ShowSeasons, SubtitlePin, SubtitleSearch, SubtitleTrack, TitleSummary,
  ViewerPreferences
} from './types';

async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(environment().apiUrl(path), {
    headers: { accept: 'application/json', ...(await authHeaders()) }
  });
  if (!response.ok) throw new Error(`${path} answered ${response.status}`);
  return (await response.json()) as T;
}

async function sendJson<T>(path: string, method: string, body?: unknown): Promise<T> {
  const response = await fetch(environment().apiUrl(path), {
    method,
    headers: {
      accept: 'application/json',
      ...(body === undefined ? {} : { 'content-type': 'application/json' }),
      ...(await authHeaders())
    },
    body: body === undefined ? undefined : JSON.stringify(body)
  });

  // The subtitle index refuses a spent allowance with a 429 and says when it comes back, which is
  // a useful answer where "it failed" is not. Carry the message rather than the status.
  if (!response.ok) {
    const said = await response.json().catch(() => null);
    throw new Error(said?.error ?? `${path} answered ${response.status}`);
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

/** Every closed route wants the viewer's access token, renewed first when it is about to run out. */
async function authHeaders(): Promise<Record<string, string>> {
  const token = await environment().freshToken();
  return token === null ? {} : { authorization: `Bearer ${token}` };
}

export const api = {
  titles: () => getJson<TitleSummary[]>('/api/titles'),
  manifest: (id: string) => getJson<Manifest>(`/api/titles/${encodeURIComponent(id)}`),

  /** The episode a room holds and the ones either side, which previous and next name. */
  around: (sessionId: string) =>
    getJson<RoomAround>(`/api/sessions/${encodeURIComponent(sessionId)}/around`),

  /** A show's seasons and their episodes, each joined with the library: what the episode menu lists. */
  show: (imdbId: string) => getJson<ShowSeasons>(`/api/shows/${encodeURIComponent(imdbId)}`),

  /** What a request for this film's bytes carries instead of who is asking. */
  mediaTicket: (id: string) =>
    getJson<{ ticket: string; expiresAt: string }>(`/api/titles/${encodeURIComponent(id)}/ticket`),

  participants: (sessionId: string) =>
    getJson<Participant[]>(`/api/sessions/${encodeURIComponent(sessionId)}/participants`),

  /**
   * Costs no allowance, so it can be called whenever a menu opens. The index takes several
   * languages at once, comma-separated, lower-case and sorted, and answers a query in any other
   * spelling with a redirect.
   */
  searchSubtitles: (id: string, languages: string[]) =>
    getJson<SubtitleSearch>(
      `/api/titles/${encodeURIComponent(id)}/subtitles/search?language=${encodeURIComponent(
        [...new Set(languages.map((l) => l.toLowerCase()))].sort().join(','))}`),

  /** Everybody who can be asked into a room, most recently seen first, without the person asking. */
  people: () => getJson<Person[]>('/api/people'),

  /** Asks one person into a room: a push to their devices, and their notifications on the site either way. */
  invite: (sessionId: string, userId: string) =>
    sendJson<{ delivery: 'push' | 'bell' }>(`/api/rooms/${encodeURIComponent(sessionId)}/invite`, 'POST', { userId }),

  /** The room's guest link: the one it has, or a new one. Everybody in the room is handed the same. */
  guestLink: (sessionId: string) =>
    sendJson<GuestLink>(`/api/rooms/${encodeURIComponent(sessionId)}/guests`, 'POST'),

  /** Takes the room's guest link away, which sends away every guest who came in on it. */
  endGuests: (sessionId: string) =>
    sendJson<void>(`/api/rooms/${encodeURIComponent(sessionId)}/guests`, 'DELETE'),

  /** How the signed-in person watches, wherever they signed in. */
  preferences: () => getJson<ViewerPreferences>('/api/me/preferences'),
  setPreferences: (preferences: ViewerPreferences) =>
    sendJson<ViewerPreferences>('/api/me/preferences', 'PUT', preferences),

  /**
   * This is the call that spends one of the day's downloads, and it adds it for the whole room. Who
   * added it is whoever the token names.
   */
  addSubtitle: (id: string, fileId: number) =>
    sendJson<{ track: SubtitleTrack; remainingDownloads?: number; alreadyPinned?: boolean }>(
      `/api/titles/${encodeURIComponent(id)}/subtitles`, 'POST', { fileId }),

  pinSubtitle: (id: string, trackId: string, positionSeconds: number) =>
    sendJson<SubtitlePin>(
      `/api/titles/${encodeURIComponent(id)}/subtitles/${encodeURIComponent(trackId)}/pin`,
      'POST', { positionSeconds }),

  unpinSubtitle: (id: string, trackId: string) =>
    sendJson<void>(
      `/api/titles/${encodeURIComponent(id)}/subtitles/${encodeURIComponent(trackId)}/pin`, 'DELETE')
};
