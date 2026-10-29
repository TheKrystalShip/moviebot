import { Icon, List, ListRow, Skeleton, toast } from "@thekrystalship/krystal";
import { posterArt } from "../api/index.js";
import { VoteButtons } from "../components/VoteButtons.jsx";
import { Empty, Failed } from "../components/Film.jsx";
import { kindWord } from "../components/TitleHero.jsx";
import { ANIME, discoverFilmPath, discoverFranchisePath, discoverListPath, discoverPersonPath, moviePath, showPath } from "../paths.js";
import { linkProps } from "../router.js";
import { ago } from "../time.js";
import { setVote, useVoteList } from "../votes.js";

const KIND_ICONS = { title: "clapperboard", person: "user", franchise: "layers", company: "building-2" };

/** Where a vote's subject is: a title's page (the library's where it holds the title), a person's, a franchise's or a studio's titles. */
function subjectPath(v) {
  const card = v.card;
  switch (v.kind) {
    case "title":
      if (!card) return null;
      if (card.kind === "show") return showPath(v.id, { held: !!(card.you && card.you.inLibrary), category: card.category });
      return card.you && card.you.libraryId ? moviePath(card.you.libraryId, card.category) : discoverFilmPath(v.id);
    case "person": return discoverPersonPath(v.id);
    case "franchise": return discoverFranchisePath(v.id);
    default: return discoverListPath({ company: v.id }, v.name);
  }
}

/** What a vote's subject is: a title's own word, or Person, Franchise or Studio. */
function subjectWord(v) {
  if (v.kind === "title") return v.card ? kindWord(v.card.kind, v.card.category === ANIME) : "Title";
  return { person: "Person", franchise: "Franchise", company: "Studio" }[v.kind];
}

/** The subject's picture: a title's poster, a person's portrait round, a franchise poster or a company logo; an icon where there is none. */
function Lead({ v }) {
  const url = v.kind === "title" ? (v.card ? posterArt(v.card) : null) : v.image ? `url("${v.image}")` : null;
  return (
    <span className={"kc-taste__lead kc-taste__lead--" + v.kind} style={url ? { backgroundImage: url } : undefined} aria-hidden="true">
      {!url && <Icon name={KIND_ICONS[v.kind]} size={18} />}
    </span>
  );
}

/** One vote: the subject, what it is and when, and the pair that moves or clears the vote. */
function VoteRow({ v, onCleared }) {
  const path = subjectPath(v);
  const link = path ? linkProps(path) : null;
  const name = v.name || "";
  return (
    <ListRow lead={<Lead v={v} />} title={name || "Unavailable"} sub={`${subjectWord(v)} · ${ago(v.votedAt)}`}
      href={link ? link.href : undefined} onOpen={link ? link.onClick : undefined} disabled={!link && !name}
      actions={<VoteButtons kind={v.kind} id={v.id} name={name} size="md"
        onChange={(next, previous) => { if (next === 0) onCleared({ ...v, vote: previous }, name); }} />} />
  );
}

function Section({ title, icon, rows, onCleared }) {
  if (rows.length === 0) return null;
  return (
    <section className="kc-section">
      <h2><Icon name={icon} size={18} /> {title}</h2>
      <List label={title} className="kc-taste__list">
        {rows.map((v) => <VoteRow key={v.kind + ":" + v.id} v={v} onCleared={onCleared} />)}
      </List>
    </section>
  );
}

function RowsSkeleton() {
  return (
    <div className="kc-taste__skeleton" aria-hidden="true">
      {Array.from({ length: 6 }, (_, i) => <Skeleton key={i} h={56} r={8} />)}
    </div>
  );
}

/** Every Like and Dislike this person has cast, newest first, each movable or clearable where it stands. */
export function Taste() {
  const { list, error, reload } = useVoteList();

  const cleared = (v, name) => {
    toast.info(`${name || "Vote"} cleared`, {
      action: {
        label: "Undo",
        onClick: () => setVote(v.kind, v.id, v.vote, { name }).catch((e) => toast.fromError(e, "Could not put that back")),
      },
    });
  };

  const liked = list ? list.filter((v) => v.vote === 1) : [];
  const disliked = list ? list.filter((v) => v.vote === -1) : [];
  return (
    <div className="kc-taste">
      <Failed error={list ? null : error} onRetry={reload} />
      {!list && !error && <RowsSkeleton />}
      {list && list.length === 0 && <Empty icon="thumbs-up" title="No likes or dislikes yet">Use the thumbs on a title, a person or a card.</Empty>}
      <Section title="Liked" icon="thumbs-up" rows={liked} onCleared={cleared} />
      <Section title="Disliked" icon="thumbs-down" rows={disliked} onCleared={cleared} />
    </div>
  );
}
