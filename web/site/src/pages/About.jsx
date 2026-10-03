import tmdbLogo from "../assets/tmdb.svg";
import { PageHead } from "../components/Film.jsx";

/** What this site is, and where what it shows about films comes from. */
export function About() {
  return (
    <div className="kc-page kc-about">
      <PageHead title="About" sub="Krystal Cinema, MovieBot's library in a browser." />

      <section className="kc-about__block">
        <h2>Films and rooms</h2>
        <p>
          The films here are the ones MovieBot has fetched for the server. Anybody in a room watches the same
          moment of the same film, whether they joined from this site or from the Activity in a Discord voice
          channel.
        </p>
      </section>

      <section className="kc-about__block kc-about__tmdb">
        <h2>Film details</h2>
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

/** The line every page ends with: where the film details come from, as TMDB's terms ask. */
export function SiteFooter({ onAbout }) {
  return (
    <footer className="kc-footer">
      <a href="https://www.themoviedb.org/" target="_blank" rel="noreferrer" className="kc-footer__logo">
        <img src={tmdbLogo} alt="TMDB" />
      </a>
      <span>This product uses the TMDB API but is not endorsed or certified by TMDB.</span>
      <a href="/about" onClick={(e) => { e.preventDefault(); onAbout(); }}>About</a>
    </footer>
  );
}
