import { About } from "../pages/About.jsx";
import { NotFound } from "../pages/NotFound.jsx";
import { Notify } from "../pages/Notify.jsx";
import { TvDiscover } from "./pages/TvDiscover.jsx";
import { TvCatalogueFilm, TvTitle } from "./pages/TvFilm.jsx";
import { TvHome } from "./pages/TvHome.jsx";
import { TvLibrary } from "./pages/TvLibrary.jsx";
import { TvProfile } from "./pages/TvProfile.jsx";
import { TvList, TvSaved } from "./pages/TvSaved.jsx";
import { TvShow } from "./pages/TvShow.jsx";

/** The page a route shows on a television. */
export function TvPage({ route }) {
  switch (route.view) {
    case "home": return <TvHome />;
    case "movies": return <TvLibrary key="film" kind="film" />;
    case "tv": return <TvLibrary key="show" kind="show" />;
    case "movie": return <TvTitle key={route.arg} id={route.arg} />;
    case "show":
    case "discover-show":
      return <TvShow key={route.arg} imdbId={route.arg} season={route.season} inLibrary={route.view === "show"} />;
    case "discover-film": return <TvCatalogueFilm key={route.arg} imdbId={route.arg} />;
    case "discover": return <TvDiscover query={route.query} />;
    case "saved": return <TvSaved />;
    case "list": return <TvList key={route.arg} id={route.arg} />;
    case "notify": return <Notify />;
    case "profile": return <TvProfile tab={route.tab} />;
    case "about": return <About />;
    default: return <NotFound />;
  }
}
