import { Button, Chat, Icon, createAssistantClient } from "@thekrystalship/krystal-ui";
import { freshToken } from "./session.js";
import { moviePath, roomPath } from "./paths.js";
import { linkProps, navigate } from "./router.js";
import { watchLink } from "./components/Film.jsx";

// moviebot-assistant, behind /assistant on the same origin, spoken to as the signed-in person. One
// client for the life of the page: the chat asks for it by the assistant's id on every render.
const client = createAssistantClient({ baseUrl: "/assistant", getToken: () => freshToken() });
const clientFor = () => client;

const HOST = { id: "cinema", name: "MovieBot", title: "MovieBot, the cinema's assistant" };

// `label` is the button; `names` is how a proposal reads ahead of its film, where the two differ.
const VERBS = {
  watch: { label: "Watch", icon: "play" },
  download: { label: "Download", icon: "download" },
  wish: { label: "Notify me", names: "Get notified about", icon: "bell-plus" },
  fetch_subtitle: { label: "Fetch subtitles", names: "Fetch subtitles for", icon: "captions" },
};

/** The room a confirmed Watch opened, which the person who agreed is taken to, as Watch on a film's page does. */
function openedRoom(resp) {
  const watch = resp && resp.result && resp.result.watch;
  return resp && resp.success && watch && watch.room ? watch : null;
}

/**
 * How Krystal Cinema reads what the assistant sends. A film the assistant put on or found comes as the
 * tool's card and is drawn as the way into it; a proposal is named by what it does and to which film,
 * and redeemed here, by the person who asked for it.
 */
const cinemaProfile = {
  adaptCard: (result) =>
    result && result.data && result.data.titleId
      ? { kind: "film", titleId: result.data.titleId, name: result.data.name, sessionId: result.data.sessionId || null }
      : null,
  proposalLabel: (verb) => (VERBS[verb] || { label: (verb || "Go ahead").replace(/_/g, " ") }).label,
  proposal: (msg) => {
    const verb = VERBS[msg.verb] || { label: (msg.verb || "Go ahead").replace(/_/g, " "), icon: "zap" };
    return {
      meta: { label: verb.label, icon: verb.icon, tone: "info" },
      target: msg.subjectId || "",
      targetName: msg.subjectId || "",
      title: verb.names && msg.subjectId ? verb.names + " " + msg.subjectId : undefined,
      runnable: true,
    };
  },
  verdict: (msg, targetName, resp) => {
    const opened = openedRoom(resp);
    if (opened) navigate(roomPath(opened.room) + (opened.libraryId ? `?title=${encodeURIComponent(opened.libraryId)}` : ""));
    return resp && resp.success
      ? { ok: true, headline: resp.text || "Done.", lines: [] }
      : { ok: false, headline: (resp && resp.text) || "That did not work.", lines: [] };
  },
};

function FilmCards({ cards }) {
  return (
    <div className="kc-chat-cards">
      {cards.filter((c) => c.kind === "film").map((card) => (
        <div key={card.titleId + (card.sessionId || "")} className="kc-chat-card">
          <Icon name="clapperboard" size={16} />
          <a className="kc-chat-card__name" {...linkProps(moviePath(card.titleId))}>{card.name}</a>
          <Button variant="primary" size="sm" icon="play" {...watchLink(card.titleId, card.sessionId)}>
            Watch
          </Button>
        </div>
      ))}
    </div>
  );
}

export function CinemaAssistant({ session, docked, dock, onClose, onExpand }) {
  const user = session && session.user;
  return (
    <Chat
      user={user ? { name: user.name, display: user.name, userId: user.id, displayName: user.name } : null}
      docked={docked}
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
      assistantName="MovieBot"
      profile={cinemaProfile}
      suggestions={["What should we watch tonight?", "What's on in the rooms?", "Get Dune: Part Two"]}
      primer="I can find any film and put it on, and tell you when one that isn't out yet comes out."
      noAssistant="MovieBot's assistant isn't answering right now."
      renderCards={(msg) => <FilmCards cards={msg.cards} />} />
  );
}
