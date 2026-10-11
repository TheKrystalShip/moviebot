// The site's remote modules and a handle on them (verify/remoteProbe.js), bundled as one script that a
// blank page of the site's origin can run: a phone with nothing on screen.
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { build } from "vite";

const siteRoot = join(dirname(fileURLToPath(import.meta.url)), "..");

export async function probeScript() {
  const result = await build({
    configFile: false, logLevel: "silent", root: siteRoot, define: { "process.env.NODE_ENV": JSON.stringify("production") },
    build: { write: false, minify: false, lib: { entry: join(siteRoot, "verify", "remoteProbe.js"), formats: ["es"], fileName: "probe" } },
  });
  const output = (Array.isArray(result) ? result[0] : result).output;
  return output.find((chunk) => chunk.type === "chunk").code;
}
