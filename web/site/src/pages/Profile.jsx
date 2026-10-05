import React from "react";
import { Icon, Select, SettingsRow, SettingsSection, ShowcaseCard, SubTabs, ThemePicker, Toggle, pushBrowser, toast } from "@thekrystalship/krystal-ui";
import { KIND } from "../notifications.js";
import { api, episodeCode, filmName, posterUrl } from "../api.js";
import { useLoad } from "../data.js";
import { accountUrl, can, useCan, useSession } from "../session.js";
import { leave } from "../leave.js";
import { PROFILE_TAB_FACES } from "../nav.js";
import { moviePath, profilePath, savedPath, showPath } from "../paths.js";
import { linkProps, navigate } from "../router.js";
import { ago, airDay } from "../time.js";
import { Empty, Failed, PageHead, watchHref } from "../components/Film.jsx";
import { TabStage } from "../components/PageStage.jsx";
import { CardsSkeleton, WishCard, keptArt } from "../components/TitleCards.jsx";
import { Cues } from "../player/Cues.jsx";
import { SubtitleStyleEditor } from "../player/SubtitleStyleEditor.jsx";

const TABS = PROFILE_TAB_FACES;

/**
 * Who is signed in and everything that is theirs: what they watched, what they asked for and how they
 * like to watch. Their sign-ins are the provider's, on its account page. The tab is in the address, so
 * a tab can be linked to and reloaded.
 */
export function Profile({ tab: named }) {
  const tab = named || TABS[0].id;
  const session = useSession();
  const user = session && session.user;

  return (
    <div className="kc-page kc-profile">
      <PageHead title={user ? user.name : "Profile"} sub={user && user.username ? "@" + user.username : null}>
        <button type="button" className="kc-btn kc-head__action" onClick={leave}>
          <Icon name="log-out" size={16} />Sign out
        </button>
      </PageHead>
      <a className="kc-row kc-row--link kc-profile__saved" {...linkProps(savedPath)}>
        <Icon name="bookmark" size={18} />
        <span className="kc-row__title">Saved</span>
        <Icon name="chevron-right" size={18} />
      </a>
      <SubTabs tabs={TABS} active={tab} onChange={(id) => navigate(profilePath(id), { replace: true })} />
      <TabStage tab={tab} render={(shown) => <div className="kc-profile__panel">{panel(shown, session)}</div>} />
    </div>
  );
}

function panel(tab, session) {
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

/** The films this person is waiting on, and the downloads they asked for, under way or done. */
function Requests({ session }) {
  const user = session && session.user;
  const wishing = can(session, "cinema:wish");
  const downloading = can(session, "cinema:download");
  const wishes = useLoad(() => (wishing ? api.wishes() : Promise.resolve([])), [wishing]);
  const downloads = useLoad(() => (downloading ? api.downloads() : Promise.resolve([])), [downloading], { every: 5000 });
  const mine = (wishes.data || []).filter((w) => w.subscribers.some((s) => user && s.userId === user.id));
  const rows = (downloads.data || [])
    .filter((d) => user && d.requestedBy === user.id)
    .sort((a, b) => Number(a.isFinished) - Number(b.isFinished));

  const cancel = async (wish) => {
    try {
      await api.cancelWish(wish.imdbId);
      toast.info(`You are no longer waiting on ${wish.display}.`);
      wishes.reload();
    } catch (error) {
      toast.fromError(error, "Could not stop waiting");
    }
  };

  // Both shelves wait for both reads, behind one row of placeholders, so the tab draws once at its height.
  const answered = (load) => Boolean(load.data || load.error);
  if (!answered(wishes) || !answered(downloads)) return <CardsSkeleton count={8} row />;

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

      {downloading && <section className="kc-shelf">
        <div className="kc-shelf__head"><h2>Downloads</h2></div>
        <Failed error={downloads.error} onRetry={downloads.reload} />
        {downloads.data && rows.length === 0 && <Empty icon="download" title="Nothing downloading" />}
        {rows.length > 0 && (
          <div className="kc-grid">
            {rows.map((d) => <DownloadCard key={d.hash} download={d} />)}
          </div>
        )}
      </section>}
    </>
  );
}

/** A download this person asked for: how far it has got, and Watch once enough of it is here. */
function DownloadCard({ download: d }) {
  const watchable = d.libraryId && (d.isFinished || d.isWatchable);
  return (
    <ShowcaseCard
      className="kc-card"
      name={d.name}
      art={keptArt(d.imdbId)}
      onPick={() => { if (d.libraryId) navigate(moviePath(d.libraryId)); }}
      progress={d.isFinished ? 1 : d.progress}
      specs={[]}
      status={d.isFinished
        ? { tone: "success", icon: "check", label: "Downloaded" }
        : { tone: "active", live: true, icon: "download", label: d.summary, title: d.release }}
      action={watchable
        ? { label: "Watch", onClick: () => navigate(watchHref(d.libraryId)) }
        : d.libraryId ? { label: "View" } : null} />
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
  { id: "available", title: "A title I'm waiting for can be downloaded", sub: "Anything on your Notify me list." },
  { id: "ready", title: "A download I started is ready to watch", sub: "Includes partial season downloads." },
  { id: "leaving", title: "Something I watched leaves tomorrow", sub: "One push per title, the day before it leaves." },
  { id: "kept", title: "Someone keeps or un-keeps a title I watched", sub: "Off by default, since it can get chatty." },
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

// The languages a film is commonly offered in. A stored language outside this list is added to it,
// so a choice made elsewhere is never shown as something else.
const LANGUAGES = ["en", "es", "fr", "de", "it", "pt", "ca", "nl", "pl", "ru", "ja", "ko", "zh", "ar", "tr"];
const QUALITIES = [{ value: "", label: "Auto" }, { value: "1080", label: "1080p" }, { value: "720", label: "720p" }];

const languageName = (code) => {
  try { return new Intl.DisplayNames(undefined, { type: "language" }).of(code) || code; } catch { return code; }
};

/** A language picker's options: `none` first, then the common languages and whatever is stored. */
const languages = (current, none) => [
  { value: "", label: none },
  ...[...new Set([...LANGUAGES, ...(current ? [current] : [])])].map((code) => ({ value: code, label: languageName(code) })),
];
const orNull = (value) => value || null;

/**
 * This person's playback preferences as stored, and changing them: `change` saves a patch at once,
 * `changeSoon` shows it at once and saves once the changing pauses, since a slider dragged across its
 * range is one decision.
 */
function usePreferences() {
  const stored = useLoad(() => api.preferences(), []);
  const [saved, setSaved] = React.useState(null);
  const pending = React.useRef(null);
  const preferences = saved || stored.data;

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
  return { preferences, stored, change, changeSoon };
}

/** How a film starts for this person: its audio language, its quality, and whether intros are skipped. */
function Playback() {
  const { preferences, stored, change } = usePreferences();
  if (!preferences) return <Failed error={stored.error} onRetry={stored.reload} />;

  const qualities = preferences.qualityHeight && !QUALITIES.some((q) => q.value === String(preferences.qualityHeight))
    ? [...QUALITIES, { value: String(preferences.qualityHeight), label: `${preferences.qualityHeight}p` }]
    : QUALITIES;

  return (
    <SettingsSection icon="play" title="Playback">
      <SettingsRow icon="audio-lines" title="Audio" sub="Original is the language it was made in.">
        <Select value={preferences.audioLanguage || ""} options={languages(preferences.audioLanguage, "Original")}
          aria-label="Audio language" onChange={(e) => change({ audioLanguage: orNull(e.target.value) })} />
      </SettingsRow>
      <SettingsRow icon="gauge" title="Quality" sub="Auto lets the connection decide.">
        <div className="kc-segmented" role="radiogroup" aria-label="Quality">
          {qualities.map((q) => {
            const on = (preferences.qualityHeight ? String(preferences.qualityHeight) : "") === q.value;
            return (
              <button key={q.value} type="button" role="radio" aria-checked={on} className={"kc-segmented__option" + (on ? " is-on" : "")}
                onClick={() => change({ qualityHeight: q.value ? Number(q.value) : null })}>{q.label}</button>
            );
          })}
        </div>
      </SettingsRow>
      <SettingsRow icon="skip-forward" title="Skip intros and recaps automatically">
        <Toggle on={Boolean(preferences.skipIntros)} label="Skip intros and recaps automatically" onChange={(on) => change({ skipIntros: on })} />
      </SettingsRow>
    </SettingsSection>
  );
}

/** Subtitles for this person: the language a film starts in, and how they are drawn, with a preview. */
function SubtitleLook() {
  const { preferences, stored, change, changeSoon } = usePreferences();
  if (!preferences) return <Failed error={stored.error} onRetry={stored.reload} />;

  return (
    <>
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
    </>
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
 * password and every session are the provider's, on its account page.
 */
function Account({ session }) {
  const user = session && session.user;
  const where = useLoad(() => accountUrl(), []);
  const url = where.data;

  return (
    <SettingsSection icon="user-cog" title="Account">
      <SettingsRow icon="user" title={user ? user.name : "Signed in"} sub={user && user.username ? "@" + user.username : null} />
      <SettingsRow icon="external-link" title="Managed outside Krystal Cinema"
        sub={url ? `Your name, password and sessions are kept at ${new URL(url).host}.` : "Your name, password and sessions are kept by your account provider."}>
        <a className="kc-btn" href={url || undefined} aria-disabled={!url}>
          <Icon name="external-link" size={16} />Manage account
        </a>
      </SettingsRow>
    </SettingsSection>
  );
}

