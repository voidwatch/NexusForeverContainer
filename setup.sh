#!/usr/bin/env bash
# NexusForever container setup: checks prerequisites, writes .env, builds and starts the stack, verifies it, and
# optionally loads world data and creates your first account.
#
# You must provide the game client data yourself (data/tbl and data/map, see README.md). This script cannot and does not
# download any game files.
#
#   ./setup.sh                          interactive
#   ./setup.sh --yes --realm-host 192.168.1.10 --account me@example.com     (password via NF_ACCOUNT_PASSWORD)
#   ./setup.sh --check                  only check that a running stack is reachable
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

PROJECT_NETWORK="nexusforever_default"
YES=0
CHECK_ONLY=0
SKIP_WORLD_DATA=0
SKIP_ACCOUNT=0
SKIP_FIREWALL=0
REALM_HOST_ARG=""
ACCOUNT_ARG=""

usage() {
  cat <<'EOF'
Usage: ./setup.sh [options]
  -y, --yes               do not prompt, accept safe defaults
      --realm-host HOST   IP or DNS name game clients use to reach this machine (default: detected LAN IP)
      --account EMAIL     create this game account (password from prompt or NF_ACCOUNT_PASSWORD)
      --skip-world-data   do not load creature/spawn data
      --skip-account      do not offer to create an account
      --skip-firewall     do not offer to change ufw
      --check             only check an already running stack
  -h, --help              this text
EOF
}

while [ $# -gt 0 ]; do
  case "$1" in
    -y|--yes) YES=1 ;;
    --realm-host) REALM_HOST_ARG="${2:-}"; shift ;;
    --account) ACCOUNT_ARG="${2:-}"; shift ;;
    --skip-world-data) SKIP_WORLD_DATA=1 ;;
    --skip-account) SKIP_ACCOUNT=1 ;;
    --skip-firewall) SKIP_FIREWALL=1 ;;
    --check) CHECK_ONLY=1 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage >&2; exit 1 ;;
  esac
  shift
done

if [ -t 1 ]; then B=$'\033[1m'; G=$'\033[32m'; Y=$'\033[33m'; R=$'\033[31m'; N=$'\033[0m'; else B=""; G=""; Y=""; R=""; N=""; fi
step() { echo; echo "${B}== $*${N}"; }
ok()   { echo "${G}ok${N}   $*"; }
warn() { echo "${Y}warn${N} $*"; }
die()  { echo "${R}error${N} $*" >&2; exit 1; }

# Ctrl-C always ends the script (the stack keeps running, it is not stopped).
trap 'echo; warn "Interrupted. Containers that were already started keep running (docker compose ps)."; exit 130' INT TERM

# bounded SECONDS command...   Run a command that must not be allowed to hang: it is killed after SECONDS, stdin is /dev/null
# (a command that reads the terminal from a background process group is stopped by SIGTTIN and can never be killed or
# interrupted), and --foreground keeps it in the terminal's foreground group so Ctrl-C reaches it.
bounded() {
  local secs="$1"; shift
  timeout --foreground -s KILL "$secs" "$@" </dev/null
}

# ask "question" default(y|n) -> 0 for yes
ask() {
  local q="$1" def="${2:-n}" reply
  if [ "$YES" -eq 1 ] || [ ! -t 0 ]; then [ "$def" = "y" ]; return; fi
  if [ "$def" = "y" ]; then read -rp "$q [Y/n] " reply; reply="${reply:-y}"; else read -rp "$q [y/N] " reply; reply="${reply:-n}"; fi
  [[ "$reply" =~ ^[Yy] ]]
}

rand() { LC_ALL=C tr -dc 'A-Za-z0-9' </dev/urandom | head -c "${1:-24}" || true; }

detect_ip() {
  ip -4 route get 1.1.1.1 2>/dev/null | awk '{for (i = 1; i < NF; i++) if ($i == "src") { print $(i + 1); exit }}' \
    || true
}

container_health() {
  local id
  id="$(bounded 20 docker compose ps -q "$1" 2>/dev/null || true)"
  [ -n "$id" ] || { echo "missing"; return; }
  bounded 20 docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$id" 2>/dev/null || echo "unknown"
}

wait_healthy() {
  local svc="$1" timeout="${2:-600}" waited=0 status
  while [ "$waited" -lt "$timeout" ]; do
    status="$(container_health "$svc")"
    case "$status" in
      healthy) [ "$waited" -eq 0 ] || echo; return 0 ;;
      exited|dead) echo; return 1 ;;
    esac
    printf '.'
    sleep 5; waited=$((waited + 5))
  done
  echo
  return 1
}

# ---------------------------------------------------------------------------------------------------------------------
preflight() {
  step "Checking prerequisites"
  command -v docker >/dev/null 2>&1 || die "Docker is not installed. Install Docker Engine: https://docs.docker.com/engine/install/"
  docker compose version >/dev/null 2>&1 || die "The 'docker compose' v2 plugin is missing. Install docker-compose-plugin."
  docker info >/dev/null 2>&1 || die "Cannot talk to the Docker daemon. Is it running, and is your user in the 'docker' group?"
  ok "docker $(docker version --format '{{.Server.Version}}' 2>/dev/null || echo '?') with compose $(docker compose version --short 2>/dev/null || echo '?')"
}

check_client_data() {
  step "Checking client data"
  local tbl maps missing=0 f
  tbl="$(find data/tbl -maxdepth 1 -name '*.tbl' 2>/dev/null | wc -l)"
  maps="$(find data/map -maxdepth 1 -name '*.nfmap' 2>/dev/null | wc -l)"
  [ "$tbl" -ge 100 ] || { warn "data/tbl has only $tbl .tbl files (expected a few hundred)"; missing=1; }
  [ -f data/tbl/en-US.bin ] || { warn "data/tbl/en-US.bin is missing"; missing=1; }
  for f in Western Eastern NewCentral; do
    [ -f "data/map/$f.nfmap" ] || { warn "data/map/$f.nfmap is missing"; missing=1; }
  done
  if [ "$missing" -ne 0 ]; then
    cat >&2 <<'EOF'

The server needs game data extracted from a WildStar client (build 16042). This repository cannot provide it.
See "Client data" in README.md for what is required and how to generate data/tbl and data/map with the included
NexusForever.MapGenerator tool. Then run ./setup.sh again.
EOF
    exit 1
  fi
  ok "$tbl tables, $maps maps"
}

write_env() {
  step "Configuration"
  if [ -f .env ]; then
    ok ".env already exists, keeping it"
  else
    local host="${REALM_HOST_ARG:-${REALM_HOST:-}}"
    [ -n "$host" ] || host="$(detect_ip)"
    if [ "$YES" -ne 1 ] && [ -t 0 ]; then
      read -rp "Address game clients use to reach this machine [${host:-none}]: " reply
      host="${reply:-$host}"
    fi
    [ -n "$host" ] || die "Could not detect an address. Re-run with --realm-host <ip-or-name>."
    ( umask 077
      cat > .env <<EOF
# Generated by setup.sh. Passwords are alphanumeric on purpose (they end up in a connection string).
# Changing DB_PASSWORD or DB_ROOT_PASSWORD later does NOT change the existing database, see README "Resetting".
DB_ROOT_PASSWORD=$(rand 24)
DB_USER=nexusforever
DB_PASSWORD=$(rand 24)

# What game clients are told to connect to for the world server (LAN IP or DNS name of THIS host).
REALM_HOST=$host
REALM_NAME=NexusForever
REALM_ID=1

# Host address the game ports are published on. 0.0.0.0 = every interface.
BIND_IP=0.0.0.0
EOF
    )
    ok "wrote .env (realm address $host, random database passwords, mode 600)"
  fi
    set -a
    # shellcheck disable=SC1091
    . ./.env
    set +a
  [ -n "${REALM_HOST:-}" ] || die "REALM_HOST is empty in .env"
}

start_stack() {
  step "Building and starting (first build downloads .NET images and packages, this takes a few minutes)"
  if ! docker compose up -d --build; then
    warn "docker compose reported a failure. Last world log lines:"
    docker compose logs --tail=40 world || true
    die "Stack did not start. Fix the error above and re-run ./setup.sh (it is safe to re-run)."
  fi
  wait_healthy world 600 || { docker compose logs --tail=60 world || true; die "world did not become healthy"; }
  ok "world is healthy"
  local svc state
  for svc in db auth sts; do
    state="$(container_health "$svc")"
    case "$state" in healthy|running) ;; *) die "$svc is not running (state: $state). See: docker compose logs $svc" ;; esac
  done
  ok "db, auth and sts are running"
}

realm_reachable() {
  # </dev/null and --foreground matter: plain `timeout` moves the command into a background process group, so when
  # docker reads the terminal it is stopped by SIGTTIN and neither the timeout nor Ctrl-C can end it (the script hangs).
  bounded 8 docker compose exec -T auth bash -c "exec 3<>/dev/tcp/${REALM_HOST}/24000" >/dev/null 2>&1
}

check_realm_reachable() {
  step "Checking that the auth server can reach the world server at ${REALM_HOST}:24000"
  if realm_reachable; then ok "reachable"; return 0; fi

  warn "The auth container cannot connect to ${REALM_HOST}:24000. Clients would see \"No realms are available\"."
  warn "Usual cause: a host firewall (ufw) dropping container -> host traffic. Docker-published ports bypass ufw for outside"
  warn "clients, but not for containers talking to the host's own address."

  local sudo_cmd="" status subnet
  [ "$(id -u)" -eq 0 ] || sudo_cmd="sudo"
  if [ "$SKIP_FIREWALL" -eq 0 ] && command -v ufw >/dev/null 2>&1; then
    subnet="$(docker network inspect "$PROJECT_NETWORK" -f '{{range .IPAM.Config}}{{.Subnet}}{{end}}' 2>/dev/null || true)"
    subnet="${subnet:-172.16.0.0/12}"
    if [ "$YES" -eq 1 ] || [ ! -t 0 ]; then
      warn "If you use ufw, allow the stack's containers to reach the world port:"
      warn "  sudo ufw allow from $subnet to any port 24000 proto tcp"
    else
      status=""
      if [ -z "$sudo_cmd" ] || sudo -n true 2>/dev/null; then
        status="$(${sudo_cmd:+$sudo_cmd -n} ufw status 2>/dev/null | head -1 || true)"
      else
        warn "sudo needs a password to inspect ufw, so this step is skipped. If clients cannot log in, run:"
        warn "  sudo ufw allow from $subnet to any port 24000 proto tcp"
      fi
      if echo "$status" | grep -qi "status: active"; then
        if ask "ufw is active. Add 'ufw allow from $subnet to any port 24000 proto tcp' (this stack's containers only)?" y; then
          $sudo_cmd ufw allow from "$subnet" to any port 24000 proto tcp comment 'nexusforever containers to world' >/dev/null
          sleep 2
          if realm_reachable; then ok "reachable now"; return 0; fi
          warn "still not reachable after adding the ufw rule"
        fi
      fi
    fi
  fi
  warn "Fix the firewall or address, then run: ./setup.sh --check   (see README, Troubleshooting)"
  return 1
}

world_data_loaded() {
  local n
  n="$(bounded 30 docker compose exec -T -e MYSQL_PWD="$DB_PASSWORD" db mariadb -u"$DB_USER" -N -e 'SELECT COUNT(*) FROM nexus_forever_world.entity' 2>/dev/null | tr -d '[:space:]' || true)"
  [ -n "$n" ] && [ "$n" -gt 0 ] 2>/dev/null
}

load_world_data() {
  [ "$SKIP_WORLD_DATA" -eq 0 ] || return 0
  step "World data (creature spawns)"
  if world_data_loaded; then ok "already loaded, skipping"; return 0; fi
  command -v git >/dev/null 2>&1 || { warn "git is not installed, skipping. Install git and run scripts/load-world-data.sh"; return 0; }
  if ask "Load the open-world creature/spawn data from the upstream NexusForever.WorldDatabase repo? (downloads ~4 MB, takes a few minutes)" y; then
    scripts/load-world-data.sh
    docker compose restart world >/dev/null
    wait_healthy world 600 || die "world did not come back healthy after loading world data"
    ok "world data loaded, world restarted"
  else
    echo "Skipped. Run scripts/load-world-data.sh later, then: docker compose restart world"
  fi
}

create_account() {
  [ "$SKIP_ACCOUNT" -eq 0 ] || return 0
  step "Game account"
  local email="$ACCOUNT_ARG"
  if [ -z "$email" ]; then
    if [ "$YES" -eq 1 ] || [ ! -t 0 ]; then echo "No --account given, skipping."; return 0; fi
    if ! ask "Create a game account now?" y; then echo "Skipped. Later: scripts/create-account.sh you@example.com"; return 0; fi
    read -rp "Email (used as the login name): " email
  fi
  [ -n "$email" ] || return 0
  scripts/create-account.sh "$email" || warn "account was not created (see message above)"
}

summary() {
  step "Done"
  cat <<EOF
Server is running.

  Realm address for clients : ${REALM_HOST}   (ports 6600 STS, 23115 auth, 24000 world)
  Logs                      : docker compose logs -f world
  Server console            : docker attach --sig-proxy=false --detach-keys=ctrl-x nexusforever-world-1   (Ctrl-X detaches, never Ctrl-C)
  Create more accounts      : scripts/create-account.sh you@example.com
  Stop / start              : docker compose stop / docker compose start

Next: point your WildStar client at ${REALM_HOST}. See README.md, "Connecting the client".
EOF
}

# ---------------------------------------------------------------------------------------------------------------------
preflight
if [ "$CHECK_ONLY" -eq 1 ]; then
  [ -f .env ] || die "No .env found. Run ./setup.sh first."
    set -a
    # shellcheck disable=SC1091
    . ./.env
    set +a
  step "Container status"
  docker compose ps
  check_realm_reachable
  exit 0
fi

check_client_data
write_env
start_stack
check_realm_reachable || true
load_world_data
create_account
summary
