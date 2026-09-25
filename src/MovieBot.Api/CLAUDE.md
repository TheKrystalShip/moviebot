# MovieBot.Api invariants

`docs/api-contract.md` is the authority for what this serves on the wire. Rooms are
`Sessions/CLAUDE.md`; subtitles fetched from outside are `Subtitles/CLAUDE.md`.

- **The server is authoritative and there is no host.** Clients send intent; the store decides,
  stamps the server clock and assigns the next revision. A client never pushes state.
- **Every type on the wire is named in a serializer context, and the two contexts are the only
  resolver.** `ApiJsonContext` holds this service's own shapes and `ManifestJsonContext` the Core
  library's, and nothing falls back to reflection, in the JIT build the tests run as much as in the
  native one, so an anonymous object or an unregistered type fails a test rather than a request. A
  list is looked up by the type the endpoint declares and written by the type it holds, so both are
  registered, and a list bound for the wire is built as a `List<T>`: a collection expression yields a
  type of the compiler's own that nothing has registered.
- **Resolve configuration inside the DI factory, not at the top of `Program.cs`.** Reading
  `builder.Configuration` while the builder is still being assembled misses sources added later — a
  test host's media root, for one — and the service silently points somewhere else.
- **Playlists for a transcoding title are `no-store`.** A cached growing playlist makes the film
  appear to end early, which looks exactly like a broken transcode.
- **A film's bytes are opened by a media ticket, everything else by the room token.** The token says
  who somebody is, which a segment has no use for; the ticket names one title and an hour, is the
  same string for every viewer of that film, and so makes their requests one cacheable URL. A client
  sends one or the other and never both: a shared cache refuses to store a response to a request
  carrying the header.
- **Media answers HEAD as well as GET.** Browsers probe a URL before fetching it, and a 405 there
  reads as the file being unavailable.
- **Which disk a film is on changes no URL.** `/media/{id}/…` resolves the id per request (see
  `src/MovieBot.Core/CLAUDE.md`), which is also what makes it safe to move a film out from under a
  room that is watching it.
- **CORS reflects the origin rather than enumerating one.** The Activity is served from Discord's
  proxy under an origin that is not known ahead of time, and a failed preflight is invisible: no
  status, no log, just a request that never happens.
- **The API publishes its public address on `/api/config`.** The page's origin is Discord's proxy,
  reachable only from inside the Activity, and the poster a viewer's presence shows is fetched by
  Discord's servers, so the page builds the poster URL from that address.

## What a film gains while people are watching it is pushed to them (`Library/TitleChanges.cs`)

The preview sheet is written after the main pass, and so are the subtitles of a source that was still
arriving, so the manifest that marks a film ready is the first to name them. The API watches the
manifests of the titles occupied rooms hold and sends the fresh copy down the hub as `TitleChanged`,
and the player adopts what changed: the head, the sheet, the subtitle list. There is no poll in the
player; a page that could not be heard from asks once on the way back. It goes over the hub rather
than a second channel because every viewer already holds an authenticated connection, and a subtitle
fetched from outside is announced by its route, since the file lives beside the manifest and the
manifest's timestamp says nothing happened.
