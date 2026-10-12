// Every view the harness looks at: each page, and each state of a page a person reaches by tapping
// something (a tab, a popover, the assistant). A view names where it goes, given what staging put in
// the library, what it does once there (given the page and the staging, whose televisions it may
// listen to), and whether one tall screenshot of it makes sense. A view acts as the staged viewer
// unless it names another `person` (stage.mjs), which keeps a view that changes something — playing a
// film — out of what the others see.
//
// Add a view here when a page or a state is added to the site, and it is looked at on every phone.

/**
 * Taps something and waits for what tapping it opens. A tap that lands before the page answers taps
 * opens nothing, so it is tried once more; a tap that still opens nothing is the view's fault to report.
 */
const tap = (selector, opens) => async (page) => {
  for (let attempt = 0; attempt < 2; attempt++) {
    await page.locator(selector).first().click();
    try {
      await page.waitForSelector(opens, { timeout: 2500 });
      await page.waitForTimeout(300);
      return;
    } catch { /* tried again below */ }
  }
  throw new Error(`tapping ${selector} did not open ${opens}`);
};

/** Scrolls the page column, which is what scrolls on the site, to a fraction of its length. */
const scrollTo = (fraction) => async (page) => {
  await page.evaluate((f) => {
    const main = document.querySelector(".app__main");
    if (main) main.scrollTop = (main.scrollHeight - main.clientHeight) * f;
  }, fraction);
  await page.waitForTimeout(400);
};

/** Opens the remote drawer by tapping its strip. */
const openRemote = async (page) => {
  await page.waitForSelector(".kc-remote-strip", { timeout: 15000 });
  await page.waitForFunction(() => document.querySelector(".kc-remote-strip .kc-dl-head__name")?.textContent, null, { timeout: 10000 });
  await tap(".kc-remote-strip__open", ".kc-remote")(page);
  await page.waitForTimeout(800);
};

export const VIEWS = [
  { name: "home", path: () => "/", ready: ".showcase-card", full: true },
  { name: "home-middle", path: () => "/", ready: ".showcase-card", act: scrollTo(0.5) },
  { name: "home-foot", path: () => "/", ready: ".kfoot", act: scrollTo(1) },
  { name: "tab-library", path: () => "/", ready: ".tabbar", act: tap(".tabbar__tab:nth-child(2)", ".toolbar") },
  { name: "movies", path: () => "/movies", ready: ".showcase-card", full: true },
  { name: "movies-filters", path: () => "/movies", ready: ".toolbar-filter__trigger", act: tap(".toolbar-filter:not(.toolbar-sort) .toolbar-filter__trigger", ".toolbar-filter__menu") },
  { name: "movies-sort", path: () => "/movies", ready: ".toolbar-sort", act: tap(".toolbar-sort .toolbar-filter__trigger", ".toolbar-sort__menu") },
  { name: "tv", path: () => "/tv", ready: ".kc-page", full: true },
  { name: "title", path: (s) => "/movies/" + encodeURIComponent(s.film.id), ready: ".kc-title", full: true },
  { name: "title-saved", path: (s) => "/movies/" + encodeURIComponent(s.film.id), ready: ".kc-title .kc-follow", act: tap(".kc-follow .k-btn--accent",".kc-follow__menu, .kc-drawer") },
  { name: "saved", path: () => "/saved", ready: ".kc-list-card", full: true },
  { name: "list-later", path: () => "/saved/later", ready: ".showcase-card", full: true },
  { name: "list-shared", path: (s) => "/saved/" + encodeURIComponent(s.list), ready: ".showcase-card", full: true },
  { name: "list-people", path: (s) => "/saved/" + encodeURIComponent(s.list), ready: ".kc-list-actions", act: tap(".kc-list-actions .k-btn",".kc-people") },
  { name: "list-filters", path: () => "/saved/later", ready: ".toolbar-filter__trigger", act: tap(".toolbar-filter:not(.toolbar-sort) .toolbar-filter__trigger", ".toolbar-filter__menu") },
  { name: "discover", path: () => "/discover", ready: ".kc-discover .showcase-card", full: true },
  { name: "discover-results", path: () => "/discover?q=the", ready: ".kc-page", full: true },
  { name: "tab-discover", path: () => "/", ready: ".tabbar", act: tap(".tabbar__tab:nth-child(3)", ".kc-discover") },
  { name: "notify", path: () => "/notify", ready: ".kc-page", full: true },
  { name: "profile-history", path: () => "/profile", ready: ".kc-page", full: true },
  { name: "profile-requests", path: () => "/profile/requests", ready: ".kc-page", full: true },
  { name: "profile-playback", path: () => "/profile/playback", ready: ".kc-page", full: true },
  { name: "about", path: () => "/about", ready: ".kc-about", full: true },
  { name: "assistant", path: () => "/", ready: ".tabbar", act: tap(".tabbar__tab:nth-child(4)", ".assistant-dock--open") },
  // The phone as a remote: paired with the staged televisions (stage.mjs), remote mode on.
  { name: "remote-strip", remote: ["living"], path: () => "/", ready: ".kc-remote-strip .kc-dl-head__name", act: (page) => page.waitForTimeout(800) },
  { name: "remote-open", remote: ["living"], path: () => "/", ready: ".kc-remote-strip", act: openRemote },
  { name: "remote-episode", remote: ["kitchen"], path: () => "/", ready: ".kc-remote-strip", act: openRemote },
  // The television's own settings, the player's menus aimed at it: Subtitles, its style editor and Quality.
  {
    name: "remote-subtitles", remote: ["living"], path: () => "/", ready: ".kc-remote-strip",
    act: async (page) => { await openRemote(page); await tap('.kc-remote .mp-menu-button[title="Subtitles"]', ".mp-aimed.mp-menu")(page); await page.waitForTimeout(600); },
  },
  {
    name: "remote-style", remote: ["living"], path: () => "/", ready: ".kc-remote-strip",
    act: async (page) => {
      await openRemote(page);
      await tap('.kc-remote .mp-menu-button[title="Subtitles"]', ".mp-aimed.mp-menu")(page);
      await tap(".mp-aimed .mp-option:has(.lucide-type)", ".mp-aimed.is-styling")(page);
    },
  },
  {
    name: "remote-quality", remote: ["living"], path: () => "/", ready: ".kc-remote-strip",
    act: async (page) => { await openRemote(page); await tap('.kc-remote .mp-menu-button[title="Quality"]', ".mp-aimed.mp-menu")(page); },
  },
  {
    name: "remote-episodes", remote: ["kitchen"], path: () => "/", ready: ".kc-remote-strip",
    act: async (page) => { await openRemote(page); await tap('.kc-remote .mp-controls button[aria-label^="Episodes"]', ".mp-episodes .kc-episode")(page); await page.waitForTimeout(600); },
  },
  {
    name: "remote-screens", remote: ["living", "bedroom"], path: () => "/", ready: ".kc-remote-strip",
    act: async (page) => { await openRemote(page); await tap(".kc-remote__tv", ".k-optlist")(page); },
  },
  {
    name: "remote-idle", remote: ["bedroom"], path: () => "/", ready: ".kc-remote-strip",
    act: async (page) => { await openRemote(page); await page.waitForSelector(".kc-remote .kc-continue", { timeout: 10000 }); await page.waitForTimeout(600); },
  },
  {
    // Home's Continue watching while the phone drives a television in no room: a card carries the
    // television's glyph, and choosing it tells the television to open the film, with the phone left on Home.
    name: "remote-home", remote: ["bedroom"], path: () => "/", ready: ".kc-home .kc-continue",
    act: async (page, staged) => {
      const tv = staged.screens.tvs.bedroom.hub;
      let told = null;
      const heard = (watch) => { told = watch; };
      tv.on("Watch", heard);
      try {
        await page.waitForSelector(".kc-remote-strip .kc-dl-head__name", { timeout: 10000 });
        const card = page.locator(".kc-home .kc-continue").first();
        const label = await card.getAttribute("aria-label");
        if (!label || !label.startsWith("Watch on TV")) throw new Error(`Home's Continue watching card is "${label}", not Watch on TV`);
        await card.click();
        const until = Date.now() + 5000;
        while (!told && Date.now() < until) await page.waitForTimeout(100);
        if (!told) throw new Error("choosing Home's Continue watching card told the television nothing");
        if (!told.titleId) throw new Error(`the television was told ${JSON.stringify(told)}, with no film`);
        const path = await page.evaluate(() => location.pathname);
        if (path !== "/") throw new Error(`choosing the card took the phone to ${path}`);
      } finally {
        tv.off("Watch", heard);
      }
    },
  },
  { name: "remote-film", remote: ["living"], path: (s) => "/movies/" + encodeURIComponent(s.other.id), ready: ".kc-title .kc-actions .k-btn", full: true },
  {
    name: "remote-replace", remote: ["living"], path: (s) => "/movies/" + encodeURIComponent(s.other.id), ready: ".kc-title .kc-actions .k-btn",
    act: async (page) => {
      await page.waitForSelector(".kc-remote-strip .kc-dl-head__name", { timeout: 10000 });
      await page.waitForTimeout(800);
      await tap(".kc-title .kc-actions .k-btn--primary", ".kc-dl-sheet")(page);
    },
  },
  {
    name: "player", person: "player", path: (s) => `/r/verify-player?title=${encodeURIComponent(s.film.id)}`, ready: "video",
    act: async (page) => {
      await page.waitForTimeout(2500);
      const { width, height } = page.viewportSize();
      await page.mouse.click(width / 2, height / 2);
      await page.waitForTimeout(500);
    },
  },
];
