import { registerThemes } from "@thekrystalship/krystal/lib/theme";

// The cinema palettes, drawn in `themes.css`. Registered before the first render so the picker
// lists them and a stored preference naming one takes effect.
registerThemes([
  { id: "noir", label: "Noir", mode: "dark" },
  { id: "velvet", label: "Velvet", mode: "dark" },
  { id: "drive-in", label: "Drive-In", mode: "dark" },
  { id: "technicolor", label: "Technicolor", mode: "dark" },
  { id: "video-store", label: "Video Store", mode: "dark" },
  { id: "silent-film", label: "Silent Film", mode: "light" },
  { id: "crimson-reel", label: "Crimson Reel", mode: "dark" },
  { id: "enchanted-navy", label: "Enchanted Navy", mode: "dark" },
  { id: "amber-library", label: "Amber Library", mode: "dark" },
  { id: "fresh-green", label: "Fresh Green", mode: "dark" },
  { id: "prime-night", label: "Prime Night", mode: "dark" },
  { id: "sunrise-violet", label: "Sunrise Violet", mode: "dark" },
  { id: "streaming-gold", label: "Streaming Gold", mode: "dark" },
  { id: "big-screen", label: "Big Screen", mode: "dark" },
  { id: "film-diary", label: "Film Diary", mode: "dark" },
  { id: "popcorn", label: "Popcorn", mode: "light" },
  { id: "grindhouse", label: "Grindhouse", mode: "dark" },
  { id: "sci-fi-bridge", label: "Sci-Fi Bridge", mode: "dark" },
]);
