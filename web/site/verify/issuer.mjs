// An identity provider for the checks: what the scratch API trusts in place of tks-auth.
//
// It answers OpenID Connect discovery and a key set over HTTP, and the checks mint access tokens with
// its key the way the provider mints Cinema's: ES256, typed `at+jwt`, audienced `cinema`, the person
// in `sub` and `name`, their actions in `tks_actions`. The key is made once and kept in the scratch
// directory, so the server scratch.sh starts and every check that mints agree.
//
//   node verify/issuer.mjs serve <dir> <port>    serve discovery and keys for the key in <dir>
//
// A browser is signed in by planting the session `oidc-client-ts` keeps, under the key it reads.

import { createHash, createPrivateKey, createPublicKey, generateKeyPairSync, sign } from "node:crypto";
import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { createServer } from "node:http";
import { join } from "node:path";

/** Every Cinema action: what the person a check signs in holds unless it names fewer. */
export const EVERYTHING = ["cinema:watch", "cinema:download", "cinema:wish", "cinema:keep", "cinema:profile.read", "cinema:profile.write"];

/** The client the site signs in as, by the API's default. */
export const SITE_CLIENT = "cinema-site";

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

  /** An access token for `person`, holding `actions`, good for `hours`. */
  const mint = (person, { actions = EVERYTHING, hours = 6 } = {}) => {
    const now = Math.floor(Date.now() / 1000);
    const header = b64(JSON.stringify({ alg: "ES256", typ: "at+jwt", kid }));
    const body = b64(JSON.stringify({
      iss: url, aud: "cinema", sub: person.id, name: person.name, preferred_username: person.id,
      client_id: SITE_CLIENT, tks_actions: actions, iat: now, nbf: now, exp: now + hours * 3600,
    }));
    const signature = sign("sha256", Buffer.from(`${header}.${body}`), { key, dsaEncoding: "ieee-p1363" });
    return `${header}.${body}.${b64(signature)}`;
  };

  /**
   * What a browser holds once `person` has signed in: the entry `oidc-client-ts` reads from
   * localStorage, which an init script puts there before the page loads.
   */
  const planted = (person, options) => {
    const token = mint(person, options);
    return {
      key: `cinema.oidc.user:${url}:${SITE_CLIENT}`,
      value: JSON.stringify({
        access_token: token, token_type: "Bearer", scope: "openid",
        profile: { sub: person.id, name: person.name, preferred_username: person.id },
        expires_at: Math.floor(Date.now() / 1000) + (options?.hours ?? 6) * 3600,
      }),
    };
  };

  const documents = {
    "/.well-known/openid-configuration": {
      issuer: url, jwks_uri: `${url}/jwks`, authorization_endpoint: `${url}/authorize`, token_endpoint: `${url}/token`,
      response_types_supported: ["code"], id_token_signing_alg_values_supported: ["ES256"],
    },
    "/jwks": { keys: [{ ...jwk, kid, use: "sig", alg: "ES256" }] },
  };

  return { url, mint, planted, documents };
}

if (process.argv[2] === "serve") {
  const [, , , dir, port] = process.argv;
  const issuer = issuerIn(dir, Number(port));
  createServer((request, response) => {
    const document = issuer.documents[new URL(request.url, issuer.url).pathname];
    // The site reads discovery from another origin, as it does the provider's.
    response.writeHead(document ? 200 : 404, { "content-type": "application/json", "access-control-allow-origin": "*" });
    response.end(document ? JSON.stringify(document) : "{}");
  }).listen(Number(port), "127.0.0.1", () => console.log(`Verify issuer on ${issuer.url}`));
}
