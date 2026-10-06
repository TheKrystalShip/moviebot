// An identity provider for the checks: what the scratch API trusts in place of tks-auth.
//
// It answers OpenID Connect discovery and a key set over HTTP, and the checks mint access tokens with
// its key the way the provider mints Cinema's: ES256, typed `at+jwt`, audienced `cinema`, the person
// in `sub` and `name`, their actions in `tks_actions`. The key is made once and kept in the scratch
// directory, so the server scratch.sh starts and every check that mints agree.
//
//   node verify/issuer.mjs serve <dir> <port>    serve discovery, keys and a television's sign-in
//
// A browser is signed in by planting the session `oidc-client-ts` keeps, under the key it reads.
//
// A television signs in for real, through the device authorization grant (RFC 8628) served the way
// tks-auth serves it: `/device_authorization` for the TV's client alone, `/token` answering a poll with
// `authorization_pending`, `slow_down`, `access_denied`, `expired_token` or a session, the refresh
// grant rotating that session's refresh token, and `/revoke` ending it (RFC 7009). Nobody approves on a
// page here; a check decides for the person through `/verify/*`:
//
//   GET  /verify/devices                          the codes waiting, newest last: `[{ userCode, client }]`
//   POST /verify/decide?code=<user code>&decision=approve|deny&person=<id>&name=<name>
//   POST /verify/lifetime?seconds=<n>             how long the next codes live (600 until set)
//   POST /verify/access?seconds=<n>               how long the next access tokens live (300 until set)
//   POST /verify/down?on=1|0                      every provider route answering 503 while on
//   GET  /verify/sessions                         the refresh tokens alive: `[{ person, client }]`

import { createHash, createPrivateKey, createPublicKey, generateKeyPairSync, randomBytes, sign } from "node:crypto";
import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { createServer } from "node:http";
import { join } from "node:path";

/** Every Cinema action: what the person a check signs in holds unless it names fewer. */
export const EVERYTHING = ["cinema:watch", "cinema:download", "cinema:wish", "cinema:profile.read", "cinema:profile.write"];

/** The client the site signs in as, by the API's default. */
export const SITE_CLIENT = "cinema-site";

/** The client a television signs in as, by the API's default. */
export const TV_CLIENT = "cinema-tv";

const b64 = (bytes) => Buffer.from(bytes).toString("base64").replace(/=+$/, "").replace(/\+/g, "-").replace(/\//g, "_");

/** The provider whose key is kept in `dir`, made there the first time. */
export function issuerIn(dir, port) {
  const file = join(dir, "issuer-key.pem");
  if (!existsSync(file)) {
    const { privateKey } = generateKeyPairSync("ec", { namedCurve: "P-256" });
    writeFileSync(file, privateKey.export({ type: "pkcs8", format: "pem" }), { mode: 0o600 });
  }
  const key = createPrivateKey(readFileSync(file));
  const jwk = createPublicKey(key).export({ format: "jwk" });
  const kid = b64(createHash("sha256").update(jwk.x + jwk.y).digest()).slice(0, 16);
  const url = `http://127.0.0.1:${port}`;

  /** An access token for `person`, holding `actions`, good for `hours`, minted for `client`. */
  const mint = (person, { actions = EVERYTHING, hours = 6, seconds, client = SITE_CLIENT } = {}) => {
    const now = Math.floor(Date.now() / 1000);
    const header = b64(JSON.stringify({ alg: "ES256", typ: "at+jwt", kid }));
    const body = b64(JSON.stringify({
      iss: url, aud: "cinema", sub: person.id, name: person.name, preferred_username: person.id,
      client_id: client, tks_actions: actions, iat: now, nbf: now, exp: now + (seconds ?? hours * 3600),
    }));
    const signature = sign("sha256", Buffer.from(`${header}.${body}`), { key, dsaEncoding: "ieee-p1363" });
    return `${header}.${body}.${b64(signature)}`;
  };

  /**
   * What a browser holds once `person` has signed in: the entry `oidc-client-ts` reads from
   * localStorage, which an init script puts there before the page loads. A television's is kept under
   * its own client, `{ client: TV_CLIENT }`, and `{ refresh }` gives it a refresh token.
   */
  const planted = (person, options = {}) => {
    const client = options.client || SITE_CLIENT;
    const token = mint(person, options);
    return {
      key: `cinema.oidc.user:${url}:${client}`,
      value: JSON.stringify({
        access_token: token, id_token: token, token_type: "Bearer", scope: "openid", refresh_token: options.refresh,
        profile: { sub: person.id, name: person.name, preferred_username: person.id },
        expires_at: Math.floor(Date.now() / 1000) + (options.seconds ?? (options.hours ?? 6) * 3600),
      }),
    };
  };

  const documents = {
    "/.well-known/openid-configuration": {
      issuer: url, jwks_uri: `${url}/jwks`, authorization_endpoint: `${url}/authorize`, token_endpoint: `${url}/token`,
      device_authorization_endpoint: `${url}/device_authorization`, revocation_endpoint: `${url}/revoke`,
      end_session_endpoint: `${url}/sign-out`, response_types_supported: ["code"], id_token_signing_alg_values_supported: ["ES256"],
      grant_types_supported: ["authorization_code", "refresh_token", "urn:ietf:params:oauth:grant-type:device_code"],
    },
    "/jwks": { keys: [{ ...jwk, kid, use: "sig", alg: "ES256" }] },
  };

  return { url, mint, planted, documents };
}

/** The device grant and the sessions it mints, held in memory for as long as the server runs. */
function deviceProvider(issuer) {
  const devices = new Map();
  const sessions = new Map();
  let lifetime = 600;
  let access = 300;
  const interval = 5;

  const secret = () => b64(randomBytes(24));
  const userCode = () => {
    const letters = "BCDFGHJKLMNPQRSTVWXZ";
    const pick = () => letters[randomBytes(1)[0] % letters.length];
    return Array.from({ length: 4 }, pick).join("") + "-" + Array.from({ length: 4 }, pick).join("");
  };
  /** An access token for a television's person, short as the provider's, and its refresh token. */
  const pair = (person) => {
    const refresh = secret();
    sessions.set(refresh, { person, client: TV_CLIENT });
    return {
      access_token: issuer.mint(person, { seconds: access, client: TV_CLIENT }),
      token_type: "Bearer", expires_in: access, refresh_token: refresh, scope: "openid",
    };
  };
  const error = (code, description) => ({ status: 400, body: { error: code, error_description: description } });

  return {
    authorize(form) {
      if (form.get("client_id") !== TV_CLIENT) return error("unauthorized_client", "Only a television signs in this way.");
      const device = {
        deviceCode: secret(), userCode: userCode(), client: TV_CLIENT, decision: null, person: null,
        expiresAt: Date.now() + lifetime * 1000, interval, lastPoll: 0,
      };
      devices.set(device.deviceCode, device);
      return {
        status: 200,
        body: {
          device_code: device.deviceCode, user_code: device.userCode,
          verification_uri: `${issuer.url}/device`, verification_uri_complete: `${issuer.url}/device?user_code=${device.userCode}`,
          expires_in: lifetime, interval,
        },
      };
    },
    token(form) {
      const grant = form.get("grant_type");
      if (grant === "urn:ietf:params:oauth:grant-type:device_code") {
        const device = devices.get(form.get("device_code") || "");
        if (!device || device.client !== form.get("client_id")) return error("invalid_grant", "No such device code.");
        const now = Date.now();
        if (now >= device.expiresAt) {
          devices.delete(device.deviceCode);
          return error("expired_token", "The code has expired.");
        }
        if (now - device.lastPoll < device.interval * 1000 - 250) {
          device.interval += 5;
          device.lastPoll = now;
          return error("slow_down", "Polled inside the interval.");
        }
        device.lastPoll = now;
        if (device.decision === "deny") {
          devices.delete(device.deviceCode);
          return error("access_denied", "The person refused.");
        }
        if (device.decision !== "approve") return error("authorization_pending", "Nobody has decided yet.");
        devices.delete(device.deviceCode);
        return { status: 200, body: pair(device.person) };
      }
      if (grant === "refresh_token") {
        const presented = form.get("refresh_token") || "";
        const session = sessions.get(presented);
        if (!session || session.client !== form.get("client_id")) return error("invalid_grant", "No such session.");
        sessions.delete(presented);
        return { status: 200, body: pair(session.person) };
      }
      return error("unsupported_grant_type", "The grants served are the device code and the refresh token.");
    },
    revoke(form) {
      const session = sessions.get(form.get("token") || "");
      if (session && session.client === form.get("client_id")) sessions.delete(form.get("token"));
      return { status: 200, body: {} };
    },
    devices: () => [...devices.values()].filter((d) => !d.decision).map((d) => ({ userCode: d.userCode, client: d.client })),
    sessions: () => [...sessions.values()],
    decide(code, decision, person) {
      const device = [...devices.values()].find((d) => d.userCode === code);
      if (!device) return false;
      device.decision = decision;
      device.person = person;
      return true;
    },
    setLifetime(seconds) { lifetime = seconds; },
    setAccess(seconds) { access = seconds; },
  };
}

if (process.argv[2] === "serve") {
  const [, , , dir, port] = process.argv;
  const issuer = issuerIn(dir, Number(port));
  const provider = deviceProvider(issuer);
  let down = false;

  const answer = (response, status, body) => {
    response.writeHead(status, {
      "content-type": "application/json",
      // The site reads discovery and posts to the provider from another origin, as it does the real one.
      "access-control-allow-origin": "*",
      "access-control-allow-headers": "content-type, accept",
      "access-control-allow-methods": "GET, POST",
      "cache-control": "no-store",
    });
    response.end(JSON.stringify(body));
  };

  createServer((request, response) => {
    const address = new URL(request.url, issuer.url);
    const path = address.pathname;
    if (request.method === "OPTIONS") return answer(response, 204, {});
    let raw = "";
    request.on("data", (chunk) => { raw += chunk; });
    request.on("end", () => {
      const form = new URLSearchParams(raw);
      const q = address.searchParams;
      if (path.startsWith("/verify/")) {
        if (path === "/verify/devices") return answer(response, 200, provider.devices());
        if (path === "/verify/sessions") return answer(response, 200, provider.sessions());
        if (path === "/verify/decide") {
          const person = { id: q.get("person") || "viewer", name: q.get("name") || q.get("person") || "Viewer" };
          return answer(response, provider.decide(q.get("code"), q.get("decision"), person) ? 200 : 404, {});
        }
        if (path === "/verify/lifetime") { provider.setLifetime(Number(q.get("seconds")) || 600); return answer(response, 200, {}); }
        if (path === "/verify/access") { provider.setAccess(Number(q.get("seconds")) || 300); return answer(response, 200, {}); }
        if (path === "/verify/down") { down = q.get("on") === "1"; return answer(response, 200, { down }); }
        return answer(response, 404, {});
      }
      if (down) return answer(response, 503, { error: "temporarily_unavailable" });
      if (request.method === "POST") {
        const handled = path === "/device_authorization" ? provider.authorize(form)
          : path === "/token" ? provider.token(form)
            : path === "/revoke" ? provider.revoke(form)
              : null;
        if (handled) return answer(response, handled.status, handled.body);
      }
      const document = issuer.documents[path];
      answer(response, document ? 200 : 404, document || {});
    });
  }).listen(Number(port), "127.0.0.1", () => console.log(`Verify issuer on ${issuer.url}`));
}
