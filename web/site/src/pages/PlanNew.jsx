import React from "react";
import { BriefCard, Button, Calendar, Icon, Input, TimeGrid, TimeInput, toast, useDebouncedValue } from "@thekrystalship/krystal";
import { dateKey, momentKey, nextStep, parseDate, parseMoment } from "@thekrystalship/krystal/lib/datetime";
import { catalogue, certificateOf, meApi, roomsApi, runtimeText } from "../api/index.js";
import { useOnline } from "../connectivity.js";
import { useLoad } from "../data.js";
import { useCrumb } from "../crumbs.js";
import { discoverFilmPath, isAnime, planPath, showPath } from "../paths.js";
import { navigate } from "../router.js";
import { FURTHEST_DAYS, MAX_TIMES, SOONEST_MINUTES } from "../plans.js";
import { ago, clock } from "../time.js";
import { PageHead, Failed } from "../components/Film.jsx";
import { PersonCard, TitleSkeleton } from "../components/FilmDetails.jsx";
import { DateTile } from "../components/DateTile.jsx";
import { HeroGroup, kindWord, TitleAbout, TitleFacts, TitleHero } from "../components/TitleHero.jsx";
import { PlanTitlePicker } from "../components/PlanTitlePicker.jsx";

/** Minutes between the times offered. */
const STEP = 30;

/** The clock, read again every half minute, so the earliest time offered keeps up with it. */
function useNow() {
  const [now, setNow] = React.useState(() => Date.now());
  React.useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 30000);
    return () => clearInterval(timer);
  }, []);
  return now;
}

/**
 * The moments a plan's times fit between, from `now`: the first step at least `SOONEST_MINUTES` away,
 * and `FURTHEST_DAYS` ahead (the API's `PlanRules`).
 */
function windowOf(now) {
  return {
    earliest: momentKey(nextStep(now + SOONEST_MINUTES * 60000, STEP)),
    latest: momentKey(new Date(now + FURTHEST_DAYS * 86400000)),
  };
}

/** A day as a picked time names it: today and tomorrow by name (tonight from six), else how far off. */
function dayWord(key, hour, now) {
  const away = Math.round((parseDate(key) - parseDate(dateKey(new Date(now)))) / 86400000);
  if (away === 0) return hour >= "18:00" ? "Tonight" : "Today";
  if (away === 1) return "Tomorrow";
  return `In ${away} days`;
}

/** A day in full: "Friday 23 October". */
function longDay(key) {
  return parseDate(key).toLocaleDateString("en-GB", { weekday: "long", day: "numeric", month: "long" });
}

/** A moment as the hero's line leads with it: "Tonight at 21:00", "Fri 23 Oct at 20:30". */
function leadWords(moment, now) {
  const [day, hour] = moment.split("T");
  const away = Math.round((parseDate(day) - parseDate(dateKey(new Date(now)))) / 86400000);
  if (away <= 1) return `${dayWord(day, hour, now)} at ${hour}`;
  return `${parseDate(day).toLocaleDateString("en-GB", { weekday: "short", day: "numeric", month: "short" })} at ${hour}`;
}

/** The hour a time plus a running time ends at, and "next day" when it runs past midnight. */
function endsAt(moment, minutes) {
  const end = new Date(parseMoment(moment).getTime() + minutes * 60000);
  const hour = end.toLocaleTimeString("en-GB", { hour: "2-digit", minute: "2-digit" });
  return dateKey(end) === moment.slice(0, 10) ? hour : `${hour} next day`;
}

/**
 * Plans watching a title together, laid out as the title's own page: its hero with the plan's line and
 * Make the plan, then When (a month to pick days from, the day's times from the next half hour on, and
 * the times picked so far) and Who (everybody who can be asked, the people with a visible want on the
 * title first and ticked). The title comes in the address (`title`, and `kind` for a show); with none,
 * the page finds one first.
 */
export function PlanNew({ query }) {
  const imdbId = query.get("title");
  const kind = query.get("kind") === "show" ? "show" : "film";
  const online = useOnline();
  const now = useNow();
  const title = useLoad(() => (imdbId ? catalogue.title(imdbId) : Promise.resolve(null)), [imdbId]);
  const people = useLoad(() => roomsApi.people().catch(() => []), []);
  const [find, setFind] = React.useState("");
  const q = useDebouncedValue(find.trim(), 250);
  const found = useLoad(() => (q ? roomsApi.people(q).catch(() => []) : Promise.resolve(null)).then((list) => ({ q, people: list })), [q]);
  const wanters = useLoad(() => (imdbId ? meApi.wanters(imdbId).catch(() => []) : Promise.resolve([])), [imdbId]);
  const [first] = React.useState(() => momentKey(nextStep(Date.now() + 60 * 60000, STEP)));
  const [times, setTimes] = React.useState([first]);
  const [day, setDay] = React.useState(first.slice(0, 10));
  const [exact, setExact] = React.useState("");
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
  if (!title.data && !title.error) return <TitleSkeleton kind={kind === "show" ? "TV series" : "Movie"} />;
  if (!title.data) return <div className="kc-page"><Failed error={title.error} onRetry={title.reload} /></div>;

  const d = title.data;
  const { earliest, latest } = windowOf(now);
  const minDay = earliest.slice(0, 10);
  const maxDay = latest.slice(0, 10);
  const shownDay = day < minDay ? minDay : day > maxDay ? maxDay : day;
  const fits = (moment) => moment >= earliest && moment <= latest;
  const full = times.length >= MAX_TIMES;
  const onDay = times.filter((t) => t.startsWith(shownDay + "T")).map((t) => t.slice(11));
  const ready = times.length > 0 && times.every(fits);
  const runtime = kind === "film" ? d.runtime : null;

  const pickTimes = (next) => setTimes([...times.filter((t) => !t.startsWith(shownDay + "T")), ...next.map((t) => `${shownDay}T${t}`)].sort());
  const exactKey = exact ? `${shownDay}T${exact}` : null;
  const exactOpen = !!exactKey && fits(exactKey) && !times.includes(exactKey) && !full;
  const addExact = () => {
    if (!exactOpen) return;
    setTimes([...times, exactKey].sort());
    setExact("");
  };

  const wanted = new Set((wanters.data || []).map((w) => w.userId));
  const everybody = people.data || [];
  const chosen = picked || new Set(everybody.filter((p) => wanted.has(p.userId)).map((p) => p.userId));
  const toggle = (id) => {
    const next = new Set(chosen);
    if (next.has(id)) next.delete(id); else next.add(id);
    setPicked(next);
  };
  const everyone = everybody.length > 0 && everybody.every((p) => chosen.has(p.userId));
  // The people the words name, as the API finds them, the ones who want the title first.
  const named = q && found.data && found.data.q === q && found.data.people ? found.data.people : everybody;
  const shown = [...named.filter((p) => wanted.has(p.userId)), ...named.filter((p) => !wanted.has(p.userId))];

  const make = async () => {
    setBusy(true);
    try {
      const plan = await meApi.makePlan({
        imdbId, kind, times: times.map((t) => parseMoment(t).toISOString()), people: [...chosen],
      });
      navigate(planPath(plan.id));
    } catch (error) {
      toast.fromError(error, "Could not make the plan");
      setBusy(false);
    }
  };

  const years = d.year ? [d.year, d.endYear && d.endYear !== d.year ? d.endYear : null].filter(Boolean).join("–") : null;
  const certificate = certificateOf(d);
  const others = chosen.size;
  return (
    <div className="kc-page kc-plan-new">
      <TitleHero art={d.backdrop || d.poster} eyebrow={kindWord(kind, isAnime(d))} title={d.title}
        facts={<TitleFacts rating={d.rating && d.rating.value} votes={d.rating && d.rating.votes} ratedBy={d.rating && d.rating.by}
          kind={kind} category={d.category}
          parts={kind === "film"
            ? [d.year, certificate && { cert: certificate }, runtimeText(d.runtime)]
            : [years, certificate && { cert: certificate }]}
          genres={d.genres} />}>
        <TitleAbout overview={d.overview || d.plot} credits={d} />
        <HeroGroup>
          <span className="kc-plan__maker">
            <Icon name="calendar-heart" size={15} />
            <span>
              <b>{times.length ? leadWords(times[0], now) : "No time yet"}</b>
              {times.length > 1 ? ` and ${times.length - 1} more` : ""}
              {" · "}{others === 0 ? "Just you" : `You and ${others} ${others === 1 ? "other" : "others"}`}
            </span>
          </span>
          <div className="kc-actions">
            <Button variant="primary" size="xl" icon="calendar-plus" busy={busy} disabled={!ready || !online} onClick={make}>Make the plan</Button>
            <Button size="xl" icon="info" onClick={() => navigate(kind === "show" ? showPath(imdbId) : discoverFilmPath(imdbId))}>More info</Button>
          </div>
        </HeroGroup>
      </TitleHero>

      <BriefCard icon="calendar-clock" title="When" count={times.length} countTone="neutral"
        action={<span className="kc-plan-new__note">Up to {MAX_TIMES} times</span>}>
        <div className="kc-when">
          <Calendar locale="en-GB" value={shownDay} min={minDay} max={maxDay} onChange={setDay}
            marked={times.map((t) => t.slice(0, 10))} className="kc-when__calendar" />
          <div className="kc-when__day">
            <div className="kc-when__head">
              <DateTile at={parseDate(shownDay)} />
              <span className="kc-when__name">
                <b>{longDay(shownDay)}</b>
                <span>{dayWord(shownDay, "18:00", now)}</span>
              </span>
            </div>
            <TimeGrid label="Start times" multiple from={earliest.slice(11)} to="23:30" step={STEP}
              max={shownDay === maxDay ? latest.slice(11) : undefined}
              disable={(t) => full && !onDay.includes(t)} value={onDay} onChange={pickTimes} />
            <div className="kc-when__exact">
              <span className="kc-when__label">Another time</span>
              <TimeInput label="Another time" size="lg" minuteStep={5} value={exact} onChange={setExact}
                invalid={!!exactKey && !fits(exactKey)} />
              <Button variant="outline" size="lg" icon="plus" disabled={!exactOpen} onClick={addExact}>Add</Button>
            </div>
          </div>
        </div>
        {times.length > 0 && (
          <ol className="kc-plan-picks">
            {times.map((t) => {
              const [key, hour] = t.split("T");
              const late = !fits(t);
              return (
                <li key={t} className={"kc-plan-pick" + (late ? " is-late" : "")}>
                  <DateTile at={parseDate(key)} />
                  <span className="kc-plan-pick__main">
                    <span className="kc-plan-pick__hour">{hour}</span>
                    <span className="kc-plan-pick__rel">{late ? "Too soon now" : dayWord(key, hour, now)}</span>
                    <span className="kc-plan-pick__when">{longDay(key)}</span>
                  </span>
                  {runtime ? <span className="kc-plan-pick__ends">Ends {endsAt(t, runtime)} · {clock(runtime * 60)}</span> : null}
                  <Button variant="ghost" size="md" icon="x" aria-label={`Remove ${longDay(key)} at ${hour}`}
                    onClick={() => setTimes(times.filter((x) => x !== t))} />
                </li>
              );
            })}
          </ol>
        )}
      </BriefCard>

      <BriefCard icon="users" title="Who" count={chosen.size} countTone="neutral" wrapHead
        action={everybody.length > 0 ? (
          <span className="kc-plan-new__find">
            <Input size="sm" type="search" className="kc-plan-new__search" aria-label="Find someone" placeholder="Find someone" value={find}
              prefix={<Icon name="search" size={14} />} onChange={(e) => setFind(e.target.value)} />
            <Button variant="outline" size="sm" onClick={() => setPicked(new Set(everyone ? [] : everybody.map((p) => p.userId)))}>
              {everyone ? "No one" : "Everyone"}
            </Button>
          </span>
        ) : null}>
        {everybody.length === 0 ? (
          <div className="k-brief__empty">{people.data ? "Nobody else has been here yet" : "Finding everybody"}</div>
        ) : shown.length === 0 ? (
          <div className="k-brief__empty">Nobody by that name</div>
        ) : (
          <div className="kc-cast kc-plan-new__people">
            {shown.map((p) => (
              <PersonCard key={p.userId} person={{ name: p.name || "Someone" }}
                line={p.lastSeenAt ? `Active ${ago(p.lastSeenAt, now)}` : "Not seen on the site"}
                tag={wanted.has(p.userId) ? "Wants it" : null}
                pressed={chosen.has(p.userId)} onClick={() => toggle(p.userId)}
                end={<span className="kc-pick-mark" aria-hidden="true"><Icon name="check" size={12} /></span>} />
            ))}
          </div>
        )}
      </BriefCard>
    </div>
  );
}
