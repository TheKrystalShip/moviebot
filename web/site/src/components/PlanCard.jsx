import { ShowcaseCard } from "@thekrystalship/krystal";
import { planPath } from "../paths.js";
import { navigate } from "../router.js";
import { when } from "../time.js";
import { kindLine } from "./Film.jsx";

/** Where a plan's title stands, as measured: what the plan's foot says. */
export function readinessLine(plan) {
  switch (plan.readiness) {
    case "ready": return { tone: "success", icon: "circle-check", label: "Ready" };
    case "fetching": return { tone: "notice", icon: "loader", label: `Fetching · ${Math.floor((plan.progress || 0) * 100)}%` };
    case "waiting": return { tone: "notice", icon: "calendar-clock", label: "Waiting for a release" };
    case "queued": return { icon: "clock", label: "Queued" };
    default: return null;
  }
}

/** What a plan's state is, in a word. */
export const STATE_WORDS = {
  proposed: "Proposed",
  fixed: "Fixed",
  open: "Room open",
  done: "Done",
  cancelled: "Cancelled",
};

/** The line on a plan's foot: its title's readiness while it is going, else how it ended. */
function statusOf(plan) {
  if (plan.state === "done") return { icon: "circle-check", label: "Done" };
  if (plan.state === "cancelled") return { icon: "x", label: "Cancelled" };
  if (plan.state === "open") return { tone: "active", icon: "door-open", label: "Room open" };
  if (plan.state === "proposed" && plan.times.some((t) => !t.mine)) return { tone: "active", icon: "message-circle", label: "Answer" };
  return readinessLine(plan) || { icon: "calendar-clock", label: STATE_WORDS[plan.state] };
}

/**
 * A plan as a card: the title's poster named a film or a series, when it is (the fixed time, else how
 * many times are proposed), how many are in it and where it stands.
 */
export function PlanCard({ plan }) {
  const fixedAt = plan.fixedTime;
  return (
    <ShowcaseCard compactFacts
      name={plan.title}
      eyebrow={kindLine(plan.kind, plan)}
      art={plan.posterUrl ? `url("${plan.posterUrl}")` : null}
      onPick={() => navigate(planPath(plan.id))}
      progress={plan.readiness === "fetching" ? plan.progress || 0 : undefined}
      specs={[
        { icon: "calendar", value: fixedAt ? when(fixedAt) : `${plan.times.length} ${plan.times.length === 1 ? "time" : "times"}`, label: "When" },
        { icon: "users", value: plan.people.length, label: "People" },
      ]}
      status={statusOf(plan)}
      action={{ label: "View" }} />
  );
}
