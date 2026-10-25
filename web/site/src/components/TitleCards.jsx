import { Icon, PosterCard, Skeleton } from "@thekrystalship/krystal";
import { cardRating, posterArt } from "../api/index.js";
import { discoverFilmPath, discoverPath, moviePath, showPath } from "../paths.js";
import { linkProps, navigate } from "../router.js";
import { watchHref } from "./Film.jsx";
import { useWantRibbon } from "./WantButton.jsx";

// Poster cards for titles that are not on a shelf: a search's answers and what somebody waits on. Every
// card is krystal's PosterCard, so a page of them reads like Home's rows.

/**
 * A grid of cards while what fills it is read, at the cards' own height so they settle in place.
 * `row` draws only as many as fit across one row, for a list of unknown length whose page should not
 * grow taller than the screen while it waits.
 */
export function CardsSkeleton({ count = 6, row = false }) {
  return (
    <div className={"kc-grid" + (row ? " kc-grid--row" : "")} aria-hidden="true">
      {Array.from({ length: count }, (_, i) => <SkeletonCard key={i} />)}
    </div>
  );
}

/** One poster card while it is read, at the card's own height. */
export function SkeletonCard() {
  return (
    <div className="k-skeleton-card">
      <Skeleton className="k-skeleton-card__art" h="auto" r={0} style={{ aspectRatio: "2 / 3" }} />
      <div className="kc-card-skel__foot">
        <Skeleton h={30} />
        <Skeleton w="70%" h={12} />
      </div>
    </div>
  );
}

/** The tile closing a shelf, leading past its last poster to more of the same in Discover. */
export function MoreTile({ label, to = discoverPath() }) {
  return (
    <a className="kc-more-tile" {...linkProps(to)}>
      <Icon name="compass" size={26} />
      <span>{label}</span>
    </a>
  );
}

/**
 * Where a card leads: a film the library holds to its library page, any other film to its catalogue
 * page, and a show to its page, which is the library's when it holds an episode of it. `here` is the
 * library's title for a film it holds, `held` whether it holds an episode of a show.
 */
export function foundPage(card, here, held) {
  if (card.kind === "show") return showPath(card.id, { held, category: card.category });
  return here ? moviePath(here.id, card.category) : discoverFilmPath(card.id);
}

/**
 * A title the catalogue listed, as its card: its poster, rating and year, and its two top-billed or
 * `line` in their place (a person's part in it, which a list filtered by them carries as `credit`). It
 * plays from the card when the library holds it ready, and nothing on the card says where it comes
 * from. Its ribbon wants it.
 */
export function FoundCard({ found, here, held, line }) {
  const show = found.kind === "show";
  const playable = here && here.status === "ready";
  const status = line || (found.credit && found.credit.role) || (found.starring || []).join(", ");
  const [ribbon, wantMenu] = useWantRibbon(found.id, show ? "show" : "film", found.title);
  return (
    <>
      <PosterCard
        name={found.title}
        art={posterArt(found)}
        onPick={() => navigate(foundPage(found, here, held))}
        facts={[
          { icon: "star", value: cardRating(found), label: "Rating" },
          { value: found.year || null, label: "Year" },
        ]}
        status={status ? { label: status, title: status } : null}
        action={playable ? { label: "Watch", onClick: () => navigate(watchHref(here.id)) } : undefined}
        ribbon={ribbon} />
      {wantMenu}
    </>
  );
}

/** How many wait on a film, as its page and its card say it: `waiting` is whether the person is one of the `waiters`. */
export function waitersWords(waiters, waiting) {
  const others = waiters - (waiting ? 1 : 0);
  if (waiting) return others ? `You + ${others} ${others === 1 ? "other" : "others"} waiting` : "You're waiting";
  return others ? `${others} waiting` : "Nobody's waiting yet";
}

/** A film not out yet that people wait on, from the Anticipated row: its year, how many wait, and its ribbon. */
export function AnticipatedCard({ film }) {
  const [ribbon, wantMenu] = useWantRibbon(film.imdbId, "film", film.title);
  return (
    <>
      <PosterCard
        name={film.title}
        art={film.posterUrl ? `url("${film.posterUrl}")` : null}
        onPick={() => navigate(discoverFilmPath(film.imdbId))}
        facts={[{ value: film.year || null, label: "Year" }]}
        status={{ icon: "bell", label: waitersWords(film.waiters, film.waiting) }}
        ribbon={ribbon} />
      {wantMenu}
    </>
  );
}
