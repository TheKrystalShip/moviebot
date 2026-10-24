import React from "react";
import { Button, Icon, List, ListRow, NotificationSettings, RadioGroup, Select, SettingsRow, SettingsSection, PosterCard, SubTabs, ThemePicker, Toggle, toast } from "@thekrystalship/krystal";
import { notificationsClient } from "../notifications.js";
import { api, episodeCode, filmName, posterUrl } from "../api.js";
import { useLoad } from "../data.js";
import { languageOptions } from "../languages.js";
import { accountUrl, can, useCan, useSession } from "../session.js";
import { leave } from "../leave.js";
import { PROFILE_TAB_FACES } from "../nav.js";
import { PROFILE_TABS, moviePath, profilePath, showPath, wantsPath } from "../paths.js";
import { useOnline } from "../connectivity.js";
import { keptPreferences } from "../offline/preferences.js";
import { offlineSupported } from "../offline/store.js";
import { handheld } from "../offline/choose.js";
import { Off } from "./Offline.jsx";
import { linkProps, navigate } from "../router.js";
import { ago, airDay } from "../time.js";
import { Empty, Failed, PageHead, watchHref } from "../components/Film.jsx";
import { TabStage } from "../components/PageStage.jsx";
import { CardsSkeleton, keptArt } from "../components/TitleCards.jsx";
import { FilterField, useTitleFilter } from "../components/FilterField.jsx";
import { Cues } from "../player/Cues.jsx";
import { SubtitleStyleEditor } from "../player/SubtitleStyleEditor.jsx";
import { BufferAheadLevels, bufferAhead, setBufferAhead } from "../player/engine/media/bufferAhead.ts";
import { onTv } from "../tv/tv.js";
import { inActivity } from "../discord/launch.js";

// The tabs that show what only the server holds. With no connection they are drawn disabled; the
// others show what the device keeps (Playback and Subtitles, read-only) or the browser's own (Appearance).
const SERVER_TABS = new Set(["history", "following", "notifications"]);

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

  React.useEffect(() => {
    if (tab !== (named || PROFILE_TABS[0])) navigate(profilePath(tab), { replace: true });
  }, [tab, named]);

  const wants = (
    <List className="kc-profile__wants">
      <ListRow lead={<Icon name="bookmark" size={18} />} title="Want to watch" aside={<Icon name="chevron-right" size={18} />}
        href={linkProps(wantsPath).href} onOpen={linkProps(wantsPath).onClick} />
    </List>
  );
  return (
    <div className="kc-page kc-profile">
      <PageHead title={user ? user.name : "Profile"} sub={user && user.username ? "@" + user.username : null}>
        {!inActivity && <Button size="lg" icon="log-out" className="kc-head__action" onClick={leave}>Sign out</Button>}
      </PageHead>
      {online ? wants : <Off>{wants}</Off>}
      <SubTabs tabs={TABS.map((t) => ({ ...t, disabled: !online && SERVER_TABS.has(t.id) }))} active={tab}
        onChange={(id) => navigate(profilePath(id), { replace: true })} />
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

export function panel(tab, session) {
  switch (tab) {
    case "following": return <Following />;
    case "notifications": return <NotificationSettings client={notificationsClient} />;
    case "playback": return <Playback />;
    case "subtitles": return <SubtitleLook />;
    case "appearance": return <Appearance />;
    case "account": return <Account session={session} />;
    default: return <History />;
  }
}

/**
 * What this person watched, newest first, and how far they got, searched by name and narrowed by the
 * filter drawer over what each entry is: the library's title it names, so an entry whose title left
 * the library is known only as a film or a show. On a television the entries under the open drawer are inert.
 */
function History() {
  const history = useLoad(() => api.history(), []);
  const titles = useLoad(() => api.titles().catch(() => []), []);
  const byId = new Map((titles.data || []).map((t) => [t.id, t]));
  const rows = history.data || [];
  const entries = rows.map((row) => ({ ...(byId.get(row.titleId) || { kind: row.episode ? "show" : "film" }), row }));
  const filter = useTitleFilter(entries, { fixed: [], named: (e) => e.row.name || "" });

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
      {rows.length > 0 && <FilterField placeholder="Search what you watched" {...filter.field} />}
      {rows.length > 0 && filter.shown.length === 0 && <Empty title="Nothing here">Nothing matches this search and these filters.</Empty>}
      {filter.shown.length > 0 && (
        <div className="kc-grid" inert={filter.drawer.open && onTv ? "" : undefined}>
          {filter.shown.map(({ row }) => <HistoryCard key={row.titleId} row={row} title={byId.get(row.titleId)} onForget={forget} />)}
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
    <PosterCard
      name={name}
      art={poster ? `url("${poster}")` : null}
      onPick={() => navigate(moviePath(row.titleId))}
      progress={fraction}
      facts={[
        { icon: "star", value: d && d.rating ? d.rating.toFixed(1) : null, label: "Rating" },
        { value: (title && title.film && title.film.year) || null, label: "Year" },
        { value: row.finished ? "Watched" : `${Math.round(100 * fraction)}% watched`, label: "Watched" },
      ]}
      status={{ icon: "history", label: ago(row.lastWatchedAt), title: new Date(row.lastWatchedAt).toLocaleString() }}
      action={ready
        ? { label: row.finished ? "Watch again" : "Resume", icon: row.finished ? "rotate-ccw" : "play", onClick: () => navigate(watchHref(row.titleId, null, row.finished ? 0 : row.positionSeconds)) }
        : undefined}
      tools={[{ icon: "x", label: `Remove ${row.name} from your history`, tone: "danger", onClick: () => onForget(row) }]} />
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
              <PosterCard
                key={row.imdbId}
                name={row.title}
                art={row.posterUrl ? `url("${row.posterUrl}")` : keptArt(row.imdbId, "show")}
                onPick={() => navigate(showPath(row.imdbId))}
                facts={[
                  { value: next ? "Next " + episodeCode({ season: next.season, number: next.episode }) : null, label: "Next episode" },
                  { value: next ? airDay(next.airDate) : null, label: "Airs" },
                ]}
                status={{ icon: "bell", label: `Since ${ago(row.since)}`, title: next && next.name ? next.name : undefined }}
                tools={tools} />
            );
          })}
        </div>
      )}
    </section>
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
    <>
    <fieldset className="kc-fieldset" disabled={fixed}>
      <SettingsSection icon="play" title="Playback">
        <SettingsRow icon="audio-lines" title="Audio" sub="Original is the language it was made in.">
          <Select value={preferences.audioLanguage || ""} options={languages(preferences.audioLanguage, "Original")}
            aria-label="Audio language" onChange={(e) => change({ audioLanguage: orNull(e.target.value) })} />
        </SettingsRow>
        <SettingsRow controlsBelow icon="gauge" title="Quality" sub="Auto lets the connection decide.">
          <Heights label="Quality" options={qualities} value={preferences.qualityHeight}
            onChange={(height) => change({ qualityHeight: height })} />
        </SettingsRow>
        <SettingsRow icon="skip-forward" title="Skip intros and recaps automatically">
          <Toggle on={Boolean(preferences.skipIntros)} label="Skip intros and recaps automatically" onChange={(on) => change({ skipIntros: on })} />
        </SettingsRow>
        {handheld() && offlineSupported() && can(session, "cinema:offline") && (
          <SettingsRow controlsBelow icon="hard-drive-download" title="Download quality" sub="Auto downloads the smallest.">
            <Heights label="Download quality" options={OFFLINE_QUALITIES} value={preferences.offlineQualityHeight}
              onChange={(height) => change({ offlineQualityHeight: height })} />
          </SettingsRow>
        )}
      </SettingsSection>
    </fieldset>
    <DeviceBuffering />
    </>
  );
}

/** What each setting fetches ahead of the playhead and roughly what it costs, said as a person would weigh it. */
const WATCHING = "Watching uses roughly 1 GB an hour at 720p, half that at 480p.";
const BUFFER_AHEAD_SUBS = {
  dynamic: "About 3 minutes ahead, and more kept on this device while the connection is fast or keeps dropping, "
    + "up to the rest of the episode and the start of the next; no more with data saver on. Roughly 1 to 1.7 GB an hour.",
  light: `About 30 seconds ahead. ${WATCHING}`,
  standard: `About 3 minutes ahead. ${WATCHING}`,
  ahead: "The rest of the episode and the start of the next, kept on this device as you watch and deleted once "
    + "watched. It fetches 480p first, then 720p: roughly 1.7 GB an hour.",
};

/**
 * How far ahead this device fetches a film. It is the device's own, kept in this browser, so it is
 * changed here with or without a connection.
 */
function DeviceBuffering() {
  const [kept, setLevel] = React.useState(bufferAhead);
  // A level that keeps the film on the device is Standard where the device can keep nothing.
  const level = BufferAheadLevels[kept].device && !offlineSupported() ? "standard" : kept;
  const choose = (next) => {
    setBufferAhead(next);
    setLevel(next);
  };
  return (
    <SettingsSection icon="smartphone" title="This device">
      <SettingsRow controlsBelow icon="download" title="Buffer ahead" sub={BUFFER_AHEAD_SUBS[level]}>
        <RadioGroup variant="segmented" fill label="Buffer ahead" value={level}
          options={Object.entries(BufferAheadLevels)
            .filter(([, { device }]) => !device || offlineSupported())
            .map(([value, { label }]) => ({ value, label }))}
          onChange={choose} />
      </SettingsRow>
    </SettingsSection>
  );
}

/** A choice of heights, Auto (`""`) among them: `value` is the stored height or null. */
function Heights({ label, options, value, onChange }) {
  return (
    <RadioGroup variant="segmented" fill className="kc-heights" label={label} value={value ? String(value) : ""}
      options={options.map((q) => ({ value: q.value, label: q.label }))}
      onChange={(next) => onChange(next ? Number(next) : null)} />
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
 * Where this person's account lives. Cinema keeps no sign-in of its own: the name, the
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
      <SettingsRow icon="external-link" title="Managed outside Cinema"
        sub={url ? `Your name, password and sessions are kept at ${new URL(url).host}.` : "Your name, password and sessions are kept by your account provider."}>
        {onTv
          ? url && <span className="kc-account-address">{url.replace(/^https?:\/\//, "")}</span>
          : <Button icon="external-link" href={url || ""} disabled={!url || !online}>Manage account</Button>}
      </SettingsRow>
    </SettingsSection>
  );
}

