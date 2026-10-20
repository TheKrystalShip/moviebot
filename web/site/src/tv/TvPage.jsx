import { ANIME } from "../paths.js";
import { NotFound } from "../pages/NotFound.jsx";
import { TvDiscover } from "./pages/TvDiscover.jsx";
import { TvCatalogueFilm, TvTitle } from "./pages/TvFilm.jsx";
import { TvHome } from "./pages/TvHome.jsx";
import { TvLibrary } from "./pages/TvLibrary.jsx";
import { TvProfile } from "./pages/TvProfile.jsx";
import { TvShow } from "./pages/TvShow.jsx";
import { TvWants } from "./pages/TvWants.jsx";

/** The page a route shows on a television. */
export function TvPage({ route }) {
  switch (route.view) {
    case "home": return <TvHome />;
    case "movies": return <TvLibrary key="film" kind="film" />;
    case "tv": return <TvLibrary key="show" kind="show" />;
    case "anime":
    case "anime-films": return <TvLibrary key="anime" category={ANIME} />;
    case "movie": return <TvTitle key={route.arg} id={route.arg} />;
    case "show":
    case "discover-show":
      return <TvShow key={route.arg} imdbId={route.arg} season={route.season} inLibrary={route.view === "show"} />;
    case "discover-film": return <TvCatalogueFilm key={route.arg} imdbId={route.arg} />;
    case "discover": return <TvDiscover query={route.query} />;
    case "wants": return <TvWants />;
    case "profile": return <TvProfile tab={route.tab} />;
    case "about": return <TvProfile about />;
    default: return <NotFound />;
  }
}
