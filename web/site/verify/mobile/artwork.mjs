// The film database's artwork for the scratch site, fetched once and kept: the scratch API's
// `Tmdb__ImageBaseUrl` points here instead of at the database's image store.
//
//   node verify/mobile/artwork.mjs <dir> <port>
//
// The scratch API starts on a fresh state directory, so every start asks for every image again. The
// database's image store does not answer the same image path with the same bytes every time: one start
// can be handed a JPEG half again as heavy as the next one's for the same poster at the same size. The
// TV performance gate counts what a page fetches, so artwork that changes under it between starts reads
// as a regression of the site. Here each image is fetched the first time it is asked for, kept under
// <dir>/artwork/<size>/<file>, and answered from the kept bytes ever after, so every start serves the
// artwork the baseline was measured on. Deleting <dir>/artwork fetches it afresh.

import { existsSync, mkdirSync, readFileSync, renameSync, writeFileSync } from "node:fs";
import { createServer } from "node:http";
import { dirname, join } from "node:path";

const [dir, port] = process.argv.slice(2);
if (!dir || !port) { console.error("node verify/mobile/artwork.mjs <dir> <port>"); process.exit(2); }
const store = join(dir, "artwork");
const upstream = "https://image.tmdb.org/t/p";
/** `/<size>/<file>.<ext>`, the only shape the database's image store answers. */
const Shape = /^\/([a-z0-9_]+)\/([A-Za-z0-9_-]+\.(?:jpg|png|svg))$/;
const types = { jpg: "image/jpeg", png: "image/png", svg: "image/svg+xml" };
const fetching = new Map();

async function kept(size, file) {
  const path = join(store, size, file);
  if (existsSync(path)) return readFileSync(path);
  const key = `${size}/${file}`;
  if (!fetching.has(key)) {
    fetching.set(key, (async () => {
      const answer = await fetch(`${upstream}/${size}/${file}`);
      if (answer.status === 404) return null;
      if (!answer.ok) throw new Error(`${answer.status}`);
      const bytes = Buffer.from(await answer.arrayBuffer());
      mkdirSync(dirname(path), { recursive: true });
      writeFileSync(path + ".tmp", bytes);
      renameSync(path + ".tmp", path);
      return bytes;
    })().finally(() => fetching.delete(key)));
  }
  return fetching.get(key);
}

createServer(async (req, res) => {
  const match = Shape.exec(new URL(req.url, "http://x").pathname.replace(/^\/t\/p/, ""));
  if (!match) { res.writeHead(404).end(); return; }
  try {
    const bytes = await kept(match[1], match[2]);
    if (!bytes) { res.writeHead(404).end(); return; }
    res.writeHead(200, { "content-type": types[match[2].split(".").pop()], "content-length": bytes.length }).end(bytes);
  } catch (error) {
    res.writeHead(502).end(String(error.message));
  }
}).listen(Number(port), "127.0.0.1");
