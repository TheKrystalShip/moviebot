# Deploying MovieBot

This guide takes you from a downloaded release to a working MovieBot: a Discord bot that
downloads films and plays them to everyone in a voice channel. You do not need to know how
MovieBot works inside. Follow the steps in order and copy the commands as they are, changing only
the parts in `CAPITALS` or marked as examples.

With everything under [Before you start](#before-you-start) ready, it takes about ten minutes.

## What gets installed

| Piece | What it does |
|---|---|
| `moviebot-api` | Serves the player, your films, and the live connection that keeps everyone in a room at the same moment of the film |
| `moviebot-bot` | The Discord bot: the `/watch`, `/download`, `/notify` and `/keep` commands |
| `moviebot-handoff` | Watches for finished downloads and prepares them for playback on your GPU |
| `moviebot-ingest` | A command for adding films you already have |

All three services run under their own user account, `moviebot`, and are managed by systemd.

## Before you start

You need all of these. The steps below assume they are in place.

- **A Linux machine** (x86_64) running systemd. The commands are given for Debian/Ubuntu and for
  Arch Linux.
- **An NVIDIA graphics card** with its driver installed. Running `nvidia-smi` should print a table
  that shows your card. MovieBot uses the card to convert films into a format browsers can play.
- **A domain name** (for example `movies.example.com`) that points at this machine, with ports
  80 and 443 reachable from the internet. Discord only opens the player from a public https
  address.
- **A Discord server** where you have the Manage Server permission, so you can add the bot.
- **An account on a supported tracker**, with its address, your username, your passkey, and the
  names of the tracker's film categories. The tracker must offer a member API that accepts a
  username and passkey.
- **`sudo` access** on the machine.

## Step 1: Install the system packages

MovieBot needs ffmpeg (to convert films), nginx and certbot (to serve it over https), and
qBittorrent (to download).

Debian or Ubuntu:

```bash
sudo apt update
sudo apt install ffmpeg nginx certbot python3-certbot-nginx qbittorrent-nox curl
```

Arch Linux:

```bash
sudo pacman -S --needed ffmpeg nginx certbot certbot-nginx qbittorrent-nox curl
```

Now check that ffmpeg can use your graphics card. This encodes one second of a test picture:

```bash
ffmpeg -hide_banner -loglevel error -f lavfi -i testsrc2=duration=1:size=1280x720 -c:v h264_nvenc -f null - && echo "GPU encoding works"
```

If it prints `GPU encoding works`, continue. If it prints an error instead, your ffmpeg or NVIDIA
driver cannot encode on the card; see [Troubleshooting](#troubleshooting).

## Step 2: Download and install MovieBot

Download the latest release, check that it arrived intact, and unpack it:

```bash
cd ~
curl -LO https://github.com/TheKrystalShip/moviebot/releases/latest/download/moviebot-linux-x64.tar.gz
curl -LO https://github.com/TheKrystalShip/moviebot/releases/latest/download/moviebot-linux-x64.tar.gz.sha256
sha256sum -c moviebot-linux-x64.tar.gz.sha256
tar -xzf moviebot-linux-x64.tar.gz
```

`sha256sum` should print `moviebot-linux-x64.tar.gz: OK`. Then run the installer:

```bash
cd moviebot-*/
sudo ./install.sh
```

The installer:

- creates the `moviebot` user,
- puts the programs in `/opt/moviebot`,
- creates `/srv/moviebot/media` (your film library), `/srv/moviebot/downloads` and
  `/srv/moviebot/incoming` (for films you add yourself),
- creates the settings file `/etc/moviebot/moviebot.env`, with its internal keys already filled in,
- sets up qBittorrent's settings and the three systemd services.

It does not start anything yet.

## Step 3: Create the Discord application

Open the [Discord Developer Portal](https://discord.com/developers/applications) and sign in.
Keep a text editor open: you will copy four values from here.

1. Click **New Application**, name it (for example `MovieBot`), accept the terms and click
   **Create**.
2. On **General Information**, copy the **Application ID**. This is value 1.
3. Open **Bot** in the left menu. Click **Reset Token**, confirm, and copy the token. This is
   value 2. Discord shows it only once.
4. Open **OAuth2**. Under **Client Secret**, click **Reset Secret** and copy it. This is value 3.
5. Still on **OAuth2**, under **Redirects**, click **Add Redirect**, enter `https://127.0.0.1`
   and click **Save Changes**. Discord needs one redirect to allow the sign-in the player uses.
   Nothing is ever sent to that address.
6. Open **Activities** → **Settings** and turn on **Enable Activities**.
7. Open **Activities** → **URL Mappings**. For the root mapping (prefix `/`), enter your domain
   without `https://`, for example `movies.example.com`, and save.

Finally, the ID of your Discord server (value 4): in the Discord app, open **User Settings** →
**Advanced**, turn on **Developer Mode**, then right-click your server's icon and click
**Copy Server ID**.

## Step 4: Fill in the settings

Open the settings file:

```bash
sudo nano /etc/moviebot/moviebot.env
```

Fill in each line marked `REQUIRED`. Put the value straight after the `=`, with no spaces and no
quotes.

| Line | What to put there |
|---|---|
| `MOVIEBOT_TOKEN` | Value 2, the bot token |
| `MOVIEBOT_CLIENTID` and `Discord__ClientId` | Value 1, the application ID, on both lines |
| `Discord__ClientSecret` | Value 3, the client secret |
| `Discord__GuildIds__0` | Value 4, your server ID |
| `Player__BaseUrl` and `Api__PublicBaseUrl` | `https://` and your domain, on both lines, for example `https://movies.example.com` |
| `Tracker__BaseUrl` | Your tracker's address, for example `https://tracker.example/` |
| `Tracker__Username`, `Tracker__Passkey` | Your tracker username and passkey |
| `Selection__AllowedCategories__0` | A tracker category films may come from, spelled exactly as on the tracker. Add `__1`, `__2` and so on for more |

Leave the three keys under "Internal keys" as they are. `OpenSubtitles__ApiKey` is optional.

Save with `Ctrl+O`, `Enter`, then exit with `Ctrl+X`.

## Step 5: Start qBittorrent

qBittorrent runs as the `moviebot` user, with the settings the installer put in place:

```bash
sudo systemctl enable --now qbittorrent-nox@moviebot
```

Check that it answers:

```bash
curl -s http://127.0.0.1:8080/api/v2/app/version; echo
```

It should print a version number such as `v5.0.4`. If it prints `Forbidden` or nothing, wait a
few seconds and try again.

## Step 6: Set up nginx and https

Copy the example site into place, putting in your own domain instead of `movies.example.com`:

```bash
sudo sed 's/movies.example.com/YOUR.DOMAIN/' /etc/moviebot/examples/nginx-moviebot.conf \
  | sudo tee /etc/nginx/conf.d/moviebot.conf > /dev/null
```

**On Arch Linux only:** nginx does not read `/etc/nginx/conf.d` by default. Open
`/etc/nginx/nginx.conf` and add this line inside the `http {` block, just before its closing `}`:

```nginx
include /etc/nginx/conf.d/*.conf;
```

Check the configuration, start nginx, and get a certificate:

```bash
sudo nginx -t
sudo systemctl enable --now nginx
sudo systemctl reload nginx
sudo certbot --nginx -d YOUR.DOMAIN
```

certbot asks for an email address and for agreement to its terms, then adds https to the site.

## Step 7: Start MovieBot

```bash
sudo systemctl enable --now moviebot-api moviebot-bot moviebot-handoff
```

Check that all three are running:

```bash
systemctl status moviebot-api moviebot-bot moviebot-handoff --no-pager
```

Each should say `active (running)`. Then check the API from outside, in a browser or with curl:

```bash
curl -s https://YOUR.DOMAIN/health; echo
```

It should print a line starting with `{"status":"ok"`.

## Step 8: Invite the bot to your server

The bot writes its invite link to its log when it starts:

```bash
journalctl -u moviebot-bot --no-pager | grep -o 'https://discord.com/oauth2/authorize[^ ]*' | tail -1
```

Open that link in a browser, choose your server and click **Authorize**. The link already asks
for every permission the bot needs.

## Step 9: Watch a film

1. Join a voice channel in your server.
2. Type `/watch` and start typing a film's name in the `title` box. Films you already have appear
   first; otherwise the list shows what the tracker offers.
3. Pick one. If it has to be downloaded, the bot says so and posts a message once it can be
   played, usually within a few minutes. You do not have to wait for the whole download.
4. Press **Watch together** on the bot's message. The player opens inside Discord for everyone who joins.

Play, pause and seeking are shared by everyone in the room. Volume, subtitles and the audio
track are each person's own.

## Adding films you already have

Copy the film into `/srv/moviebot/incoming`, where the `moviebot` user can read it, then prepare
it for playback:

```bash
sudo cp /path/to/film.mkv /srv/moviebot/incoming/
sudo chown moviebot: /srv/moviebot/incoming/film.mkv
sudo -u moviebot moviebot-ingest /srv/moviebot/incoming/film.mkv --out /srv/moviebot/media
```

The film appears in `/watch` while it is still being prepared. Once the command finishes, the
copy in `incoming` is no longer needed and can be deleted. `moviebot-ingest --help` lists the
options.

## Upgrading

Download and unpack the new release as in [step 2](#step-2-download-and-install-moviebot), then run
`sudo ./install.sh` from its directory. Your settings, films and downloads are kept, and the
running services restart on the new version.

## Troubleshooting

Every service writes to the system journal. The last lines usually say exactly what is wrong:

```bash
journalctl -u moviebot-api -n 50 --no-pager
journalctl -u moviebot-bot -n 50 --no-pager
journalctl -u moviebot-handoff -n 50 --no-pager
```

| Problem | What to check |
|---|---|
| A service keeps restarting | Its journal names the missing or wrong setting. Fix it in `/etc/moviebot/moviebot.env`, then `sudo systemctl restart` that service |
| The bot is online but `/watch` does not appear | Commands appear only in the server set in `Discord__GuildIds__0`. Check the number, then restart `moviebot-bot` |
| The player shows "Discord sign-in failed" | `Discord__ClientId` or `Discord__ClientSecret` is wrong, or the redirect from step 3.5 is missing |
| Pressing **Watch together** shows a blank or failed Activity | The URL mapping in step 3.7 must be your domain without `https://`, and `https://YOUR.DOMAIN/health` must work from outside your network |
| The GPU check in step 1 fails | Run `nvidia-smi` to confirm the driver works. Some distributions ship an ffmpeg built for a newer NVIDIA driver than the card supports; updating the driver, or installing an ffmpeg built for your driver, fixes it |
| A download finishes but the film never appears | Check the `moviebot-handoff` journal, and that the GPU check in step 1 still passes |
| `/watch` finds nothing on the tracker | Check `Tracker__BaseUrl`, the passkey, and that the category names match the tracker's spelling exactly |

## Where everything is

| Path | What it holds |
|---|---|
| `/etc/moviebot/moviebot.env` | Your settings |
| `/opt/moviebot` | The programs, replaced on every upgrade |
| `/srv/moviebot/media` | Your film library |
| `/srv/moviebot/downloads` | Downloads, which keep seeding for a week unless someone uses `/keep` |
| `/srv/moviebot/incoming` | Films you add yourself, before `moviebot-ingest` prepares them |
| `/var/lib/moviebot`, `/var/lib/moviebot-bot` | Open rooms, fetched subtitles, and the bot's saved state |
| `/etc/systemd/system/moviebot-*.service` | The service definitions |
