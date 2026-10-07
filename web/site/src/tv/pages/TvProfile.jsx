import { Icon } from "@thekrystalship/krystal-ui";
import { PROFILE_TAB_FACES } from "../../nav.js";
import { aboutPath, profilePath } from "../../paths.js";
import { navigate } from "../../router.js";
import { useSession } from "../../session.js";
import { leave } from "../../leave.js";
import { initials } from "../../components/FilmDetails.jsx";
import { TabStage } from "../../components/PageStage.jsx";
import { panel } from "../../pages/Profile.jsx";
import { About } from "../../pages/About.jsx";

/**
 * The profile's sections on a television: every tab but the browser's pushes, which a TV does not take.
 */
const SECTIONS = PROFILE_TAB_FACES.filter((t) => t.id !== "notifications");

/**
 * Who is signed in and everything that is theirs, on a television: the person and their sections down
 * the left — History, Requests (which holds what they wait on), Following, Playback, Subtitles,
 * Appearance, Account and About — with Sign out under them, and the open section on the right. The
 * page is the screen's height and never scrolls: the open section scrolls inside its panel, under its
 * title, so neither the title nor the list can be carried off the screen by a long section. The
 * section is in the address, as on every surface; About keeps its own, `/about`, and is drawn here
 * with focus on its place in the list, since it holds nothing to press.
 */
export function TvProfile({ tab: named, about = false }) {
  const session = useSession();
  const user = session && session.user;
  const tab = SECTIONS.some((t) => t.id === named) ? named : SECTIONS[0].id;
  const face = SECTIONS.find((t) => t.id === tab);

  return (
    <div className="kc-tv-page kc-tvp">
      <aside className="kc-tvp__side">
        <div className="kc-tvp__who">
          <span className="kc-tvp__avatar">{initials((user && (user.name || user.username)) || "")}</span>
          <span className="kc-tvp__names">
            <span className="kc-tvp__name">{user && user.name}</span>
            {user && user.username && <span className="kc-tvp__handle">@{user.username}</span>}
          </span>
        </div>
        <nav className="kc-tvp__nav" aria-label="Profile">
          {SECTIONS.map((t) => (
            <button key={t.id} type="button" className="tv-pill kc-tvp__item" aria-current={!about && t.id === tab ? "page" : undefined}
              onClick={() => navigate(profilePath(t.id), { replace: true })}>
              <Icon name={t.icon} size={18} />{t.label}
            </button>
          ))}
          <button type="button" className="tv-pill kc-tvp__item" aria-current={about ? "page" : undefined}
            data-tv-initial={about ? "" : undefined} onClick={() => navigate(aboutPath, { replace: true })}>
            <Icon name="info" size={18} />About
          </button>
          <button type="button" className="tv-pill kc-tvp__item kc-tvp__out" onClick={leave}><Icon name="log-out" size={18} />Sign out</button>
        </nav>
      </aside>
      <section className="kc-tvp__panel">
        <h1 className="kc-tvp__title">{about ? "About" : face.label}</h1>
        <div className="kc-tvp__body">
          {about ? <About head={false} /> : <TabStage tab={tab} render={(shown) => panel(shown, session)} />}
        </div>
      </section>
    </div>
  );
}
