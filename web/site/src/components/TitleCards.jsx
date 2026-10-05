import { ShowcaseCard, Skeleton } from "@thekrystalship/krystal-ui";
import { discoverFilmPath, moviePath, showPath } from "../paths.js";
import { navigate } from "../router.js";
import { watchHref } from "./Film.jsx";

// Poster cards for titles that are not on a shelf: a search's answers, what somebody waits on, what
// they asked to be fetched. Every card is krystal-ui's ShowcaseCard, so a page of them reads like
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

/** A grid of cards while what fills it is read, at the cards' own height so they settle in place. */
export function CardsSkeleton({ count = 6 }) {
  return (
    <div className="kc-grid" aria-hidden="true">
      {Array.from({ length: count }, (_, i) => (
        <div key={i} className="skel-card">
          <Skeleton className="skel-card__art" h="auto" r={0} style={{ aspectRatio: "2 / 3" }} />
          <div className="kc-card-skel__foot">
            <Skeleton h={30} />
            <Skeleton w="70%" h={12} />
          </div>
        </div>
      ))}
    </div>
  );
}

/** Where a found film or show leads: the library's own page when it is here, its catalogue page when not. */
export function foundPage(found, here, held) {
  if (found.kind === "show") return showPath(found.imdbId, { held });
  return here ? moviePath(here.id) : discoverFilmPath(found.imdbId);
}

/**
 * A film or show the catalogue answered a search with: its rating, year, whether it is a film or a
 * show, and its cast. `here` is the library's title for a film it holds, `held` whether it holds an
 * episode of a show.
 */
export function FoundCard({ found, here, held }) {
  const show = found.kind === "show";
  const inLibrary = show ? held : !!here;
  const playable = here && here.status === "ready";
  return (
    <ShowcaseCard
      className="kc-card"
      name={found.title}
      art={found.posterUrl ? `url("${found.posterUrl}")` : null}
      onPick={() => navigate(foundPage(found, here, held))}
      specs={[
        { icon: "star", value: found.rating != null ? found.rating.toFixed(1) : null, label: "Rating" },
        { icon: "calendar", value: found.year || null, label: "Year" },
        { icon: show ? "tv" : "clapperboard", value: show ? "Series" : "Movie", label: "Type" },
      ]}
      status={inLibrary
        ? { tone: "success", icon: "check", label: "In the library" }
        : found.starring ? { label: found.starring, title: found.starring } : null}
      action={playable
        ? { label: "Watch", onClick: () => navigate(watchHref(here.id)) }
        : { label: inLibrary ? "Open" : "View" }} />
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
