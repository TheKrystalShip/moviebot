import { Button, Drawer, Icon, IconButton, useStore } from "@thekrystalship/krystal";
import { stillSources } from "../remoteFilm.js";
import { replaceStore } from "../remoteWatch.js";
import { timeLeft } from "../time.js";
import "./download.css";
import "./remote.css";

/** What the television is playing, as the card says it: "Mr Inbetween · S2 E6 · 26m left". */
function playingLine(film, position, preparing) {
  if (!film || !film.manifest) return preparing ? "Getting it ready" : null;
  const { manifest } = film;
  const left = manifest.durationSeconds > 0 ? timeLeft(Math.max(0, manifest.durationSeconds - (position ?? 0))) : null;
  const place = manifest.episode ? `S${manifest.episode.season} E${manifest.episode.number}` : manifest.film && manifest.film.year;
  return [manifest.episode && manifest.film && manifest.film.name, place, left].filter(Boolean).join(" · ");
}

/**
 * The question before Watch on TV replaces a film the television is part way through: what plays, what
 * would play instead, and Watch or Keep watching. It is drawn from the question `remoteWatch.js` is
 * asking, and answers it.
 */
export function ReplaceSheet() {
  const ask = useStore(replaceStore, (a) => a);
  if (!ask) return null;
  const { screen, target, film, position, playing, answer } = ask;
  const tv = screen.name || "TV";
  const manifest = film && film.manifest;
  const name = manifest
    ? (manifest.episode && film.around && film.around.current && film.around.current.name) || (manifest.film && manifest.film.name) || manifest.title
    : playing || "";
  const art = manifest ? stillSources(manifest)[0] : null;
  const fraction = manifest && manifest.durationSeconds > 0 ? Math.min(1, (position ?? 0) / manifest.durationSeconds) : 0;
  const close = () => answer(false);
  return (
    <Drawer theme="dark" className="kc-dl-sheet kc-dl-drawer" label={`Watch on ${tv}`} onClose={close} foot={(
      <>
        <Button variant="primary" size="xl" icon="tv" wide onClick={() => answer(true)}>Watch {target.name}</Button>
        <Button variant="outline" size="lg" wide onClick={close}>Keep watching</Button>
      </>
    )} head={(
      <div className="kc-dl-head">
        <span className="kc-dl-head__text">
          <span className="kc-dl-head__eyebrow"><Icon name="tv" size={13} />{tv}</span>
          <span className="kc-dl-head__name">Watch on TV</span>
        </span>
        <IconButton variant="ghost" icon="x" label="Close" className="kc-dl-head__close" onClick={close} />
      </div>
    )}>
      <div className="kc-replace">
        <div className="kc-replace__card is-playing">
          <span className="kc-replace__art">
            {art ? <img src={art} alt="" /> : <span className="kc-remote__nostill"><Icon name="tv" size={18} /></span>}
            {fraction > 0 && <span className="kc-bar"><span style={{ width: `${Math.round(fraction * 100)}%` }} /></span>}
          </span>
          <span className="kc-replace__text">
            <span className="kc-replace__label">Playing</span>
            <span className="kc-replace__name">{name}</span>
            <span className="kc-replace__meta">{playingLine(film, position, playing)}</span>
          </span>
        </div>
        <div className="kc-replace__arrow"><Icon name="arrow-down" size={18} /></div>
        <div className="kc-replace__card is-next">
          <span className="kc-replace__art">
            {target.art ? <img src={target.art} alt="" /> : <span className="kc-remote__nostill"><Icon name="clapperboard" size={18} /></span>}
          </span>
          <span className="kc-replace__text">
            <span className="kc-replace__label">Next</span>
            <span className="kc-replace__name">{target.name}</span>
            {target.meta && <span className="kc-replace__meta">{target.meta}</span>}
          </span>
        </div>
      </div>
    </Drawer>
  );
}

