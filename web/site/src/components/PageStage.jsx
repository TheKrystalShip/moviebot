import React from "react";
import { PageKey } from "../crumbs.js";
import { LoadTracker } from "../data.js";

/** The longest a page waits out of sight for its reads before it is shown as it stands. */
const READY_CAP_MS = 500;

/**
 * Moving between pages without showing one half-built.
 *
 * A page reads the API when it mounts, so mounted in place it is empty for the length of a round
 * trip and then rearranges itself around what came back. Here the page navigated to is mounted out
 * of sight, at the column's width, while the page being left stays where it is; once the new page's
 * first reads are answered, and the artwork in view has decoded, it takes the old one's place and
 * fades in. A page that takes longer than `READY_CAP_MS` is shown as it stands, waiting the way it
 * always does. The address, the tab bar and the sidebar move at once, so the press is answered
 * whatever the page is still doing.
 *
 * A page is its route's key: its path less the part that only chooses what the page shows (a show's
 * season, a profile tab), so a new season, a new tab or a new query on the same path (a search) is the
 * same page given new words, and is drawn in place. Both pages are children of one keyed list, so the
 * page prepared out of sight is the one shown, never mounted a second time.
 *
 * `above(route)` draws what sits over the stage once, for the route of the page shown, so it never
 * runs ahead of the page it describes while the next is being prepared.
 */
export function PageStage({ route, render, above, onShown }) {
  const stage = useStage(route.key, route, route.path + "?" + route.query.toString(), onShown);
  const page = (entry, tracker) => (
    <PageKey.Provider value={entry.key}><LoadTracker.Provider value={tracker}>{render(entry.value)}</LoadTracker.Provider></PageKey.Provider>
  );
  return <>{above && above(stage.shown.value)}<Staged stage={stage} draw={page} settled={null} /></>;
}

/**
 * Moving between the tabs of one page the way `PageStage` moves between pages: the tab strip answers
 * at once, the tab chosen is prepared out of sight under the one shown, and fades in once its first
 * reads are answered. The tab first shown reads under the page's own preparation, so the page and its
 * opening tab arrive together.
 */
export function TabStage({ tab, render }) {
  const inherited = React.useContext(LoadTracker);
  const stage = useStage(tab, tab, tab);
  const panel = (entry, tracker) => <LoadTracker.Provider value={tracker}>{render(entry.value)}</LoadTracker.Provider>;
  return <Staged stage={stage} draw={panel} settled={inherited} />;
}

/**
 * One thing shown at a time under a key. The same key with a new `revision` is drawn in place; a new
 * key is mounted out of sight with a tracker of its own and promoted once its reads are answered and
 * its artwork has decoded, or after `READY_CAP_MS`, whichever is first.
 */
function useStage(key, value, revision, onShown) {
  const [shown, setShown] = React.useState({ key, value });
  const [incoming, setIncoming] = React.useState(null);
  const [entering, setEntering] = React.useState(null);
  const box = React.useRef(null);

  React.useEffect(() => {
    if (key === shown.key) {
      setShown({ key, value });
      setIncoming(null);
    } else {
      setIncoming({ key, value, tracker: makeTracker() });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, revision]);

  React.useEffect(() => {
    if (!incoming) return undefined;
    let live = true;
    const promote = () => {
      if (!live) return;
      live = false;
      setShown({ key: incoming.key, value: incoming.value });
      setIncoming(null);
      setEntering(incoming.key);
    };
    const cap = setTimeout(promote, READY_CAP_MS);
    incoming.tracker.whenIdle(() => {
      if (!live) return;
      artworkDecoded(box.current).then(promote);
    });
    return () => { live = false; clearTimeout(cap); };
  }, [incoming]);

  React.useLayoutEffect(() => {
    if (entering && onShown) onShown();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [entering]);

  return { shown, incoming, entering, box, settle: () => setEntering(null) };
}

/**
 * The entry shown and the one being prepared, children of one keyed list so the one prepared is the
 * one shown, never mounted a second time. `settled` is the tracker the shown entry reads under.
 */
function Staged({ stage, draw, settled }) {
  const { shown, incoming, entering, box, settle } = stage;
  const entries = [
    <div key={shown.key} className={"kc-stage__page" + (entering === shown.key ? " is-entering" : "")}
      onAnimationEnd={(e) => { if (e.target === e.currentTarget) settle(); }}>
      {draw(shown, settled)}
    </div>,
  ];
  if (incoming) {
    entries.push(
      <div key={incoming.key} ref={box} className="kc-stage__page is-incoming" aria-hidden="true">
        {draw(incoming, incoming.tracker)}
      </div>,
    );
  }
  return <div className="kc-stage">{entries}</div>;
}

/**
 * Counts the reads a page makes on mounting. Idle is checked a frame after the count reaches zero,
 * because a read answered can start the next one (a show's page reads its offers once it knows it).
 */
function makeTracker() {
  let pending = 0;
  let waiter = null;
  const check = () => requestAnimationFrame(() => {
    if (pending === 0 && waiter) {
      const done = waiter;
      waiter = null;
      done();
    }
  });
  return {
    begin() { pending += 1; },
    end() {
      pending = Math.max(0, pending - 1);
      if (pending === 0) check();
    },
    whenIdle(done) {
      waiter = done;
      check();
    },
  };
}

/** Every image in the first screenful of a page, decoded or failed, so artwork does not pop in. */
function artworkDecoded(root) {
  if (!root) return Promise.resolve();
  const fold = window.innerHeight;
  const images = [...root.querySelectorAll("img")].filter((img) => !img.complete && img.getBoundingClientRect().top < fold);
  return Promise.all(images.map((img) => img.decode().catch(() => {}))).then(() => {});
}
