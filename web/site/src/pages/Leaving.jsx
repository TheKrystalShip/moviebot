import { Icon, toast } from "@thekrystalship/krystal-ui";
import { api, filmName } from "../api.js";
import { useLoad, staysFor } from "../data.js";
import { Empty, Failed, PageHead, PosterArt } from "../components/Film.jsx";
import { linkProps } from "../router.js";
import { keepWords } from "./Title.jsx";

const SOON_DAYS = 3;

/**
 * Every downloaded film and how long it stays. A film nobody keeps leaves once it has seeded for its
 * window; keeping it holds it for everybody until somebody lets it go. A film added by hand is on no
 * clock and is not listed.
 */
export function Leaving() {
  const keeps = useLoad(() => api.keeps(), []);
  const titles = useLoad(() => api.titles(), []);
  const byId = new Map((titles.data || []).map((t) => [t.id, t]));
  const all = keeps.data || [];

  const groups = [
    { id: "soon", label: `Leaving in the next ${SOON_DAYS} days`, tone: "warn", rows: all.filter((k) => !k.keeper && k.isFinished && k.remainingSeconds < SOON_DAYS * 86400) },
    { id: "clock", label: "On the clock", rows: all.filter((k) => !k.keeper && k.isFinished && k.remainingSeconds >= SOON_DAYS * 86400) },
    { id: "arriving", label: "Still downloading", rows: all.filter((k) => !k.keeper && !k.isFinished) },
    { id: "kept", label: "Kept", tone: "accent", rows: all.filter((k) => k.keeper) },
  ];

  const act = async (row, keeping) => {
    try {
      const reply = keeping ? await api.keep(row.hash) : await api.release(row.hash);
      const name = row.libraryId && byId.get(row.libraryId) ? filmName(byId.get(row.libraryId)) : row.name;
      if (reply.outcome === "unavailable" || reply.outcome === "notFound") toast.error(keepWords(reply, name));
      else toast.success(keepWords(reply, name));
      keeps.reload();
    } catch (error) {
      toast.fromError(error, "That did not change anything");
    }
  };

  return (
    <div className="kc-page">
      <PageHead title="Leaving soon" sub="Downloaded films leave once they have seeded for their window. Keeping one holds it for everybody until somebody lets it go." />
      <Failed error={keeps.error} onRetry={keeps.reload} />
      {keeps.data && all.length === 0 && <Empty icon="hourglass" title="Nothing on the clock">No downloaded film is on disk.</Empty>}

      {groups.filter((g) => g.rows.length > 0).map((group) => (
        <section key={group.id} className="kc-shelf">
          <div className="kc-shelf__head">
            <h2 className={group.tone ? "kc-tone--" + group.tone : ""}>{group.label}<span className="kc-count">{group.rows.length}</span></h2>
          </div>
          <div className="kc-rows">
            {group.rows.sort((a, b) => a.remainingSeconds - b.remainingSeconds).map((row) => {
              const title = row.libraryId ? byId.get(row.libraryId) : null;
              return (
                <div key={row.hash} className="kc-row">
                  {title ? <a {...linkProps("/title/" + encodeURIComponent(title.id))}><PosterArt title={title} className="kc-row__thumb" /></a> : <span className="kc-row__thumb" />}
                  <div className="kc-row__text">
                    <span className="kc-row__title">{title ? filmName(title) : row.name}</span>
                    <span className="kc-row__sub kc-mono">{row.release}</span>
                  </div>
                  <span className={"kc-pill" + (group.id === "soon" ? " kc-pill--warn" : "")}>{staysFor(row)}</span>
                  {row.keeper
                    ? <button type="button" className="kc-btn kc-btn--ghost" onClick={() => act(row, false)}><Icon name="pin-off" size={15} />Let go</button>
                    : <button type="button" className="kc-btn" onClick={() => act(row, true)}><Icon name="pin" size={15} />Keep</button>}
                </div>
              );
            })}
          </div>
        </section>
      ))}
    </div>
  );
}
