## NexusForever
[![Discord](https://img.shields.io/discord/499473932131500034.svg?style=flat&logo=discord)](https://discord.gg/8wT3GEQ)

### Information
A server emulator for WildStar written in C# that supports build 16042.


### Links
 * [Website](https://emulator.ws)
 * [Discord](https://discord.gg/8wT3GEQ)
 * [World Database](https://github.com/NexusForever/NexusForever.WorldDatabase)

## Running with Docker (self-hosted)

Docker only (Docker Engine 24+ with the `docker compose` v2 plugin). Podman is not supported or tested.

Requirements: **your own WildStar client data** (not distributable):
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
* Docker Engine's default `json-file` logging has no size cap. Add `logging: {driver: json-file, options: {max-size: 10m, max-file: "3"}}` to services if disk matters.
* Backups: `docker compose exec db mariadb-dump -uroot -p"$DB_ROOT_PASSWORD" --all-databases > backup.sql`

## Status

Fork of [NexusForever](https://github.com/NexusForever/NexusForever) with a Docker setup on top. The Docker files have been statically checked only (compose config parses, scripts pass `bash -n`); the images have not yet been built and run against a live WildStar client. Expect first-run fixes and please open an issue with the `docker compose logs` output if something fails.

Base runtime is .NET 5 (end-of-life), pinned because the code targets EF Core 5. Do not expose the server to untrusted networks beyond the three game ports.
