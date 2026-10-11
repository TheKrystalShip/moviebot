// The recorder a measured page carries, installed before the site's own scripts. It records from the
// moment it is installed; `__tvPerf.begin()` starts a fresh record and `__tvPerf.take()` answers it.
//
// Per animation frame it keeps the focused element (an identity and its rectangle) and what is drawn
// under a grid of points across the screen: the element hit, and for an image whether it has decoded.
// Beside the frames it keeps the browser's own entries: layout shifts, long animation frames with the
// scripts in them, key events slower than a frame, and every resource fetched. A key press is stamped
// with the event's own time, so how long the key took to reach the page is not counted.
(() => {
  if (window.__tvPerf) return;
  const Cols = 6;
  const Rows = 4;
  const fresh = () => ({ drawn: S?.drawn ?? false, frames: [], shifts: [], loafs: [], events: [], resources: [], keys: [], recording: true, since: performance.now() });
  let S = null;
  S = fresh();

  const ids = new WeakMap();
  let nextId = 1;
  const idOf = (el) => {
    if (!el) return 0;
    if (!ids.has(el)) ids.set(el, nextId++);
    return ids.get(el);
  };
  const nameOf = (el) => !el || el === document.body ? "" :
    (el.getAttribute("aria-label") || el.textContent || el.getAttribute("href") || el.tagName).trim().replace(/\s+/g, " ").slice(0, 48);

  // What is under one point: blank when it is the document itself or the page's empty root.
  const hit = (x, y) => {
    const el = document.elementFromPoint(x, y);
    if (!el || el === document.documentElement || el === document.body || el.id === "root") return "0";
    return el.tagName === "IMG" ? `${idOf(el)}${el.complete && el.naturalWidth ? "" : "~"}` : String(idOf(el));
  };

  const observe = (type, take, extra = {}) => {
    try {
      new PerformanceObserver((list) => { if (S.recording) for (const e of list.getEntries()) take(e); })
        .observe({ type, buffered: true, ...extra });
    } catch { /* the entry type is not offered by this engine */ }
  };
  observe("layout-shift", (e) => S.shifts.push({ t: e.startTime, v: e.value }));
  observe("long-animation-frame", (e) => S.loafs.push({
    t: e.startTime, d: e.duration, block: e.blockingDuration,
    scripts: (e.scripts || []).filter((s) => s.duration >= 5).map((s) => ({
      d: Math.round(s.duration), layout: Math.round(s.forcedStyleAndLayoutDuration || 0), invoker: s.invoker,
      source: `${(s.sourceURL || "").split("/").pop()}:${s.sourceFunctionName || ""}`,
    })),
  }));
  observe("event", (e) => S.events.push({ name: e.name, t: e.startTime, d: e.duration }), { durationThreshold: 16 });
  observe("resource", (e) => S.resources.push({ t: e.startTime, end: e.responseEnd, size: e.transferSize, type: e.initiatorType,
    name: e.name.replace(location.origin, "") }));
  observe("paint", (e) => { if (e.name === "first-contentful-paint") S.fcp = e.startTime; });

  addEventListener("keydown", (e) => { if (S.recording) S.keys.push({ key: e.key, t: e.timeStamp }); }, true);

  const frame = (t) => {
    if (S.recording && document.body) {
      const el = document.activeElement;
      const r = el && el !== document.body ? el.getBoundingClientRect() : null;
      const grid = [];
      for (let row = 0; row < Rows; row++) {
        for (let col = 0; col < Cols; col++) grid.push(hit((col + 0.5) * innerWidth / Cols, (row + 0.5) * innerHeight / Rows));
      }
      if (!S.drawn && grid.filter((p) => p !== "0").length >= grid.length / 4) S.drawn = true;
      const focus = idOf(r ? el : null);
      const last = S.frames.at(-1);
      S.frames.push({
        t, focus, rect: r ? [Math.round(r.left), Math.round(r.top), Math.round(r.width), Math.round(r.height)] : null,
        grid: grid.join(" "), path: location.pathname, name: !last || last.focus !== focus ? nameOf(el) : undefined,
      });
    }
    requestAnimationFrame(frame);
  };
  requestAnimationFrame(frame);

  window.__tvPerf = {
    begin() { S = fresh(); },
    take() { S.recording = false; const out = JSON.parse(JSON.stringify(S)); S.recording = true; return out; },
    /**
     * Milliseconds since anything recorded last changed: the grid, the focus, a shift or a fetch. A
     * document that has drawn nothing yet is not quiet, however long its scripts take to start.
     */
    quiet() {
      if (!S.drawn) return 0;
      const now = performance.now();
      let last = S.since;
      const f = S.frames;
      for (let i = f.length - 1; i > 0; i--) {
        const a = f[i - 1], b = f[i];
        if (a.grid !== b.grid || a.focus !== b.focus || String(a.rect) !== String(b.rect)) { last = b.t; break; }
      }
      for (const s of S.shifts) last = Math.max(last, s.t);
      for (const r of S.resources) last = Math.max(last, r.end);
      return now - last;
    },
    where() { const el = document.activeElement; return { path: location.pathname + location.search, focus: nameOf(el) }; },
  };
})();
