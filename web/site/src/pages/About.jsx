import tmdbLogo from "../assets/tmdb.svg";
import { PageHead } from "../components/Film.jsx";
import { onTv } from "../tv/tv.js";

/**
 * What this site is, and where what it shows about movies and shows comes from. `head` false draws it
 * without its page heading, as a section of a page that heads it.
 */
export function About({ head = true }) {
  return (
    <div className="kc-page kc-about">
      {head && <PageHead title="About" sub="Cinema, MovieBot's library in a browser." />}

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
        {/* A television leaves the site for nothing, so there the logo leads nowhere. */}
        {onTv
          ? <span className="kc-about__logo"><img src={tmdbLogo} alt="The Movie Database (TMDB)" /></span>
          : (
            <a href="https://www.themoviedb.org/" target="_blank" rel="noreferrer" className="kc-about__logo">
              <img src={tmdbLogo} alt="The Movie Database (TMDB)" />
            </a>
          )}
        <p>
          Credits, certificates, overviews and artwork come from The Movie Database (TMDB), and so do the
          ratings of titles IMDb does not rate. This product uses TMDB and the TMDB APIs but is not endorsed,
          certified, or otherwise approved by TMDB.
        </p>
      </section>

      <section className="kc-about__block">
        <h2>Awards, adaptations and places</h2>
        <p>
          What a title won, what it is based on and where it is set and filmed come from Wikidata
          ({onTv ? "wikidata.org" : <a href="https://www.wikidata.org" target="_blank" rel="noreferrer">wikidata.org</a>}).
        </p>
      </section>

      <section className="kc-about__block">
        <h2>Ratings</h2>
        <p>
          Ratings and votes are IMDb's wherever IMDb rates a title. Information courtesy of IMDb
          ({onTv ? "imdb.com" : <a href="https://www.imdb.com" target="_blank" rel="noreferrer">imdb.com</a>}).
          Used with permission.
        </p>
      </section>
    </div>
  );
}
