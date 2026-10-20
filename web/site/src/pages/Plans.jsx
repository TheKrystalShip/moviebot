import { api } from "../api.js";
import { useLoad } from "../data.js";
import { usePlanChanges } from "../plans.js";
import { Empty, Failed, PageHead } from "../components/Film.jsx";
import { PlanCard } from "../components/PlanCard.jsx";
import { CardsSkeleton } from "../components/TitleCards.jsx";

/** The person's plans to watch something together: the ones going, then the ones that ended this week. */
export function Plans() {
  const plans = useLoad(() => api.plans(), []);
  usePlanChanges(() => plans.reload());

  const list = plans.data || [];
  return (
    <div className="kc-page">
      <PageHead title="Plans" />
      <Failed error={plans.data ? null : plans.error} onRetry={plans.reload} />
      {!plans.data && !plans.error && <CardsSkeleton count={4} />}
      {plans.data && list.length === 0 && <Empty icon="calendar-clock" title="No plans" />}
      {list.length > 0 && (
        <div className="kc-grid">
          {list.map((plan) => <PlanCard key={plan.id} plan={plan} />)}
        </div>
      )}
    </div>
  );
}
