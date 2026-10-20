import React from "react";
import {
  AppShell, Dock, DockFab, NavItem, Sidebar, SidebarAccount, SidebarGroup, TabBar, Toasts,
  useDock, useStoredFlag,
} from "@thekrystalship/krystal";
import markUrl from "@thekrystalship/krystal/assets/tks-mark.png";
import { can, restore, signIn, useEnded, useSession } from "./session.js";
import { leave } from "./leave.js";
import { goBack, navigate, useRoute } from "./router.js";
import { CinemaAssistant } from "./CinemaAssistant.jsx";
import { PLACES } from "./nav.js";
import { ANIME, OFFLINE_FLAG, chatPath, discoverPath, homePath, libraryPath, moviePath, profilePath, roomPath, signedInPath } from "./paths.js";
import { useOnline } from "./connectivity.js";
import { sendProgress } from "./offline/progress.js";
import { useOfflineSync } from "./offline/sync.js";
import { Assistant } from "./pages/Assistant.jsx";
import { FirstSignIn, Off, Unreachable } from "./pages/Offline.jsx";
import { SignOutDialog } from "./components/SignOutDialog.jsx";
import { Home } from "./pages/Home.jsx";
import { Library } from "./pages/Library.jsx";
import { Title } from "./pages/Title.jsx";
import { Show } from "./pages/Show.jsx";
import { CatalogueFilm } from "./pages/CatalogueFilm.jsx";
import { Discover, DISCOVER_FIELD } from "./pages/Discover.jsx";
import { Wants } from "./pages/Wants.jsx";
import { Profile } from "./pages/Profile.jsx";
import { ActivityProblem, Dark, NoAccess, Waiting } from "./pages/SignIn.jsx";
import { Pairing } from "./pages/Pairing.jsx";
import { followPairings, forgetPairings } from "./remote.js";
import { About } from "./pages/About.jsx";
import { NotFound } from "./pages/NotFound.jsx";
import { SiteFooter } from "./components/SiteFooter.jsx";
import { BellNavItem } from "./components/Bell.jsx";
import { PageStage } from "./components/PageStage.jsx";
import { settleSplash } from "./splash.js";
import { Trail } from "./components/Trail.jsx";
import { CallBar } from "./components/CallBar.jsx";
import { RemoteDock } from "./components/RemoteDock.jsx";
import { ReplaceSheet } from "./components/ReplaceSheet.jsx";
import { activityRoom, inActivity } from "./discord/launch.js";
import { api } from "./api.js";
import { onTv } from "./tv/tv.js";
import { startScreenConnection, stopScreenConnection } from "./tv/screen.js";
import { TvFrame } from "./tv/TvFrame.jsx";
import { TvSignIn } from "./tv/TvSignIn.jsx";

// The player carries hls.js, the hub client and Discord's SDK; only a room pays for them.
const SiteRoom = React.lazy(() => import("./player/PlayerPage.jsx").then((m) => ({ default: m.SiteRoom })));
const GuestRoom = React.lazy(() => import("./player/PlayerPage.jsx").then((m) => ({ default: m.GuestRoom })));

const UNANSWERED = "The sign-in service is not answering. Try again in a moment.";
const ENDED = "Your session ended.";
const ENDED_IN_ACTIVITY = "Your session ended. Close the Activity and start it again.";

const place = (id) => ({ id, ...PLACES[id] });
const HOME = ["home", "discover", "wants"].map(place);
const LIBRARY = ["movies", "tv", "anime"].map(place);

// A phone's way around: a tab for each place, the assistant's page among them.
const TABS = [
  { ...place("home") },
  { id: "library", path: PLACES.movies.path, icon: "clapperboard", label: "Library" },
  { ...place("discover") },
  { id: "assistant", path: chatPath, icon: "bot", label: "Assistant" },
  { id: "me", path: PLACES.profile.path, icon: "user", label: "Me" },
];

// What a page lights: the sidebar item and the phone's tab it belongs to, by the route's view. A view
// not listed lights neither.
const LIT = {
  home: { item: "home", tab: "home" },
  movies: { item: "movies", tab: "library" },
  movie: { item: "movies", tab: "library" },
  tv: { item: "tv", tab: "library" },
  show: { item: "tv", tab: "library" },
  anime: { item: "anime", tab: "library" },
  "anime-films": { item: "anime", tab: "library" },
  discover: { item: "discover", tab: "discover" },
  "discover-film": { item: "discover", tab: "discover" },
  "discover-show": { item: "discover", tab: "discover" },
  profile: { tab: "me" },
  wants: { item: "wants", tab: "me" },
  chat: { tab: "assistant" },
};

// The places that need the server, drawn disabled with no connection. Every other place draws what the
// device holds.
const NEEDS_SERVER = new Set(["discover", "wants", "assistant"]);

/**
 * Krystal Cinema: the shelf, the films on their way and on their way out, and the assistant. Every page
 * needs somebody signed in: one opened with nothing held leaves for the provider at once, and is where
 * the browser returns once they are. A session that has ended is the one thing drawn in place of a
 * page, with the way back offered, never followed; a session being restored or recovered draws
 * nothing of its own. Somebody signed in whose account may not watch is told there is nothing here for
 * them yet.
 *
 * A television never leaves for the provider: with nothing held it shows its sign-in code. A session
 * that ends there puts the code over the page, which stays drawn and out of reach beneath it and is
 * where approval returns. A room is not kept under it: its position is the room's, and joining again
 * after approval picks it up where it is.
 *
 * The Discord Activity is the same site, signed in through Discord (`discord/activity.js`) and never
 * through the provider. Its one room is the call's: every film is played there, the player is where
 * the call's room is at, and the call moving on to something else takes everybody browsing back to it
 * (`components/CallBar.jsx`).
 */
export function App() {
  const session = useSession();
  const ended = useEnded();
  const route = useRoute();
  // A guest link answers to no session: it needs nobody to be anybody.
  const embedded = route.view === "guest";
  const restoredHere = useRestored(!inActivity && !embedded);
  const activity = useActivity();
  const restored = inActivity ? activity.ready : restoredHere;
  const online = useOnline();
  // The page the Activity's player was opened from. Discord's frame does not go back through its
  // history, so the player's way back is this page, navigated to.
  const browsed = React.useRef(homePath);
  if (route.view !== "room" && route.view !== "embed") browsed.current = route.href;

  // Whatever this draws is on screen once committed, and lets the splash go unless it holds it.
  React.useEffect(() => settleSplash());

  const [waiting, setWaiting] = React.useState(null);
  // The last session held, which a television keeps its page drawn under while it signs in again.
  const held = React.useRef(null);
  if (session) held.current = session;
  // The rooms this page has opened, which stay open when the connection goes: carrying on from the
  // device is the player's to offer.
  const opened = React.useRef(new Set());

  // What was watched from the device goes to the API whenever somebody is signed in and it can be
  // reached: on opening, and each time the connection comes back.
  const sub = session && session.user.id;
  React.useEffect(() => {
    if (sub && online && !embedded && !inActivity) void sendProgress();
  }, [sub, online, embedded]);
  // A television is a screen to the server for as long as it is signed in, in a room or not.
  React.useEffect(() => {
    if (!onTv || !sub) return undefined;
    startScreenConnection();
    return stopScreenConnection;
  }, [sub]);
  // A phone or a browser with a television paired hears it while it can reach the server.
  React.useEffect(() => {
    if (onTv || inActivity || embedded || !sub || !online) return undefined;
    void followPairings().catch(() => {});
    return forgetPairings;
  }, [sub, online, embedded]);
  // The saves on this device set beside the API's records, and the ones under way carried on.
  useOfflineSync(!embedded && !inActivity && !onTv);

  React.useEffect(() => {
    // With no connection nothing goes to the provider: a device with no session says a first sign-in
    // needs one, and signs in once the connection is back.
    if (!restored || session || ended || route.view === "signed-in" || embedded || inActivity || onTv || !online) {
      setWaiting(null);
      return;
    }
    let live = true;
    void signIn(route.href).then((left) => { if (live && !left) setWaiting({ note: UNANSWERED }); });
    return () => { live = false; };
  }, [restored, session, ended, route.view, route.href, embedded, online]);

  if (route.view === "guest") return <React.Suspense fallback={<Dark />}><GuestRoom code={route.arg} /></React.Suspense>;
  // A television's code is scanned by a phone; a television or the Activity opening one has nothing to pair.
  if (route.view === "pair" && (onTv || inActivity)) return <Away to={homePath} />;
  if (inActivity) {
    if (activity.error) return <ActivityProblem note={activity.error} />;
    if (!activity.ready) return <Dark />;
    if (!session) return <ActivityProblem note={ENDED_IN_ACTIVITY} />;
    if (!can(session, "cinema:watch")) return <NoAccess />;
    if (route.view === "embed") return <ActivityLanding />;
    if (route.view === "room") {
      return (
        <React.Suspense fallback={<Dark />}>
          <SiteRoom room={activityRoom} title={route.query.get("title")}
            onBack={() => navigate(browsed.current)} onEmpty={() => navigate(homePath, { replace: true })} />
        </React.Suspense>
      );
    }
    return <Frame session={session} route={route} />;
  }
  if (route.view === "embed") return <Away to={homePath} />;
  if (route.view === "signed-in") return <Dark />;
  const covered = onTv && restored && !session && ended && held.current && route.view !== "room";
  if (onTv && restored && !session && !covered) return <TvSignIn />;
  if (covered || (onTv && session && can(session, "cinema:watch") && route.view !== "room")) {
    const shown = session || held.current;
    // One tree whether covered or not, so the page under the code is the page approval returns to.
    return (
      <>
        <div inert={covered ? "" : undefined} aria-hidden={covered ? "true" : undefined} style={{ display: "contents" }}>
          <TvFrame key={shown.user.id} session={shown} route={route} held={!!covered} />
        </div>
        {covered && <TvSignIn over />}
      </>
    );
  }
  if (restored && ended) return <Waiting note={ENDED} returnTo={route.href} />;
  if (restored && !session && !online && !onTv) return <FirstSignIn />;
  if (!restored || !session) return waiting ? <Waiting note={waiting.note} returnTo={route.href} /> : <Dark />;
  if (!can(session, "cinema:watch")) return <><NoAccess onSignOut={leave} /><SignOutDialog /></>;
  if (route.view === "pair") return <Pairing code={route.arg} />;
  // A room fills the screen: the film is the page, and the way out is its back button. A saved film
  // played from the device (`?offline=1`) is the player's alone; a room opened with no connection
  // says it is out of reach.
  const fromDevice = route.query.get(OFFLINE_FLAG) === "1";
  if (route.view === "room" && (online || fromDevice || opened.current.has(route.arg))) {
    if (!fromDevice) opened.current.add(route.arg);
    return (
      <React.Suspense fallback={<Dark />}>
        <SiteRoom key={route.arg} room={route.arg} title={route.query.get("title")} offline={fromDevice}
          onBack={() => goBack(route.query.get("title") ? moviePath(route.query.get("title")) : homePath)}
          onEmpty={() => navigate(libraryPath("film"))} />
      </React.Suspense>
    );
  }
  return <Frame session={session} route={route} />;
}

function Frame({ session, route }) {
  const online = useOnline();
  const [collapsed, setCollapsed] = useStoredFlag("cinema:sidebar:collapsed");
  const dock = useDock({ storageKey: "cinema:dock" });
  const mainRef = React.useRef(null);
  useDiscoverKey(route.view === "discover");

  // A new page starts at its top, from the moment it is shown.
  const toTop = React.useCallback(() => {
    if (mainRef.current) mainRef.current.scrollTop = 0;
  }, []);

  // A film's or a show's page under Anime lights Anime rather than its kind's shelf.
  const lit = (route.category === ANIME && route.view !== "discover-show" ? LIT.anime : LIT[route.view]) || {};
  const go = (path) => navigate(path);
  const item = (n) => (online || !NEEDS_SERVER.has(n.id)
    ? <NavItem key={n.id} icon={n.icon} label={n.label} active={lit.item === n.id} onClick={() => go(n.path)} />
    : <Off key={n.id}><NavItem icon={n.icon} label={n.label} /></Off>);
  // A tab goes to its page. The tab of the page already open takes it back to its top.
  const tab = (t) => ({
    id: t.id, icon: t.icon, label: t.label, disabled: !online && NEEDS_SERVER.has(t.id),
    active: lit.tab === t.id && (online || !NEEDS_SERVER.has(t.id)),
    onClick: () => {
      if (route.path === t.path && mainRef.current) mainRef.current.scrollTo({ top: 0, behavior: "smooth" });
      else go(t.path);
    },
  });
  // The dock is the assistant beside a page, on a screen wide enough to have one. A phone reaches the
  // assistant by its tab, as a page; the assistant's own page has no dock beside it.
  const docked = dock.desktop && route.view !== "chat";

  return (
    <>
      <AppShell
        collapsed={dock.desktop && collapsed}
        drawerOpen={false}
        onDrawer={noDrawer}
        dockPush={docked && dock.pushing ? dock.width : 0}
        mainRef={mainRef}
        mainClassName="kc-main"
        sidebar={
          <Sidebar
            mark={markUrl}
            wordmark="Krystal Cinema"
            open={false}
            collapsed={dock.desktop && collapsed}
            onToggleCollapse={() => setCollapsed((c) => !c)}
            closeNav
            foot={<>
              {online ? <BellNavItem /> : <Off><BellNavItem /></Off>}
              <SidebarAccount
                user={session.user}
                sub={session.user.username ? "@" + session.user.username : null}
                detail={session.user.username ? "@" + session.user.username : null}
                collapsed={dock.desktop && collapsed}
                items={[
                  { icon: "user", label: "Profile", onClick: () => go(profilePath()) },
                  // In the Activity the session is the Discord account in it, which only leaving the Activity ends.
                  ...(inActivity ? [] : [{ icon: "log-out", label: "Sign out", danger: true, onClick: leave }]),
                ]} />
            </>}>
            <SidebarGroup>{HOME.map(item)}</SidebarGroup>
            <SidebarGroup>{LIBRARY.map(item)}</SidebarGroup>
          </Sidebar>
        }
        aside={docked && <>
          <Dock dock={dock}>
            {dock.open && (online
              ? <CinemaAssistant session={session} docked dock={dock} onClose={() => dock.setOpen(false)} />
              : <Unreachable place="assistant" onClose={() => dock.setOpen(false)} />)}
          </Dock>
          {!dock.open && online && <DockFab onClick={() => dock.setOpen(true)} />}
        </>}
        tabBar={<TabBar items={TABS.map(tab)} />}>
        {inActivity && <CallBar />}
        <PageStage route={route} render={(r) => <Reachable route={r} />} above={(r) => <Trail route={r} />} onShown={toTop} />
        {route.view !== "chat" && <SiteFooter />}
      </AppShell>
      {!inActivity && <RemoteDock />}
      {!inActivity && <ReplaceSheet />}
      <Toasts />
      <SignOutDialog />
    </>
  );
}

function noDrawer() {}

/**
 * A page as the connection allows when it was opened. Opened with no connection it is the same page
 * drawn from the device (`fromDevice`), and drawn again from the server once the connection is back.
 * Opened with one, it stays as it is when the connection goes: what it shows stands, and its controls
 * that ask the server are disabled where they are drawn.
 */
function Reachable({ route }) {
  const online = useOnline();
  const [fromDevice, setFromDevice] = React.useState(!online);
  React.useEffect(() => {
    if (online && fromDevice) setFromDevice(false);
  }, [online, fromDevice]);
  return <Page key={fromDevice ? "device" : "server"} route={route} fromDevice={fromDevice} />;
}

/**
 * The Discord Activity starting: `{ ready, error }`. Outside the Activity it is never ready and
 * nothing is loaded; inside it, the SDK and the sign-in run once, before anything is drawn.
 */
function useActivity() {
  const [state, setState] = React.useState({ ready: false, error: null });
  React.useEffect(() => {
    if (!inActivity) return;
    let live = true;
    import("./discord/activity.js")
      .then((m) => m.startActivity())
      .then(() => { if (live) setState({ ready: true, error: null }); })
      .catch((error) => { if (live) setState({ ready: false, error: error instanceof Error ? error.message : "Discord sign-in failed." }); });
    return () => { live = false; };
  }, []);
  return state;
}

/**
 * Where the Activity opens: at the player when the call's room already holds something, or is private
 * to this viewer, whose door is there; on the site's first page otherwise.
 */
function ActivityLanding() {
  React.useEffect(() => {
    let live = true;
    api.room(activityRoom)
      .then((push) => !!(push && push.state && (push.state.titleId || push.state.preparing)), (error) => error.status === 403)
      .then((watching) => { if (live) navigate(watching ? roomPath(activityRoom) : homePath, { replace: true }); });
    return () => { live = false; };
  }, []);
  return <Dark />;
}

/** An address that is somewhere else: replaced by `to` as soon as it is drawn. */
function Away({ to }) {
  React.useEffect(() => { navigate(to, { replace: true }); }, [to]);
  return <Dark />;
}

/**
 * Whether the session this browser holds has been picked up, renewing it first when its token has run
 * out. Until it has, nothing is shown, so a signed-in person is never sent through the door. The
 * callback's own address is never left on screen: a completed sign-in goes on to the page that was
 * asked for, and anything else goes Home, where a missing session signs in again and an ended one is
 * drawn.
 */
function useRestored(wanted) {
  const [restored, setRestored] = React.useState(false);
  React.useEffect(() => {
    if (!wanted || restored) return;
    let live = true;
    const callback = window.location.pathname === signedInPath;
    restore().catch(() => null).then((returnTo) => {
      if (!live) return;
      if (callback || returnTo) navigate(returnTo || homePath, { replace: true });
      setRestored(true);
    });
    return () => { live = false; };
  }, [wanted, restored]);
  return restored;
}

/**
 * `/`, or Ctrl+K and Cmd+K, from anywhere but a field being typed in: to Discover with its field
 * focused, which the page does itself when it opens with a keyboard at hand.
 */
function useDiscoverKey(onDiscover) {
  React.useEffect(() => {
    const onKey = (e) => {
      const slash = e.key === "/" && !e.ctrlKey && !e.metaKey && !e.altKey;
      const k = e.key.toLowerCase() === "k" && (e.ctrlKey || e.metaKey) && !e.altKey && !e.shiftKey;
      if (!slash && !k) return;
      const target = e.target;
      const typing = target && (target.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(target.tagName));
      if (typing) return;
      e.preventDefault();
      if (onDiscover) {
        const field = document.querySelector(`.${DISCOVER_FIELD} input`);
        if (field) { field.focus(); field.select(); }
      } else {
        navigate(discoverPath());
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onDiscover]);
}

/**
 * The page a route names. `fromDevice` is the page opened with no connection: the pages the device can
 * draw are drawn from it, Discover says it is out of reach, and so does every page only the server
 * can give.
 */
function Page({ route, fromDevice }) {
  switch (route.view) {
    case "home": return <Home fromDevice={fromDevice} />;
    case "movies": return <Library key="film" kind="film" fromDevice={fromDevice} />;
    case "tv": return <Library key="show" kind="show" fromDevice={fromDevice} />;
    case "anime": return <Library key="anime-show" kind="show" category={ANIME} fromDevice={fromDevice} />;
    case "anime-films": return <Library key="anime-film" kind="film" category={ANIME} fromDevice={fromDevice} />;
    case "movie": return <Title key={route.arg} id={route.arg} category={route.category} fromDevice={fromDevice} />;
    case "show":
    case "discover-show":
      return <Show key={route.arg} imdbId={route.arg} season={route.season} inLibrary={route.view === "show"} category={route.category} fromDevice={fromDevice} />;
    case "profile": return <Profile tab={route.tab} fromDevice={fromDevice} />;
    case "about": return <About />;
    case "notfound": return <NotFound />;
    default: break;
  }
  if (fromDevice) {
    const place = route.view === "discover" || route.view === "discover-film" ? "discover" : route.view === "chat" ? "assistant" : null;
    return <Unreachable place={place} />;
  }
  switch (route.view) {
    case "discover-film": return <CatalogueFilm key={route.arg} imdbId={route.arg} />;
    case "discover": return <Discover query={route.query} />;
    case "wants": return <Wants />;
    case "chat": return <Assistant />;
    default: return <NotFound />;
  }
}
