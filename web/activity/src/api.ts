import { environment } from './environment';
import type { Manifest, Participant, SubtitlePin, SubtitleSearch, SubtitleTrack, TitleSummary } from './types';

async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(environment().apiUrl(path), {
    headers: { accept: 'application/json', ...authHeaders() }
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
      ...authHeaders()
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

/** Every closed route wants the proof the Activity was given. */
function authHeaders(): Record<string, string> {
  const token = environment().authToken();
  return token === null ? {} : { authorization: `Bearer ${token}` };
}

export const api = {
  titles: () => getJson<TitleSummary[]>('/api/titles'),
  manifest: (id: string) => getJson<Manifest>(`/api/titles/${encodeURIComponent(id)}`),
  participants: (sessionId: string) =>
    getJson<Participant[]>(`/api/sessions/${encodeURIComponent(sessionId)}/participants`),

  /** Costs no allowance, so it can be called whenever a menu opens. */
  searchSubtitles: (id: string, language = 'en') =>
    getJson<SubtitleSearch>(
      `/api/titles/${encodeURIComponent(id)}/subtitles/search?language=${encodeURIComponent(language)}`),

  /** This is the call that spends one of the day's downloads, and it adds it for the whole room. */
  addSubtitle: (id: string, fileId: number, addedBy: string | null) =>
    sendJson<{ track: SubtitleTrack; remainingDownloads?: number; alreadyPinned?: boolean }>(
      `/api/titles/${encodeURIComponent(id)}/subtitles`, 'POST', { fileId, addedBy }),

  pinSubtitle: (id: string, trackId: string, pinnedBy: string | null, positionSeconds: number) =>
    sendJson<SubtitlePin>(
      `/api/titles/${encodeURIComponent(id)}/subtitles/${encodeURIComponent(trackId)}/pin`,
      'POST', { pinnedBy, positionSeconds }),

  unpinSubtitle: (id: string, trackId: string) =>
    sendJson<void>(
      `/api/titles/${encodeURIComponent(id)}/subtitles/${encodeURIComponent(trackId)}/pin`, 'DELETE')
};
