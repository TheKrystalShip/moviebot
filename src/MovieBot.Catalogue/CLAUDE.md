# MovieBot.Catalogue invariants

The catalogue is `moviebot-catalogue`: one entry per film and show, built from IMDb's datasets, TMDB and
Wikidata, written to `catalogue.db` and `images/` in its state directory (`/var/lib/moviebot-catalogue`).
It answers no request. Its settings are `Catalogue` in `src/moviebot.settings.json`; the TMDB token is
the `Tmdb` section's, from the environment.

- **A title is the catalogue's own id, mapped to each source's** (`titles`: the IMDb id, unique and
  required; the TMDB id with its kind; the Wikidata item). IMDb's kind is the title's: a TMDB answer of
  the other kind is a title TMDB does not hold, and two IMDb ids naming one TMDB title leave the second
  without a TMDB layer.
- **Each source keeps its own layer, and the entry is built from the layers by rule**
  (`Entries/EntryBuilder`). A layer is written with the entry built from it, in one transaction, so a
  reader never sees one without the other. The builder clears everything it writes before writing it,
  so building twice from the same layers gives the same entry, whatever order the layers came in; the
  rules for which source each field comes from are its summary. Ratings are not built: each source
  writes its own row in `ratings`, and neither overrides the other.
- **An answer that changes nothing writes nothing but when it was asked.** Each layer keeps a hash of
  what it holds; the film database's leaves out popularity and votes, which move daily and are written
  straight to `titles.popularity` and `ratings`. An entry keeps the fingerprint of the inputs it was
  built from (`EntryBuilder.Fingerprint`: the IMDb layer's fields, the other layers' hashes and
  `EntryBuilder.Rules`), and a build from the same inputs is skipped. `titles.changed_at` is when
  anything a page shows of the title last changed: a build, or a rating that moved. Changing what an
  entry is built into bumps `EntryBuilder.Rules`, and `BuildWorker` builds every entry again once.
- **Similar titles are kept by the film database's ids** and joined to the titles listed when read, so
  a title the catalogue gains later appears beside the titles that recommend it without their being
  built again.
- **What becomes an entry**: an IMDb film (`movie`, `tvMovie`) or show (`tvSeries`, `tvMiniSeries`),
  not adult, with `MinVotes` votes is a candidate; a ranking admits a title under the line when TMDB
  names its IMDb id. A candidate is listed once TMDB gives it a poster and it is not a placeholder (an
  unreleased title named `Untitled …`; a released one keeps its name). Nothing leaves the catalogue
  because its votes fell.
- **Everything a page filters, sorts, groups or searches by is a column or a join table**, never JSON;
  the layers are JSON because they are what a source said. Search is FTS5 over every name a listed title
  goes by (`titles_fts`) and every person's name (`people_fts`), each row keyed by the catalogue's id.
- **TMDB keeps nothing older than six months** (its terms, §1.C). `TmdbWorker` asks first about the
  titles marked due, then those never asked (most voted first), then the layers `SweepDays` old;
  `ChangesWorker` marks due what the changes feed names, daily, and the titles whose rating has come due,
  since the feed carries no votes. Every request goes through `TmdbGate`: at most `TmdbAtOnce` in flight,
  started no faster than `TmdbPerSecond`, and after a `429` nothing starts until the time TMDB named.
  A description is one `append_to_response` call and is pruned (`TmdbLayer.Prune`) to what the builder
  reads before it is kept.
- **Images are kept for what a page shows, and nothing else is** (`Images/ImageKeeper`): each listed
  entry's poster, backdrop and logo, its top `PortraitsKept` cast portraits, each franchise's poster and
  backdrop, and the season posters and episode stills of the shows the library holds (read from the
  manifests under `Media.Root` and `Media.ColdRoot`). A file lives at `images/<size><path>` by TMDB's
  path, is fetched again inside the six months, and is deleted once no entry names it. Only a path of
  the image store's own shape becomes a file.
- **Franchises are TMDB's collections; Wikidata adds universes** (`Entries/Universes`): an item listed
  titles name as their universe, franchise or series, when they span two collections or more or hold
  films and shows both. A title's place in its series is Wikidata's series ordinal where it has one.
- **Wikidata is asked by IMDb id, many at a time**, with `Cinema/<version> (<Contact>)` in the
  User-Agent and at most `WikidataAtOnce` questions in flight; after a `429` nothing is asked until the
  time it named. Where several items name one IMDb id, the lowest-numbered is the title's.
- **Tests never reach a source**: `ImdbImport.Load` takes the files as streams, and `TmdbSource` and
  `WikidataSource` are typed clients a test hands a handler (`tests/MovieBot.Tests/LocalCatalogueTests.cs`).
