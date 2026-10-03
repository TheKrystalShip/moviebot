# Krystal Cinema

MovieBot's site: the library in a browser, for members of the server MovieBot plays films in. It is
served by `MovieBot.Api` from the root of its `wwwroot`, beside the player under `/activity/`, and
built on `@thekrystalship/krystal-ui`, the design system the KGSM Control Panel is built on too.

```bash
npm install        # @thekrystalship/krystal-ui comes from GitHub Packages, which wants a token
npm run dev        # http://localhost:5175
npm run lint
npm run build      # -> dist/; scripts/build-player.sh installs it into the API's wwwroot
```

The dev server proxies `/api`, `/media` and `/hub` to the API (`MOVIEBOT_API`, default
`http://127.0.0.1:8099`) and `/assistant` to moviebot-assistant (`MOVIEBOT_ASSISTANT`, default
`http://127.0.0.1:8191`), which is what the vhost does in production.

## Pages

| Path | What it is |
|---|---|
| `/` | A film to watch, the rooms watching something now, what is on its way, what is leaving soon, and the shelf |
| `/library` | Every film, narrowed by state and ordered by name, year or length |
| `/title/<id>` | One film: what it is, its tracks, how long it stays, keep or let go, and watch |
| `/search?q=` | The library first, then the tracker's releases to fetch, and a film with no release yet to wait on |
| `/notify` | The films the signed-in person is waiting on |
| `/leaving` | Every downloaded film and how long it stays, kept or not |
| `/downloads` | What is downloading and seeding, read again every few seconds |
| `/profile` | The theme, and signing out |
| `/signin` | The door, and where Discord sends the browser back to |

A page is a path, so it can be linked to and reloaded: the API answers any path that is not its own
with this site's index.

## Signing in

Discord is the only door. Signing in asks Discord for `identify guilds.members.read`; the API redeems
the code, asks Discord whether the person is a member of the server, and issues its own token only to
a member. That token is the bearer on every call to the API and to the assistant, and is kept in this
browser under `cinema:session` until it expires. The redirect, `<origin>/signin`, is registered with
the Discord application.

## Watching

Watching opens the player at `/activity/?session=<room>&title=<id>`: a room of its own for whoever
opens it, or the room somebody is already watching in, which is how a browser joins a voice
channel's film. The player shares this origin and takes the signed-in person's token and name from
the same session.

## The assistant

The dock holds krystal-ui's `Chat`, talking to moviebot-assistant at `/assistant` as the signed-in
person. A film the assistant puts on or finds comes back as the tool's card and is drawn with a way
to watch it; a proposal (a download, a wish, a keep, subtitles) is confirmed in the chat by the person
who asked for it.

## Rules

- **Only what the API knows is shown.** The catalogue gives a film's name, year, billing and poster;
  the manifest its length, picture and tracks; the retention rule how long it stays. Nothing the API
  does not hold, a rating or a genre, is drawn, and a row with nothing in it is left out.
- **One module attaches the token.** `src/api.js` is the only place a bearer is added to a call to
  the API; the assistant's client is given a way to read the token, never the token itself. A 401
  signs the browser out.
- **Every act is the person's own.** The API takes who asked from the token, so the site sends no
  name of its own and cannot act for anybody else.
