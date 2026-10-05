import { ShowcaseCard, toast } from "@thekrystalship/krystal-ui";
import { api, filmName, posterUrl } from "../api.js";
import { SOON_DAYS, leaveRule, stayWords, useLoad } from "../data.js";
import { useCan, useSession } from "../session.js";
import { ago } from "../time.js";
import { Empty, Failed, PageHead, watchHref } from "../components/Film.jsx";
import { CardsSkeleton } from "../components/TitleCards.jsx";
import { moviePath, showPath } from "../paths.js";
import { navigate } from "../router.js";

/** A film by its name; a season of a show, which stays and leaves as one, by the show's and its number. */
function unitName(title) {
  if (title.episode && title.film && title.film.name) return `${title.film.name} · Season ${title.episode.season}`;
  return (title.film && title.film.name) || filmName(title);
}

function unitHref(title) {
  return title.episode && title.film && title.film.imdbId
    ? showPath(title.film.imdbId, { season: title.episode.season })
    : moviePath(title.id);
}

/**
 * Every settled film and season, and how long it stays. A film nobody keeps leaves after a spell without a play;
 * keeping it holds it for everybody until somebody lets it go.
 */
export function Leaving() {
  const session = useSession();
  const keeping = useCan("cinema:keep");
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
    const name = unitName(byId.get(row.titleId));
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
      <PageHead title="Leaving soon" sub={leaveRule(all[0])} />
      <Failed error={stays.error} onRetry={stays.reload} />
      {!stays.data && !stays.error && <CardsSkeleton count={6} />}
      {stays.data && all.length === 0 && <Empty icon="hourglass" title="Nothing on the clock" />}

      {groups.filter((g) => g.rows.length > 0).map((group) => (
        <section key={group.id} className="kc-shelf">
          <div className="kc-shelf__head">
            <h2 className={group.tone ? "kc-tone--" + group.tone : ""}>{group.label}<span className="kc-count">{group.rows.length}</span></h2>
          </div>
          <div className="kc-grid">
            {group.rows.map((row) => {
              const title = byId.get(row.titleId);
              const poster = posterUrl(title);
              const d = title.details;
              const kept = !!row.keeperId;
              return (
                <ShowcaseCard
                  key={row.unit || row.titleId}
                  className="kc-card"
                  name={unitName(title)}
                  art={poster ? `url("${poster}")` : null}
                  onPick={() => navigate(unitHref(title))}
                  specs={[
                    { icon: "star", value: d && d.rating ? d.rating.toFixed(1) : null, label: "Rating" },
                    { icon: "calendar", value: (title.film && title.film.year) || null, label: "Year" },
                    { icon: kept ? "pin" : "hourglass", value: kept ? "Kept" : row.daysLeft != null ? `${row.daysLeft}d` : null, label: kept ? "Stays" : "Left" },
                  ]}
                  status={kept
                    ? { tone: "success", icon: "bookmark-check", label: stayWords(row, me) }
                    : {
                      tone: group.id === "soon" ? "warning" : null,
                      icon: "play",
                      label: row.lastPlayedAt ? `Played ${ago(row.lastPlayedAt)}` : "Never played",
                      title: stayWords(row, me),
                    }}
                  action={title.status === "ready" && !title.episode
                    ? { label: "Watch", onClick: () => navigate(watchHref(title.id)) }
                    : { label: "View" }}
                  tools={keeping ? [{
                    icon: kept ? "pin-off" : "pin", on: kept,
                    label: kept ? `Let ${unitName(title)} go` : `Keep ${unitName(title)}`,
                    onClick: () => act(row, !kept),
                  }] : null} />
              );
            })}
          </div>
        </section>
      ))}
    </div>
  );
}
