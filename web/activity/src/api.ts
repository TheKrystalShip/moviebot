import { environment } from './environment';
import type { Manifest, Participant, TitleSummary } from './types';

async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(environment().apiUrl(path), {
    headers: { accept: 'application/json', ...authHeaders() }
  });
  if (!response.ok) throw new Error(`${path} answered ${response.status}`);
  return (await response.json()) as T;
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
    getJson<Participant[]>(`/api/sessions/${encodeURIComponent(sessionId)}/participants`)
};
