import { Footer, GithubMark } from "@thekrystalship/krystal";
import markUrl from "@thekrystalship/krystal/assets/tks-mark.png";
import { api } from "../api.js";
import { useLoad } from "../data.js";
import { inApp } from "../offline/shell.js";
import { PLACES } from "../nav.js";
import { navigate } from "../router.js";

const page = (id, sub) => {
  const { label, path, icon } = PLACES[id];
  return { name: label, href: path, icon, sub, onClick: () => navigate(path) };
};

const BROWSE = [
  page("movies", "Every movie on the shelf"),
  page("tv", "Every show on the shelf"),
  page("wants", "What you want to watch"),
  page("plans", "When you watch together"),
  page("about", "This site, and where its details come from"),
];

const SOURCE = [
  { name: "TheKrystalShip", href: "https://github.com/TheKrystalShip", glyph: <GithubMark size={18} />, featured: true, sub: "Where everything here is built" },
  { name: "moviebot", href: "https://github.com/TheKrystalShip/moviebot", icon: "git-branch", sub: "Rooms, the player and this site" },
  { name: "moviebot-acquire", href: "https://github.com/TheKrystalShip/moviebot-acquire", icon: "git-branch", sub: "Finding and fetching movies and shows" },
  { name: "tks-krystal", href: "https://github.com/TheKrystalShip/tks-krystal", icon: "git-branch", sub: "The design system this site is drawn with" },
];

/**
 * The phone app, offered on an Android phone's browser: a tap fetches the APK the site serves, which
 * the phone installs once it allows installing from the browser. Inside the app it is not offered.
 */
const APP = [
  { name: "Krystal Cinema for Android", href: "/phone.apk", icon: "smartphone", featured: true, sub: "Downloads that carry on with the screen off" },
];
const offerApp = () => typeof navigator !== "undefined" && /Android/.test(navigator.userAgent) && !inApp();

/**
 * What every page ends with: what the cinema is, how many rooms are watching something right now,
 * the way around it, the phone app on an Android phone, and its source. Where the details and
 * artwork come from is credited on About.
 */
export function SiteFooter() {
  const rooms = useLoad(() => api.rooms().catch(() => null), [], { every: 30000 });
  const playing = (rooms.data || []).filter((r) => r.titleId && r.participants > 0).length;

  return (
    <Footer
      mark={markUrl}
      wordmark="Krystal Cinema"
      tagline="The server's movies and shows, watched together: everyone in a room sees the same moment, from this site or from a Discord voice channel."
      note={playing > 0 ? (playing === 1 ? "A room is watching now" : `${playing} rooms are watching now`) : "Self-hosted and open source"}
      groups={[
        { label: "Browse", links: BROWSE },
        ...(offerApp() ? [{ label: "App", links: APP }] : []),
        { label: "Source code", mono: true, links: SOURCE },
      ]}
      copy={`© ${new Date().getFullYear()} The Krystal Ship · watched together.`}
      end={
        <a href="https://github.com/TheKrystalShip" target="_blank" rel="noreferrer noopener" className="kc-footer__org">
          <GithubMark size={13} />
          github.com/TheKrystalShip
        </a>
      }
    />
  );
}
