// Download's progress where a person sees it with the page out of view: while the page is hidden and
// this device is downloading something, or waiting on the server to prepare it, one notification says
// what and how far it has got ("Downloading · 42% · 2 more"), replaced in place as it moves and closed
// when the page is looked at again or nothing is left. A download that finishes while the page is
// hidden says so in a notification of its own, which stays until it is opened.
//
// It is the page's: a browser hiding the page slows its timers and, sooner or later, stops it, and the
// notification says no more than the page last knew. It is drawn through the service worker, which is
// what a phone shows a page's notification through, so a build without one (development, the Discord
// Activity) shows none, as does a browser the person has not let notify. Permission is asked for once
// on this device, from the press of the first Download.

import { libraryPath } from "../paths.js";
import { sessionStore } from "../session.js";
import { onSaving, percentOf, saveTitle, savingNow } from "./saver.js";
import { readSave } from "./store.js";
import { onWants, stageWords, wantTitle, wantsNow } from "./wants.js";

const TAG = "kc-downloads";
const DONE_TAG = "kc-downloaded-";
const ASKED_KEY = "cinema.offline.notifyAsked";
/** How often the notification is drawn again at most: a phone draws a notification replaced faster no sooner. */
const EVERY_MS = 2000;
const ICON = "/icons/icon-192.png";

let stops = null;
let timer = null;
let lastAt = 0;
/** What the notification last said, so one that says the same is not drawn again; null when none is shown. */
let shown = null;
/** The saves being written when last looked, by title id, to tell a finished one from one still going. */
let writing = new Set();

const currentSub = () => {
  const session = sessionStore.getState();
  return session ? session.user.id : null;
};

const hidden = () => typeof document !== "undefined" && document.visibilityState === "hidden";

/**
 * Asks to notify, from a press: once on this device, and only when the browser has not been answered.
 * The download goes ahead whatever the answer.
 */
export function askToNotify() {
  if (typeof Notification === "undefined" || Notification.permission !== "default") return;
  try {
    if (localStorage.getItem(ASKED_KEY)) return;
    localStorage.setItem(ASKED_KEY, "1");
  } catch {
    // Storage refused: asked anyway, the browser keeping the answer.
  }
  const asked = Notification.requestPermission();
  if (asked && asked.catch) asked.catch(() => {});
}

async function registration() {
  if (typeof Notification === "undefined" || Notification.permission !== "granted") return null;
  if (typeof navigator === "undefined" || !navigator.serviceWorker) return null;
  return (await navigator.serviceWorker.getRegistration().catch(() => null)) || null;
}

/** Starts drawing the notification for the signed-in account; answers the call that stops it. */
export function watchDownloads() {
  if (stops) return stopWatching;
  writing = new Set(Object.entries(savingNow()).filter(([, e]) => e.state === "saving").map(([id]) => id));
  const onVisibility = () => schedule();
  document.addEventListener("visibilitychange", onVisibility);
  stops = [
    onSaving(() => { finished(); schedule(); }),
    onWants(schedule),
    () => document.removeEventListener("visibilitychange", onVisibility),
  ];
  void close(true);
  return stopWatching;
}

function stopWatching() {
  if (!stops) return;
  for (const stop of stops) stop();
  stops = null;
  clearTimeout(timer);
  timer = null;
  void close();
}

function schedule() {
  if (timer) return;
  timer = setTimeout(() => {
    timer = null;
    lastAt = Date.now();
    void draw();
  }, hidden() ? Math.max(0, EVERY_MS - (Date.now() - lastAt)) : 0);
}

async function draw() {
  if (!hidden()) return close();
  const said = await compose();
  if (!said) return close();
  if (shown && shown.title === said.title && shown.body === said.body) return;
  const reg = await registration();
  if (!reg || !hidden()) return;
  shown = said;
  await reg.showNotification(said.title, {
    body: said.body, tag: TAG, silent: true, icon: ICON, badge: ICON, data: { url: said.url },
  }).catch(() => { shown = null; });
}

/** Closes the notification; `always` closes one a page before this one left behind. */
async function close(always = false) {
  if (!shown && !always) return;
  shown = null;
  const reg = await registration();
  if (reg) for (const n of await reg.getNotifications({ tag: TAG }).catch(() => [])) n.close();
}

/**
 * What the notification says: the save being written (or the first waiting its turn, or for a
 * connection), else the first title the server is preparing, and how many more there are. Null when
 * there is nothing.
 */
async function compose() {
  const sub = currentSub();
  if (!sub) return null;
  const saves = Object.entries(savingNow()).filter(([, e]) => ["saving", "queued", "waiting"].includes(e.state));
  const { list, refused } = wantsNow();
  const preparing = list.filter((w) => w.stage !== "failed" && !refused[w.id] && !(w.titleId && savingNow()[w.titleId]));
  const more = saves.length + preparing.length - 1;
  const tail = more > 0 ? ` · ${more} more` : "";

  const lead = saves.find(([, e]) => e.state === "saving") || saves[0];
  if (lead) {
    const [titleId, entry] = lead;
    const meta = (await readSave(sub, titleId).catch(() => null))?.meta || null;
    const url = libraryPath(meta && meta.kind === "episode" ? "show" : "film", meta && meta.category);
    const title = meta ? saveTitle(meta) : "Download";
    if (entry.state === "waiting") return { title, body: "Waiting for a connection" + tail, url };
    if (entry.state === "queued") return { title, body: "Queued" + tail, url };
    const bytes = entry.bytes || (meta ? meta.bytes : 0);
    return { title, body: `Downloading · ${percentOf(meta, entry.received || 0, bytes)}%` + tail, url };
  }

  const want = preparing[0];
  if (!want) return null;
  const share = want.stage === "downloading" && want.progress != null ? ` · ${Math.floor(want.progress * 100)}%` : ` · ${stageWords(want)}`;
  return { title: wantTitle(want) || "Download", body: "Preparing" + share + tail, url: libraryPath(want.episode ? "show" : "film") };
}

/** Says a save finished while the page was hidden, in a notification of its own. */
function finished() {
  const now = savingNow();
  const gone = [...writing].filter((id) => !now[id]);
  writing = new Set(Object.entries(now).filter(([, e]) => e.state === "saving").map(([id]) => id));
  const sub = currentSub();
  if (!sub || !hidden()) return;
  for (const titleId of gone) {
    void (async () => {
      const save = await readSave(sub, titleId).catch(() => null);
      if (!save || !save.complete) return;
      const reg = await registration();
      if (!reg) return;
      await reg.showNotification(saveTitle(save.meta), {
        body: "Downloaded", tag: DONE_TAG + titleId, icon: ICON, badge: ICON,
        data: { url: libraryPath(save.meta.kind === "episode" ? "show" : "film", save.meta.category) },
      }).catch(() => {});
    })();
  }
}
