// Reading one step's trace from a `sweep.mjs --trace` run (the <step>.json beside results.json).
//
//   node verify/tvsweep/inspect.mjs threads <trace> [windowMs]            busy time by thread and event
//   node verify/tvsweep/inspect.mjs tasks   <trace> [windowMs] [minMs]    the long main-thread tasks, and
//                                                                         the biggest events inside each
//   node verify/tvsweep/inspect.mjs forced  <trace> [windowMs] [minMs]    big style and layout passes, with
//                                                                         the JS stack that forced each
//                                                                         (needs --invalidations)
//   node verify/tvsweep/inspect.mjs profile <trace> [windowMs]            JS self time by source function
//                                                                         (needs --profile)
//   node verify/tvsweep/inspect.mjs callers <trace> [windowMs] <leaf...>  who calls a native leaf such as
//                                                                         getBoundingClientRect (--profile)
//
// The window starts at the step's keydown. Script locations are mapped through the source maps of the
// build in dist/, so inspect a trace taken of the build dist/ holds: the one deployed, or the one the
// scratch site serves.

import { existsSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { createRequire } from "node:module";

const { SourceMapConsumer } = createRequire(import.meta.url)("source-map-js");
const assets = fileURLToPath(new URL("../../dist/assets/", import.meta.url));
const [command, file, windowArg, ...rest] = process.argv.slice(2);
if (!command || !file) { console.error("inspect.mjs <threads|tasks|forced|profile|callers> <trace.json> [windowMs] ..."); process.exit(2); }
const events = JSON.parse(readFileSync(file, "utf8"));
const key = events.find((e) => e.name === "EventDispatch" && e.args?.data?.type === "keydown");
if (!key) { console.error("No keydown in this trace."); process.exit(1); }
const windowMs = Number(windowArg || 1500);
const t0 = key.ts;
const t1 = t0 + windowMs * 1000;
const threads = new Map();
for (const e of events) if (e.ph === "M" && e.name === "thread_name") threads.set(`${e.pid}:${e.tid}`, e.args.name);
const threadOf = (e) => threads.get(`${e.pid}:${e.tid}`) || "?";
const main = `${key.pid}:${key.tid}`;
const at = (ts) => Math.round((ts - t0) / 1000);

const maps = new Map();
function place(url, line, column) {
  const name = (url || "").split("/").pop();
  if (!name.endsWith(".js")) return null;
  if (!maps.has(name)) {
    const path = assets + name + ".map";
    maps.set(name, existsSync(path) ? new SourceMapConsumer(JSON.parse(readFileSync(path, "utf8"))) : null);
  }
  const map = maps.get(name);
  if (!map) return `${name}:${line}`;
  const p = map.originalPositionFor({ line, column });
  return `${p.name || "?"}@${(p.source || "").replace(/^.*node_modules\//, "nm/").replace(/^.*\/src\//, "src/")}:${p.line}`;
}

if (command === "threads") {
  const sum = new Map();
  for (const e of events) {
    if (e.ph !== "X" || e.ts < t0 || e.ts > t1) continue;
    const k = `${threadOf(e)} | ${e.name}`;
    const a = sum.get(k) || [0, 0];
    a[0] += e.dur / 1000; a[1]++;
    sum.set(k, a);
  }
  for (const [k, [ms, n]] of [...sum].sort((a, b) => b[1][0] - a[1][0]).slice(0, 40)) console.log(ms.toFixed(0).padStart(6), String(n).padStart(5), k);
} else if (command === "tasks") {
  const min = Number(rest[0] || 60) * 1000;
  const X = events.filter((e) => e.ph === "X" && `${e.pid}:${e.tid}` === main && e.ts >= t0 - 1000 && e.ts <= t1).sort((a, b) => a.ts - b.ts);
  for (const task of X.filter((e) => e.name === "ThreadControllerImpl::RunTask" && e.dur >= min)) {
    console.log(`task at ${at(task.ts)} ms, ${Math.round(task.dur / 1000)} ms:`);
    const inside = X.filter((e) => e !== task && e.ts >= task.ts && e.ts + e.dur <= task.ts + task.dur && e.dur > 15000 && !/RunTask|ThreadController/.test(e.name));
    for (const e of inside.slice(0, 14)) {
      const d = e.args?.data || e.args?.beginData || {};
      const extra = d.functionName ? ` ${d.functionName}@${(d.url || "").split("/").pop()}:${d.lineNumber}` : d.type ? ` ${d.type}` : e.args?.elementCount ? ` els=${e.args.elementCount}` : "";
      console.log(`      ${e.name}${extra} ${Math.round(e.dur / 1000)}`);
    }
  }
} else if (command === "forced") {
  const min = Number(rest[0] || 15) * 1000;
  for (const e of events) {
    if (!/^(UpdateLayoutTree|Layout)$/.test(e.name) || e.ts < t0 || e.ts > t1 || e.dur < min) continue;
    const stack = e.args?.beginData?.stackTrace;
    const where = stack ? stack.slice(0, 5).map((f) => place(f.url, f.lineNumber, f.columnNumber - 1) || f.functionName).join(" < ") : "(rendering)";
    console.log(`${at(e.ts)} ${e.name} ${Math.round(e.dur / 1000)} ms els=${e.args?.elementCount ?? ""} ${where}`);
  }
} else if (command === "profile" || command === "callers") {
  const nodes = new Map();
  const samples = [];
  let start = null;
  for (const e of events) {
    if (e.name === "Profile" && e.args?.data?.startTime) start = e.args.data.startTime;
    if (e.name !== "ProfileChunk") continue;
    const d = e.args.data;
    for (const n of d.cpuProfile?.nodes || []) nodes.set(n.id, n);
    (d.cpuProfile?.samples || []).forEach((id, i) => samples.push([id, d.timeDeltas[i]]));
  }
  if (start === null) { console.error("No profile in this trace: run the sweep with --profile."); process.exit(1); }
  const label = (cf) => (cf.url ? place(cf.url, cf.lineNumber + 1, cf.columnNumber) : null) || cf.functionName || "(native)";
  const stackOf = (n) => { const s = []; for (let x = n; x && s.length < 7; x = nodes.get(x.parent)) s.push(label(x.callFrame)); return s; };
  const self = new Map();
  let t = start;
  for (const [id, dt] of samples) {
    t += dt;
    if (t < t0 || t > t1) continue;
    const n = nodes.get(id);
    if (!n) continue;
    const leaf = label(n.callFrame);
    if (/\(idle\)|\(program\)/.test(leaf)) continue;
    let k = leaf;
    if (command === "callers") {
      if (!rest.includes(leaf)) continue;
      k = leaf + " < " + stackOf(n).slice(1).filter((x) => !/react-dom|scheduler|\(root\)|\(program\)/.test(x)).slice(0, 4).join(" < ");
    }
    self.set(k, (self.get(k) || 0) + dt / 1000);
  }
  for (const [k, ms] of [...self].sort((a, b) => b[1] - a[1]).slice(0, 25)) console.log(ms.toFixed(0).padStart(6), k.slice(0, 260));
} else {
  console.error(`No command ${command}.`);
  process.exit(2);
}
