# NexusForever Container

A self-hosted, containerised **WildStar** server emulator. One `./setup.sh` gets you a database, the STS, auth and world servers, and a first account, all in Docker.

This is a fork of [NexusForever](https://github.com/NexusForever/NexusForever), a WildStar server emulator written in C# for client build **16042** (the final live build), with a Docker setup on top and a handful of Linux/container fixes.

> **This repository contains no game files.** No client, no game tables, no maps, nothing extracted from the game. You have to supply the client data yourself, see [Client data](#client-data-you-must-provide-this). Please do not open issues asking where to download it.

---

## Contents

- [What you get](#what-you-get)
- [Requirements](#requirements)
- [Client data (you must provide this)](#client-data-you-must-provide-this)
- [Quick start](#quick-start)
- [Connecting the game client](#connecting-the-game-client)
- [Day-to-day operation](#day-to-day-operation)
- [Configuration](#configuration)
- [Manual setup (without setup.sh)](#manual-setup-without-setupsh)
- [Troubleshooting](#troubleshooting)
- [Security notes](#security-notes)
- [Known limitations](#known-limitations)
- [Credits and license](#credits-and-license)

---

## What you get

```
 WildStar client
   │ 6600/tcp   STS   (login handshake, token)         ┐
   │ 23115/tcp  auth  (checks account, picks a realm)  ├─ published on the host
   │ 24000/tcp  world (the game itself)                ┘
   ▼
┌─────────── docker compose ────────────────────────────────┐
│  sts ─┐                                                    │
│  auth ├──►  db (MariaDB 10.11: auth / character / world)   │
│  world┘     ▲                                              │
│  world applies the schema (EF migrations) on start         │
│  and registers the realm; auth/sts start after it          │
└────────────────────────────────────────────────────────────┘
```

| Service | Port | Role |
|---|---|---|
| `db` | not published | MariaDB with three databases (`nexus_forever_auth`, `_character`, `_world`) |
| `world` | 24000 | Game world. Owns the database schema; also serves the (localhost-only) web console |
| `auth` | 23115 | Authenticates the client and hands it the realm address |
| `sts` | 6600 | Initial login handshake |

Game data (`data/tbl`, `data/map`) is mounted read-only into `world`. Parsed tables are cached in a named volume so restarts are quick.

## Requirements

- A Linux host (tested on Debian/Ubuntu-family), 4 GB RAM or more recommended, a few GB of disk.
- **Docker Engine 24+** with the **`docker compose` v2 plugin**. Podman is not supported or tested.
- `git`, `bash` and the usual coreutils (`setup.sh` uses `timeout`), plus internet access to pull Docker images, NuGet packages and (optionally) the upstream world data from GitHub.
- A **WildStar client at build 16042** on a Windows PC to play, and to generate the data below.

## Client data (you must provide this)

The server needs two folders that are generated from a WildStar client:

| Folder | Contents | Used for |
|---|---|---|
| `data/tbl/` | ~385 game tables (`*.tbl`) plus the text table `en-US.bin` (`fr-FR.bin` and `de-DE.bin` optional) | All game rules: items, spells, creatures, quests, ... |
| `data/map/` | `*.nfmap` base maps (one per world) | Terrain and collision |

**Getting a client is your responsibility.** The game was shut down in 2018 and the client is NCSoft's copyrighted software, so it is not distributed here and will not be linked from here. Search the web and the upstream NexusForever community ([website](https://emulator.ws), [Discord](https://discord.gg/8wT3GEQ)) for how people obtain a build **16042** client. Other builds will not match the server's tables and code.

Once you have a 16042 client installed, generate the data with the tool included in this repository, `Source/NexusForever.MapGenerator`. It reads the client's `Patch` folder (`ClientData.index` / `ClientData.archive`, plus `ClientDataEN.index` for the English text) and writes `tbl/` and `map/` into the current directory.

On the Windows PC that has the client (PowerShell):

```powershell
# 1. .NET 5 SDK (the project targets net5.0)
winget install Microsoft.DotNet.SDK.5
#    Open a NEW terminal afterwards so `dotnet` is on the PATH. If winget can't find it, download the .NET 5 SDK
#    from Microsoft. (Untested fallback: install a newer SDK and set $env:DOTNET_ROLL_FORWARD = "LatestMajor".)

# 2. get this repo and run the generator against YOUR client's Patch folder
git clone https://github.com/voidwatch/NexusForeverContainer.git
cd NexusForeverContainer\Source\NexusForever.MapGenerator
dotnet run -c Release -- -i "C:\Program Files (x86)\NCSOFT\WildStar\Patch" -e -g
```

`-e` extracts the tables and text, `-g` generates the maps (about 2-3 minutes; `--worldId 51` generates a single world for a quick test). The tool ends with "Finished!" and waits for Enter. Sanity check:

```powershell
(Get-ChildItem tbl).Count                       # about 385
(Get-ChildItem map -Filter *.nfmap).Count       # about 115
```

Copy both folders into `data/` of the machine that will run the server, so the files sit **directly** in `data/tbl/` and `data/map/` (not `data/tbl/tbl/`):

```powershell
scp -r tbl map user@your-server:~/NexusForeverContainer/data/
```

Both folders are git-ignored, so they never end up in a commit by accident. At minimum the default configuration requires `Western.nfmap`, `Eastern.nfmap` and `NewCentral.nfmap` and `en-US.bin`.

## Quick start

```bash
git clone https://github.com/voidwatch/NexusForeverContainer.git
cd NexusForeverContainer
# put your generated tbl/ and map/ into ./data/ (see above)
./setup.sh
```

`./setup.sh` is safe to re-run. It:

1. checks Docker and Compose,
2. refuses to continue if `data/tbl` / `data/map` are not there, with a pointer back to this README,
3. writes `.env` with random database passwords (mode 600) and the address clients will use (your detected LAN IP, editable),
4. builds and starts the stack, and waits until the world server is healthy,
5. checks that the auth container can reach the world server on the address it advertises, and offers the fix (a `ufw` rule) if not,
6. offers to load creature/spawn data, and
7. offers to create your first game account (asks for an email and password, never puts the password on a command line).

Non-interactive: `NF_ACCOUNT_PASSWORD='...' ./setup.sh --yes --realm-host 192.168.1.10 --account you@example.com`. See `./setup.sh --help`.

The first build downloads the .NET images and NuGet packages and takes a few minutes. After that, the world server is ready about half a minute after start.

## Connecting the game client

The client has to talk to your server instead of NCSoft's. Do this on the Windows PC with the client. Replace `SERVER` with the address you set as `REALM_HOST` (your server's LAN IP, or a public DNS name if you host for others).

### Method 1: hosts file redirect (recommended)

This works regardless of how the client is started. The client looks up NCSoft's login hostnames; you point them at your server. The default ports of the emulator (6600, 23115, 24000) are the ones the client expects, so nothing else is needed.

1. Open the hosts file **as administrator**. In PowerShell:
   ```powershell
   Start-Process notepad "C:\Windows\System32\drivers\etc\hosts" -Verb RunAs
   ```
2. Add these lines at the bottom (use your address instead of `SERVER`) and save:
   ```
   SERVER cligate.ncsoft.com
   SERVER auth.eu.wildstar-online.com
   ```
   The exact hostnames the client uses can differ by region and language. If the client shows *"Cannot connect to NCSoft login services -- <host> -- <host>"*, add **every host it lists**.
3. `ipconfig /flushdns`, then start the game (press **PLAY** in the launcher, or run `Client64\WildStar64.exe`).
4. Log in with the account you created.

To go back to normal, delete those lines from the hosts file.

### Method 2: launch arguments (upstream's method)

The upstream project starts the client with explicit server addresses:

```powershell
cd "C:\Program Files (x86)\NCSOFT\WildStar\Client64"
.\WildStar64.exe /auth SERVER /authNc SERVER /lang en /patcher SERVER /SettingsKey WildStar /realmDataCenterId 9
```

On the retail-launcher install this was tested with, the arguments were ignored and the client still tried NCSoft's servers, so Method 1 is the one to rely on. Your mileage may vary with other client installs. `Source/NexusForever.ClientConnector` (a small .NET Framework launcher) does the same thing as this command.

### Adding the client to Steam (optional)

In Steam: **Add a Game** → **Add a Non-Steam Game...** → **Browse...**, set the filter to *All Files*, pick the launcher `WildStar.exe` in the install root (not `Client64\WildStar64.exe`), then **Add Selected Programs**. The hosts-file redirect is system-wide, so nothing else is needed. Steam's overlay and Steam Input do not attach to this client.

## Day-to-day operation

```bash
docker compose ps                         # status, world should be (healthy)
docker compose logs -f world              # follow the world log (add auth, sts for the login servers)
docker compose stop                       # stop everything, data is kept
docker compose start                      # start again
docker compose up -d --build              # apply an update after `git pull`
```

**Server console.** `world` runs with a TTY so you can use the built-in commands (`help`, `account ...`, ...):

```bash
docker attach --sig-proxy=false --detach-keys=ctrl-x nexusforever-world-1
```

Press **Ctrl-X** to detach. **Never press Ctrl-C while attached:** the console is a real terminal, so Ctrl-C goes straight to the server process and stops it (Docker then restarts it, but players are disconnected). Log lines scroll over what you type, the input still goes through.

**Accounts.**

```bash
scripts/create-account.sh you@example.com      # prompts for the password
```

Emails are stored lower-case. There is no self-service signup, accounts are created by you.

**World data (creature spawns).** Without it the world is empty. `./setup.sh` offers to load it; you can also run it any time:

```bash
scripts/load-world-data.sh && docker compose restart world
```

It downloads [NexusForever.WorldDatabase](https://github.com/NexusForever/NexusForever.WorldDatabase) at a revision that matches this server's schema and applies the open-world zones (Alizar, Olyssia, Isigrol). Each file replaces its own zone, so re-running is safe. See [Known limitations](#known-limitations) for what is left out.

**Backups.**

```bash
set -a; . ./.env; set +a
docker compose exec -T db mariadb-dump -uroot -p"$DB_ROOT_PASSWORD" --all-databases > backup-$(date +%F).sql
# restore into a running, empty stack:
docker compose exec -T db mariadb -uroot -p"$DB_ROOT_PASSWORD" < backup-2026-01-01.sql
```

**Changing the advertised address** (new IP, new DNS name): edit `REALM_HOST` in `.env`, then `docker compose up -d`. The world server rewrites the realm row in the database on start.

**Resetting.** `docker compose down -v` deletes the database and the table cache (all accounts and characters). The data in `./data` is untouched. Do this too if you changed `DB_PASSWORD` after the first start: MariaDB only reads those variables when it creates a new database volume.

## Configuration

`.env` (created by `setup.sh`, or copy `.env.example`):

| Variable | Default | Meaning |
|---|---|---|
| `DB_ROOT_PASSWORD` | random | MariaDB root password (alphanumeric only) |
| `DB_USER` / `DB_PASSWORD` | `nexusforever` / random | Application database user (alphanumeric only) |
| `REALM_HOST` | detected LAN IP | Address clients are told to use for the world server. Must be reachable **from the clients and from inside the containers** |
| `REALM_NAME` | `NexusForever` | Realm name shown in the client |
| `REALM_ID` | `1` | Realm id in the auth database |
| `BIND_IP` | `0.0.0.0` | Host address the three game ports are published on |

The servers read the upstream JSON config files (bundled defaults) and let environment variables override them, using `__` as the separator, for example `Map__PrecacheBaseMaps__0=22` or `MessageOfTheDay="Welcome!"`. Add them to the `environment:` block of the service in `docker-compose.yml`. Other switches: `NF_SKIP_MIGRATIONS=1` stops the world server touching the database schema (if you manage it yourself).

Repository layout:

```
setup.sh                  guided setup / re-run to repair
docker-compose.yml        db, world, auth, sts
Dockerfile                one image definition for all three servers (build arg PROJECT)
docker/mariadb-init.sh    creates the three databases on first start
scripts/                  create-account.sh, load-world-data.sh
data/tbl  data/map        YOUR client data (git-ignored)
Source/                   the NexusForever source, with fixes (see below)
```

Changes compared with upstream: EF migrations run on start again, the realm row and the first account can be created from the environment, Windows-style asset paths (`Map\Western`) work on Linux, and the console loop no longer spins when there is no terminal attached.

## Manual setup (without setup.sh)

```bash
cp .env.example .env && chmod 600 .env    # set DB_ROOT_PASSWORD, DB_PASSWORD, REALM_HOST
docker compose up -d --build
docker compose logs -f world               # wait for "Ready!"
scripts/load-world-data.sh && docker compose restart world
scripts/create-account.sh you@example.com
```

## Troubleshooting

Start with `./setup.sh --check` (container status plus the auth-to-world reachability test) and `docker compose logs --tail=80 world`.

| Symptom | Cause and fix |
|---|---|
| `setup.sh` says client data is missing | `data/tbl` or `data/map` is empty or nested one level too deep (`data/tbl/tbl`). See [Client data](#client-data-you-must-provide-this) |
| `dependency failed to start: container ...-world-1 is unhealthy` | The world server crashed on start. `docker compose logs --tail=80 world`, the last exception says why |
| `Could not find file '/data/map/....nfmap'` | A map is missing. Regenerate with `-g`, or copy all of `map/` |
| Client: *"Cannot connect to NCSoft login services"* | The client is not being redirected. Use the [hosts file method](#method-1-hosts-file-redirect-recommended), as administrator, and add every host the message lists |
| Client login screen says **"No realms are available at this time"** | You are logged in, but the auth server thinks the world server is offline. It checks by opening a TCP connection to `REALM_HOST:24000` **from inside its container**. Usually a host firewall blocks container to host traffic. With ufw: `sudo ufw allow from <docker subnet> to any port 24000 proto tcp` (`./setup.sh` finds the subnet and offers this). If `REALM_HOST` is a DNS name, it must resolve to the host from inside the container. Right after a restart it can also take up to about 15 seconds |
| Windows error *"The instruction at ... referenced memory at ... could not be read"* when closing the client | Seen on exit with the 64-bit client (also when started through Steam, with the overlay off). It happens after you have quit; characters and settings were intact afterwards and the server was unaffected. The cause is in the client executable and is not investigated further. Click OK |
| Login fails with a wrong-credentials error | No such account or wrong password. Create it with `scripts/create-account.sh`. Emails are lower-case |
| `Realm id 1 doesn't exist in the database` | `REALM_HOST` is empty, so the realm row was not created. Set it in `.env`, `docker compose up -d` |
| `Access denied` for the database user after editing `.env` | You changed a DB password after the first start. See *Resetting* |
| French/German "not loaded" warnings in the world log | Harmless. Only `en-US.bin` is required |
| Empty world, no creatures | World data not loaded, run `scripts/load-world-data.sh`, then `docker compose restart world` |

## Security notes

- The database is **not published**. Only ports 6600, 23115 and 24000 are. The world server's embedded web console listens on `localhost` inside its container and is not exposed.
- Docker publishes ports by inserting its own firewall rules, **ahead of ufw/firewalld**. `ufw allow`/`deny` does not restrict these three ports for outside traffic. Restrict them with `BIND_IP` (for example your LAN address), or with rules in the `DOCKER-USER` iptables chain.
- Do not expose the server to the internet casually. It is a hobby emulator: the .NET 5 runtime it targets is end-of-life, there is no rate limiting on login, and the game protocol predates modern security practices. Prefer a LAN or a VPN (WireGuard, Tailscale) for friends.
- `.env` holds your database passwords. It is created with mode 600 and is git-ignored. Keep it out of backups you share.
- The realm name and address are visible to anyone who can reach the auth port.

## Known limitations

- **Instance content is not loaded.** Expeditions, arenas and the tutorial data in the upstream world database were written for a newer NexusForever schema than this fork has, so `load-world-data.sh` only loads the open-world zones. Those instances will be empty or unavailable.
- Only English text is required. French and German work if you also copy `fr-FR.bin` and `de-DE.bin` into `data/tbl/`.
- What works is what upstream NexusForever implements, which is a work in progress by nature. This repository only makes it easy to run.
- Verified with a real build 16042 client on a LAN through login and realm selection. Not load-tested.
- The images are built on .NET 5 (end-of-life). Porting to a newer runtime is a code change, not a container change.
- Linux hosts only. Podman is untested.

## Credits and license

- [NexusForever](https://github.com/NexusForever/NexusForever) and its contributors wrote the emulator. All the game logic here is theirs.
- World data: [NexusForever.WorldDatabase](https://github.com/NexusForever/NexusForever.WorldDatabase).
- Licensed under the **GNU AGPL v3.0** (see `LICENSE`). If you run a modified version as a network service, the AGPL requires you to offer your users the corresponding source.

WildStar, NCSOFT and Carbine are trademarks or registered trademarks of NCSOFT Corporation. This project is not affiliated with or endorsed by NCSOFT or Carbine, contains none of their assets, and exists for preservation and private hobby use. What you host and who you host it for is your own responsibility.
