import { Footer, GithubMark } from "@thekrystalship/krystal-ui";
import markUrl from "@thekrystalship/krystal-ui/assets/tks-mark.png";
import { api } from "../api.js";
import { useLoad } from "../data.js";
import { navigate } from "../router.js";

const page = (name, path, icon, sub) => ({ name, href: path, icon, sub, onClick: () => navigate(path) });

const BROWSE = [
  page("Library", "/library", "clapperboard", "Every film on the shelf"),
  page("Leaving soon", "/leaving", "hourglass", "Films on their way out"),
  page("Notify me", "/notify", "bell-plus", "Films you are waiting for"),
  page("About", "/about", "info", "This site, and where film details come from"),
];

const SOURCE = [
  { name: "TheKrystalShip", href: "https://github.com/TheKrystalShip", glyph: <GithubMark size={18} />, featured: true, sub: "Where everything here is built" },
  { name: "moviebot", href: "https://github.com/TheKrystalShip/moviebot", icon: "git-branch", sub: "Rooms, the player and this site" },
  { name: "moviebot-acquire", href: "https://github.com/TheKrystalShip/moviebot-acquire", icon: "git-branch", sub: "Finding and fetching films" },
];

/**
 * What every page ends with: what the cinema is, how many rooms are watching something right now,
 * the way around it, and its source. Where the film details come from is credited on About.
 */
export function SiteFooter() {
  const rooms = useLoad(() => api.rooms().catch(() => null), [], { every: 30000 });
  const playing = (rooms.data || []).filter((r) => r.titleId && r.participants > 0).length;

  return (
    <Footer
      mark={markUrl}
      wordmark="Krystal Cinema"
      tagline="The server's films, watched together: everyone in a room sees the same moment, from this site or from a Discord voice channel."
      live={playing > 0}
      note={playing > 0 ? (playing === 1 ? "A room is watching now" : `${playing} rooms are watching now`) : "Self-hosted and open source"}
      groups={[
        { label: "Browse", links: BROWSE },
        { label: "Source code", mono: true, links: SOURCE },
      ]}
      copy={`© ${new Date().getFullYear()} The Krystal Ship · films for friends, watched together.`}
      end={
        <a href="https://github.com/TheKrystalShip" target="_blank" rel="noreferrer noopener" className="kfoot__org">
          <GithubMark size={13} />
          github.com/TheKrystalShip
        </a>
      }
    />
  );
}
