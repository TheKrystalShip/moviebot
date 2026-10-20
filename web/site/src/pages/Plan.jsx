import React from "react";
import { Button, ConfirmDialog, Icon, Pill, RadioGroup, toast } from "@thekrystalship/krystal";
import { api, episodeCode } from "../api.js";
import { useOnline } from "../connectivity.js";
import { useLoad } from "../data.js";
import { useCrumb } from "../crumbs.js";
import { plansPath } from "../paths.js";
import { navigate } from "../router.js";
import { usePlanChanges } from "../plans.js";
import { when } from "../time.js";
import { Empty, Failed, PageHead, watchHref } from "../components/Film.jsx";
import { STATE_WORDS, readinessLine } from "../components/PlanCard.jsx";
import { Faces } from "../components/Wanters.jsx";

const ANSWERS = [
  { value: "in", label: "In" },
  { value: "maybe", label: "Maybe" },
  { value: "cant", label: "Can't" },
];

const TONES = { in: "ok", maybe: "warn", cant: "muted" };

/** One proposed time: when, how many are in, each person's answer, the viewer's own to change, and Fix for the maker. */
function TimeRow({ plan, time, onAnswer, onFix, busy }) {
  const answerable = plan.state === "proposed" || plan.state === "fixed";
  return (
    <div className="kc-plan-time">
      <div className="kc-plan-time__head">
        <span className="kc-plan-time__when">{when(time.at)}</span>
        <span className="kc-mono kc-quiet">{time.in} in</span>
        {plan.mine && plan.state === "proposed" && (
          <Button variant="outline" size="lg" icon="calendar-check" disabled={busy} onClick={() => onFix(time.at)}>Fix</Button>
        )}
      </div>
      <RadioGroup variant="segmented" fill label="Your answer" className="kc-plan-time__answer" value={time.mine || ""}
        disabled={!answerable || busy} options={ANSWERS} onChange={(answer) => onAnswer(time.at, answer)} />
      <div className="kc-plan-time__answers">
        {time.answers.map((a) => (
          <Pill key={a.userId} size="sm" tone={TONES[a.answer]}>{(a.name || "Someone") + " · " + a.answer}</Pill>
        ))}
      </div>
    </div>
  );
}

/**
 * One plan: its title and where it stands, the times and what each person said to each, the people,
 * and the room once it is made. The maker fixes a time or cancels the plan; everybody in it answers.
 * It reads again whenever the API says the plan changed.
 */
export function Plan({ id }) {
  const online = useOnline();
  const plan = useLoad(() => api.plan(id), [id]);
  const [busy, setBusy] = React.useState(false);
  const [cancelling, setCancelling] = React.useState(false);
  usePlanChanges((changed) => { if (changed === null || changed === id) plan.reload(); });

  const p = plan.data;
  useCrumb(p ? p.title : plan.error && plan.error.status === 404 ? "No such plan" : null);

  if (plan.error && plan.error.status === 404) {
    return <div className="kc-page"><Empty icon="calendar-clock" title="No such plan" /></div>;
  }
  if (!p) return <div className="kc-page"><Failed error={plan.error} onRetry={plan.reload} /></div>;

  const run = async (work, failure) => {
    setBusy(true);
    try {
      await work();
      plan.reload();
    } catch (error) {
      toast.fromError(error, failure);
    } finally {
      setBusy(false);
    }
  };

  const status = readinessLine(p);
  const sub = [p.kind === "show" && p.season != null ? episodeCode({ season: p.season, number: p.number }) : null, p.year]
    .filter(Boolean).join(" · ");
  const going = p.state === "proposed" || p.state === "fixed";

  return (
    <div className="kc-page kc-plan">
      <PageHead title={p.title} sub={sub || null}>
        <div className="kc-actions">
          <Pill tone={p.state === "cancelled" ? "muted" : "accent"}>{STATE_WORDS[p.state]}</Pill>
          {status && going && <Pill tone={status.tone === "success" ? "ok" : "info"} icon={status.icon}>{status.label}</Pill>}
        </div>
      </PageHead>
      <Failed error={plan.error} onRetry={plan.reload} />

      {p.state === "open" && p.roomId && (
        <div className="kc-actions">
          <Button variant="primary" size="xl" icon="users" disabled={!online}
            onClick={() => navigate(watchHref(p.libraryId, p.roomId))}>Join the room</Button>
        </div>
      )}

      <div className="kc-plan__times">
        {p.times.map((time) => (
          <TimeRow key={time.at} plan={p} time={time} busy={busy || !online}
            onAnswer={(at, answer) => run(() => api.answerPlan(p.id, at, answer), "Could not save that")}
            onFix={(at) => run(() => api.fixPlan(p.id, at), "Could not fix that time")} />
        ))}
      </div>

      <div className="kc-plan__people">
        <Icon name="users" size={16} />
        <Faces people={p.people} />
      </div>

      {p.mine && going && (
        <div className="kc-actions">
          <Button variant="danger" size="lg" icon="x" disabled={busy || !online} onClick={() => setCancelling(true)}>Cancel the plan</Button>
        </div>
      )}
      {cancelling && (
        <ConfirmDialog title="Cancel this plan?" icon="x" tone="danger" confirm="Cancel the plan" busyLabel="Cancelling…" cancel="Keep it"
          onClose={() => setCancelling(false)}
          onConfirm={async () => {
            try {
              await api.cancelPlan(p.id);
              navigate(plansPath);
            } catch (error) {
              return { refusal: error.message };
            }
            return undefined;
          }}>
          Everybody invited is told.
        </ConfirmDialog>
      )}
    </div>
  );
}
