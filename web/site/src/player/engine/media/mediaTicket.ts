import Hls from 'hls.js';
import { api } from '../api';
import { environment } from '../environment';

interface HeldTicket {
  ticket: string;
  /**
   * When the server stops honouring it, in this device's clock: the reply's `expiresIn` counted
   * from its arrival, since a device's clock can be hours off the server's.
   */
  expiresAt: number;
}

/** The query parameter a media request carries it on. */
const QueryName = 'mt';

/**
 * What this viewer carries on a request for a film's bytes.
 *
 * The library and the rooms are opened by the viewer's access token, which says who somebody is. A segment is
 * opened by a ticket, which says only which film and until when. That is what makes a film
 * cacheable: a shared cache will not answer a request carrying an `Authorization` header.
 *
 * Every viewer of a title is handed the same string, so their URLs match and one cached copy
 * serves the room. A ticket is held until shortly before it expires and renewed then, so a page
 * left open for days, as a TV keeps it, never offers the server one it has stopped honouring.
 */
const tickets = new Map<string, HeldTicket>();
const renewing = new Map<string, Promise<void>>();

/**
 * How long before its expiry a ticket is renewed. A ticket lives for hours, so renewing early
 * costs one request, and a request already on its way when the ticket lapses is never refused.
 */
const RenewAheadMs = 60 * 60 * 1000;

/** The ticket held for a title while the server still honours it. */
function usable(titleId: string): string | undefined {
  const held = tickets.get(titleId);
  return held !== undefined && held.expiresAt > Date.now() ? held.ticket : undefined;
}

/** Whether the title's ticket has a while left to run, so a request can go out with it now. */
function fresh(titleId: string): boolean {
  const held = tickets.get(titleId);
  return held !== undefined && held.expiresAt - Date.now() > RenewAheadMs;
}

/**
 * Fetches the ticket for a title, unless one with a while left to run is already held. Callers
 * asking at once share one request. Failure is not fatal: the Authorization header opens
 * everything a ticket does, at the cost of being cacheable.
 */
export function ensureTicket(titleId: string): Promise<void> {
  if (fresh(titleId)) return Promise.resolve();

  let pending = renewing.get(titleId);
  if (pending === undefined) {
    pending = fetchTicket(titleId).finally(() => renewing.delete(titleId));
    renewing.set(titleId, pending);
  }
  return pending;
}

async function fetchTicket(titleId: string): Promise<void> {
  try {
    const reply = await api.mediaTicket(titleId);
    if (reply.ticket && Number.isFinite(reply.expiresIn)) {
      tickets.set(titleId, { ticket: reply.ticket, expiresAt: Date.now() + reply.expiresIn * 1000 });
    }
  } catch {
    // The ticket held stays in use until it expires. Without one, requests fall back to the header.
  }
}

/** Drops the title's ticket, so the next request for it fetches another. */
export function forget(titleId: string): void {
  tickets.delete(titleId);
}

/** Whether a media URL carries a ticket, and so must not also carry the Authorization header. */
export function carriesTicket(url: string): boolean {
  return new URL(url, window.location.href).searchParams.has(QueryName);
}

type Loader = typeof Hls.DefaultConfig.loader;
type LoadArguments = Parameters<InstanceType<Loader>['load']>;

/**
 * The hls.js loader that puts the ticket on every request it makes.
 *
 * It belongs in the loader rather than on the URL handed to `loadSource`: hls.js resolves a
 * segment URI against the playlist that listed it, and resolving a relative URI drops the query
 * string, so a ticket on the playlist reaches nothing underneath it.
 *
 * A ticket close to expiry is renewed before the request goes out. A request the server refuses
 * with 401 despite its ticket (a key the server replaced, a device asleep past the expiry) is sent
 * once more on a newly fetched ticket: hls.js never retries a 4xx itself, and a refused playlist
 * would leave the film waiting for bytes that never come.
 */
export function ticketedLoader(titleId: string): Loader {
  const Base = Hls.DefaultConfig.loader;

  return class TicketedLoader extends Base {
    private readonly hlsConfig: ConstructorParameters<Loader>[0];
    /** The loader sending the request again, which an hls.js loader cannot do twice itself. */
    private again: InstanceType<Loader> | null = null;
    private cancelled = false;

    constructor(config: ConstructorParameters<Loader>[0]) {
      super(config);
      this.hlsConfig = config;
    }

    override load(context: LoadArguments[0], config: LoadArguments[1], callbacks: LoadArguments[2]): void {
      const url = context.url;
      const onError: LoadArguments[2]['onError'] = (response, failed, details, stats) => {
        if (response.code !== 401 || !carriesTicket(failed.url) || this.cancelled) {
          callbacks.onError(response, failed, details, stats);
          return;
        }
        forget(titleId);
        void ensureTicket(titleId).then(() => {
          if (this.cancelled) return;
          this.again = new Base(this.hlsConfig);
          context.url = withTicket(url, titleId);
          this.again.load(context, config, callbacks);
        });
      };
      const send = () => {
        if (this.cancelled) return;
        context.url = withTicket(url, titleId);
        super.load(context, config, { ...callbacks, onError });
      };

      if (fresh(titleId) || environment().solo) send();
      else void ensureTicket(titleId).then(send);
    }

    override abort(): void {
      this.cancelled = true;
      if (this.again !== null) this.again.abort();
      else super.abort();
    }

    override destroy(): void {
      this.cancelled = true;
      this.again?.destroy();
      this.again = null;
      super.destroy();
    }
  };
}

const TicketParameter = new RegExp(`([?&])${QueryName}=[^&#]*&?`);

/**
 * Adds the ticket to a media URL for a title that has one.
 *
 * Left alone otherwise, including for a `blob:` URL, which is the composed master playlist and
 * is never fetched over the network.
 */
export function withTicket(url: string, titleId: string): string {
  if (url.startsWith('blob:')) return url;

  // A URL an earlier attempt already ticketed loses that ticket, so a request never carries two.
  const bare = url.replace(TicketParameter, '$1').replace(/[?&]$/, '');
  const ticket = usable(titleId);
  if (ticket === undefined) return bare;

  return bare + (bare.includes('?') ? '&' : '?') + `${QueryName}=${encodeURIComponent(ticket)}`;
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
