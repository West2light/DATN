#!/usr/bin/env bash
set -euo pipefail

SERVER_ENV_FILE=/etc/tank-mapf/server.env
RUNTIME_ENV_FILE=/etc/tank-mapf/runtime.env

# shellcheck disable=SC1090
source "$SERVER_ENV_FILE"

WEB_PUBLIC_BASE_URL="${WEB_PUBLIC_BASE_URL:-http://${PUBLIC_IP}}"
WEB_HOST="${WEB_HOST:-${PUBLIC_IP}}"
WEB_GAME_PORT_PUBLIC="${WEB_GAME_PORT_PUBLIC:-${WEB_GAME_PORT}}"
WEB_TRANSPORT="${WEB_TRANSPORT:-websocket}"
REGISTRY_INTERNAL_URL="${REGISTRY_INTERNAL_URL:-http://127.0.0.1:${REGISTRY_PORT}}"

MAP_FILE=""
ALGORITHM=""
SESSION_CODE=""
MAX_PLAYERS="8"
ENEMY_MULTIPLIER="3"

while [ $# -gt 0 ]; do
  case "$1" in
    --map) MAP_FILE="$2"; shift 2 ;;
    --algorithm) ALGORITHM="$2"; shift 2 ;;
    --code) SESSION_CODE="$2"; shift 2 ;;
    --max-players) MAX_PLAYERS="$2"; shift 2 ;;
    --enemy-multiplier) ENEMY_MULTIPLIER="$2"; shift 2 ;;
    *) echo "Unknown argument: $1" >&2; exit 1 ;;
  esac
done

if [ -z "$MAP_FILE" ] || [ -z "$ALGORITHM" ] || [ -z "$SESSION_CODE" ]; then
  echo "Usage: tank-mapf-create-room --map MAP --algorithm ALG --code CODE [--max-players N] [--enemy-multiplier 1|3|6]" >&2
  exit 1
fi

case "$ENEMY_MULTIPLIER" in
  1|3|6) ;;
  *) echo "Enemy multiplier must be 1, 3, or 6." >&2; exit 1 ;;
esac

export MAP_FILE ALGORITHM SESSION_CODE MAX_PLAYERS ENEMY_MULTIPLIER PUBLIC_IP GAME_PORT WEB_GAME_PORT WEB_PUBLIC_BASE_URL WEB_HOST WEB_GAME_PORT_PUBLIC WEB_TRANSPORT REGISTRY_ADMIN_TOKEN REGISTRY_PUBLIC_BASE_URL REGISTRY_INTERNAL_URL

cat >"$RUNTIME_ENV_FILE" <<EOF
RELEASE_SHA=
MAP_FILE=${MAP_FILE}
ALGORITHM=${ALGORITHM}
SESSION_CODE=${SESSION_CODE}
MAX_PLAYERS=${MAX_PLAYERS}
ENEMY_MULTIPLIER=${ENEMY_MULTIPLIER}
TANK_ENEMY_MULTIPLIER=${ENEMY_MULTIPLIER}
EOF
chmod 0640 "$RUNTIME_ENV_FILE"

WEB_LOG_FILE=/var/log/tank-mapf/server-web.log
LOG_START_LINE=0
if [ -f "$WEB_LOG_FILE" ]; then
  LOG_START_LINE=$(wc -l < "$WEB_LOG_FILE" 2>/dev/null || echo 0)
fi

systemctl restart tank-mapf-server.service
systemctl restart tank-mapf-server-web.service

READY_TIMEOUT=45
READY=0
for i in $(seq 1 "$READY_TIMEOUT"); do
  if systemctl is-active --quiet tank-mapf-server-web.service \
     && ss -lnt 2>/dev/null | grep -q ":$WEB_GAME_PORT " \
     && tail -n +$((LOG_START_LINE + 1)) "$WEB_LOG_FILE" 2>/dev/null | grep -q "StartServer ok"; then
    READY=1
    break
  fi
  sleep 1
done

if [ "$READY" -ne 1 ]; then
  echo "WebSocket server not ready after $READY_TIMEOUT s (port $WEB_GAME_PORT / StartServer ok marker missing)" >&2
  exit 1
fi

python3 - <<'PY'
import json
import os
import subprocess

payload = {
    "code": os.environ["SESSION_CODE"],
    "host": os.environ["PUBLIC_IP"],
    "gamePort": int(os.environ["GAME_PORT"]),
    "transport": "udp",
    "webHost": os.environ["WEB_HOST"],
    "webGamePort": int(os.environ["WEB_GAME_PORT_PUBLIC"]),
    "webTransport": os.environ["WEB_TRANSPORT"],
    "webUrl": f"{os.environ['WEB_PUBLIC_BASE_URL'].rstrip('/')}/play?session={os.environ['SESSION_CODE']}",
    "map": os.environ["MAP_FILE"],
    "algorithm": os.environ["ALGORITHM"],
    "maxPlayers": int(os.environ["MAX_PLAYERS"]),
    "enemyMultiplier": int(os.environ["ENEMY_MULTIPLIER"]),
    "expiresInSeconds": 7200,
}
data = json.dumps(payload).encode("utf-8")
subprocess.run([
    "curl", "-fsS", "-X", "POST",
    "-H", f"Authorization: Bearer {os.environ['REGISTRY_ADMIN_TOKEN']}",
    "-H", "Content-Type: application/json",
    "--data-binary", "@-",
    f"{os.environ['REGISTRY_INTERNAL_URL'].rstrip('/')}/api/sessions",
], input=data, check=True)
print(f"{os.environ['REGISTRY_PUBLIC_BASE_URL'].rstrip('/')}/s/{os.environ['SESSION_CODE']}")
PY
