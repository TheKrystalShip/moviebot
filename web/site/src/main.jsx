import { createRoot } from "react-dom/client";
import "@thekrystalship/krystal-ui/styles.css";
import "./site.css";
import { App } from "./App.jsx";
import { registerServiceWorker } from "./registerSW.js";

createRoot(document.getElementById("root")).render(<App />);
registerServiceWorker();
