import React from "react";
import { Button, Checkbox, Field, Input, toast } from "@thekrystalship/krystal";
import { api } from "../api.js";
import { useOnline } from "../connectivity.js";
import { useLoad } from "../data.js";
import { useCrumb } from "../crumbs.js";
import { planPath } from "../paths.js";
import { navigate } from "../router.js";
import { MAX_TIMES, localInput, timeBounds } from "../plans.js";
import { Failed, PageHead } from "../components/Film.jsx";
import { PlanTitlePicker } from "../components/PlanTitlePicker.jsx";

/** A time to start from: an hour ahead, on the half hour. */
function suggested() {
  const at = new Date(Date.now() + 60 * 60000);
  at.setMinutes(Math.ceil(at.getMinutes() / 30) * 30, 0, 0);
  return localInput(at);
}

/**
 * Plans watching a title together: one or more times, and who is asked. The title comes in the
 * address (`title`, and `kind` for a show), and the people with a visible want on it start ticked;
 * with none in the address, the page finds one first.
 */
export function PlanNew({ query }) {
  const imdbId = query.get("title");
  const kind = query.get("kind") === "show" ? "show" : "film";
  const online = useOnline();
  const title = useLoad(() => (imdbId ? (kind === "show" ? api.show(imdbId) : api.film(imdbId)) : Promise.resolve(null)), [imdbId, kind]);
  const people = useLoad(() => api.people().catch(() => []), []);
  const wanters = useLoad(() => (imdbId ? api.wanters(imdbId).catch(() => []) : Promise.resolve([])), [imdbId]);
  const [times, setTimes] = React.useState(() => [suggested()]);
  const [picked, setPicked] = React.useState(null);
  const [busy, setBusy] = React.useState(false);

  useCrumb(title.data ? title.data.title : null);

  if (!imdbId) {
    return (
      <div className="kc-page">
        <PageHead title="Plan a watch" />
        <PlanTitlePicker autoFocus />
      </div>
    );
  }

  const chosen = picked || new Set((wanters.data || []).map((w) => w.userId));
  const toggle = (id, on) => {
    const next = new Set(chosen);
    if (on) next.add(id); else next.delete(id);
    setPicked(next);
  };

  const now = Date.now();
  const { min, max } = timeBounds(now);
  const valid = (value) => value && value >= min && value <= max;
  const ready = times.every(valid) && new Set(times).size === times.length;

  const make = async () => {
    setBusy(true);
    try {
      const plan = await api.makePlan({
        imdbId, kind, times: times.map((t) => new Date(t).toISOString()), people: [...chosen],
      });
      navigate(planPath(plan.id));
    } catch (error) {
      toast.fromError(error, "Could not make the plan");
      setBusy(false);
    }
  };

  const d = title.data;
  return (
    <div className="kc-page kc-plan-form">
      <PageHead title="Plan a watch" sub={d ? [d.title, d.year || d.firstYear].filter(Boolean).join(" · ") : null} />
      <Failed error={title.data ? null : title.error} onRetry={title.reload} />

      <Field label="When" group>
        {times.map((value, i) => (
          <div key={i} className="kc-plan-form__time">
            <Input type="datetime-local" size="lg" aria-label={`Time ${i + 1}`} value={value} min={min} max={max}
              invalid={!!value && !valid(value)}
              onChange={(e) => setTimes(times.map((t, j) => (j === i ? e.target.value : t)))} />
            {times.length > 1 && (
              <Button variant="ghost" size="lg" icon="x" aria-label="Remove this time"
                onClick={() => setTimes(times.filter((_, j) => j !== i))} />
            )}
          </div>
        ))}
        {times.length < MAX_TIMES && (
          <Button variant="outline" size="lg" icon="plus" onClick={() => setTimes([...times, suggested()])}>Add a time</Button>
        )}
      </Field>

      <Field label="Who" group>
        <div className="kc-plan-form__people">
          {(people.data || []).map((p) => (
            <Checkbox key={p.userId} checked={chosen.has(p.userId)} onChange={(on) => toggle(p.userId, on)}>
              {p.name || "Someone"}
            </Checkbox>
          ))}
        </div>
      </Field>

      <div className="kc-actions">
        <Button variant="primary" size="xl" icon="calendar-plus" disabled={!ready || busy || !online || !d} onClick={make}>Make the plan</Button>
      </div>
    </div>
  );
}
