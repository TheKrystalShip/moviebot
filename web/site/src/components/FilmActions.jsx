import React from "react";
import { Button, Pill, toast } from "@thekrystalship/krystal";
import { libraryApi } from "../api/index.js";
import { useOnline } from "../connectivity.js";
import { useLoad } from "../data.js";
import { useCan } from "../session.js";
import { clock } from "../time.js";
import { defaultOffer, watchNow } from "../watchNow.js";
import { setRemoteMode } from "../remote.js";
import { watchOnTv } from "../remoteWatch.js";
import { useDriving } from "./WatchOnTv.jsx";
import { DownloadButton, useOffersSaving } from "./DownloadButton.jsx";
import { WantButton } from "./WantButton.jsx";
import { PlanButton } from "./PlanButton.jsx";
import { WatchedButton } from "./WatchedButton.jsx";
import { Wanters } from "./Wanters.jsx";

/**
 * What can be done with a film that has to be fetched, from what its availability says: Watch when it
 * can be fetched or is already being prepared, with how far the preparing has got, Want, with the faces
 * of the others who want it, and Download onto this device when it can be watched, in the order a
 * library film's page puts them. `state` is the `useLoad` of `library.availability`, which the caller
 * asks for and can reload; `film` is the film's catalogue card. `compact` keeps to the actions that watch or wait, for a film shown among others;
 * `afterWatch` follows Watch (a page's Trailer), and `children` follow the actions on their row.
 */
export function FilmActions({ imdbId, film, state, compact = false, afterWatch = null, children }) {
  const downloading = useCan("cinema:download");
  const saving = useOffersSaving();
  const online = useOnline();
  const driving = useDriving();
  const [working, setBusy] = React.useState(false);
  const busy = working || !online;

  const a = state.data;
  const offer = defaultOffer(a);
  const full = a && a.streams.used >= a.streams.limit;
  const arriving = a && a.download;
  const name = film.year ? `${film.title} (${film.year})` : film.title;

  const watch = async () => {
    if (full && !arriving) {
      toast.info(`The server is preparing ${a.streams.limit} films for people watching right now. Try again in a few minutes.`);
      return;
    }
    setBusy(true);
    const started = await watchNow({ imdbId, torrentId: arriving ? undefined : offer && offer.torrentId }, { limit: a.streams.limit });
    if (!started) setBusy(false);
  };

  const watchOnTelevision = async () => {
    if (full && !arriving) {
      toast.info(`The server is preparing ${a.streams.limit} films for people watching right now. Try again in a few minutes.`);
      return;
    }
    setBusy(true);
    await watchOnTv({
      request: { imdbId, torrentId: arriving ? undefined : offer && offer.torrentId }, limit: a.streams.limit,
      name, meta: film.runtime ? clock(film.runtime * 60) : null, art: film.backdrop || film.poster,
    });
    setBusy(false);
  };

  return (
    <>
      <div className="kc-actions">
        {!a && state.loading && <span className="kc-quiet">Asking the tracker…</span>}
        {a && a.trackerUnanswered && <span className="kc-note">The tracker is not answering, so whether this can be fetched is not known. Try again in a minute.</span>}
        {a && a.notOffered && (
          <span className="kc-note">The tracker has it, but nothing this server takes. {a.emptyExplanation}</span>
        )}

        {a && downloading && (arriving || offer) && driving && (
          <>
            <Button variant="primary" size="xl" icon="tv" className={full && !arriving ? "kc-dim" : undefined}
              disabled={busy} onClick={watchOnTelevision}>
              Watch on TV
            </Button>
            <Button variant="secondary" size="xl" icon="smartphone" className={full && !arriving ? "kc-dim" : undefined}
              disabled={busy} onClick={() => { setRemoteMode(false); void watch(); }}>
              Watch
            </Button>
          </>
        )}
        {a && downloading && (arriving || offer) && !driving && (
          <Button variant="primary" size="xl" icon="play" className={full && !arriving ? "kc-dim" : undefined}
            disabled={busy} onClick={watch}>
            Watch
          </Button>
        )}
        {afterWatch}
        {arriving && <Pill tone="info">preparing · {Math.floor(arriving.progress * 100)}%</Pill>}

        {(!compact || (a && a.notOut)) && <WantButton imdbId={imdbId} kind="film" title={film.title} />}
        {!compact && <PlanButton imdbId={imdbId} kind="film" notOut={!!(a && a.notOut)} />}
        {!compact && film.you && <WatchedButton imdbId={imdbId} watched={film.you.watched} />}
        {!compact && a && downloading && saving && (arriving || offer) && (
          <DownloadButton imdbId={imdbId} head={{
            name: film.title, context: film.year || null, runtime: film.runtime ? clock(film.runtime * 60) : null,
            still: film.poster, portrait: true, backdrop: film.backdrop || film.poster,
          }} />
        )}
        {children}
      </div>
      {!compact && <Wanters imdbId={imdbId} />}
    </>
  );
}

/**
 * A film that has to be fetched, as its own page offers it, by its card: whether it can be is asked as
 * soon as it is shown. `compact` and `children` are FilmActions'.
 */
export function FilmMatchActions({ found, compact, children }) {
  const downloading = useCan("cinema:download");
  const wishing = useCan("cinema:wish");
  const state = useLoad(() => (downloading || wishing ? libraryApi.availability(found.id) : Promise.resolve(null)), [found.id, downloading, wishing]);
  return <FilmActions imdbId={found.id} film={found} state={state} compact={compact}>{children}</FilmActions>;
}
