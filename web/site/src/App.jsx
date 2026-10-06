import React from "react";
import {
  AppShell, Dock, DockFab, NavItem, Sidebar, SidebarAccount, SidebarGroup, TabBar, Toasts,
  useDock, useEdgeSwipe, useStoredFlag,
} from "@thekrystalship/krystal-ui";
import markUrl from "@thekrystalship/krystal-ui/assets/tks-mark.png";
import { beginSignIn, can, restore, sessionEnded, useSession } from "./session.js";
import { leave } from "./leave.js";
import { goBack, navigate, useRoute } from "./router.js";
import { CinemaAssistant } from "./CinemaAssistant.jsx";
import { PLACES } from "./nav.js";
import { discoverPath, homePath, libraryPath, moviePath, profilePath } from "./paths.js";
import { Home } from "./pages/Home.jsx";
import { Library } from "./pages/Library.jsx";
import { Title } from "./pages/Title.jsx";
import { Show } from "./pages/Show.jsx";
import { CatalogueFilm } from "./pages/CatalogueFilm.jsx";
import { Discover, DISCOVER_FIELD } from "./pages/Discover.jsx";
import { Notify } from "./pages/Notify.jsx";
import { ListPage, Saved } from "./pages/Saved.jsx";
import { Leaving } from "./pages/Leaving.jsx";
import { Profile } from "./pages/Profile.jsx";
import { Dark, NoAccess, SignInReturn, Waiting } from "./pages/SignIn.jsx";
import { About } from "./pages/About.jsx";
import { NotFound } from "./pages/NotFound.jsx";
import { SiteFooter } from "./components/SiteFooter.jsx";
import { BellNavItem } from "./components/Bell.jsx";
import { PageStage } from "./components/PageStage.jsx";
import { Trail } from "./components/Trail.jsx";
import { onBack as onTvBack, onTv } from "./tv/tv.js";
import { focusNavigation, pageShown } from "./tv/navigator.js";

// The player carries hls.js, the hub client and Discord's SDK; only a room pays for them.
const EmbedRoom = React.lazy(() => import("./player/PlayerPage.jsx").then((m) => ({ default: m.EmbedRoom })));
const SiteRoom = React.lazy(() => import("./player/PlayerPage.jsx").then((m) => ({ default: m.SiteRoom })));

const UNANSWERED = "The sign-in service is not answering. Try again in a moment.";
const ENDED = "Your session ended.";

const place = (id) => ({ id, ...PLACES[id] });
const HOME = ["home", "discover", "saved"].map(place);
const LIBRARY = ["movies", "tv"].map(place);
const LIFECYCLE = ["notify", "leaving"].map(place);

// A phone's way around: a tab for each place. The assistant's tab opens the dock rather than a page.
const TABS = [
  { ...place("home") },
  { id: "library", path: PLACES.movies.path, icon: "clapperboard", label: "Library" },
  { ...place("discover") },
  { id: "assistant", icon: "bot", label: "Assistant" },
  { id: "me", path: PLACES.profile.path, icon: "user", label: "Me" },
];

// What a page lights: the sidebar item and the phone's tab it belongs to, by the route's view. A view
// not listed lights neither.
const LIT = {
  home: { item: "home", tab: "home" },
  leaving: { item: "leaving", tab: "home" },
  movies: { item: "movies", tab: "library" },
  movie: { item: "movies", tab: "library" },
  tv: { item: "tv", tab: "library" },
  show: { item: "tv", tab: "library" },
  discover: { item: "discover", tab: "discover" },
  "discover-film": { item: "discover", tab: "discover" },
  "discover-show": { item: "discover", tab: "discover" },
  profile: { tab: "me" },
  notify: { item: "notify", tab: "me" },
  saved: { item: "saved", tab: "me" },
  list: { item: "saved", tab: "me" },
};

/**
 * Krystal Cinema: the shelf, the films on their way and on their way out, and the assistant. Every page
 * needs somebody signed in: one opened with nothing held leaves for the provider at once, and is where
 * the browser returns once they are. A session that ends while a page is open is shown, with the way
 * back offered, never followed. Somebody signed in whose account may not watch is told there is nothing
 * here for them yet.
 */
export function App() {
  const session = useSession();
  const route = useRoute();
  const embedded = route.view === "embed";
  const restored = useRestored(!embedded && route.view !== "signin");

  const [waiting, setWaiting] = React.useState(null);

  React.useEffect(() => {
    if (!restored || session || route.view === "signin" || embedded) {
      setWaiting(null);
      return;
    }
    if (sessionEnded()) setWaiting({ note: ENDED });
    else beginSignIn(route.href).catch(() => setWaiting({ note: UNANSWERED }));
  }, [restored, session, route.view, route.href, embedded]);

  // The Activity proves who is watching through Discord, so it answers to no site session.
  if (embedded) return <React.Suspense fallback={<Dark />}><EmbedRoom /></React.Suspense>;
  if (route.view === "signin") return <SignInReturn query={route.query} />;
  if (!restored || !session) return waiting ? <Waiting note={waiting.note} returnTo={route.href} /> : <Dark />;
  if (!can(session, "cinema:watch")) return <NoAccess onSignOut={leave} />;
  // A room fills the screen: the film is the page, and the way out is its back button.
  if (route.view === "room") {
    return (
      <React.Suspense fallback={<Dark />}>
        <SiteRoom key={route.arg} room={route.arg} title={route.query.get("title")}
          onBack={() => goBack(route.query.get("title") ? moviePath(route.query.get("title")) : homePath)}
          onEmpty={() => navigate(libraryPath("film"))} />
      </React.Suspense>
    );
  }
  return <Frame session={session} route={route} />;
}

function Frame({ session, route }) {
  const [collapsed, setCollapsed] = useStoredFlag("cinema:sidebar:collapsed");
  const dock = useDock({ storageKey: "cinema:dock" });
  // A phone has the tab bar and no drawer, so only the right edge's swipe, the dock's, is taken.
  useEdgeSwipe(false, noDrawer, dock.open, dock.setOpen);
  const mainRef = React.useRef(null);
  useDiscoverKey(route.view === "discover");

  // A new page starts at its top, from the moment it is shown, and on a television with focus on it.
  const toTop = React.useCallback(() => {
    if (mainRef.current) mainRef.current.scrollTop = 0;
    if (onTv) pageShown(mainRef.current);
  }, []);
  React.useEffect(() => { if (onTv) pageShown(mainRef.current); }, []);

  // A remote's Back goes back a page. Home is where the app is left from, once focus is in the
  // sidebar: the first Back there puts it there.
  const home = route.view === "home";
  React.useEffect(() => onTv ? onTvBack(() => {
    if (home) return focusNavigation();
    goBack(homePath);
    return true;
  }) : undefined, [home]);

  const lit = LIT[route.view] || {};
  const go = (path) => navigate(path);
  const item = (n) => (
    <NavItem key={n.id} icon={n.icon} label={n.label} active={lit.item === n.id} onClick={() => go(n.path)} />
  );
  // A tab leaves the dock for its page. The tab of the page already open takes it back to its top.
  const tab = (t) => t.path
    ? {
      id: t.id, icon: t.icon, label: t.label,
      active: !dock.open && lit.tab === t.id,
      onClick: () => {
        dock.setOpen(false);
        if (route.path === t.path && mainRef.current) mainRef.current.scrollTo({ top: 0, behavior: "smooth" });
        else go(t.path);
      },
    }
    : { id: t.id, icon: t.icon, label: t.label, active: dock.open, onClick: () => dock.setOpen(true) };

  return (
    <>
      <AppShell
        collapsed={dock.desktop && collapsed}
        drawerOpen={false}
        onDrawer={noDrawer}
        dockPush={dock.pushing ? dock.width : 0}
        mainRef={mainRef}
        sidebar={
          <Sidebar
            mark={markUrl}
            wordmark="Krystal Cinema"
            open={false}
            collapsed={dock.desktop && collapsed}
            onToggleCollapse={() => setCollapsed((c) => !c)}
            closeNav
            foot={<>
              <BellNavItem />
              <SidebarAccount
                user={session.user}
                sub={session.user.username ? "@" + session.user.username : null}
                detail={session.user.username ? "@" + session.user.username : null}
                collapsed={dock.desktop && collapsed}
                items={[
                  { icon: "user", label: "Profile", onClick: () => go(profilePath()) },
                  { icon: "log-out", label: "Sign out", danger: true, onClick: leave },
                ]} />
            </>}>
            <SidebarGroup>{HOME.map(item)}</SidebarGroup>
            <SidebarGroup>{LIBRARY.map(item)}</SidebarGroup>
            <SidebarGroup>{LIFECYCLE.filter((n) => n.id !== "notify" || can(session, "cinema:wish")).map(item)}</SidebarGroup>
          </Sidebar>
        }
        aside={onTv ? null : <>
          <Dock dock={dock}>
            {dock.open && <CinemaAssistant session={session} docked dock={dock} onClose={() => dock.setOpen(false)} />}
          </Dock>
          {!dock.open && <DockFab onClick={() => dock.setOpen(true)} />}
        </>}
        tabBar={<TabBar items={TABS.map(tab)} />}>
        <PageStage route={route} render={(r) => <Page route={r} />} above={(r) => <Trail route={r} />} onShown={toTop} />
        <SiteFooter />
      </AppShell>
      <Toasts />
    </>
  );
}

function noDrawer() {}

/**
 * Whether the session this browser holds has been picked up, renewing it first when its token has run
 * out. Until it has, nothing is shown, so a signed-in person is never sent through the door.
 */
function useRestored(wanted) {
  const [restored, setRestored] = React.useState(false);
  React.useEffect(() => {
    if (!wanted || restored) return;
    let live = true;
    restore().catch(() => null).finally(() => { if (live) setRestored(true); });
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

function Page({ route }) {
  switch (route.view) {
    case "home": return <Home />;
    case "movies": return <Library key="film" kind="film" />;
    case "tv": return <Library key="show" kind="show" />;
    case "movie": return <Title key={route.arg} id={route.arg} />;
    case "show":
    case "discover-show":
      return <Show key={route.arg} imdbId={route.arg} season={route.season} inLibrary={route.view === "show"} />;
    case "discover-film": return <CatalogueFilm key={route.arg} imdbId={route.arg} />;
    case "discover": return <Discover query={route.query} />;
    case "saved": return <Saved />;
    case "list": return <ListPage key={route.arg} id={route.arg} />;
    case "notify": return <Notify />;
    case "leaving": return <Leaving />;
    case "profile": return <Profile tab={route.tab} />;
    case "about": return <About />;
    default: return <NotFound />;
  }
}
