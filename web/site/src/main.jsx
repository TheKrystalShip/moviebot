import { createRoot } from "react-dom/client";
import "@thekrystalship/krystal-ui/styles.css";
import "./site.css";
import { App } from "./App.jsx";
import { registerServiceWorker } from "./registerSW.js";
import { onTv, startTv } from "./tv/tv.js";
import { startNavigator } from "./tv/navigator.js";

startTv();
if (onTv) startNavigator();

createRoot(document.getElementById("root")).render(<App />);
registerServiceWorker();
