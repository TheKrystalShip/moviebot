import React from "react";
import {
  AppShell, Dock, DockFab, NavItem, Sidebar, SidebarAccount, SidebarGroup, TabBar, Toasts,
  useDock, useEdgeSwipe, useStoredFlag,
} from "@thekrystalship/krystal-ui";
import markUrl from "@thekrystalship/krystal-ui/assets/tks-mark.png";
import { signOut, useSession } from "./session.js";
import { goBack, navigate, useRoute } from "./router.js";
import { CinemaAssistant } from "./CinemaAssistant.jsx";
import { Home } from "./pages/Home.jsx";
import { Library } from "./pages/Library.jsx";
import { Title } from "./pages/Title.jsx";
import { Show } from "./pages/Show.jsx";
import { CatalogueFilm } from "./pages/CatalogueFilm.jsx";
import { Discover, DISCOVER_FIELD } from "./pages/Discover.jsx";
import { Notify } from "./pages/Notify.jsx";
import { ListPage, Lists } from "./pages/Lists.jsx";
import { Leaving } from "./pages/Leaving.jsx";
import { Profile } from "./pages/Profile.jsx";
import { SignIn } from "./pages/SignIn.jsx";
import { About } from "./pages/About.jsx";
import { SiteFooter } from "./components/SiteFooter.jsx";
import { BellNavItem } from "./components/Bell.jsx";
import { PageStage } from "./components/PageStage.jsx";

// The player carries hls.js, the hub client and Discord's SDK; only a room pays for them.
const EmbedRoom = React.lazy(() => import("./player/PlayerPage.jsx").then((m) => ({ default: m.EmbedRoom })));
const SiteRoom = React.lazy(() => import("./player/PlayerPage.jsx").then((m) => ({ default: m.SiteRoom })));
const Dark = () => <div style={{ position: "fixed", inset: 0, background: "#000" }} />;

const HOME = [
  { page: "home", path: "/", icon: "house", label: "Home" },
  { page: "discover", path: "/discover", icon: "compass", label: "Discover", also: ["film"] },
];

const LIBRARY = [
  { page: "movies", path: "/movies", icon: "clapperboard", label: "Movies" },
  { page: "tv", path: "/tv", icon: "tv", label: "TV Shows", also: ["show"] },
  { page: "lists", path: "/lists", icon: "bookmark", label: "Lists" },
];

const LIFECYCLE = [
  { page: "notify", path: "/notify", icon: "bell-plus", label: "Notify me" },
  { page: "leaving", path: "/leaving", icon: "hourglass", label: "Leaving soon" },
];

// A phone's way around: a tab for each place, lit on the pages that belong to it. The assistant's
// tab opens the dock rather than a page.
const TABS = [
  { id: "home", path: "/", icon: "house", label: "Home", pages: ["home", "leaving"] },
  { id: "library", path: "/movies", icon: "clapperboard", label: "Library", pages: ["movies", "tv", "title", "show", "lists"] },
  { id: "discover", path: "/discover", icon: "compass", label: "Discover", pages: ["discover", "film"] },
  { id: "assistant", icon: "bot", label: "Assistant" },
  { id: "me", path: "/profile", icon: "user", label: "Me", pages: ["profile", "notify"] },
];

/**
 * Krystal Cinema: the shelf, the films on their way and on their way out, and the assistant, for the
 * members of the server. Everything but the door needs somebody signed in, and a page asked for while
 * signed out is where the browser returns once they are.
 */
export function App() {
  const session = useSession();
  const route = useRoute();
  const embedded = route.page === "embed";

  React.useEffect(() => {
    if (!session && route.page !== "signin" && !embedded) {
      navigate("/signin?next=" + encodeURIComponent(route.path + (route.query.toString() ? "?" + route.query : "")), { replace: true });
    }
  }, [session, route.page, route.path, route.query, embedded]);

  // The Activity proves who is watching through Discord, so it answers to no site session.
  if (embedded) return <React.Suspense fallback={<Dark />}><EmbedRoom /></React.Suspense>;
  if (route.page === "signin" || !session) return <><SignIn query={route.query} /><Toasts /></>;
  // A room fills the screen: the film is the page, and the way out is its back button.
  if (route.page === "r" && route.arg) {
    return (
      <React.Suspense fallback={<Dark />}>
        <SiteRoom key={route.arg} room={route.arg} title={route.query.get("title")}
          onBack={() => goBack(route.query.get("title") ? "/title/" + encodeURIComponent(route.query.get("title")) : "/")}
          onEmpty={() => navigate("/movies")} />
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
  useDiscoverKey(route.page === "discover");

  // A new page starts at its top, from the moment it is shown.
  const toTop = React.useCallback(() => {
    if (mainRef.current) mainRef.current.scrollTop = 0;
  }, []);

  const go = (path) => navigate(path);
  const item = (n) => (
    <NavItem key={n.page} icon={n.icon} label={n.label} active={route.page === n.page || (n.also || []).includes(route.page)} onClick={() => go(n.path)} />
  );
  // A tab leaves the dock for its page. The tab of the page already open takes it back to its top.
  const tab = (t) => t.path
    ? {
      id: t.id, icon: t.icon, label: t.label,
      active: !dock.open && t.pages.includes(route.page),
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
                  { icon: "user", label: "Profile", onClick: () => go("/profile") },
                  { icon: "log-out", label: "Sign out", danger: true, onClick: () => { signOut(); navigate("/signin", { replace: true }); } },
                ]} />
            </>}>
            <SidebarGroup>{HOME.map(item)}</SidebarGroup>
            <SidebarGroup>{LIBRARY.map(item)}</SidebarGroup>
            <SidebarGroup>{LIFECYCLE.map(item)}</SidebarGroup>
          </Sidebar>
        }
        aside={<>
          <Dock dock={dock}>
            {dock.open && <CinemaAssistant session={session} docked dock={dock} onClose={() => dock.setOpen(false)} />}
          </Dock>
          {!dock.open && <DockFab onClick={() => dock.setOpen(true)} />}
        </>}
        tabBar={<TabBar items={TABS.map(tab)} />}>
        <PageStage route={route} render={(r) => <Page route={r} />} onShown={toTop} />
        <SiteFooter />
      </AppShell>
      <Toasts />
    </>
  );
}

function noDrawer() {}

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
        navigate("/discover");
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onDiscover]);
}

function Page({ route }) {
  switch (route.page) {
    case "home": return <Home />;
    case "movies": return <Library key="film" kind="film" />;
    case "tv": return <Library key="show" kind="show" />;
    case "title": return route.arg ? <Title key={route.arg} id={route.arg} /> : <Library kind="film" />;
    case "show": return route.arg ? <Show key={route.arg} imdbId={route.arg} query={route.query} /> : <Library kind="show" />;
    case "film": return route.arg ? <CatalogueFilm key={route.arg} imdbId={route.arg} /> : <Discover query={route.query} />;
    case "discover": return <Discover query={route.query} />;
    case "lists": return route.arg ? <ListPage key={route.arg} id={route.arg} /> : <Lists />;
    case "notify": return <Notify />;
    case "leaving": return <Leaving />;
    case "profile": return <Profile query={route.query} />;
    case "about": return <About />;
    default: return <Home />;
  }
}
