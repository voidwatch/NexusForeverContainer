#!/bin/bash
# Applies the optional upstream world data (creature spawns etc.) to nexus_forever_world.
# Run AFTER the world container has started once (it creates the schema). Idempotency: each file is a dump, re-running
# duplicates spawns, so run it once on a fresh world DB (or truncate entity* tables first).
set -euo pipefail
cd "$(dirname "$0")/.."
set -a; . ./.env; set +a
SRC="${1:-./world-data}"
if [ ! -d "$SRC" ]; then
  git clone --depth 1 https://github.com/NexusForever/NexusForever.WorldDatabase "$SRC"
fi
find "$SRC" -name '*.sql' | sort | while read -r f; do
  echo "applying $f"
  docker compose exec -T db mariadb -u"$DB_USER" -p"$DB_PASSWORD" nexus_forever_world < "$f"
done
