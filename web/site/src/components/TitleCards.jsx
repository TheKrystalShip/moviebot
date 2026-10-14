import { Icon, ShowcaseCard, Skeleton } from "@thekrystalship/krystal";
import { discoverFilmPath, discoverPath, moviePath, showPath } from "../paths.js";
import { linkProps, navigate } from "../router.js";
import { kindLine, watchHref } from "./Film.jsx";
import { useSaveRibbon } from "./SaveButton.jsx";

// Poster cards for titles that are not on a shelf: a search's answers, what somebody waits on, what
// they asked to be fetched. Every card is krystal's ShowcaseCard, so a page of them reads like
// Home's rows; `kc-card` lets a plain status line (a cast, a note) end in an ellipsis.

/**
 * The poster a title's page shows, kept by this server, as a card's art: the film's, else the
 * show's, else `fallback`. Each is a layer, and a layer whose image is not there draws nothing, so the
 * first that exists is the one seen.
 */
export function keptArt(imdbId, fallback) {
  if (!imdbId) return fallback ? `url("${fallback}")` : null;
  const id = encodeURIComponent(imdbId);
  return [`url("/films/${id}/poster.jpg")`, `url("/shows/${id}/poster.jpg")`, fallback && `url("${fallback}")`]
    .filter(Boolean).join(", ");
}

/**
 * A grid of cards while what fills it is read, at the cards' own height so they settle in place.
 * `row` draws only as many as fit across one row, for a list of unknown length whose page should not
 * grow taller than the screen while it waits.
 */
export function CardsSkeleton({ count = 6, row = false }) {
  return (
    <div className={"kc-grid" + (row ? " kc-grid--row" : "")} aria-hidden="true">
      {Array.from({ length: count }, (_, i) => (
        <div key={i} className="k-skeleton-card">
          <Skeleton className="k-skeleton-card__art" h="auto" r={0} style={{ aspectRatio: "2 / 3" }} />
          <div className="kc-card-skel__foot">
            <Skeleton h={30} />
            <Skeleton w="70%" h={12} />
          </div>
        </div>
      ))}
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

/** Where a found film or show leads: the library's own page when it is here, its catalogue page when not. */
export function foundPage(found, here, held) {
  if (found.kind === "show") return showPath(found.imdbId, { held, category: found.category });
  return here ? moviePath(here.id, here.category) : discoverFilmPath(found.imdbId);
}

/**
 * A film or show the catalogue answered with: whether it is a film or a show on its poster, its rating
 * and year, and its cast, or `line` in its place (a person's part in it). `here` is the library's title for a film
 * it holds, `held` whether it holds an episode of a show; they decide where the card leads and whether
 * it plays from here, and nothing on the card says which. Its ribbon saves it.
 */
export function FoundCard({ found, here, held, line = found.starring }) {
  const show = found.kind === "show";
  const playable = here && here.status === "ready";
  const [ribbon, lists] = useSaveRibbon(found.imdbId, show ? "show" : "film", found.title);
  return (
    <>
      <ShowcaseCard
        className="kc-card"
        name={found.title}
        eyebrow={kindLine(found.kind, found)}
        art={found.posterUrl ? `url("${found.posterUrl}")` : null}
        onPick={() => navigate(foundPage(found, here, held))}
        specs={[
          { icon: "star", value: found.rating != null ? found.rating.toFixed(1) : null, label: "Rating" },
          { icon: "calendar", value: found.year || null, label: "Year" },
        ]}
        status={line ? { label: line, title: line } : null}
        action={playable
          ? { label: "Watch", onClick: () => navigate(watchHref(here.id)) }
          : { label: "View" }}
        ribbon={ribbon} />
      {lists}
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
  const [ribbon, lists] = useSaveRibbon(film.imdbId, "film", film.title);
  return (
    <>
      <ShowcaseCard
        className="kc-card"
        name={film.title}
        eyebrow={kindLine("film", film)}
        art={keptArt(film.imdbId, film.posterUrl)}
        onPick={() => navigate(discoverFilmPath(film.imdbId))}
        specs={[{ icon: "calendar", value: film.year || null, label: "Year" }]}
        status={{ icon: "bell", label: waitersWords(film.waiters, film.waiting) }}
        action={{ label: "View" }}
        ribbon={ribbon} />
      {lists}
    </>
  );
}

/**
 * A film somebody is waiting on: out now with its release, or how many wait on it. `onStop` lets the
 * person stop waiting; `downloading` turns a film that is out into Get it.
 */
export function WishCard({ wish, onStop, downloading, status }) {
  const out = !!wish.release;
  const waiting = wish.subscribers.length === 1 ? "Only you are waiting" : `${wish.subscribers.length} waiting`;
  return (
    <ShowcaseCard
      className="kc-card"
      name={wish.title}
      eyebrow={kindLine("film", wish)}
      art={keptArt(wish.imdbId, wish.posterUrl)}
      onPick={() => navigate(discoverFilmPath(wish.imdbId))}
      badge={out ? { tone: "accent", label: "Out now" } : null}
      specs={[
        { icon: "calendar", value: wish.year || null, label: "Year" },
        { icon: "users", value: wish.subscribers.length, label: "Waiting" },
      ]}
      status={status || (out
        ? { tone: "success", icon: "circle-check", label: wish.release.summary, title: wish.release.summary }
        : wish.starring ? { label: wish.starring, title: wish.starring } : { label: waiting })}
      action={{ label: out && downloading ? "Get it" : "View" }}
      tools={onStop ? [{ icon: "bell-off", label: `Stop waiting on ${wish.display}`, onClick: () => onStop(wish) }] : null} />
  );
}
