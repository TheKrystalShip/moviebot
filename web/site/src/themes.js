import { registerThemes } from "@thekrystalship/krystal";

// The cinema palettes, drawn in `themes.css`. Registered before the first render so the picker
// lists them and a stored preference naming one takes effect.
registerThemes([
  { id: "noir", label: "Noir", mode: "dark" },
  { id: "velvet", label: "Velvet", mode: "dark" },
  { id: "drive-in", label: "Drive-In", mode: "dark" },
  { id: "technicolor", label: "Technicolor", mode: "dark" },
  { id: "video-store", label: "Video Store", mode: "dark" },
  { id: "silent-film", label: "Silent Film", mode: "light" },
]);
