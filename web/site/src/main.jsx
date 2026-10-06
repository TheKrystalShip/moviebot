import { createRoot } from "react-dom/client";
import "@thekrystalship/krystal-ui/styles.css";
import "./site.css";
import "./tv/tv.css";
import { App } from "./App.jsx";
import { registerServiceWorker } from "./registerSW.js";
import { startConnectivity } from "./connectivity.js";
import { onTv, startTv } from "./tv/tv.js";
import { startNavigator } from "./tv/navigator.js";

startTv();
if (onTv) startNavigator();
startConnectivity();

createRoot(document.getElementById("root")).render(<App />);
registerServiceWorker();
