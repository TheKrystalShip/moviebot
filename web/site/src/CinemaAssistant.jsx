import { Icon } from "@thekrystalship/krystal";
import React from "react";
import { Chat, CHAT_DEFAULT_PROFILE, ChatSuggestionChips, createAssistantClient } from "@thekrystalship/krystal-chat";
import { freshToken } from "./session.js";
import { discoverFilmPath, planPath, showPath } from "./paths.js";
import { linkProps } from "./router.js";
import { when } from "./time.js";
import { HANDOFF_MS, settleHandoff, useHandoff } from "./assistantHandoff.js";

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
 * What each action the assistant proposes is offered as: the button's words, its icon and its tone
 * (`src/MovieBot.Assistant/Tools/`, one verb per proposal).
 */
const PROPOSALS = {
  want: { label: "Want to watch", icon: "bookmark-plus", tone: "info" },
  unwant: { label: "Remove", icon: "bookmark-minus", tone: "info" },
  plan: { label: "Make plan", icon: "calendar-plus", tone: "info" },
  answer: { label: "Answer", icon: "calendar-check", tone: "info" },
  suggest_time: { label: "Suggest time", icon: "calendar-clock", tone: "info" },
  invite: { label: "Invite", icon: "user-plus", tone: "info" },
  pick_time: { label: "Pick time", icon: "calendar-check", tone: "info" },
  cancel_plan: { label: "Cancel plan", icon: "calendar-x", tone: "danger" },
};

/** A proposal's lines past its first: the titles, the times, who is in it. */
function ProposalLines({ lines }) {
  return (
    <div className="kc-chat-proposal-lines">
      {lines.map((line, i) => <div key={i}>{line}</div>)}
    </div>
  );
}

/**
 * How Cinema reads what the assistant sends: a `titles` result is the titles a tool named and a `plans`
 * result the plans, kept as they came; any other result draws nothing. A proposal's first summary line
 * says what it does and the rest are drawn beneath it.
 */
const cinemaProfile = {
  ...CHAT_DEFAULT_PROFILE,
  adaptCard: (result) => {
    if (result && result.kind === "titles" && Array.isArray(result.titles)) return { kind: "titles", titles: result.titles };
    if (result && result.kind === "plans" && Array.isArray(result.plans)) return { kind: "plans", plans: result.plans };
    return null;
  },
  proposalLabel: (verb) => (PROPOSALS[verb] ? PROPOSALS[verb].label : CHAT_DEFAULT_PROFILE.proposalLabel(verb)),
  proposal: (msg) => {
    const meta = PROPOSALS[msg.verb];
    if (!meta) return CHAT_DEFAULT_PROFILE.proposal(msg);
    const [first, ...rest] = String(msg.summary || "").split("\n");
    return {
      ...CHAT_DEFAULT_PROFILE.proposal(msg),
      meta,
      title: first || msg.subject,
      sub: null,
      body: rest.length > 0 ? <ProposalLines lines={rest} /> : null,
    };
  },
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

/** The plans the cards carry, once each, in the order they came. */
function plansOf(cards) {
  const seen = new Set();
  return (cards || [])
    .filter((card) => card.kind === "plans")
    .flatMap((card) => card.plans)
    .filter((p) => p && p.id && !seen.has(p.id) && seen.add(p.id));
}

/** A plan a reply names, as the way into its page: the title's poster and name, and its time once one is picked. */
function PlanCard({ plan }) {
  return (
    <a className="kc-chat-card" {...linkProps(planPath(plan.id))}>
      <span className="kc-chat-card__art">
        {plan.posterUrl ? <img src={plan.posterUrl} alt="" loading="lazy" /> : <Icon name="calendar" size={18} />}
      </span>
      <span className="kc-chat-card__body">
        <span className="kc-chat-card__name">{plan.title}</span>
        <span className="kc-chat-card__note">{plan.fixedTime ? when(plan.fixedTime) : "No time picked"}</span>
      </span>
    </a>
  );
}

function TitleCards({ msg }) {
  const titles = titlesOf(msg.cards).filter((t) => named(t, msg.content));
  const plans = plansOf(msg.cards).filter((p) => named(p, msg.content));
  if (titles.length === 0 && plans.length === 0) return null;
  return (
    <div className="kc-chat-cards">
      {plans.map((p) => <PlanCard key={p.id} plan={p} />)}
      {titles.filter((t) => !plans.some((p) => p.imdbId === t.imdbId)).map((t) => <TitleCard key={t.imdbId} title={t} />)}
    </div>
  );
}

/**
 * Sends the words a page handed over (`assistantHandoff.js`) as the first message of a new conversation,
 * once the assistant answers. Draws nothing.
 */
function Handoff({ chat }) {
  const request = useHandoff();
  React.useEffect(() => {
    if (!request || !chat.usable) return;
    if (Date.now() - request.at > HANDOFF_MS) {
      settleHandoff(request);
      return;
    }
    if (!chat.empty) {
      chat.newChat();
      return;
    }
    settleHandoff(request);
    chat.ask(request.text);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- a request is taken when the chat is ready for it
  }, [request, chat.usable, chat.empty]);
  return null;
}

/** What stands over the composer: the handoff, and the opening suggestions until the person writes. */
const aboveComposer = (chat) => (
  <>
    <Handoff chat={chat} />
    {chat.empty && !chat.input.trim() ? <ChatSuggestionChips /> : null}
  </>
);

/**
 * The assistant, in the dock beside a page when `docked` (a wide screen), and as a page of its own
 * otherwise (`pages/Assistant.jsx`).
 */
export function CinemaAssistant({ session, docked = false, dock, onClose }) {
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
      assistantHost={HOST}
      clientFor={clientFor}
      authed={!!session}
      connection={{ tone: "online", label: "Connected", usable: !!session }}
      storageKey={"cinema:chat:" + (user ? user.id : "anonymous")}
      assistantName="Assistant"
      profile={cinemaProfile}
      suggestions={SUGGESTIONS}
      slots={{ aboveComposer }}
      noAssistant="The assistant isn't answering right now."
      renderCards={(msg) => <TitleCards msg={msg} />} />
  );
}
