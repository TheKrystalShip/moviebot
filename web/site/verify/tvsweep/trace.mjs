// Reduces a Chrome trace of one key press: where the renderer's main thread and the GPU thread spent
// the window after the keydown, what image decoding cost, and the frames the compositor drew or dropped.

const BUCKET = [
  [/^(MinorGC|MajorGC|V8\.GC|BlinkGC|GCEvent|V8\.Scavenge|V8\.MarkCompact|ThreadState::|CppGC)/, "gc"],
  [/^(UpdateLayoutTree|RecalculateStyles|ParseAuthorStyleSheet|InvalidateLayout|ScheduleStyleRecalculation|Document::updateStyle|StyleRecalc)/, "style"],
  [/^(Layout|UpdateLayout|LocalFrameView::layout|LayoutShift)/, "layout"],
  [/^(Paint|PrePaint|PaintImage|Layerize|UpdateLayer|CompositeLayers|Commit|PaintArtifactCompositor|cc::|LocalFrameView::RunPaintLifecyclePhase|Decode|ImageDecodeTask)/, "paint"],
  [/^(FunctionCall|EventDispatch|TimerFire|FireAnimationFrame|EvaluateScript|RunMicrotasks|v8\.|V8\.|XHR|FireIdleCallback|ParseHTML|HitTest|ScrollLayer|Animation)/, "script"],
];
const bucketOf = (name) => { for (const [re, b] of BUCKET) if (re.test(name)) return b; return "other"; };

export function analyse(events, windowMs) {
  const threads = new Map();
  for (const e of events) if (e.ph === "M" && e.name === "thread_name") threads.set(`${e.pid}:${e.tid}`, e.args.name);
  const tname = (e) => threads.get(`${e.pid}:${e.tid}`) || "";
  const key = events.find((e) => e.name === "EventDispatch" && e.args?.data?.type === "keydown");
  if (!key) return { error: "no keydown in trace" };
  const main = `${key.pid}:${key.tid}`;
  const t0 = key.ts;
  const t1 = t0 + windowMs * 1000;
  const inWin = (e) => e.ts + (e.dur || 0) >= t0 && e.ts <= t1;
  const clip = (e) => (Math.min(e.ts + (e.dur || 0), t1) - Math.max(e.ts, t0)) / 1000;

  // Main thread: self time per event, so nested work is counted once.
  const mainX = events.filter((e) => e.ph === "X" && `${e.pid}:${e.tid}` === main && inWin(e)).sort((a, b) => a.ts - b.ts || b.dur - a.dur);
  const stack = [];
  const self = new Map();
  for (const e of mainX) {
    while (stack.length && stack.at(-1).ts + stack.at(-1).dur <= e.ts) stack.pop();
    if (stack.length) self.set(stack.at(-1), (self.get(stack.at(-1)) ?? stack.at(-1).dur) - e.dur);
    self.set(e, self.get(e) ?? e.dur);
    stack.push(e);
  }
  const buckets = { script: 0, style: 0, layout: 0, paint: 0, gc: 0, other: 0 };
  const byName = new Map();
  for (const [e, s] of self) {
    if (/RunTask|ThreadController|ThreadPool|TaskQueue|SequenceManager|MessageLoop/.test(e.name)) continue;
    const ms = Math.max(0, s) / 1000;
    buckets[bucketOf(e.name)] += ms;
    byName.set(e.name, (byName.get(e.name) || 0) + ms);
  }
  // Busy: top-level tasks.
  const top = mainX.filter((e) => /RunTask/.test(e.name));
  let busy = 0, lastEnd = 0;
  for (const e of top) { if (e.ts >= lastEnd) { busy += clip(e); lastEnd = e.ts + e.dur; } }
  const longTasks = top.filter((e) => e.dur > 50000).map((e) => ({ at: Math.round((e.ts - t0) / 1000), d: Math.round(e.dur / 1000) }));

  // Image decodes run on worker threads, in parallel with the rest; summed, they are CPU spent.
  const decode = Math.round(events.filter((e) => e.ph === "X" && e.name === "ImageDecodeTask" && inWin(e)).reduce((s, e) => s + clip(e), 0));
  const gpuThreads = (e) => /CrGpuMain|VizCompositor|GpuMain|InProcGpu/i.test(tname(e));
  const gpuTop = events.filter((e) => e.ph === "X" && inWin(e) && gpuThreads(e)).sort((a, b) => a.ts - b.ts);
  let gpu = 0, gEnd = 0;
  for (const e of gpuTop) { if (e.ts >= gEnd) { gpu += clip(e); gEnd = e.ts + e.dur; } }

  // Frames: the compositor's draws, and what the frame reporter says was dropped. This WebView
  // rasterizes on its GPU thread (Chrome_InProcGpuThread), so raster is inside `gpu`.
  const drawn = events.filter((e) => e.name === "DrawFrame" && e.ts >= t0 && e.ts <= t1).length;
  const reporters = events.filter((e) => e.name === "PipelineReporter" && e.ph === "b" && e.ts >= t0 && e.ts <= t1);
  const states = {};
  for (const e of reporters) { const s = e.args?.chrome_frame_reporter?.state ?? e.args?.data?.state ?? "?"; states[s] = (states[s] || 0) + 1; }
  const dropped = events.filter((e) => /DroppedFrame|Frame Dropped/.test(e.name) && e.ts >= t0 && e.ts <= t1).length
    + (states.STATE_DROPPED || 0) + (states.STATE_PRESENTED_PARTIAL || 0);
  const r = (x) => Math.round(x);
  return {
    main: { busy: r(busy), script: r(buckets.script), style: r(buckets.style), layout: r(buckets.layout), paint: r(buckets.paint), gc: r(buckets.gc), other: r(buckets.other) },
    topNames: [...byName].sort((a, b) => b[1] - a[1]).slice(0, 8).map(([n, ms]) => `${n} ${r(ms)}`),
    longTasks, gpu: r(gpu), decode, frames: { drawn, dropped, states },
  };
}

/** For calibrating: the names and threads in a trace, most frequent first. */
export function census(events) {
  const threads = new Map();
  for (const e of events) if (e.ph === "M" && e.name === "thread_name") threads.set(`${e.pid}:${e.tid}`, e.args.name);
  const n = new Map();
  for (const e of events) { const k = `${threads.get(`${e.pid}:${e.tid}`) || "?"} | ${e.name} | ${e.ph}`; n.set(k, (n.get(k) || 0) + 1); }
  return [...n].sort((a, b) => b[1] - a[1]);
}
