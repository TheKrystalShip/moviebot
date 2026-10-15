import { Icon } from "@thekrystalship/krystal";
import { Chat, createAssistantClient } from "@thekrystalship/krystal-chat";
import { freshToken } from "./session.js";
import { discoverFilmPath, showPath } from "./paths.js";
import { linkProps } from "./router.js";

// The assistant, behind /assistant on the same origin, spoken to as the signed-in person. One client
// for the life of the page: the chat asks for it by the assistant's id on every render.
const BASE = "/assistant";
const client = createAssistantClient({
  baseUrl: BASE,
  getToken: () => freshToken(),
  // A token the assistant refused is renewed once and the call sent again, where the call may be sent twice.
  renewToken: () => freshToken({ refused: true }),
});
const clientFor = () => client;

const HOST = { id: "cinema", name: "the assistant", title: "the assistant" };

const SUGGESTIONS = [
  "A film I half remember",
  "What's new this month",
  "Something for tonight",
  "Learn my taste",
];

/**
 * How Krystal Cinema reads what the assistant sends: a `titles` result is the titles a tool named,
 * kept as they came. Any other result draws nothing.
 */
const cinemaProfile = {
  adaptCard: (result) =>
    result && result.kind === "titles" && Array.isArray(result.titles) ? { kind: "titles", titles: result.titles } : null,
};

/** The titles the cards carry, once each, in the order they came. */
function titlesOf(cards) {
  const seen = new Set();
  return (cards || [])
    .filter((card) => card.kind === "titles")
    .flatMap((card) => card.titles)
    .filter((t) => t && t.imdbId && !seen.has(t.imdbId) && seen.add(t.imdbId));
}

/** Whether the reply says the title's name: a tool may return more than the answer chose to name. */
function named(title, content) {
  return !!title.title && String(content || "").toLowerCase().includes(title.title.toLowerCase());
}

/** A title's page: the library's own where it holds the film (the catalogue page forwards), the catalogue's otherwise. */
function titlePage(title) {
  return title.kind === "show" ? showPath(title.imdbId, { held: !!title.inLibrary }) : discoverFilmPath(title.imdbId);
}

/** A title a reply names, as the way into its page: poster, name, year and where it stands for this person. */
function TitleCard({ title }) {
  const marks = [title.kind === "show" ? "Series" : null, title.inLibrary ? "In library" : null, title.watched ? "Watched" : null]
    .filter(Boolean);
  return (
    <a className="kc-chat-card" {...linkProps(titlePage(title))}>
      <span className="kc-chat-card__art">
        {title.posterUrl ? <img src={title.posterUrl} alt="" loading="lazy" /> : <Icon name="clapperboard" size={18} />}
      </span>
      <span className="kc-chat-card__body">
        <span className="kc-chat-card__name">{title.title}</span>
        <span className="kc-chat-card__note">{[title.year, ...marks].filter(Boolean).join(" · ")}</span>
      </span>
    </a>
  );
}

function TitleCards({ msg }) {
  const titles = titlesOf(msg.cards).filter((t) => named(t, msg.content));
  if (titles.length === 0) return null;
  return (
    <div className="kc-chat-cards">
      {titles.map((t) => <TitleCard key={t.imdbId} title={t} />)}
    </div>
  );
}

/**
 * The assistant, in the dock beside a page when `docked` (a wide screen), and as a page of its own
 * otherwise (`pages/Assistant.jsx`).
 */
export function CinemaAssistant({ session, docked = false, dock, onClose, onExpand }) {
  const user = session && session.user;
  return (
    <Chat
      user={user ? { name: user.name, display: user.name, userId: user.id, displayName: user.name } : null}
      docked={docked}
      layout={docked ? "panel" : "page"}
      framed={!docked}
      showPin={!!dock && dock.desktop}
      pinned={dock && dock.pinned}
      pinDisabled={!!dock && !dock.canPush}
      onTogglePin={dock && dock.togglePin}
      onClose={onClose}
      onExpand={onExpand}
      assistantHost={HOST}
      clientFor={clientFor}
      authed={!!session}
      connection={{ tone: "online", label: "Connected", usable: !!session }}
      storageKey={"cinema:chat:" + (user ? user.id : "anonymous")}
      assistantName="Assistant"
      profile={cinemaProfile}
      suggestions={SUGGESTIONS}
      noAssistant="The assistant isn't answering right now."
      renderCards={(msg) => <TitleCards msg={msg} />} />
  );
}
