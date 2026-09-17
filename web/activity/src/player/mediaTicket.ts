import Hls from 'hls.js';
import { api } from '../api';
import { environment } from '../environment';

/**
 * What this viewer carries on a request for a film's bytes.
 *
 * The library and the rooms are opened by a room token, which says who somebody is. A segment is
 * opened by a ticket, which says only which film and until when. That is what makes a film
 * cacheable: a shared cache will not answer a request carrying an `Authorization` header.
 *
 * Every viewer of a title is handed the same string, so their URLs match and one cached copy
 * serves the room. It is fetched once per title and held for as long as that film is open.
 */
const tickets = new Map<string, string>();

/** Fetches the ticket for a title, unless one is already held. Failure is not fatal: the
 *  Authorization header opens everything a ticket does, at the cost of being cacheable. */
export async function ensureTicket(titleId: string): Promise<void> {
  if (tickets.has(titleId)) return;

  try {
    const reply = await api.mediaTicket(titleId);
    if (reply.ticket) tickets.set(titleId, reply.ticket);
  } catch {
    // Left without one. Requests fall back to the header.
  }
}

export function forget(titleId: string): void {
  tickets.delete(titleId);
}

export function hasTicket(titleId: string): boolean {
  return tickets.has(titleId);
}

/**
 * The hls.js loader that puts the ticket on every request it makes.
 *
 * It belongs in the loader rather than on the URL handed to `loadSource`: hls.js resolves a
 * segment URI against the playlist that listed it, and resolving a relative URI drops the query
 * string, so a ticket on the playlist reaches nothing underneath it.
 */
export function ticketedLoader(titleId: string): typeof Hls.DefaultConfig.loader {
  const Base = Hls.DefaultConfig.loader;

  return class TicketedLoader extends Base {
    override load(
      context: Parameters<InstanceType<typeof Base>['load']>[0],
      config: Parameters<InstanceType<typeof Base>['load']>[1],
      callbacks: Parameters<InstanceType<typeof Base>['load']>[2]
    ): void {
      context.url = withTicket(context.url, titleId);
      super.load(context, config, callbacks);
    }
  };
}

/**
 * Adds the ticket to a media URL for a title that has one.
 *
 * Left alone otherwise, including for a `blob:` URL, which is the composed master playlist and
 * is never fetched over the network.
 */
export function withTicket(url: string, titleId: string): string {
  const ticket = tickets.get(titleId);
  if (ticket === undefined || url.startsWith('blob:')) return url;

  return url + (url.includes('?') ? '&' : '?') + `mt=${encodeURIComponent(ticket)}`;
}

/**
 * A media URL for a title, carrying the ticket when one is held.
 *
 * Every media fetch outside hls.js goes through here, so the preview sheet and a subtitle file
 * are cached on the same terms as the film.
 */
export function ticketedMediaUrl(titleId: string, relative: string): string {
  return withTicket(environment().mediaUrl(titleId, relative), titleId);
}
