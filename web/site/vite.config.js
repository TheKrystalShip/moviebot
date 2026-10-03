import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// The site is served by MovieBot.Api from its wwwroot, beside the API it talks to, so every request
// is same-origin and relative. The dev server stands in for that origin: the API, the media and the
// hub are the API's, the assistant is moviebot-assistant's.
const api = process.env.MOVIEBOT_API || "http://127.0.0.1:8099";
const assistant = process.env.MOVIEBOT_ASSISTANT || "http://127.0.0.1:8191";

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5175,
    strictPort: true,
    proxy: {
      "/api": api,
      "/media": api,
      "/films": api,
      "/hub": { target: api, ws: true },
      "/assistant": { target: assistant, rewrite: (path) => path.replace(/^\/assistant/, "") },
    },
  },
  build: { outDir: "dist", sourcemap: true, target: "es2022" },
});
