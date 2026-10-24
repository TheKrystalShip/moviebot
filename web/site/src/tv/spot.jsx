import React from "react";
import { Icon, TvBackdrop, TvSpotlight, createStore, useStore } from "@thekrystalship/krystal";

/** How long focus rests on a card before the spotlight turns to it, so a held arrow loads nothing. */
const SPOT_MS = 300;

/**
 * A page whose top is a spotlight on the card focused in its rows. `pageRef` goes on the page,
 * `regionRef` on what holds the rows, each row marked `data-spot-row`; `focusOn(row, spot)` goes on
 * each card, as its focus handler. While focus is in the region the store holds the index of the row
 * it is in and what the spotlight says, which follows a new card once focus has rested on it; focus
 * anywhere else puts both away at once.
 *
 * Moving focus re-renders nothing of the page: the spotlight reads the store (`useSpot`), and the page
 * is marked `data-spot` (and `data-tv-dim`, which steps the other cards back) and each row above the focused one `data-past` directly, since a page of a
 * hundred cards drawn again on every press is a dropped frame on a television.
 */
export function useSpotlight() {
  const pageRef = React.useRef(null);
  const regionRef = React.useRef(null);
  const store = React.useMemo(() => createStore({ row: -1, spot: null }), []);
  const timer = React.useRef(null);

  React.useEffect(() => {
    const mark = () => {
      const { row } = store.getState();
      if (pageRef.current) {
        pageRef.current.toggleAttribute("data-spot", row >= 0);
        pageRef.current.toggleAttribute("data-tv-dim", row >= 0);
      }
      if (!regionRef.current) return;
      regionRef.current.querySelectorAll("[data-spot-row]").forEach((el, i) => el.toggleAttribute("data-past", row >= 0 && i < row));
    };
    mark();
    const stop = store.subscribe(mark);
    const onFocus = (e) => {
      if (!regionRef.current || regionRef.current.contains(e.target)) return;
      clearTimeout(timer.current);
      if (store.getState().row >= 0) store.setState({ row: -1, spot: null });
    };
    document.addEventListener("focusin", onFocus);
    return () => { stop(); clearTimeout(timer.current); document.removeEventListener("focusin", onFocus); };
  }, [store]);

  const focusOn = React.useCallback((row, s) => ({
    onFocus: () => {
      clearTimeout(timer.current);
      const now = store.getState();
      if (!now.spot) { store.setState({ row, spot: s }); return; }
      if (now.row !== row) store.setState({ row });
      timer.current = setTimeout(() => store.setState({ spot: s }), SPOT_MS);
    },
  }), [store]);
  return { store, pageRef, regionRef, focusOn };
}

/** What a spotlight's store holds: `row`, -1 while focus is outside the rows, and `spot`. */
export function useSpot(store) {
  return useStore(store);
}

/**
 * The artwork behind a spotlit page and, while focus is in its rows, the spotlight over them: the
 * focused card's name, its facts (or a line in their place), its story, and how far somebody got or
 * who made it. `art` is the artwork while focus is elsewhere, at `idleSize`.
 */
export function SpotStage({ store, art, idleSize = "tall" }) {
  const { row, spot } = useSpot(store);
  const inRows = row >= 0;
  return (
    <>
      <TvBackdrop src={(inRows && spot && spot.art) || art} size={inRows ? "short" : idleSize} />
      {inRows && spot && (
        <div className="kc-tvh__spot" key={spot.key}>
          <TvSpotlight size="md" eyebrow={spot.eyebrow} title={spot.title} meta={spot.facts ? <SpotFacts facts={spot.facts} /> : spot.line} overview={spot.overview}
            credit={!spot.progress && spot.credit ? <>{spot.credit.label} <b>{spot.credit.names}</b></> : undefined}>
            {spot.progress && <SpotProgress fraction={spot.progress.fraction} left={spot.progress.left} />}
          </TvSpotlight>
        </div>
      )}
    </>
  );
}

/** A title's facts in one line: its rating, year, certificate, length and genres. */
export function SpotFacts({ facts }) {
  if (!facts) return null;
  return (
    <>
      {facts.rating ? (
        <span className="kc-tv-rating">
          <Icon name="star" size={13} />
          <span className="kc-mono">{facts.rating.toFixed(1)}</span>
          {facts.ratedBy && <span className="kc-tv-rating__by">{facts.ratedBy === "imdb" ? "IMDb" : "TMDB"}</span>}
        </span>
      ) : null}
      {facts.year && <span>{facts.year}</span>}
      {facts.cert && <span className="kc-cert">{facts.cert}</span>}
      {facts.length && <span>{facts.length}</span>}
      {facts.genres && facts.genres.length > 0 && <span>{facts.genres.slice(0, 3).join(" · ")}</span>}
    </>
  );
}

/** How far somebody got: the bar and the time left. */
export function SpotProgress({ fraction, left }) {
  return (
    <div className="kc-tv-progress">
      <span className="kc-tv-progress__bar"><span style={{ width: Math.round(fraction * 100) + "%" }} /></span>
      {left}
    </div>
  );
}
