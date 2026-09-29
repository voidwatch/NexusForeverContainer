## NexusForever
[![Discord](https://img.shields.io/discord/499473932131500034.svg?style=flat&logo=discord)](https://discord.gg/8wT3GEQ)

### Information
A server emulator for WildStar written in C# that supports build 16042.


### Links
 * [Website](https://emulator.ws)
 * [Discord](https://discord.gg/8wT3GEQ)
 * [World Database](https://github.com/NexusForever/NexusForever.WorldDatabase)

## Build Status

### Linux


## Running with Docker (self-hosted)

Untested against a live client: see "Status" below.

Requirements: Docker + compose plugin, and **your own WildStar client data** (not distributable):
* `data/tbl/`: the extracted `.tbl` game tables and `.bin` text tables (build 16042)
* `data/map/`: the `.nfmap` base maps generated with `NexusForever.MapGenerator` from your client

```bash
cp .env.example .env            # set passwords and REALM_HOST (the IP/DNS clients will connect to)
docker compose up -d --build    # db -> world (applies migrations, registers realm) -> auth + sts
docker compose logs -f world
```

Optional creature/spawn data from upstream (run once, after `world` has started):
```bash
scripts/load-world-data.sh
```

Create an account: attach to the world console, type the command, detach with `Ctrl-p Ctrl-q`:
```bash
docker attach nexusforever-world-1
>> account create you@example.com yourpassword
```

Ports to open/forward: 6600 (STS), 23115 (auth), 24000 (world). The database and the embedded web console (localhost:5000 inside the container) are not published.

Notes
* `WorldServer` is the only service that migrates the schema, so `auth`/`sts` wait for it to be healthy.
* Config comes from the bundled `*.example.json` plus environment overrides (`Database__Auth__ConnectionString`, `Map__MapPath`, `GameTablePath`, ...), see `docker-compose.yml`.
* `NF_SKIP_MIGRATIONS=1` disables EF migrations if you manage the schema by hand.
* Backups: `docker compose exec db mariadb-dump -uroot -p"$DB_ROOT_PASSWORD" --all-databases > backup.sql`
