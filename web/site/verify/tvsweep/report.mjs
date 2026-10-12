// Sums sweep runs side by side, one line a run and walk: frames over budget, the worst stall, the
// median time for focus to move, and, for steps that open a page, how long focus took to land.
//
//   node verify/tvsweep/report.mjs <results dir> [<results dir> ...] [--walks discover,home-rows]

import { readFileSync } from "node:fs";
import { basename } from "node:path";

const args = process.argv.slice(2);
const at = args.indexOf("--walks");
const walks = at >= 0 ? args[at + 1].split(",") : null;
const dirs = args.filter((a, i) => !a.startsWith("--") && (at < 0 || i !== at + 1));
for (const dir of dirs) {
  let results;
  try { results = JSON.parse(readFileSync(dir + "/results.json", "utf8")); } catch { console.log(basename(dir).padEnd(20), "no results.json"); continue; }
  const by = new Map();
  for (const r of results) {
    const walk = r.step.split("/")[0];
    if (walks && !walks.includes(walk)) continue;
    if (!by.has(walk)) by.set(walk, []);
    by.get(walk).push(r);
  }
  for (const [walk, steps] of by) {
    const moves = steps.filter((s) => !/open|back to/.test(s.step));
    const opens = steps.filter((s) => /open|back to/.test(s.step));
    const sum = (a, k) => a.reduce((n, s) => n + (s[k] || 0), 0);
    const focus = moves.map((s) => s.focusMs).filter((x) => x != null).sort((a, b) => a - b);
    const line = [
      `${basename(dir).padEnd(20)} ${walk.padEnd(14)}`,
      moves.length ? `moves ${moves.length}: >25 ${String(sum(moves, "over25")).padStart(3)} >50 ${String(sum(moves, "over50")).padStart(3)} worst ${String(Math.max(...moves.map((s) => s.longestGap))).padStart(4)} focus median ${focus[Math.floor(focus.length / 2)] ?? "-"}` : "",
      opens.length ? `| opens: ${opens.map((s) => `${s.step.split(":")[1]} ${s.focusMs}/${s.longestGap}`).join(", ")}` : "",
    ];
    console.log(line.filter(Boolean).join(" "));
  }
}
