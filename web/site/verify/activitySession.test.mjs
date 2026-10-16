// The Discord Activity's session: the API mints it and the site adopts it, with no sign-in round trip,
// so nothing but the adopt path teaches the site which claim lists the Cinema actions.
import assert from 'node:assert/strict';
import { test } from 'node:test';
class MemoryStorage {
  #items = new Map();
  get length() { return this.#items.size; }
  key(i) { return [...this.#items.keys()][i] ?? null; }
  getItem(k) { return this.#items.has(k) ? this.#items.get(k) : null; }
  setItem(k, v) { this.#items.set(k, String(v)); }
  removeItem(k) { this.#items.delete(k); }
  clear() { this.#items.clear(); }
}
const target = new EventTarget();
const browser = {
  localStorage: new MemoryStorage(),
  sessionStorage: new MemoryStorage(),
  location: new URL('https://movies.example/r/room'),
  history: { replaceState() {}, pushState() {} },
  navigator: { onLine: true, userAgent: 'node' },
  document: Object.assign(new EventTarget(), { visibilityState: 'visible', documentElement: { dataset: {}, style: {}, classList: { add() {}, remove() {}, toggle() {} }, setAttribute() {}, removeAttribute() {} }, querySelector: () => null }),
  matchMedia: () => ({ matches: false, addEventListener() {}, removeEventListener() {} }),
  addEventListener: target.addEventListener.bind(target),
  removeEventListener: target.removeEventListener.bind(target),
};
browser.window = Object.assign(globalThis, {});
for (const [name, value] of Object.entries(browser)) {
  if (name === 'window') continue;
  Object.defineProperty(globalThis, name, { value, configurable: true, writable: true });
}
Object.defineProperty(globalThis, 'window', { value: globalThis, configurable: true, writable: true });
globalThis.fetch = async (url) => {
  if (String(url).endsWith('/api/config')) {
    return new Response(JSON.stringify({ issuer: 'https://auth.example', clientId: 'cinema-site', tvClientId: 'cinema-tv', actionsClaim: 'tks_actions' }), { status: 200 });
  }
  return new Response('{}', { status: 404 });
};

const b64 = (o) => Buffer.from(JSON.stringify(o)).toString('base64url');
const jwt = (payload) => `${b64({ alg: 'none' })}.${b64(payload)}.sig`;

test('an adopted session reads its Cinema actions from the claim the config names', async () => {
  const { adopt, session, sessionStore } = await import('../src/session.js');
  const token = jwt({ sub: 'u1', name: 'Ana', preferred_username: 'ana', tks_actions: ['cinema:watch'], exp: Math.floor(Date.now() / 1000) + 300 });
  try {
    await adopt({ token, expiresIn: 300 }, async () => null);
    const view = sessionStore.getState();
    assert.ok(view, 'somebody is signed in');
    assert.deepEqual(view.actions, ['cinema:watch']);
    assert.equal(view.user.name, 'Ana');
  } finally {
    // The adopted session's renewal timer would otherwise hold the test process open.
    session.end('revoked');
  }
});
