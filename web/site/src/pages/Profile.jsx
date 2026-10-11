import React from "react";
import { Button, Icon, Select, SettingsRow, SettingsSection, ShowcaseCard, SubTabs, ThemePicker, Toggle, pushBrowser, toast } from "@thekrystalship/krystal-ui";
import { KIND } from "../notifications.js";
import { api, episodeCode, filmName, posterUrl } from "../api.js";
import { useLoad } from "../data.js";
import { languageOptions } from "../languages.js";
import { accountUrl, can, useCan, useSession } from "../session.js";
import { leave } from "../leave.js";
import { PROFILE_TAB_FACES } from "../nav.js";
import { PROFILE_TABS, moviePath, profilePath, savedPath, showPath } from "../paths.js";
import { useOnline } from "../connectivity.js";
import { keptPreferences } from "../offline/preferences.js";
import { offlineSupported } from "../offline/store.js";
import { Off } from "./Offline.jsx";
import { linkProps, navigate } from "../router.js";
import { ago, airDay } from "../time.js";
import { Empty, Failed, PageHead, kindLine, watchHref } from "../components/Film.jsx";
import { TabStage } from "../components/PageStage.jsx";
import { CardsSkeleton, WishCard, keptArt } from "../components/TitleCards.jsx";
import { Cues } from "../player/Cues.jsx";
import { SubtitleStyleEditor } from "../player/SubtitleStyleEditor.jsx";
import { onTv } from "../tv/tv.js";
import { inActivity } from "../discord/launch.js";

// The tabs that show what only the server holds. With no connection they are drawn disabled; the
// others show what the device keeps (Playback and Subtitles, read-only) or the browser's own (Appearance).
const SERVER_TABS = new Set(["history", "requests", "following", "notifications"]);

/** The tab a profile opened with no connection shows when its address names none it can. */
const DEVICE_TAB = "playback";

/** The tabs off a television: Remote is a television's, where it shows the code a phone scans. */
const TABS = PROFILE_TAB_FACES.filter((t) => t.id !== "remote");

/**
 * Who is signed in and everything that is theirs: what they watched, what they asked for and how they
 * like to watch. Their sign-ins are the provider's, on its account page. The tab is in the address, so
 * a tab can be linked to and reloaded.
 *
 * With no connection the tabs that need the server are disabled. `fromDevice` is the page opened with
 * none: a tab that needs the server is never drawn, and the page shows Playback in its place.
 */
export function Profile({ tab: named, fromDevice = false }) {
  const asked = TABS.some((t) => t.id === named) ? named : PROFILE_TABS[0];
  const tab = fromDevice && SERVER_TABS.has(asked) ? DEVICE_TAB : asked;
  const session = useSession();
  const user = session && session.user;
  const online = useOnline();
  const tabsRef = React.useRef(null);
  useDisabledTabs(tabsRef, !online, TABS.map((t) => SERVER_TABS.has(t.id)));

  React.useEffect(() => {
    if (tab !== (named || PROFILE_TABS[0])) navigate(profilePath(tab), { replace: true });
  }, [tab, named]);

  const saved = (
    <a className="kc-row kc-row--link kc-profile__saved" {...linkProps(savedPath)}>
      <Icon name="bookmark" size={18} />
      <span className="kc-row__title">Saved</span>
      <Icon name="chevron-right" size={18} />
    </a>
  );
  return (
    <div className="kc-page kc-profile">
      <PageHead title={user ? user.name : "Profile"} sub={user && user.username ? "@" + user.username : null}>
        {!inActivity && <Button size="lg" icon="log-out" className="kc-head__action" onClick={leave}>Sign out</Button>}
      </PageHead>
      {online ? saved : <Off>{saved}</Off>}
      <div ref={tabsRef} className="kc-profile__tabs">
        <SubTabs tabs={TABS} active={tab} onChange={(id) => navigate(profilePath(id), { replace: true })} />
      </div>
      <TabStage tab={tab} render={(shown) => (
        <div className="kc-profile__panel">
          {/* A tab that asks the server keeps what it drew when the connection goes, and asks nothing. */}
          <fieldset className="kc-fieldset" disabled={!online && SERVER_TABS.has(shown)}>
            {panel(shown, session)}
          </fieldset>
        </div>
      )} />
    </div>
  );
}

/**
 * Disables the tabs that need the server while `offline`, both the row of tabs and the phone's
 * picker. krystal-ui's `SubTabs` takes no disabled tab, so they are disabled in place, by the order
 * `TABS` gives them; React sets no `disabled` of its own on them to undo it.
 */
function useDisabledTabs(ref, offline, needsServer) {
  const key = needsServer.join();
  React.useLayoutEffect(() => {
    if (!ref.current) return;
    const off = (i) => offline && !!needsServer[i];
    ref.current.querySelectorAll(".subtab").forEach((button, i) => { button.disabled = off(i); });
    ref.current.querySelectorAll(".subtabs-mobile option").forEach((option, i) => { option.disabled = off(i); });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [offline, key]);
}

export function panel(tab, session) {
  switch (tab) {
    case "requests": return <Requests session={session} />;
    case "following": return <Following />;
    case "notifications": return <Notifications />;
    case "playback": return <Playback />;
    case "subtitles": return <SubtitleLook />;
    case "appearance": return <Appearance />;
    case "account": return <Account session={session} />;
    default: return <History />;
  }
}

/** What this person watched, newest first, and how far they got. */
function History() {
  const history = useLoad(() => api.history(), []);
  const titles = useLoad(() => api.titles().catch(() => []), []);
  const byId = new Map((titles.data || []).map((t) => [t.id, t]));
  const rows = history.data || [];

  const forget = async (row) => {
    try {
      await api.forgetHistory(row.titleId);
      history.reload();
    } catch (error) {
      toast.fromError(error, "Could not remove it from your history");
    }
  };

  return (
    <section className="kc-shelf">
      <Failed error={history.error} onRetry={history.reload} />
      {!history.data && !history.error && <CardsSkeleton count={8} row />}
      {history.data && rows.length === 0 && <Empty icon="history" title="Nothing watched yet" />}
      {rows.length > 0 && (
        <div className="kc-grid">
          {rows.map((row) => <HistoryCard key={row.titleId} row={row} title={byId.get(row.titleId)} onForget={forget} />)}
        </div>
      )}
    </section>
  );
}

/** One thing watched: how far, when, and Watch or Resume where it is still here. */
function HistoryCard({ row, title, onForget }) {
  const fraction = row.finished ? 1 : Math.min(1, row.positionSeconds / Math.max(1, row.durationSeconds));
  const poster = title && posterUrl(title);
  const d = title && title.details;
  const ready = title && title.status === "ready";
  const name = !title ? row.name : title.episode || !title.film || !title.film.name ? filmName(title) : title.film.name;
  return (
    <ShowcaseCard
      className="kc-card"
      name={name}
      eyebrow={kindLine(row.episode ? "show" : "film", title)}
      art={poster ? `url("${poster}")` : null}
      onPick={() => navigate(moviePath(row.titleId))}
      progress={fraction}
      specs={[
        { icon: "star", value: d && d.rating ? d.rating.toFixed(1) : null, label: "Rating" },
        { icon: "calendar", value: (title && title.film && title.film.year) || null, label: "Year" },
        { icon: row.finished ? "check" : "history", value: row.finished ? "All" : `${Math.round(100 * fraction)}%`, label: "Watched" },
      ]}
      status={{ icon: "history", label: ago(row.lastWatchedAt), title: new Date(row.lastWatchedAt).toLocaleString() }}
      action={ready
        ? { label: row.finished ? "Watch again" : "Resume", onClick: () => navigate(watchHref(row.titleId, null, row.finished ? 0 : row.positionSeconds)) }
        : { label: "View" }}
      tools={[{ icon: "x", label: `Remove ${row.name} from your history`, tone: "danger", onClick: () => onForget(row) }]} />
  );
}

/** The films this person is waiting on. */
function Requests({ session }) {
  const user = session && session.user;
  const wishing = can(session, "cinema:wish");
  const downloading = can(session, "cinema:download");
  const wishes = useLoad(() => (wishing ? api.wishes() : Promise.resolve([])), [wishing]);
  const mine = (wishes.data || []).filter((w) => w.subscribers.some((s) => user && s.userId === user.id));

  const cancel = async (wish) => {
    try {
      await api.cancelWish(wish.imdbId);
      toast.info(`You are no longer waiting on ${wish.display}.`);
      wishes.reload();
    } catch (error) {
      toast.fromError(error, "Could not stop waiting");
    }
  };

  if (!wishes.data && !wishes.error) return <CardsSkeleton count={8} row />;

  return (
    <>
      {wishing && <section className="kc-shelf">
        <div className="kc-shelf__head"><h2>Waiting on</h2></div>
        <Failed error={wishes.error} onRetry={wishes.reload} />
        {wishes.data && mine.length === 0 && <Empty icon="bell" title="Nothing yet">A film that is not out yet can be waited on from Notify me.</Empty>}
        {mine.length > 0 && (
          <div className="kc-grid">
            {mine.map((wish) => {
              const asked = wish.subscribers.find((s) => s.userId === user.id);
              return (
                <WishCard key={wish.imdbId} wish={wish} downloading={downloading} onStop={cancel}
                  status={wish.release ? null : { icon: "bell", label: `Asked ${ago(asked.askedAt)}` }} />
              );
            })}
          </div>
        )}
      </section>}
    </>
  );
}

/** The shows this person follows, whether each is fetched when an episode airs, and unfollowing. */
function Following() {
  const downloading = useCan("cinema:download");
  const follows = useLoad(() => api.follows(), []);
  const rows = follows.data || [];

  const setAuto = async (row, on) => {
    try {
      await api.follow(row.imdbId, on);
      follows.reload();
    } catch (error) {
      toast.fromError(error, "Could not save that");
    }
  };
  const unfollow = async (row) => {
    try {
      await api.unfollow(row.imdbId);
      toast.info(`You no longer follow ${row.title}.`);
      follows.reload();
    } catch (error) {
      toast.fromError(error, "Could not unfollow it");
    }
  };

  return (
    <section className="kc-shelf">
      <Failed error={follows.error} onRetry={follows.reload} />
      {!follows.data && !follows.error && <CardsSkeleton count={8} row />}
      {follows.data && rows.length === 0 && <Empty icon="tv" title="Not following anything">Follow a show from its page to hear when a new episode is out.</Empty>}
      {rows.length > 0 && (
        <div className="kc-grid">
          {rows.map((row) => {
            const next = row.nextEpisode;
            const tools = [
              ...(downloading ? [{
                icon: "download", on: row.autoDownload,
                label: row.autoDownload ? `New episodes of ${row.title} are downloaded` : `Download new episodes of ${row.title}`,
                onClick: () => setAuto(row, !row.autoDownload),
              }] : []),
              { icon: "bell-off", label: `Unfollow ${row.title}`, onClick: () => unfollow(row) },
            ];
            return (
              <ShowcaseCard
                key={row.imdbId}
                className="kc-card"
                name={row.title}
                art={row.posterUrl ? `url("${row.posterUrl}")` : keptArt(row.imdbId)}
                onPick={() => navigate(showPath(row.imdbId))}
                specs={[
                  { icon: "calendar-clock", value: next ? episodeCode({ season: next.season, number: next.episode }) : null, label: "Next" },
                  { icon: "calendar", value: next ? airDay(next.airDate) : null, label: "Airs" },
                  { icon: "download", value: row.autoDownload ? "On" : "Off", label: "Auto" },
                ]}
                status={{ icon: "bell", label: `Since ${ago(row.since)}`, title: next && next.name ? next.name : undefined }}
                action={{ label: "View" }}
                tools={tools} />
            );
          })}
        </div>
      )}
    </section>
  );
}

const KINDS = [
  { id: "available", title: "A title I'm waiting for can be watched", sub: "Anything on your Notify me list." },
  { id: "ready", title: "Something I asked for is ready to watch", sub: "Includes each episode of a season." },
  { id: "episode", title: "New episodes of shows you follow" },
  { id: "shared", title: "Someone shares a list with me" },
];

/** What this browser says about receiving pushes, when it cannot be switched on here. */
const PUSH_BLOCKED = {
  unsupported: "This browser cannot receive pushes.",
  "needs-install": "Add Krystal Cinema to your Home Screen to get pushes on this phone.",
  denied: "Notifications are blocked for this site in the browser's settings.",
};

/**
 * Whether this browser is pushed to, and which kinds of news are pushed at all. A kind switched off
 * still lands in the bell; without a push anywhere, what is switched on comes as a Discord message.
 */
function Notifications() {
  const stored = useLoad(() => api.notificationPreferences(), []);
  const [saved, setSaved] = React.useState(null);
  const preferences = saved || stored.data;
  const [push, setPush] = React.useState({ on: false, checking: true, busy: false });
  const support = pushBrowser.support();

  React.useEffect(() => {
    let live = true;
    (async () => {
      const endpoint = await pushBrowser.currentEndpoint();
      const status = endpoint ? await api.pushStatus(endpoint).catch(() => null) : null;
      if (live) setPush({ on: Boolean(status && status.subscribed), checking: false, busy: false });
    })();
    return () => { live = false; };
  }, []);

  const turnPush = async (on) => {
    setPush((p) => ({ ...p, busy: true }));
    try {
      if (on) {
        const { publicKey } = await api.pushKey();
        const subscription = await pushBrowser.acquire(publicKey);
        await api.subscribePush(subscription.toJSON());
      } else {
        await pushBrowser.release((endpoint) => api.unsubscribePush(endpoint).catch(() => {}));
      }
      setPush({ on, checking: false, busy: false });
    } catch (error) {
      setPush((p) => ({ ...p, busy: false }));
      toast.fromError(error, on ? "Could not turn pushes on" : "Could not turn pushes off");
    }
  };

  const change = async (id, on) => {
    try {
      setSaved(await api.setNotificationPreferences({ ...preferences, [id]: on }));
    } catch (error) {
      toast.fromError(error, "Could not save that");
    }
  };

  const blocked = PUSH_BLOCKED[support.state];
  const pushSub = blocked
    || (push.on ? "This browser gets a push when something here is for you." : "Turn them on to hear about releases without checking the site.");

  return (
    <>
      <SettingsSection icon="bell-ring" title="This device">
        <SettingsRow icon="bell-ring" title="Browser push on this device" sub={pushSub} tone={blocked ? "warn" : push.on ? "success" : undefined}>
          <Toggle on={push.on} disabled={Boolean(blocked) || push.checking || push.busy} label="Browser push on this device" onChange={turnPush} />
        </SettingsRow>
      </SettingsSection>
      {!preferences ? <Failed error={stored.error} onRetry={stored.reload} /> : (
        <SettingsSection icon="sliders-horizontal" title="Send me a push when">
          {KINDS.map((k) => (
            <SettingsRow key={k.id} icon={KIND[k.id].icon} title={k.title} sub={k.sub}>
              <Toggle on={Boolean(preferences[k.id])} label={k.title} onChange={(on) => change(k.id, on)} />
            </SettingsRow>
          ))}
        </SettingsSection>
      )}
    </>
  );
}

const QUALITIES = [{ value: "", label: "Auto" }, { value: "1080", label: "1080p" }, { value: "720", label: "720p" }];
// What Download picks by default; a title downloads the widest rung no wider than the height's 16:9
// width (`offline/choose.js`).
const OFFLINE_QUALITIES = [...QUALITIES, { value: "480", label: "480p" }];

const languages = languageOptions;
const orNull = (value) => value || null;

/**
 * This person's playback preferences as stored, and changing them: `change` saves a patch at once,
 * `changeSoon` shows it at once and saves once the changing pauses, since a slider dragged across its
 * range is one decision. With no connection they are the copy kept on the device, shown and not
 * changed (`fixed`).
 */
function usePreferences() {
  const session = useSession();
  const sub = session && session.user.id;
  const online = useOnline();
  const stored = useLoad(() => {
    const kept = keptPreferences(sub);
    if (!online && kept) return Promise.resolve(kept);
    return api.preferences().catch((error) => {
      if (error.status === 0 && kept) return kept;
      throw error;
    });
  }, [sub]);
  const [saved, setSaved] = React.useState(null);
  const pending = React.useRef(null);
  const preferences = saved || stored.data;
  const fixed = !online;

  const change = async (patch) => {
    try {
      setSaved(await api.setPreferences({ ...preferences, ...patch }));
    } catch (error) {
      toast.fromError(error, "Could not save that");
    }
  };
  const changeSoon = (patch) => {
    const next = { ...preferences, ...patch };
    setSaved(next);
    clearTimeout(pending.current);
    pending.current = setTimeout(() => {
      api.setPreferences(next).then(setSaved, (error) => toast.fromError(error, "Could not save that"));
    }, 600);
  };
  return { preferences, stored, change, changeSoon, fixed };
}

/**
 * How a film starts for this person: its audio language, its quality, and whether intros are skipped;
 * and the quality Download picks, where this browser can download.
 */
function Playback() {
  const session = useSession();
  const { preferences, stored, change, fixed } = usePreferences();
  if (!preferences) return <Failed error={stored.error} onRetry={stored.reload} />;

  const qualities = preferences.qualityHeight && !QUALITIES.some((q) => q.value === String(preferences.qualityHeight))
    ? [...QUALITIES, { value: String(preferences.qualityHeight), label: `${preferences.qualityHeight}p` }]
    : QUALITIES;

  return (
    <fieldset className="kc-fieldset" disabled={fixed}>
      <SettingsSection icon="play" title="Playback">
        <SettingsRow icon="audio-lines" title="Audio" sub="Original is the language it was made in.">
          <Select value={preferences.audioLanguage || ""} options={languages(preferences.audioLanguage, "Original")}
            aria-label="Audio language" onChange={(e) => change({ audioLanguage: orNull(e.target.value) })} />
        </SettingsRow>
        <SettingsRow icon="gauge" title="Quality" sub="Auto lets the connection decide.">
          <Heights label="Quality" options={qualities} value={preferences.qualityHeight}
            onChange={(height) => change({ qualityHeight: height })} />
        </SettingsRow>
        <SettingsRow icon="skip-forward" title="Skip intros and recaps automatically">
          <Toggle on={Boolean(preferences.skipIntros)} label="Skip intros and recaps automatically" onChange={(on) => change({ skipIntros: on })} />
        </SettingsRow>
        {offlineSupported() && can(session, "cinema:offline") && (
          <SettingsRow icon="hard-drive-download" title="Download quality" sub="Auto downloads the smallest on a phone or tablet.">
            <Heights label="Download quality" options={OFFLINE_QUALITIES} value={preferences.offlineQualityHeight}
              onChange={(height) => change({ offlineQualityHeight: height })} />
          </SettingsRow>
        )}
      </SettingsSection>
    </fieldset>
  );
}

/** A choice of heights, Auto (`""`) among them: `value` is the stored height or null. */
function Heights({ label, options, value, onChange }) {
  return (
    <div className="kc-segmented kc-heights" role="radiogroup" aria-label={label}>
      {options.map((q) => {
        const on = (value ? String(value) : "") === q.value;
        return (
          <button key={q.value} type="button" role="radio" aria-checked={on} className={"kc-segmented__option" + (on ? " is-on" : "")}
            onClick={() => onChange(q.value ? Number(q.value) : null)}>{q.label}</button>
        );
      })}
    </div>
  );
}

/** Subtitles for this person: the language a film starts in, and how they are drawn, with a preview. */
function SubtitleLook() {
  const { preferences, stored, change, changeSoon, fixed } = usePreferences();
  if (!preferences) return <Failed error={stored.error} onRetry={stored.reload} />;

  return (
    <fieldset className="kc-fieldset" disabled={fixed}>
      <SettingsSection icon="languages" title="Language">
        <SettingsRow icon="captions" title="Subtitles" sub="The language to start in, when there is one.">
          <Select value={preferences.subtitleLanguage || ""} options={languages(preferences.subtitleLanguage, "Off")}
            aria-label="Subtitle language" onChange={(e) => change({ subtitleLanguage: orNull(e.target.value) })} />
        </SettingsRow>
      </SettingsSection>
      <SettingsSection icon="type" title="Style">
        <div className="kc-cue-style">
          <div className="kc-cue-preview" aria-hidden="true">
            <Cues style={preferences.subtitleStyle} sample />
          </div>
          <SubtitleStyleEditor style={preferences.subtitleStyle} onChange={(subtitleStyle) => changeSoon({ subtitleStyle })} />
        </div>
      </SettingsSection>
    </fieldset>
  );
}

/** The site's theme in this browser. */
function Appearance() {
  return (
    <SettingsSection icon="palette" title="Theme">
      <ThemePicker />
    </SettingsSection>
  );
}

/**
 * Where this person's account lives. Krystal Cinema keeps no sign-in of its own: the name, the
 * password and every session are the provider's, on its account page. A television leaves the site for
 * nothing, so there its address is shown to open on a phone.
 */
function Account({ session }) {
  const user = session && session.user;
  const where = useLoad(() => accountUrl(), []);
  const url = where.data;
  const online = useOnline();

  return (
    <SettingsSection icon="user-cog" title="Account">
      <SettingsRow icon="user" title={user ? user.name : "Signed in"} sub={user && user.username ? "@" + user.username : null} />
      <SettingsRow icon="external-link" title="Managed outside Krystal Cinema"
        sub={url ? `Your name, password and sessions are kept at ${new URL(url).host}.` : "Your name, password and sessions are kept by your account provider."}>
        {onTv
          ? url && <span className="kc-account-address">{url.replace(/^https?:\/\//, "")}</span>
          : <Button icon="external-link" href={url || ""} disabled={!url || !online}>Manage account</Button>}
      </SettingsRow>
    </SettingsSection>
  );
}

