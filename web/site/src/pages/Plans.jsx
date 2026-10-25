import { Button, Shelf } from "@thekrystalship/krystal";
import { meApi } from "../api/index.js";
import { useLoad } from "../data.js";
import { discoverPath, newPlanPath } from "../paths.js";
import { usePlanChanges } from "../plans.js";
import { POSTERS } from "../railDensity.js";
import { navigate } from "../router.js";
import { useWantList } from "../wants.js";
import { counted } from "../counts.js";
import { Empty, Failed, PageHead } from "../components/Film.jsx";
import { PlanCard } from "../components/PlanCard.jsx";
import { PlanTitlePicker } from "../components/PlanTitlePicker.jsx";
import { CardsSkeleton } from "../components/TitleCards.jsx";
import { WantCard } from "./Wants.jsx";

/** A want's card whose action plans watching it. */
function PlanWantCard({ want }) {
  return <WantCard want={want} action={{ label: "Plan a watch", icon: "calendar-plus", onClick: () => navigate(newPlanPath(want.imdbId, want.kind)) }} />;
}

/**
 * What a person with no plans can plan: the titles they want that others want too, then the rest of
 * what they want, each planned from its card; with neither, a title found by name, or Discover. A title
 * waiting for a release has nothing to watch yet and is left out.
 */
function Plannable() {
  const together = useLoad(() => meApi.together().catch(() => []), []);
  const { list } = useWantList();
  if (!together.data || !list) return <CardsSkeleton count={4} />;

  const out = (w) => w.state !== "waiting";
  const shared = together.data.map((t) => t.want).filter(out);
  const sharedIds = new Set(shared.map((w) => w.imdbId));
  const mine = list.filter((w) => out(w) && !sharedIds.has(w.imdbId));

  if (shared.length === 0 && mine.length === 0) {
    return (
      <>
        <PlanTitlePicker />
        <div className="kc-actions">
          <Button size="xl" icon="compass" onClick={() => navigate(discoverPath())}>Discover</Button>
        </div>
      </>
    );
  }
  return (
    <>
      {shared.length > 0 && (
        <Shelf perView={POSTERS} title="Watch together" sub={counted(shared.length, "title")}>
          {shared.map((w) => <PlanWantCard key={w.imdbId} want={w} />)}
        </Shelf>
      )}
      {mine.length > 0 && (
        <Shelf perView={POSTERS} title="Want to watch" sub={counted(mine.length, "title")}>
          {mine.map((w) => <PlanWantCard key={w.imdbId} want={w} />)}
        </Shelf>
      )}
    </>
  );
}

/** The person's plans to watch something together, soonest first, and Plan a watch for another. */
export function Plans() {
  const plans = useLoad(() => meApi.plans(), []);
  usePlanChanges(() => plans.reload());

  const list = plans.data || [];
  return (
    <div className="kc-page">
      <PageHead title="Plans">
        <Button variant="primary" size="xl" icon="calendar-plus" className="kc-head__action" onClick={() => navigate(newPlanPath())}>
          Plan a watch
        </Button>
      </PageHead>
      <Failed error={plans.data ? null : plans.error} onRetry={plans.reload} />
      {!plans.data && !plans.error && <CardsSkeleton count={4} />}
      {plans.data && list.length === 0 && (
        <>
          <Empty icon="calendar-clock" title="No plans" />
          <Plannable />
        </>
      )}
      {list.length > 0 && (
        <div className="kc-grid">
          {list.map((plan) => <PlanCard key={plan.id} plan={plan} />)}
        </div>
      )}
    </div>
  );
}
