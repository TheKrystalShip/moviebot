import { Icon } from "@thekrystalship/krystal-ui";
import { downloadLine } from "./DownloadButton.jsx";
import { clock } from "../time.js";

/**
 * One episode as a season lists it, on a show's page and in the player's Episodes menu: its number,
 * its still with how far this person got, its name, its score, its length. Under the name is where its
 * download to this device stands when there is one (`download`, from `useDownloadMarks`), else when it
 * aired. Choosing the row is all it takes to watch it, so nothing on it asks for anything else.
 *
 * What choosing does is the caller's: `link` is a link's props, `onPick` a press; with neither the
 * row cannot be watched from here and is drawn dimmed. `current` marks the episode playing, `room`
 * that people are watching it now, `starting` that a press is being answered, and `off` that there is
 * no connection, which a press needs.
 */
export function EpisodeRow({ episode: e, download = null, link = null, onPick = null, current = false, room = false,
  starting = false, off = false }) {
  const done = e.progress && e.progress.finished;
  const fraction = done ? 1
    : e.progress && e.durationSeconds ? Math.min(1, e.progress.positionSeconds / e.durationSeconds) : 0;
  const runtime = e.runtimeMinutes ? clock(e.runtimeMinutes * 60) : e.durationSeconds ? clock(e.durationSeconds) : null;
  const watchable = Boolean(link || onPick);
  const inner = (
    <>
      <span className="kc-episode__n">{e.number}</span>
      <span className="kc-episode__still">
        {e.stillUrl && <img src={e.stillUrl} alt="" loading="lazy" />}
        {fraction > 0 && <span className="kc-bar"><span style={{ width: `${Math.round(fraction * 100)}%` }} className={done ? "is-done" : ""} /></span>}
      </span>
      <span className="kc-episode__text">
        <span className={"kc-episode__title" + (done ? " is-done" : "")}>{e.name || `Episode ${e.number}`}</span>
        {download
          ? <span className={"kc-episode__sub kc-episode__sub--download" + (download.kind === "done" ? " is-done" : "")}>{downloadLine(download)}</span>
          : <span className="kc-episode__sub">{starting ? "Starting…" : e.airDate ? `Aired ${e.airDate}` : null}</span>}
      </span>
      <span className="kc-episode__progress"><span style={{ width: `${Math.round(fraction * 100)}%` }} className={done ? "is-done" : ""} /></span>
      <span className="kc-episode__score">{e.rating ? <><Icon name="star" size={11} />{e.rating.toFixed(1)}</> : null}</span>
      <span className="kc-episode__runtime">{runtime}</span>
      <span className="kc-episode__go">
        {watchable && <Icon name={current ? "audio-lines" : room ? "users" : done ? "check" : "play"} size={13} />}
      </span>
    </>
  );

  // A link plays from the device where it has to, so having no connection does not dim it.
  if (link) return <a className={"kc-episode" + (current ? " is-current" : "")} aria-current={current || undefined} {...link}>{inner}</a>;
  const state = (current ? " is-current" : "") + (off ? " is-off" : "");
  if (onPick) {
    return (
      <button type="button" className={"kc-episode" + state} aria-current={current || undefined} disabled={starting || off}
        onClick={onPick}>{inner}</button>
    );
  }
  return <div className={"kc-episode is-absent" + state}>{inner}</div>;
}
