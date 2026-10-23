// identity.js — Cinema's mark: Lucide's clapperboard, drawn in each palette's accent on its tint. The
// build keeps the tab's icon, the browser's colour and the splash in the palette shown (`siteIdentity`
// in vite.config.js), and `npm run icons` writes the fixed artwork in the default palette: the install
// icons and launch screens under public/, and the phone app's launcher icon.

import "./src/themes.js";

export default {
  mark: [
    "M20.2 6 3 11l-.9-2.4c-.3-1.1.3-2.2 1.3-2.5l13.5-4c1.1-.3 2.2.3 2.5 1.3Z",
    "m6.2 5.3 3.1 3.9",
    "m12.4 3.4 3.1 4",
    "M3 11h18v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2Z",
  ],
  palettes: ["src/themes.css"],
  android: "../../phone/res",
  androidPalettes: true,
};
