// Every view the harness looks at: each page, and each state of a page a person reaches by tapping
// something (a tab, a popover, the assistant). A view names where it goes, given what staging
// put in the library, what it does once there, and whether one tall screenshot of it makes sense. A
// view acts as the staged viewer unless it names another `person` (stage.mjs), which keeps a view
// that changes something — playing a film — out of what the others see.
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

export const VIEWS = [
  { name: "signin", path: () => "/signin", signedOut: true, ready: ".kc-signin, main, body" },
  { name: "home", path: () => "/", ready: ".showcase-card", full: true },
  { name: "home-middle", path: () => "/", ready: ".showcase-card", act: scrollTo(0.5) },
  { name: "home-foot", path: () => "/", ready: ".kfoot", act: scrollTo(1) },
  { name: "tab-library", path: () => "/", ready: ".tabbar", act: tap(".tabbar__tab:nth-child(2)", ".toolbar") },
  { name: "movies", path: () => "/movies", ready: ".showcase-card", full: true },
  { name: "movies-filters", path: () => "/movies", ready: ".toolbar-filter__trigger", act: tap(".toolbar-filter:not(.toolbar-sort) .toolbar-filter__trigger", ".toolbar-filter__menu") },
  { name: "movies-sort", path: () => "/movies", ready: ".toolbar-sort", act: tap(".toolbar-sort .toolbar-filter__trigger", ".toolbar-sort__menu") },
  { name: "tv", path: () => "/tv", ready: ".kc-page", full: true },
  { name: "title", path: (s) => "/title/" + encodeURIComponent(s.film.id), ready: ".kc-title", full: true },
  { name: "title-leaving", path: (s) => "/title/" + encodeURIComponent(s.leaving.id), ready: ".kc-title", full: true },
  { name: "title-kept", path: (s) => "/title/" + encodeURIComponent(s.kept.id), ready: ".kc-title", full: true },
  { name: "discover", path: () => "/discover", ready: ".kc-discover .showcase-card", full: true },
  { name: "discover-results", path: () => "/discover?q=the", ready: ".kc-page", full: true },
  { name: "tab-discover", path: () => "/", ready: ".tabbar", act: tap(".tabbar__tab:nth-child(3)", ".kc-discover") },
  { name: "notify", path: () => "/notify", ready: ".kc-page", full: true },
  { name: "leaving", path: () => "/leaving", ready: ".kc-page", full: true },
  { name: "profile-history", path: () => "/profile?tab=history", ready: ".kc-page", full: true },
  { name: "profile-requests", path: () => "/profile?tab=requests", ready: ".kc-page", full: true },
  { name: "profile-playback", path: () => "/profile?tab=playback", ready: ".kc-page", full: true },
  { name: "profile-sessions", path: () => "/profile?tab=sessions", ready: ".kc-page", full: true },
  { name: "about", path: () => "/about", ready: ".kc-about", full: true },
  { name: "assistant", path: () => "/", ready: ".tabbar", act: tap(".tabbar__tab:nth-child(4)", ".assistant-dock--open") },
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
