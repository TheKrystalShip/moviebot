import React from "react";
import { Toasts, TvBar, TvBarButton } from "@thekrystalship/krystal-ui";
import markUrl from "@thekrystalship/krystal-ui/assets/tks-mark.png";
import { Bell } from "../components/Bell.jsx";
import { PageStage } from "../components/PageStage.jsx";
import { useNotifications } from "../notifications.js";
import { PLACES } from "../nav.js";
import { homePath, profilePath } from "../paths.js";
import { goBack, linkProps, navigate } from "../router.js";
import { initials } from "../components/FilmDetails.jsx";
import { onBack } from "./tv.js";
import { focusNavigation, pageShown } from "./navigator.js";
import { TvPage } from "./TvPage.jsx";

/** The places in the bar, and the views that light each. */
const BAR = [
  { id: "home", views: ["home"] },
  { id: "discover", views: ["discover"] },
  { id: "movies", views: ["movies"] },
  { id: "tv", views: ["tv"] },
  { id: "saved", views: ["saved", "list"] },
];

/** How far a page scrolls before the bar moves out of its way. */
const BAR_SCROLL = 40;

/** A page's way to put the bar away while its own top is in use, by `useBarAway(true)`. */
const BarAway = React.createContext(() => {});

export function useBarAway(away) {
  const set = React.useContext(BarAway);
  React.useEffect(() => {
    set(away);
    return () => set(false);
  }, [set, away]);
}

/**
 * Krystal Cinema on a television: one page at a time in a column that fills the screen, under the
 * bar on the pages it leads to. A film's, a show's and the profile's pages have no bar; Back leaves
 * them. On a page with the bar, Back first brings focus up into it, and from there goes back a page,
 * or from Home leaves the app. `held` is the frame kept under the sign-in code after its session
 * ended, which takes no key: Back there leaves the app.
 */
export function TvFrame({ session, route, held = false }) {
  const mainRef = React.useRef(null);
  const lit = BAR.find((b) => b.views.includes(route.view));
  const [scrolled, setScrolled] = React.useState(false);
  const [away, setAway] = React.useState(false);

  const shown = React.useCallback(() => {
    if (mainRef.current) mainRef.current.scrollTop = 0;
    setScrolled(false);
    pageShown(mainRef.current);
  }, []);
  React.useEffect(() => { pageShown(mainRef.current); }, []);

  // Whether the page has scrolled past its top, from a mark at the top leaving the column: reading the
  // column's scroll position on every scroll event lays the page out again on each one.
  const topRef = React.useRef(null);
  React.useEffect(() => {
    const seen = new IntersectionObserver(([entry]) => setScrolled(!entry.isIntersecting), { root: mainRef.current });
    seen.observe(topRef.current);
    return () => seen.disconnect();
  }, []);

  const home = route.view === "home";
  const barred = !!lit;
  React.useEffect(() => held ? undefined : onBack(() => {
    if (barred && focusNavigation()) return true;
    if (home) return false;
    goBack(homePath);
    return true;
  }), [home, barred, held]);

  // Focus come up into the bar takes the page back to its top, where the bar belongs, and the page
  // steps back while it is there.
  const [inBar, setInBar] = React.useState(false);
  const toTop = () => {
    setInBar(true);
    if (mainRef.current) mainRef.current.scrollTop = 0;
  };

  const items = BAR.map((b) => ({
    id: b.id,
    label: PLACES[b.id].label,
    active: lit === b,
    ...linkProps(PLACES[b.id].path),
  }));

  return (
    <>
      {barred && (
        <div onFocus={toTop} onBlur={() => setInBar(false)}>
          <TvBar
            mark={markUrl}
            name="Krystal Cinema"
            items={items}
            hidden={(scrolled || away) && !inBar}
            actions={<>
              <TvBell />
              <TvBarButton label="Profile" className="kc-tv-avatar" onClick={() => navigate(profilePath())}>
                <span>{initials(session.user.name || session.user.username || "")}</span>
              </TvBarButton>
            </>} />
        </div>
      )}
      <main className={"kc-tv-main" + (barred && inBar ? " is-under-bar" : "")} ref={mainRef} data-tv-line="230">
        <div ref={topRef} className="kc-tv-top-mark" style={{ height: BAR_SCROLL }} aria-hidden="true" />
        <BarAway.Provider value={setAway}>
          <Pages route={route} session={session} onShown={shown} />
        </BarAway.Provider>
      </main>
      <Toasts />
    </>
  );
}

/**
 * The page shown, drawn again only for a new route: the bar sliding away or taking focus changes the
 * frame, and a page of a hundred cards drawn again for it is a dropped frame on a television.
 */
const Pages = React.memo(function Pages({ route, session, onShown }) {
  return <PageStage route={route} render={(r) => <TvPage route={r} session={session} />} onShown={onShown} />;
});

function TvBell() {
  const { unread } = useNotifications();
  return (
    <Bell placement="sheet">
      {(open, toggle) => (
        <TvBarButton icon="bell" label={"Notifications" + (unread > 0 ? `, ${unread} unread` : "")} dot={unread > 0}
          onClick={toggle} aria-haspopup="dialog" aria-expanded={open} />
      )}
    </Bell>
  );
}
