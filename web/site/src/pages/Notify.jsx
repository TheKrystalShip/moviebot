import React from "react";
import { Icon, toast } from "@thekrystalship/krystal-ui";
import { api } from "../api.js";
import { useLoad } from "../data.js";
import { navigate } from "../router.js";
import { Empty, Failed, PageHead } from "../components/Film.jsx";
import { NotOutYet } from "./Search.jsx";

/**
 * Films somebody is waiting on: ones that cannot be downloaded yet. The server asks the tracker about
 * each of them every hour, and a film counts as out once a release from a web encode up is offered.
 */
export function Notify() {
  const wishes = useLoad(() => api.wishes(), []);
  const [draft, setDraft] = React.useState("");
  const [query, setQuery] = React.useState("");
  const found = useLoad(() => (query ? api.releases(query) : Promise.resolve(null)), [query]);

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

      {query && (
        <div className="kc-rows">
          <Failed error={found.error} onRetry={found.reload} />
          {found.loading && <span className="kc-quiet">Looking it up…</span>}
          {found.data && found.data.candidates.length > 0 && (
            <div className="kc-row">
              <div className="kc-row__text">
                <span className="kc-row__title">{found.data.film ? found.data.film.display : query}</span>
                <span className="kc-row__sub">It can be downloaded already.</span>
              </div>
              <button type="button" className="kc-btn kc-btn--primary" onClick={() => navigate("/search?q=" + encodeURIComponent(query))}>
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
      )}

      <section className="kc-shelf">
        <div className="kc-shelf__head"><h2>What you are waiting on</h2></div>
        <Failed error={wishes.error} onRetry={wishes.reload} />
        {wishes.data && wishes.data.length === 0 && <Empty icon="bell" title="Nothing yet">Find a film above to be told when it is out.</Empty>}
        <div className="kc-rows">
          {(wishes.data || []).map((wish) => (
            <div key={wish.imdbId} className="kc-row">
              {wish.posterUrl ? <img className="kc-row__thumb" src={wish.posterUrl} alt="" /> : <span className="kc-row__thumb" />}
              <div className="kc-row__text">
                <span className="kc-row__title">{wish.display}</span>
                {wish.starring && <span className="kc-row__sub">{wish.starring}</span>}
                <span className="kc-row__sub">
                  {wish.subscribers.length === 1 ? "Only you are waiting" : `${wish.subscribers.length} people are waiting`}
                </span>
              </div>
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
