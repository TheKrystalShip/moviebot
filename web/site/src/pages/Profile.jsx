import React from "react";
import { Icon, Select, SettingsRow, SettingsSection, SubTabs, ThemePicker, Toggle, pushBrowser, toast } from "@thekrystalship/krystal-ui";
import { KIND } from "../notifications.js";
import { api, episodeCode, filmName } from "../api.js";
import { useLoad } from "../data.js";
import { signOut, useSession } from "../session.js";
import { PROFILE_TAB_FACES } from "../nav.js";
import { moviePath, profilePath, savedPath, showPath, signInPath } from "../paths.js";
import { linkProps, navigate } from "../router.js";
import { ago, airDay } from "../time.js";
import { Empty, Failed, PageHead, PosterArt } from "../components/Film.jsx";
import { KeptPoster } from "../components/FilmDetails.jsx";
import { Cues } from "../player/Cues.jsx";
import { SubtitleStyleEditor } from "../player/SubtitleStyleEditor.jsx";

const TABS = PROFILE_TAB_FACES;

/**
 * Who is signed in and everything that is theirs: what they watched, what they asked for, how they
 * like to watch, and the devices holding their sign-in. The tab is in the address, so a tab can be
 * linked to and reloaded.
 */
export function Profile({ tab: named }) {
  const tab = named || TABS[0].id;
  const session = useSession();
  const user = session && session.user;

  return (
    <div className="kc-page kc-profile">
      <PageHead title={user ? user.name : "Profile"} sub={user ? "@" + user.username : null}>
        <button type="button" className="kc-btn kc-head__action" onClick={() => { signOut(); navigate(signInPath, { replace: true }); }}>
          <Icon name="log-out" size={16} />Sign out
        </button>
      </PageHead>
      <a className="kc-row kc-row--link kc-profile__saved" {...linkProps(savedPath)}>
        <Icon name="bookmark" size={18} />
        <span className="kc-row__title">Saved</span>
        <Icon name="chevron-right" size={18} />
      </a>
      <SubTabs tabs={TABS} active={tab} onChange={(id) => navigate(profilePath(id), { replace: true })} />
      {tab === "history" && <History />}
      {tab === "requests" && <Requests user={user} />}
      {tab === "following" && <Following />}
      {tab === "notifications" && <Notifications />}
      {tab === "playback" && <Playback />}
      {tab === "sessions" && <Sessions />}
    </div>
  );
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
      {history.data && rows.length === 0 && <Empty icon="history" title="Nothing watched yet" />}
      <div className="kc-rows">
        {rows.map((row) => {
          const title = byId.get(row.titleId);
          const href = moviePath(row.titleId);
          const fraction = row.finished ? 1 : Math.min(1, row.positionSeconds / Math.max(1, row.durationSeconds));
          const how = row.finished ? "Watched" : `${Math.round(100 * fraction)}% watched`;
          return (
            <div key={row.titleId} className="kc-row">
              <a {...linkProps(href)}><PosterArt title={title || { id: row.titleId, title: row.name }} className="kc-row__thumb" /></a>
              <div className="kc-row__text">
                <a className="kc-row__title" {...linkProps(href)}>{title ? filmName(title) : row.name}</a>
                <span className="kc-row__sub">{how} · {ago(row.lastWatchedAt)}</span>
              </div>
              <span className={"kc-bar kc-history__bar" + (row.finished ? " is-done" : "")} aria-hidden="true">
                <span style={{ width: `${Math.round(100 * fraction)}%` }} />
              </span>
              <button type="button" className="kc-btn kc-btn--ghost kc-btn--icon" aria-label={`Remove ${row.name} from your history`} title="Remove from history" onClick={() => forget(row)}>
                <Icon name="x" size={15} />
              </button>
            </div>
          );
        })}
      </div>
    </section>
  );
}

/** The films this person is waiting on, and the downloads they asked for, under way or done. */
function Requests({ user }) {
  const wishes = useLoad(() => api.wishes(), []);
  const downloads = useLoad(() => api.downloads(), [], { every: 5000 });
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

  return (
    <>
      <section className="kc-shelf">
        <div className="kc-shelf__head"><h2>Waiting on</h2></div>
        <Failed error={wishes.error} onRetry={wishes.reload} />
        {wishes.data && mine.length === 0 && <Empty icon="bell" title="Nothing yet">A film that is not out yet can be waited on from Notify me.</Empty>}
        <div className="kc-rows">
          {mine.map((wish) => {
            const asked = wish.subscribers.find((s) => s.userId === user.id);
            return (
              <div key={wish.imdbId} className="kc-row">
                <KeptPoster imdbId={wish.imdbId} />
                <div className="kc-row__text">
                  <span className="kc-row__title">{wish.display}</span>
                  <span className="kc-row__sub">Asked {ago(asked.askedAt)}</span>
                </div>
                <button type="button" className="kc-btn kc-btn--ghost" onClick={() => cancel(wish)}>
                  <Icon name="bell-off" size={16} />Stop waiting
                </button>
              </div>
            );
          })}
        </div>
      </section>

      <section className="kc-shelf">
        <div className="kc-shelf__head"><h2>Downloads</h2></div>
        <Failed error={downloads.error} onRetry={downloads.reload} />
        {downloads.data && rows.length === 0 && <Empty icon="download" title="Nothing downloading" />}
        <div className="kc-rows">
          {rows.map((d) => (
            <div key={d.hash} className="kc-row">
              <span className={"kc-row__icon" + (d.isFinished ? "" : " kc-row__icon--live")}>
                <Icon name={d.isFinished ? "check" : "download"} size={16} />
              </span>
              <div className="kc-row__text">
                {d.libraryId
                  ? <a className="kc-row__title" {...linkProps(moviePath(d.libraryId))}>{d.name}</a>
                  : <span className="kc-row__title">{d.name}</span>}
                <span className="kc-row__sub">{d.summary}</span>
              </div>
            </div>
          ))}
        </div>
      </section>
    </>
  );
}

/** The shows this person follows, whether each is fetched when an episode airs, and unfollowing. */
function Following() {
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
      {follows.data && rows.length === 0 && <Empty icon="tv" title="Not following anything">Follow a show from its page to hear when a new episode is out.</Empty>}
      <div className="kc-rows">
        {rows.map((row) => {
          const href = showPath(row.imdbId);
          const next = row.nextEpisode;
          return (
            <div key={row.imdbId} className="kc-row">
              <a {...linkProps(href)}>
                {row.posterUrl
                  ? <img className="kc-row__thumb" src={row.posterUrl} alt="" loading="lazy" />
                  : <PosterArt title={{ id: row.imdbId, title: row.title }} className="kc-row__thumb" />}
              </a>
              <div className="kc-row__text">
                <a className="kc-row__title" {...linkProps(href)}>{row.title}</a>
                {next && <span className="kc-row__sub">Next episode: {episodeCode({ season: next.season, number: next.episode })} · {airDay(next.airDate)}</span>}
              </div>
              <label className="kc-follow__option kc-follow__option--row">
                <span>Download new episodes</span>
                <Toggle on={row.autoDownload} label={`Download new episodes of ${row.title} automatically`} onChange={(on) => setAuto(row, on)} />
              </label>
              <button type="button" className="kc-btn kc-btn--ghost" onClick={() => unfollow(row)}>
                <Icon name="bell-off" size={16} />Unfollow
              </button>
            </div>
          );
        })}
      </div>
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

/** How this person watches: the subtitle and audio language a film starts with, and its quality. */
function Playback() {
  const stored = useLoad(() => api.preferences(), []);
  const [saved, setSaved] = React.useState(null);
  const styleSave = React.useRef(null);
  const preferences = saved || stored.data;

  if (!preferences) return <Failed error={stored.error} onRetry={stored.reload} />;

  const change = async (patch) => {
    try {
      setSaved(await api.setPreferences({ ...preferences, ...patch }));
    } catch (error) {
      toast.fromError(error, "Could not save that");
    }
  };

  // The preview follows every change at once; the save waits for the choosing to pause, since a
  // slider dragged across its range is one decision.
  const styleChange = (subtitleStyle) => {
    const next = { ...preferences, subtitleStyle };
    setSaved(next);
    clearTimeout(styleSave.current);
    styleSave.current = setTimeout(() => {
      api.setPreferences(next).then(setSaved, (error) => toast.fromError(error, "Could not save that"));
    }, 600);
  };

  const languages = (current, none) => [
    { value: "", label: none },
    ...[...new Set([...LANGUAGES, ...(current ? [current] : [])])].map((code) => ({ value: code, label: languageName(code) })),
  ];
  const qualities = preferences.qualityHeight && !QUALITIES.some((q) => q.value === String(preferences.qualityHeight))
    ? [...QUALITIES, { value: String(preferences.qualityHeight), label: `${preferences.qualityHeight}p` }]
    : QUALITIES;
  const orNull = (value) => value || null;

  return (
    <>
      <SettingsSection icon="play" title="Playback">
        <SettingsRow icon="captions" title="Subtitles" sub="The language to start in, when there is one.">
          <Select value={preferences.subtitleLanguage || ""} options={languages(preferences.subtitleLanguage, "Off")}
            aria-label="Subtitle language" onChange={(e) => change({ subtitleLanguage: orNull(e.target.value) })} />
        </SettingsRow>
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
      <SettingsSection icon="captions" title="Subtitle style">
        <div className="kc-cue-style">
          <div className="kc-cue-preview" aria-hidden="true">
            <Cues style={preferences.subtitleStyle} sample />
          </div>
          <SubtitleStyleEditor style={preferences.subtitleStyle} onChange={(subtitleStyle) => styleChange(subtitleStyle)} />
        </div>
      </SettingsSection>
      <SettingsSection icon="palette" title="Appearance">
        <ThemePicker />
      </SettingsSection>
    </>
  );
}

/** The devices holding one of this person's sign-ins. */
function Sessions() {
  const sessions = useLoad(() => api.sessions(), []);
  const rows = sessions.data || [];
  const others = rows.filter((s) => !s.current);

  const signOutOne = async (row) => {
    try {
      await api.signOutDevice(row.id);
      sessions.reload();
    } catch (error) {
      toast.fromError(error, "Could not sign that device out");
    }
  };
  const signOutOthers = async () => {
    try {
      await api.signOutOtherDevices();
      toast.success("Signed out everywhere else.");
      sessions.reload();
    } catch (error) {
      toast.fromError(error, "Could not sign the other devices out");
    }
  };

  return (
    <section className="kc-shelf">
      <div className="kc-shelf__head">
        <h2>Signed in on</h2>
        {others.length > 0 && (
          <button type="button" className="kc-btn kc-btn--ghost kc-head__action" onClick={signOutOthers}>
            <Icon name="log-out" size={15} />Sign out everywhere else
          </button>
        )}
      </div>
      <Failed error={sessions.error} onRetry={sessions.reload} />
      <div className="kc-rows">
        {rows.map((row) => (
          <div key={row.id} className="kc-row">
            <span className="kc-row__icon"><Icon name="monitor-smartphone" size={16} /></span>
            <div className="kc-row__text">
              <span className="kc-row__title">{row.device}{row.current && <span className="kc-tag">This device</span>}</span>
              <span className="kc-row__sub">Active {ago(row.lastSeenAt)} · signed in {ago(row.issuedAt)}</span>
            </div>
            {!row.current && (
              <button type="button" className="kc-btn kc-btn--ghost" onClick={() => signOutOne(row)}>
                <Icon name="log-out" size={15} />Sign out
              </button>
            )}
          </div>
        ))}
      </div>
    </section>
  );
}
