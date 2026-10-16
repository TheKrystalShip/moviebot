// What the sign-in client starts from in browser storage: a television's stored session carried over,
// a website's old session and sign-ins in flight removed.
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { migrateSession, userKey } from '../src/migrateSession.js';

const ISSUER = 'https://auth.example.com';

class Storage {
  #items = new Map();
  get length() { return this.#items.size; }
  key(i) { return [...this.#items.keys()][i] ?? null; }
  getItem(key) { return this.#items.has(key) ? this.#items.get(key) : null; }
  setItem(key, value) { this.#items.set(key, String(value)); }
  removeItem(key) { this.#items.delete(key); }
  keys() { return [...this.#items.keys()].sort(); }
}

function storages() {
  return { local: new Storage(), session: new Storage() };
}

test('a television keeps its session: the stored user moves to the client key, byte for byte', () => {
  const { local, session } = storages();
  const user = JSON.stringify({ access_token: 'a', refresh_token: 'r', profile: { sub: 's' } });
  local.setItem(`cinema.oidc.user:${ISSUER}:cinema-tv`, user);
  migrateSession({ issuer: ISSUER, clientId: 'cinema-tv', onTv: true, local, session });
  assert.equal(local.getItem(userKey(ISSUER, 'cinema-tv')), user);
  assert.deepEqual(local.keys(), [userKey(ISSUER, 'cinema-tv')]);
});

test('the client key is spelled as the sign-in client spells it', () => {
  assert.equal(userKey(ISSUER, 'cinema-tv'), `tks-auth-client:cinema-tv:user:${ISSUER}:cinema-tv`);
});

test('a television already signed in under the client key is not overwritten', () => {
  const { local, session } = storages();
  local.setItem(`cinema.oidc.user:${ISSUER}:cinema-tv`, 'old');
  local.setItem(userKey(ISSUER, 'cinema-tv'), 'current');
  migrateSession({ issuer: ISSUER, clientId: 'cinema-tv', onTv: true, local, session });
  assert.equal(local.getItem(userKey(ISSUER, 'cinema-tv')), 'current');
  assert.deepEqual(local.keys(), [userKey(ISSUER, 'cinema-tv')]);
});

test('a website session is removed, with every sign-in in flight, and nothing is carried over', () => {
  const { local, session } = storages();
  local.setItem(`cinema.oidc.user:${ISSUER}:cinema-site`, 'user');
  session.setItem('cinema.oidc.state-id', 'state');
  local.setItem('cinema.config', 'kept');
  local.setItem('cinema:sidebar:collapsed', 'true');
  migrateSession({ issuer: ISSUER, clientId: 'cinema-site', onTv: false, local, session });
  assert.deepEqual(local.keys(), ['cinema.config', 'cinema:sidebar:collapsed']);
  assert.deepEqual(session.keys(), []);
});

test('a television never takes the website client\'s user as its own', () => {
  const { local, session } = storages();
  local.setItem(`cinema.oidc.user:${ISSUER}:cinema-site`, 'site');
  migrateSession({ issuer: ISSUER, clientId: 'cinema-tv', onTv: true, local, session });
  assert.deepEqual(local.keys(), []);
});

test('with the old keys gone a second boot changes nothing', () => {
  const { local, session } = storages();
  local.setItem(`cinema.oidc.user:${ISSUER}:cinema-tv`, 'user');
  migrateSession({ issuer: ISSUER, clientId: 'cinema-tv', onTv: true, local, session });
  const after = local.keys();
  migrateSession({ issuer: ISSUER, clientId: 'cinema-tv', onTv: true, local, session });
  assert.deepEqual(local.keys(), after);
  assert.equal(local.getItem(userKey(ISSUER, 'cinema-tv')), 'user');
});

test('storage that throws leaves the page to sign in afresh', () => {
  const broken = { length: 0, key: () => null, getItem: () => { throw new Error('denied'); }, setItem() {}, removeItem() {} };
  assert.doesNotThrow(() => migrateSession({ issuer: ISSUER, clientId: 'cinema-tv', onTv: true, local: broken, session: broken }));
});
