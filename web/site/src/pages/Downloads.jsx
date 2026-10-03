import { Icon } from "@thekrystalship/krystal-ui";
import { api } from "../api.js";
import { useLoad } from "../data.js";
import { linkProps } from "../router.js";
import { Empty, Failed, PageHead, watchLink } from "../components/Film.jsx";

/** What is downloading and what has arrived, read again every few seconds while the page is open. */
export function Downloads() {
  const downloads = useLoad(() => api.downloads(), [], { every: 5000 });
  const rows = (downloads.data || []).slice().sort((a, b) => Number(a.isFinished) - Number(b.isFinished));

  return (
    <div className="kc-page">
      <PageHead title="Downloads" sub="A film can be watched while it is still arriving, as soon as enough of it has been prepared." />
      <Failed error={downloads.error} onRetry={downloads.reload} />
      {downloads.data && rows.length === 0 && <Empty icon="download" title="Nothing downloading">Search for a film to fetch one.</Empty>}
      <div className="kc-rows">
        {rows.map((d) => (
          <div key={d.hash} className="kc-row">
            <span className={"kc-row__icon" + (d.isFinished ? "" : " kc-row__icon--live")}>
              <Icon name={d.isFinished ? "check" : "download"} size={16} />
            </span>
            <div className="kc-row__text">
              {d.libraryId
                ? <a className="kc-row__title" {...linkProps("/title/" + encodeURIComponent(d.libraryId))}>{d.name}</a>
                : <span className="kc-row__title">{d.name}</span>}
              <span className="kc-row__sub">{d.summary}{d.isKept ? " · kept" : ""}</span>
            </div>
            {d.libraryId && (d.isFinished || d.isWatchable) && (
              <a className="kc-btn" {...watchLink(d.libraryId)}><Icon name="play" size={15} />Watch</a>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}
