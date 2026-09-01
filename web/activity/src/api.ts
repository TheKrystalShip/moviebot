import { environment } from './environment';
import type { Manifest, Participant, TitleSummary } from './types';

async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(environment().apiUrl(path), { headers: { accept: 'application/json' } });
  if (!response.ok) throw new Error(`${path} answered ${response.status}`);
  return (await response.json()) as T;
}

export const api = {
  titles: () => getJson<TitleSummary[]>('/api/titles'),
  manifest: (id: string) => getJson<Manifest>(`/api/titles/${encodeURIComponent(id)}`),
  participants: (sessionId: string) =>
    getJson<Participant[]>(`/api/sessions/${encodeURIComponent(sessionId)}/participants`)
};
