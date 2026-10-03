import { Icon, toast } from "@thekrystalship/krystal-ui";
import { api, filmName } from "../api.js";
import { LEAVE_AFTER_DAYS, stayWords, useLoad } from "../data.js";
import { useSession } from "../session.js";
import { ago } from "../time.js";
import { Empty, Failed, PageHead, PosterArt } from "../components/Film.jsx";
import { linkProps } from "../router.js";

const SOON_DAYS = 3;

/**
 * Every settled film and how long it stays. A film nobody keeps leaves after a spell without a play;
 * keeping it holds it for everybody until somebody lets it go.
 */
export function Leaving() {
  const session = useSession();
  const me = session && session.user;
  const stays = useLoad(() => api.stays(), []);
  const titles = useLoad(() => api.titles(), []);
  const byId = new Map((titles.data || []).map((t) => [t.id, t]));
  const all = (stays.data || []).filter((s) => byId.has(s.titleId));

  const groups = [
    { id: "soon", label: `Leaving in the next ${SOON_DAYS} days`, tone: "warn", rows: all.filter((s) => !s.keeperId && s.daysLeft != null && s.daysLeft <= SOON_DAYS) },
    { id: "clock", label: "On the clock", rows: all.filter((s) => !s.keeperId && !(s.daysLeft != null && s.daysLeft <= SOON_DAYS)) },
    { id: "kept", label: "Kept", tone: "accent", rows: all.filter((s) => s.keeperId) },
  ];

  const act = async (row, keeping) => {
    const name = filmName(byId.get(row.titleId));
    try {
      if (keeping) await api.keep(row.titleId);
      else await api.unkeep(row.titleId);
      toast.success(keeping ? `${name} is kept, so it stays.` : `${name} is back on the clock.`);
      stays.reload();
    } catch (error) {
      toast.fromError(error, "That did not change anything");
    }
  };

  return (
    <div className="kc-page">
      <PageHead title="Leaving soon" sub={`A film leaves after ${LEAVE_AFTER_DAYS} days without a play. Keeping one holds it for everybody until somebody lets it go.`} />
      <Failed error={stays.error} onRetry={stays.reload} />
      {stays.data && all.length === 0 && <Empty icon="hourglass" title="Nothing on the clock">No film has settled in the library yet.</Empty>}

      {groups.filter((g) => g.rows.length > 0).map((group) => (
        <section key={group.id} className="kc-shelf">
          <div className="kc-shelf__head">
            <h2 className={group.tone ? "kc-tone--" + group.tone : ""}>{group.label}<span className="kc-count">{group.rows.length}</span></h2>
          </div>
          <div className="kc-rows">
            {group.rows.map((row) => {
              const title = byId.get(row.titleId);
              return (
                <div key={row.titleId} className="kc-row">
                  <a {...linkProps("/title/" + encodeURIComponent(title.id))}><PosterArt title={title} className="kc-row__thumb" /></a>
                  <div className="kc-row__text">
                    <span className="kc-row__title">{filmName(title)}</span>
                    <span className="kc-row__sub">{row.lastPlayedAt ? `Last played ${ago(row.lastPlayedAt)}` : "Not played yet"}</span>
                  </div>
                  <span className={"kc-pill" + (group.id === "soon" ? " kc-pill--warn" : "")}>{stayWords(row, me)}</span>
                  {row.keeperId
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
