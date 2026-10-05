import React from "react";
import { Button, Icon, toast } from "@thekrystalship/krystal-ui";
import { api } from "../api.js";
import { useLoad } from "../data.js";
import { useCan } from "../session.js";
import { Empty, Failed, PageHead } from "../components/Film.jsx";
import { CardsSkeleton, FoundCard, WishCard } from "../components/TitleCards.jsx";

/**
 * Films somebody is waiting on: ones that cannot be downloaded yet. The server asks the tracker about
 * each of them every hour, and a film counts as out once a release from a web encode up is offered;
 * one that has come out stays here, with its release, until they stop waiting on it. A film is found
 * by name in the catalogue, and waited on from its own page.
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
        <Button type="submit" variant="primary" size="lg">Find it</Button>
      </form>

      {query && <FoundInCatalogue key={query} query={query} />}

      <section className="kc-shelf">
        <div className="kc-shelf__head"><h2>What you are waiting on</h2></div>
        <Failed error={wishes.error} onRetry={wishes.reload} />
        {!wishes.data && !wishes.error && <CardsSkeleton count={4} />}
        {wishes.data && wishes.data.length === 0 && <Empty icon="bell" title="Nothing yet">Find a film above to be told when it is out.</Empty>}
        {wishes.data && wishes.data.length > 0 && (
          <div className="kc-grid">
            {wishes.data.map((wish) => <WishCard key={wish.imdbId} wish={wish} downloading={downloading} onStop={cancel} />)}
          </div>
        )}
      </section>
    </div>
  );
}

/** The catalogue's films for the name, each leading to its page, where it is waited on or fetched. */
function FoundInCatalogue({ query }) {
  const found = useLoad(() => api.findFilms(query), [query]);
  const titles = useLoad(() => api.titles().catch(() => []), []);
  const here = new Map((titles.data || []).filter((t) => t.kind !== "show" && t.film).map((t) => [t.film.imdbId, t]));
  return (
    <section className="kc-shelf">
      <Failed error={found.error} onRetry={found.reload} />
      {found.loading && <CardsSkeleton count={6} />}
      {found.data && found.data.length === 0 && <span className="kc-quiet">Nothing in the catalogue is called that.</span>}
      {found.data && found.data.length > 0 && (
        <div className="kc-grid">
          {found.data.map((film) => <FoundCard key={film.imdbId} found={film} here={here.get(film.imdbId)} />)}
        </div>
      )}
    </section>
  );
}
