namespace TheKrystalShip.MovieBot.Catalogue;

/// <summary>
/// The catalogue's settings (<c>Catalogue</c> in <c>moviebot.settings.json</c>). The film database's
/// address, language and token are its own section, <c>Tmdb</c>, which the API reads too.
/// </summary>
public sealed class CatalogueOptions
{
    public const string Section = "Catalogue";

    /// <summary>
    /// The directory the database and the images live in, which the API reads too. Empty is the state
    /// directory systemd hands the unit, or the working directory without one.
    /// </summary>
    public string Directory { get; set; } = "";

    /// <summary>The IMDb votes a title needs to be a candidate. A ranking admits titles under it.</summary>
    public int MinVotes { get; set; } = 100;

    /// <summary>Where IMDb's datasets are served from.</summary>
    public string ImdbBaseUrl { get; set; } = "https://datasets.imdbws.com/";

    /// <summary>How often the datasets are asked after. An unchanged day costs two HEAD requests.</summary>
    public int ImdbHours { get; set; } = 24;

    /// <summary>The film database's request rate the catalogue holds to, and how many may be in flight.</summary>
    public int TmdbPerSecond { get; set; } = 20;

    public int TmdbAtOnce { get; set; } = 16;

    /// <summary>
    /// The age at which a title's film database layer is asked for again whether or not it changed.
    /// The database's terms keep nothing it said longer than six months, so this stays under that.
    /// </summary>
    public int SweepDays { get; set; } = 150;

    /// <summary>
    /// How often the film database's rating is asked for again for a title a ranking holds or that
    /// came out within <see cref="RecentDays"/>; the database's changes feed does not carry votes.
    /// </summary>
    public int RatingDays { get; set; } = 7;

    public int RecentDays { get; set; } = 60;

    /// <summary>How often the rankings are asked for, and how many pages of twenty each keeps.</summary>
    public int RankingHours { get; set; } = 6;

    public int RankingPages { get; set; } = 5;

    /// <summary>How many of a title's top-billed cast have their portrait kept.</summary>
    public int PortraitsKept { get; set; } = 10;

    /// <summary>How many images are fetched at once from the film database's image store.</summary>
    public int ImagesAtOnce { get; set; } = 8;

    /// <summary>Wikidata's SPARQL endpoint, how often every title's facts are asked for, and how many questions run at once (it allows five per address).</summary>
    public string WikidataEndpoint { get; set; } = "https://query.wikidata.org/sparql";

    public int WikidataDays { get; set; } = 7;

    public int WikidataAtOnce { get; set; } = 2;

    /// <summary>Where Wikidata's operators can reach whoever asks, sent in the User-Agent Wikidata requires.</summary>
    public string Contact { get; set; } = "https://movies.thekrystalship.com";
}
