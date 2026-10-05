import React from "react";
import { Icon, toast } from "@thekrystalship/krystal-ui";
import { api } from "../api.js";
import { useLoad } from "../data.js";
import { useCan } from "../session.js";
import { discoverFilmPath, discoverPath } from "../paths.js";
import { linkProps, navigate } from "../router.js";
import { Empty, Failed, PageHead } from "../components/Film.jsx";
import { NotOutYet } from "./Discover.jsx";
import { KeptPoster } from "../components/FilmDetails.jsx";

/**
 * Films somebody is waiting on: ones that cannot be downloaded yet. The server asks the tracker about
 * each of them every hour, and a film counts as out once a release from a web encode up is offered;
 * one that has come out stays here, with its release, until they stop waiting on it.
 */
export function Notify() {
  const downloading = useCan("cinema:download");
  const wishes = useLoad(() => api.wishes(), []);
  const [draft, setDraft] = React.useState("");
  const [query, setQuery] = React.useState("");

  const cancel = async (wish) => {
    try {
      await api.cancelWish(wish.imdbId);
      toast.info(`You are no longer waiting on ${wish.display}.`);
      wishes.reload();
    } catch (error) {
      toast.fromError(error, "Could not stop waiting");
    }
  };

  return (
    <div className="kc-page">
      <PageHead title="Notify me" sub="Films that are not out yet. The server checks the tracker every hour, and says so when one can be downloaded." />

      <form className="kc-search" onSubmit={(e) => { e.preventDefault(); setQuery(draft.trim()); }}>
        <Icon name="search" size={17} />
        <input value={draft} onChange={(e) => setDraft(e.target.value)} placeholder="A film that is not out yet" />
        <button type="submit" className="kc-btn kc-btn--primary">Find it</button>
      </form>

      {query && (downloading ? <FoundOnTracker query={query} /> : <FoundInCatalogue query={query} />)}

      <section className="kc-shelf">
        <div className="kc-shelf__head"><h2>What you are waiting on</h2></div>
        <Failed error={wishes.error} onRetry={wishes.reload} />
        {wishes.data && wishes.data.length === 0 && <Empty icon="bell" title="Nothing yet">Find a film above to be told when it is out.</Empty>}
        <div className="kc-rows">
          {(wishes.data || []).map((wish) => (
            <div key={wish.imdbId} className="kc-row">
              <KeptPoster imdbId={wish.imdbId} />
              <div className="kc-row__text">
                <a className="kc-row__title" {...linkProps(discoverFilmPath(wish.imdbId))}>{wish.display}</a>
                {wish.release
                  ? <span className="kc-row__sub"><Icon name="circle-check" size={13} />Out now · {wish.release.summary}</span>
                  : wish.starring && <span className="kc-row__sub">{wish.starring}</span>}
                <span className="kc-row__sub">
                  {wish.subscribers.length === 1 ? "Only you are waiting" : `${wish.subscribers.length} people are waiting`}
                </span>
              </div>
              {wish.release && downloading && (
                <button type="button" className="kc-btn kc-btn--primary" onClick={() => navigate(discoverFilmPath(wish.imdbId))}>
                  <Icon name="download" size={16} />Get it
                </button>
              )}
              <button type="button" className="kc-btn kc-btn--ghost" onClick={() => cancel(wish)}>
                <Icon name="bell-off" size={16} />Stop waiting
              </button>
            </div>
          ))}
        </div>
      </section>
    </div>
  );
}

/** What the tracker holds for the name, for somebody who may download: out already, or not yet. */
function FoundOnTracker({ query }) {
  const found = useLoad(() => api.releases(query), [query]);
  return (
    <div className="kc-rows">
      <Failed error={found.error} onRetry={found.reload} />
      {found.loading && <span className="kc-quiet">Looking it up…</span>}
      {found.data && found.data.candidates.length > 0 && (
        <div className="kc-row">
          <div className="kc-row__text">
            <span className="kc-row__title">{found.data.film ? found.data.film.display : query}</span>
            <span className="kc-row__sub">It can be downloaded already.</span>
          </div>
          <button type="button" className="kc-btn kc-btn--primary" onClick={() => navigate(discoverPath(query))}>
            <Icon name="download" size={16} />See releases
          </button>
        </div>
      )}
      {found.data && found.data.candidates.length === 0 && found.data.film && (
        <NotOutYet film={found.data.film} explanation={found.data.emptyExplanation} />
      )}
      {found.data && found.data.candidates.length === 0 && !found.data.film && (
        <span className="kc-quiet">Nothing in the catalogue is called that.</span>
      )}
    </div>
  );
}

/** The catalogue's films for the name, each leading to its page, where it is waited on. */
function FoundInCatalogue({ query }) {
  const found = useLoad(() => api.findFilms(query), [query]);
  return (
    <div className="kc-rows">
      <Failed error={found.error} onRetry={found.reload} />
      {found.loading && <span className="kc-quiet">Looking it up…</span>}
      {found.data && found.data.length === 0 && <span className="kc-quiet">Nothing in the catalogue is called that.</span>}
      {(found.data || []).map((film) => (
        <a key={film.imdbId} className="kc-row kc-row--link" {...linkProps(discoverFilmPath(film.imdbId))}>
          <div className="kc-row__text"><span className="kc-row__title">{film.display}</span></div>
          <Icon name="chevron-right" size={18} />
        </a>
      ))}
    </div>
  );
}
