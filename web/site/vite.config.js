import { createHash } from "node:crypto";
import { readFileSync, readdirSync, writeFileSync } from "node:fs";
import { join, relative, resolve, sep } from "node:path";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { notificationsWorker, siteIdentity } from "@thekrystalship/krystal/vite";
import identity from "./identity.js";

// The site is served by MovieBot.Api from its wwwroot, beside the API it talks to, so every request
// is same-origin and relative. The dev server stands in for that origin: the API, the media and the
// hub are the API's, the assistant is moviebot-assistant's.
const api = process.env.MOVIEBOT_API || "http://127.0.0.1:8099";
const assistant = process.env.MOVIEBOT_ASSISTANT || "http://127.0.0.1:8191";

/** The line in `public/sw.js` the build replaces with what it wrote. */
const BUILD_LINE = 'const BUILD = { version: "dev", files: [] };';

/**
 * Writes every file the build put in `dist/` into the service worker, so a worker installed from this
 * build fetches all of them, the player's lazy chunk, the fonts and the artwork among them, and the
 * installed app never meets a file it has not seen with no connection. The index is listed as `/`,
 * which is the address the server answers every page with. Source maps are left out, since nothing a
 * page loads asks for them, and so are the launch screens under `splash/`, which Safari reads once
 * when the app is put on the Home Screen and no page ever loads. The version is a hash of every listed
 * file, so the worker changes exactly when something it would serve does.
 */
function precache() {
  let outDir;
  return {
    name: "krystal-cinema-precache",
    apply: "build",
    configResolved(config) {
      outDir = resolve(config.root, config.build.outDir);
    },
    closeBundle() {
      const walk = (dir) => readdirSync(dir, { withFileTypes: true })
        .flatMap((entry) => (entry.isDirectory() ? walk(join(dir, entry.name)) : [join(dir, entry.name)]));
      const listed = walk(outDir)
        .map((file) => relative(outDir, file).split(sep).join("/"))
        .filter((file) => file !== "sw.js" && !file.endsWith(".map") && !file.startsWith("splash/"))
        .sort();
      const hash = createHash("sha256");
      for (const file of listed) hash.update(file).update("\0").update(readFileSync(join(outDir, file)));
      const files = listed.map((file) => (file === "index.html" ? "/" : "/" + file));
      const worker = join(outDir, "sw.js");
      const source = readFileSync(worker, "utf8");
      if (!source.includes(BUILD_LINE)) throw new Error("public/sw.js has no build line for the precache list.");
      const build = { version: hash.digest("hex").slice(0, 16), files };
      writeFileSync(worker, source.replace(BUILD_LINE, `const BUILD = ${JSON.stringify(build)};`));
    },
  };
}

export default defineConfig({
  plugins: [react(), siteIdentity(identity), notificationsWorker(), precache()],
  server: {
    port: 5175,
    strictPort: true,
    proxy: {
      "/api": api,
      "/media": api,
      "/films": api,
      "/shows": api,
      "/popular": api,
      "/glances": api,
      "/catalogue": api,
      "/hub": { target: api, ws: true },
      "/assistant": { target: assistant, rewrite: (path) => path.replace(/^\/assistant/, "") },
    },
  },
  build: { outDir: "dist", sourcemap: true, target: "es2022" },
});
