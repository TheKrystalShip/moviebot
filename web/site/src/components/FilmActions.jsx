import React from "react";
import { Button, toast } from "@thekrystalship/krystal-ui";
import { api } from "../api.js";
import { useCan } from "../session.js";
import { wishWords } from "../pages/Discover.jsx";
import { defaultOffer, watchNow } from "../watchNow.js";
import { SaveButton } from "./SaveButton.jsx";

/**
 * What can be done with a film that has to be fetched, from what its availability says: Watch when it
 * can be fetched or is already being prepared, with how far the preparing has got, Notify when it is
 * not out digitally yet, and Save. `state` is the `useLoad` of `api.availability`, which the caller
 * asks for and can reload; `film` is `{title, year}`.
 */
export function FilmActions({ imdbId, film, state }) {
  const downloading = useCan("cinema:download");
  const wishing = useCan("cinema:wish");
  const [busy, setBusy] = React.useState(false);

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

  const wait = async () => {
    setBusy(true);
    try {
      const reply = await api.wish(imdbId);
      toast.info(wishWords(reply, name));
      state.reload();
    } catch (error) {
      toast.fromError(error, "Could not wait on it");
    } finally {
      setBusy(false);
    }
  };
  const stopWaiting = async () => {
    setBusy(true);
    try {
      await api.cancelWish(imdbId);
      toast.info(`Stopped notifying you about ${name}.`);
      state.reload();
    } catch (error) {
      toast.fromError(error, "Could not stop waiting");
    } finally {
      setBusy(false);
    }
  };

  const others = a ? a.waiters - (a.waiting ? 1 : 0) : 0;
  const waitingWords = a && (a.waiting
    ? (others ? `You + ${others} ${others === 1 ? "other" : "others"} waiting` : "You're waiting")
    : (others ? `${others} waiting` : "Nobody's waiting yet"));

  return (
    <div className="kc-actions">
      {!a && state.loading && <span className="kc-quiet">Asking the tracker…</span>}
      {a && a.trackerUnanswered && <span className="kc-note">The tracker is not answering, so whether this can be fetched is not known. Try again in a minute.</span>}
      {a && a.notOffered && (
        <span className="kc-note">The tracker has it, but nothing this server takes. {a.emptyExplanation}</span>
      )}

      {a && downloading && (arriving || offer) && (
        <Button variant="primary" size="xl" icon="play" className={full && !arriving ? "is-dim" : undefined}
          disabled={busy} onClick={watch}>
          Watch
        </Button>
      )}
      {arriving && <span className="kc-pill kc-pill--info">preparing · {Math.floor(arriving.progress * 100)}%</span>}

      {a && wishing && a.notOut && (a.waiting
        ? <Button variant="accent" size="xl" icon="bell-ring" disabled={busy} onClick={stopWaiting}>Notifying</Button>
        : <Button variant="primary" size="xl" icon="bell-plus" disabled={busy} onClick={wait}>Notify</Button>)}
      <SaveButton imdbId={imdbId} kind="film" title={film.title} />
      {a && a.notOut && <span className="kc-note">{waitingWords}</span>}
    </div>
  );
}
