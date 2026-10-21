import React from "react";
import { Button, ConfirmDialog, Drawer, Icon, Input, Popover, RadioGroup, toast } from "@thekrystalship/krystal";
import { api, episodeCode } from "../api.js";
import { useOnline } from "../connectivity.js";
import { useLoad } from "../data.js";
import { useCrumb } from "../crumbs.js";
import { discoverFilmPath, moviePath, plansPath, showPath } from "../paths.js";
import { navigate } from "../router.js";
import { MAX_TIMES, localInput, timeBounds, usePlanChanges } from "../plans.js";
import { ahead, clock, hourOf } from "../time.js";
import { Empty, Failed, kindLine, watchHref } from "../components/Film.jsx";
import { PersonCard } from "../components/FilmDetails.jsx";
import { DateTile } from "../components/DateTile.jsx";
import { HeroPoster, TitleHero } from "../components/TitleHero.jsx";
import { InvitePeople } from "../components/InvitePeople.jsx";

const ANSWERS = [
  { value: "in", label: "In" },
  { value: "maybe", label: "Maybe" },
  { value: "cant", label: "Can't" },
];

/** A plan's way to its room, each step named as the plan's state names it. */
const STAGES = [
  { state: "proposed", label: "Proposed" },
  { state: "fixed", label: "Time picked" },
  { state: "open", label: "Room open" },
];

/** Whether the page is a phone's width, where the invite list opens as a sheet. */
function useNarrow() {
  const query = "(max-width: 720px)";
  const [narrow, setNarrow] = React.useState(() => typeof window !== "undefined" && !!window.matchMedia && window.matchMedia(query).matches);
  React.useEffect(() => {
    if (!window.matchMedia) return undefined;
    const list = window.matchMedia(query);
    const change = () => setNarrow(list.matches);
    list.addEventListener("change", change);
    return () => list.removeEventListener("change", change);
  }, []);
  return narrow;
}

/** Where the plan is on its way to its room. */
function Stages({ state }) {
  const at = STAGES.findIndex((s) => s.state === state);
  return (
    <ol className="kc-plan-stages" aria-label="Where the plan is">
      {STAGES.map((stage, i) => (
        <li key={stage.state} className={"kc-plan-stages__step" + (i <= at ? " is-reached" : "")}
          aria-current={i === at ? "step" : undefined}>{stage.label}</li>
      ))}
    </ol>
  );
}

/** How a time stands with everybody in the plan: a bar of who is in and who might be, and the counts. */
function Tally({ time, people }) {
  const count = (answer) => time.answers.filter((a) => a.answer === answer).length;
  const yes = count("in");
  const maybe = count("maybe");
  const cant = count("cant");
  const waiting = Math.max(0, people - time.answers.length);
  const parts = [[yes, "in"], [maybe, "maybe"], [cant, "can't"], [waiting, "waiting"]].filter(([n]) => n > 0).map(([n, word]) => `${n} ${word}`);
  return (
    <div className="kc-plan-time__tally">
      <span className="kc-plan-time__bar" aria-hidden="true">
        <span className="kc-plan-time__bar-in" style={{ width: `${(yes / people) * 100}%` }} />
        <span className="kc-plan-time__bar-maybe" style={{ width: `${(maybe / people) * 100}%` }} />
      </span>
      <span className="kc-plan-time__count">{parts.join(" · ")}</span>
    </div>
  );
}

/**
 * One time: the day, the hour and how far off it is, who put it forward when that was not the maker,
 * how it stands, and the viewer's answer, which they change whenever they like. The picked time is
 * marked; the maker picks any other time ahead, and picks again.
 */
function TimeRow({ plan, time, onAnswer, onPick, busy }) {
  const picked = plan.fixedTime === time.at;
  const isAhead = new Date(time.at).getTime() > Date.now();
  const by = time.suggestedBy && !time.suggestedBy.maker ? time.suggestedBy.name || "Someone" : null;
  return (
    <div className={"kc-plan-time" + (picked ? " is-picked" : "")}>
      <DateTile at={time.at} />
      <div className="kc-plan-time__main">
        <div className="kc-plan-time__when">
          <span className="kc-plan-time__hour">{hourOf(time.at)}</span>
          {isAhead && <span className="kc-plan-time__ahead">{ahead(time.at)}</span>}
          {picked && <span className="kc-plan-time__picked"><Icon name="calendar-check" size={13} />Picked</span>}
        </div>
        {by && <span className="kc-plan-time__by">Suggested by {by}</span>}
        <Tally time={time} people={plan.people.length} />
      </div>
      <div className="kc-plan-time__answer">
        <span className={"kc-plan-time__ask" + (time.mine ? "" : " is-unanswered")}>Your answer</span>
        <RadioGroup variant="segmented" fill label="Your answer" value={time.mine || ""}
          disabled={busy} options={ANSWERS} onChange={(answer) => onAnswer(time.at, answer)} />
        {plan.mine && !picked && isAhead && (
          <Button variant="outline" icon="calendar-check" disabled={busy} onClick={() => onPick(time.at)}>
            {plan.fixedTime ? "Move to this time" : "Pick this time"}
          </Button>
        )}
      </div>
    </div>
  );
}

/** A time a few steps on from `from`, for suggesting one: later the same evening, or the next day. */
const NUDGES = [
  { label: "+30 min", minutes: 30 },
  { label: "+1 h", minutes: 60 },
  { label: "+2 h", minutes: 120 },
  { label: "Next day", minutes: 24 * 60 },
];

/**
 * Puts another time forward: a few steps on from the time the plan is on (the picked one, else the
 * earliest ahead), or any time typed. Anybody in the plan suggests, and is in for what they suggest.
 */
function SuggestTime({ plan, from, onSuggest, busy }) {
  const [open, setOpen] = React.useState(false);
  const [value, setValue] = React.useState("");
  const { min, max } = timeBounds();
  const valid = Boolean(value) && value >= min && value <= max;
  const upcoming = plan.times.filter((t) => new Date(t.at).getTime() > Date.now()).length;
  if (upcoming >= MAX_TIMES) return null;

  if (!open) {
    return (
      <div className="kc-actions">
        <Button variant="outline" icon="calendar-plus" onClick={() => { setValue(""); setOpen(true); }}>Suggest a time</Button>
      </div>
    );
  }
  const base = from ? new Date(from).getTime() : Date.now();
  return (
    <div className="kc-plan-suggest">
      <div className="kc-plan-suggest__nudges">
        {NUDGES.map((n) => {
          const at = localInput(new Date(base + n.minutes * 60000));
          return (
            <Button key={n.label} variant="outline" size="sm" pressed={value === at} disabled={at < min || at > max}
              onClick={() => setValue(at)}>{n.label}</Button>
          );
        })}
      </div>
      <div className="kc-plan-suggest__row">
        <Input type="datetime-local" size="lg" aria-label="Suggested time" value={value} min={min} max={max}
          invalid={Boolean(value) && !valid} onChange={(e) => setValue(e.target.value)} />
        <Button variant="primary" size="lg" icon="send" busy={busy} disabled={!valid}
          onClick={async () => { if (await onSuggest(new Date(value).toISOString())) setOpen(false); }}>Suggest</Button>
        <Button variant="ghost" size="lg" onClick={() => setOpen(false)}>Cancel</Button>
      </div>
    </div>
  );
}

/**
 * What a person said, in a line under their name, and the tone of the mark before it: their answer to
 * the picked time once there is one, else the best of what they said to the times still ahead.
 */
function standing(plan, times, userId) {
  const answerTo = (time) => (time.answers.find((a) => a.userId === userId) || {}).answer;
  const picked = plan.fixedTime && times.find((t) => t.at === plan.fixedTime);
  if (picked) {
    const answer = answerTo(picked);
    if (answer === "in") return { line: `In for ${dayAndHour(picked.at)}`, dot: "ok" };
    if (answer === "maybe") return { line: `Maybe for ${dayAndHour(picked.at)}`, dot: "warn" };
    if (answer === "cant") return { line: `Can't make ${dayAndHour(picked.at)}`, dot: "muted" };
    return { line: "Hasn't answered", dot: "none" };
  }
  const said = (answer) => times.filter((t) => answerTo(t) === answer);
  const phrase = (word, list) => (list.length === 1 ? `${word} for ${dayAndHour(list[0].at)}` : `${word} for ${list.length} times`);
  const yes = said("in");
  if (yes.length > 0) return { line: phrase("In", yes), dot: "ok" };
  const maybe = said("maybe");
  if (maybe.length > 0) return { line: phrase("Maybe", maybe), dot: "warn" };
  if (said("cant").length > 0) return { line: "Can't make it", dot: "muted" };
  return { line: "Hasn't answered", dot: "none" };
}

function dayAndHour(at) {
  return `${new Date(at).toLocaleDateString("en-GB", { weekday: "short" })} ${hourOf(at)}`;
}

/** The card that asks more people in, opening the people who can be: beside it, or as a sheet on a phone. */
function InviteCard({ plan }) {
  const narrow = useNarrow();
  const [open, setOpen] = React.useState(false);
  const anchor = React.useRef(null);
  const here = React.useMemo(() => new Set(plan.people.map((p) => p.userId)), [plan.people]);
  const invite = React.useCallback((person) => api.invitePlan(plan.id, person.userId).then((reply) => reply.delivery), [plan.id]);
  const list = <InvitePeople load={api.people} invite={invite} here={here} hereMark="In this plan" label={null} />;

  return (
    <div ref={anchor} className="kc-plan__invite">
      <PersonCard person={{ name: "Invite people" }} line="Ask someone in" className="kc-cast__card--add"
        face={<span className="kc-cast__face kc-cast__face--add"><Icon name="user-plus" size={18} /></span>}
        onClick={() => setOpen((o) => !o)} />
      {narrow ? open && (
        <Drawer title="Invite people" onClose={() => setOpen(false)}>{list}</Drawer>
      ) : (
        <Popover open={open} onOpenChange={setOpen} anchorRef={anchor} placement="bottom-start" title="Invite people" className="kc-plan__invite-panel">
          {list}
        </Popover>
      )}
    </div>
  );
}

/** Where the plan's title is read about: its page on the shelf when the library holds it, else in Discover. */
function titleHref(p) {
  if (p.kind === "show") return showPath(p.imdbId, { held: Boolean(p.libraryId), season: p.season });
  return p.libraryId ? moviePath(p.libraryId) : discoverFilmPath(p.imdbId);
}

/**
 * One plan: its title as the title's own page draws it, who planned it and where it is on its way, the
 * times still ahead and how each stands, and the people in it as cards, with one more card that invites
 * somebody else. Nothing locks: everybody in it answers, suggests a time and invites, and the maker
 * picks a time, picks again, or cancels, which deletes the plan. It reads again whenever the API says the
 * plan changed.
 */
export function Plan({ id }) {
  const online = useOnline();
  const plan = useLoad(() => api.plan(id), [id]);
  const [busy, setBusy] = React.useState(false);
  const [cancelling, setCancelling] = React.useState(false);
  usePlanChanges((changed) => { if (changed === null || changed === id) plan.reload(); });

  const p = plan.data;
  const imdbId = p && p.imdbId;
  const kind = p && p.kind;
  const described = useLoad(() => (imdbId ? (kind === "show" ? api.show(imdbId) : api.film(imdbId)).catch(() => null) : Promise.resolve(null)), [imdbId, kind]);
  useCrumb(p ? p.title : plan.error && plan.error.status === 404 ? "No such plan" : null);

  if (plan.error && plan.error.status === 404) {
    return (
      <div className="kc-page">
        <Empty icon="calendar-clock" title="No such plan">
          <Button icon="calendar-days" onClick={() => navigate(plansPath)}>Your plans</Button>
        </Empty>
      </div>
    );
  }
  if (!p) return <div className="kc-page"><Failed error={plan.error} onRetry={plan.reload} /></div>;

  const run = async (work, failure) => {
    setBusy(true);
    try {
      await work();
      plan.reload();
      return true;
    } catch (error) {
      toast.fromError(error, failure);
      return false;
    } finally {
      setBusy(false);
    }
  };

  const d = described.data;
  const art = (d && d.backdropUrl) || p.posterUrl;
  const now = Date.now();
  // The times still ahead, and the picked one whatever its hour; a time that has passed drops out.
  const shown = p.times.filter((t) => t.at === p.fixedTime || new Date(t.at).getTime() > now);
  const from = p.fixedTime || (shown[0] && shown[0].at) || null;
  const invited = p.people.length - 1;
  const maker = p.mine ? "You" : (p.madeBy && p.madeBy.name) || "Someone";

  return (
    <div className="kc-page kc-plan">
      <TitleHero art={art} poster={<HeroPoster url={p.posterUrl} name={p.title} />}
        eyebrow={`Plan · ${kindLine(p.kind, d).label}`} title={p.title}>
        <div className="kc-tt__meta">
          {p.kind === "show" && p.season != null && <span>{episodeCode({ season: p.season, number: p.number })}</span>}
          {p.year && <span>{p.year}</span>}
          {d && d.certification && <span className="kc-cert">{d.certification}</span>}
          {p.kind !== "show" && d && d.runtimeMinutes && <span>{clock(d.runtimeMinutes * 60)}</span>}
        </div>
        <span className="kc-plan__maker">
          <Icon name="calendar-heart" size={15} />
          <span><b>{maker}</b> planned this{invited > 0 ? ` · ${invited} invited` : ""}</span>
        </span>
        <Stages state={p.state} />
        <Failed error={plan.error} onRetry={plan.reload} />
        <div className="kc-actions">
          {p.state === "open" && p.roomId && (
            <Button variant="primary" size="xl" icon="users" disabled={!online}
              onClick={() => navigate(watchHref(p.libraryId, p.roomId))}>Join the room</Button>
          )}
          <Button size="xl" icon="info" onClick={() => navigate(titleHref(p))}>More info</Button>
          {p.mine && (
            <Button variant="danger" size="xl" icon="x" disabled={busy || !online} onClick={() => setCancelling(true)}>Cancel plan</Button>
          )}
        </div>
      </TitleHero>

      <section className="kc-section">
        <h2>When</h2>
        {shown.length > 0 ? (
          <div className="kc-plan__times">
            {shown.map((time) => (
              <TimeRow key={time.at} plan={p} time={time} busy={busy || !online}
                onAnswer={(at, answer) => run(() => api.answerPlan(p.id, at, answer), "Could not save that")}
                onPick={(at) => run(() => api.fixPlan(p.id, at), "Could not pick that time")} />
            ))}
          </div>
        ) : (
          <span className="kc-note">Every time has passed. Suggest a new one.</span>
        )}
        {online && (
          <SuggestTime plan={p} from={from} busy={busy}
            onSuggest={(at) => run(() => api.suggestPlanTime(p.id, at), "Could not suggest that time")} />
        )}
      </section>

      <section className="kc-section">
        <h2>Who's coming</h2>
        <div className="kc-cast">
          {p.people.map((person) => {
            const { line, dot } = standing(p, shown, person.userId);
            return <PersonCard key={person.userId} person={{ name: person.name || "Someone" }} line={line} dot={dot}
              tag={person.maker ? "Planned it" : null} />;
          })}
          {online && <InviteCard plan={p} />}
        </div>
      </section>

      {cancelling && (
        <ConfirmDialog title="Cancel this plan?" icon="x" tone="danger" confirm="Cancel plan" busyLabel="Cancelling…" cancel="Keep it"
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
