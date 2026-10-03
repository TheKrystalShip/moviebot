import tmdbLogo from "../assets/tmdb.svg";
import { PageHead } from "../components/Film.jsx";

/** What this site is, and where what it shows about movies and shows comes from. */
export function About() {
  return (
    <div className="kc-page kc-about">
      <PageHead title="About" sub="Krystal Cinema, MovieBot's library in a browser." />

      <section className="kc-about__block">
        <h2>The library and its rooms</h2>
        <p>
          The movies and shows here are the ones MovieBot has fetched for the server. Anybody in a room watches
          the same moment of the same one, whether they joined from this site or from the Activity in a Discord
          voice channel.
        </p>
      </section>

      <section className="kc-about__block kc-about__tmdb">
        <h2>Details and artwork</h2>
        <a href="https://www.themoviedb.org/" target="_blank" rel="noreferrer" className="kc-about__logo">
          <img src={tmdbLogo} alt="The Movie Database (TMDB)" />
        </a>
        <p>
          Ratings, credits, certificates, genres, overviews and artwork come from The Movie Database (TMDB).
          This product uses the TMDB API but is not endorsed or certified by TMDB.
        </p>
      </section>
    </div>
  );
}
