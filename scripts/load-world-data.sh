#!/bin/bash
# Applies the optional upstream world data (creature spawns etc.) to nexus_forever_world.
# Run AFTER the world container has started once (it creates the schema).
# Only the open-world zones are loaded (Alizar, Isigrol, Olyssia). They match this fork's schema and each file starts with
# a DELETE for its own zone, so re-running is safe. Instance/ (expeditions, arenas, tutorial) is skipped on purpose: it was
# written in 2024+ for a newer NexusForever schema (entity_event, creature_info, ...) that this fork does not have.
set -euo pipefail
cd "$(dirname "$0")/.."
set -a; . ./.env; set +a
SRC="${1:-./world-data}"
if [ ! -d "$SRC" ]; then
  git clone --depth 1 https://github.com/NexusForever/NexusForever.WorldDatabase "$SRC"
fi
find "$SRC/Alizar" "$SRC/Isigrol" "$SRC/Olyssia" -name '*.sql' | sort | while read -r f; do
  echo "applying $f"
  docker compose exec -T db mariadb -u"$DB_USER" -p"$DB_PASSWORD" nexus_forever_world < "$f"
done
