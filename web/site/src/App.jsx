import React from "react";
import {
  AppShell, Dock, DockFab, Icon, NavItem, Sidebar, SidebarGroup, Toasts,
  useDock, useEdgeSwipe, useStoredFlag,
} from "@thekrystalship/krystal-ui";
import markUrl from "@thekrystalship/krystal-ui/assets/tks-mark.png";
import { useSession } from "./session.js";
import { goBack, navigate, useRoute } from "./router.js";
import { CinemaAssistant } from "./CinemaAssistant.jsx";
import { Home } from "./pages/Home.jsx";
import { Library } from "./pages/Library.jsx";
import { Title } from "./pages/Title.jsx";
import { Search } from "./pages/Search.jsx";
import { Notify } from "./pages/Notify.jsx";
import { Leaving } from "./pages/Leaving.jsx";
import { Downloads } from "./pages/Downloads.jsx";
import { Profile } from "./pages/Profile.jsx";
import { SignIn } from "./pages/SignIn.jsx";
import { About } from "./pages/About.jsx";
import { SiteFooter } from "./components/SiteFooter.jsx";

// The player carries hls.js, the hub client and Discord's SDK; only a room pays for them.
const EmbedRoom = React.lazy(() => import("./player/PlayerPage.jsx").then((m) => ({ default: m.EmbedRoom })));
const SiteRoom = React.lazy(() => import("./player/PlayerPage.jsx").then((m) => ({ default: m.SiteRoom })));
const Dark = () => <div style={{ position: "fixed", inset: 0, background: "#000" }} />;

const NAV = [
  { page: "home", path: "/", icon: "house", label: "Home" },
  { page: "library", path: "/library", icon: "clapperboard", label: "Library" },
  { page: "search", path: "/search", icon: "search", label: "Search" },
  { page: "downloads", path: "/downloads", icon: "download", label: "Downloads" },
];

const LIFECYCLE = [
  { page: "notify", path: "/notify", icon: "bell-plus", label: "Notify me" },
  { page: "leaving", path: "/leaving", icon: "hourglass", label: "Leaving soon" },
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
          onEmpty={() => navigate("/library")} />
      </React.Suspense>
    );
  }
  return <Frame session={session} route={route} />;
}

function Frame({ session, route }) {
  const [collapsed, setCollapsed] = useStoredFlag("cinema:sidebar:collapsed");
  const [drawerOpen, setDrawerOpen] = React.useState(false);
  const dock = useDock({ storageKey: "cinema:dock" });
  useEdgeSwipe(drawerOpen, setDrawerOpen, dock.open, dock.setOpen);
  const mainRef = React.useRef(null);

  // A new page starts at its top, and closes the drawer it was chosen from.
  React.useEffect(() => {
    if (mainRef.current) mainRef.current.scrollTop = 0;
    setDrawerOpen(false);
  }, [route.path]);

  const go = (path) => navigate(path);
  const item = (n) => (
    <NavItem key={n.page} icon={n.icon} label={n.label} active={route.page === n.page} onClick={() => go(n.path)} />
  );

  return (
    <>
      <AppShell
        collapsed={dock.desktop && collapsed}
        drawerOpen={drawerOpen}
        onDrawer={setDrawerOpen}
        dockPush={dock.pushing ? dock.width : 0}
        mainRef={mainRef}
        sidebar={
          <Sidebar
            mark={markUrl}
            wordmark="Krystal Cinema"
            open={drawerOpen}
            collapsed={dock.desktop && collapsed}
            onToggleCollapse={() => setCollapsed((c) => !c)}
            top={<SidebarSearch />}
            foot={<NavItem icon="circle-user" label={session.user.name} active={route.page === "profile"} onClick={() => go("/profile")} />}>
            <SidebarGroup>{NAV.map(item)}</SidebarGroup>
            <SidebarGroup label="Coming and going">{LIFECYCLE.map(item)}</SidebarGroup>
          </Sidebar>
        }
        aside={<>
          <Dock dock={dock}>
            {dock.open && <CinemaAssistant session={session} docked onClose={() => dock.setOpen(false)} />}
          </Dock>
          {!dock.open && <DockFab onClick={() => dock.setOpen(true)} />}
        </>}>
        <Page route={route} />
        <SiteFooter />
      </AppShell>
      <Toasts />
    </>
  );
}

function Page({ route }) {
  switch (route.page) {
    case "home": return <Home />;
    case "library": return <Library />;
    case "title": return route.arg ? <Title key={route.arg} id={route.arg} /> : <Library />;
    case "search": return <Search query={route.query} />;
    case "notify": return <Notify />;
    case "leaving": return <Leaving />;
    case "downloads": return <Downloads />;
    case "profile": return <Profile query={route.query} />;
    case "about": return <About />;
    default: return <Home />;
  }
}

/** The search field at the top of the sidebar: Enter searches the library and the tracker. */
function SidebarSearch() {
  const [q, setQ] = React.useState("");
  return (
    <form className="sidebar__search kc-sidebar-search" onSubmit={(e) => {
      e.preventDefault();
      if (q.trim()) navigate("/search?q=" + encodeURIComponent(q.trim()));
    }}>
      <Icon name="search" size={16} />
      <input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Search films" aria-label="Search films" />
    </form>
  );
}
