#!/usr/bin/env bash
set -euo pipefail

log() {
  echo "[tank-mapf-deploy] $*"
}

require_env() {
  local name="$1"
  if [ -z "${!name:-}" ]; then
    echo "Missing required environment variable: $name" >&2
    exit 1
  fi
}

SERVER_ENV_FILE=/etc/tank-mapf/server.env
RUNTIME_ENV_FILE=/etc/tank-mapf/runtime.env
INSTALL_ROOT=/opt/tank-mapf
RELEASES_ROOT="${INSTALL_ROOT}/releases"
CURRENT_ROOT="${INSTALL_ROOT}/current"
SERVICE_USER=tankmapf

OVERRIDE_RELEASE_SHA="${RELEASE_SHA:-}"
OVERRIDE_GAME_PORT="${GAME_PORT:-}"
OVERRIDE_REGISTRY_PORT="${REGISTRY_PORT:-}"
OVERRIDE_WEB_GAME_PORT="${WEB_GAME_PORT:-}"
OVERRIDE_MAP_FILE="${MAP_FILE:-}"
OVERRIDE_ALGORITHM="${ALGORITHM:-}"
OVERRIDE_SESSION_CODE="${SESSION_CODE:-}"
OVERRIDE_MAX_PLAYERS="${MAX_PLAYERS:-}"
OVERRIDE_ENEMY_MULTIPLIER="${ENEMY_MULTIPLIER:-}"
OVERRIDE_REGISTRY_ADMIN_TOKEN="${REGISTRY_ADMIN_TOKEN:-}"
OVERRIDE_PUBLIC_IP="${PUBLIC_IP:-}"
OVERRIDE_REGISTRY_PUBLIC_BASE_URL="${REGISTRY_PUBLIC_BASE_URL:-}"
OVERRIDE_WEB_PUBLIC_BASE_URL="${WEB_PUBLIC_BASE_URL:-}"
OVERRIDE_WEB_HOST="${WEB_HOST:-}"
OVERRIDE_WEB_GAME_PORT_PUBLIC="${WEB_GAME_PORT_PUBLIC:-}"
OVERRIDE_WEB_TRANSPORT="${WEB_TRANSPORT:-}"

if [ -f "$SERVER_ENV_FILE" ]; then
  # shellcheck disable=SC1091
  source "$SERVER_ENV_FILE"
fi

require_env ARTIFACT_URI

RELEASE_SHA="${OVERRIDE_RELEASE_SHA:-${RELEASE_SHA:-manual-$(date +%Y%m%d%H%M%S)}}"
GAME_PORT="${OVERRIDE_GAME_PORT:-${GAME_PORT:-7777}}"
REGISTRY_PORT="${OVERRIDE_REGISTRY_PORT:-${REGISTRY_PORT:-8080}}"
WEB_GAME_PORT="${OVERRIDE_WEB_GAME_PORT:-${WEB_GAME_PORT:-7778}}"
MAP_FILE="${OVERRIDE_MAP_FILE:-${MAP_FILE:-random-32-32-10.map}}"
ALGORITHM="${OVERRIDE_ALGORITHM:-${ALGORITHM:-AStar}}"
SESSION_CODE="${OVERRIDE_SESSION_CODE:-${SESSION_CODE:-}}"
MAX_PLAYERS="${OVERRIDE_MAX_PLAYERS:-${MAX_PLAYERS:-8}}"
ENEMY_MULTIPLIER="${OVERRIDE_ENEMY_MULTIPLIER:-${ENEMY_MULTIPLIER:-3}}"
REGISTRY_ADMIN_TOKEN="${OVERRIDE_REGISTRY_ADMIN_TOKEN:-${REGISTRY_ADMIN_TOKEN:-}}"
PUBLIC_IP="${OVERRIDE_PUBLIC_IP:-${PUBLIC_IP:-}}"
REGISTRY_PUBLIC_BASE_URL="${OVERRIDE_REGISTRY_PUBLIC_BASE_URL:-${REGISTRY_PUBLIC_BASE_URL:-http://127.0.0.1:${REGISTRY_PORT}}}"
WEB_PUBLIC_BASE_URL="${OVERRIDE_WEB_PUBLIC_BASE_URL:-${WEB_PUBLIC_BASE_URL:-http://${PUBLIC_IP}}}"
WEB_HOST="${OVERRIDE_WEB_HOST:-${WEB_HOST:-${PUBLIC_IP}}}"
WEB_GAME_PORT_PUBLIC="${OVERRIDE_WEB_GAME_PORT_PUBLIC:-${WEB_GAME_PORT_PUBLIC:-${WEB_GAME_PORT}}}"
WEB_TRANSPORT="${OVERRIDE_WEB_TRANSPORT:-${WEB_TRANSPORT:-websocket}}"
REGISTRY_URL="http://127.0.0.1:${REGISTRY_PORT}/api/sessions"

if [ -z "$SESSION_CODE" ]; then
  echo "SESSION_CODE is required." >&2
  exit 1
fi

if [ -z "$REGISTRY_ADMIN_TOKEN" ]; then
  echo "REGISTRY_ADMIN_TOKEN is required." >&2
  exit 1
fi

if [ -z "$PUBLIC_IP" ]; then
  echo "PUBLIC_IP is required." >&2
  exit 1
fi

case "$ENEMY_MULTIPLIER" in
  1|3|6) ;;
  *) echo "ENEMY_MULTIPLIER must be 1, 3, or 6." >&2; exit 1 ;;
esac

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

ARCHIVE_PATH="${TMP_DIR}/release.tar.gz"
RELEASE_DIR="${RELEASES_ROOT}/${RELEASE_SHA}"

log "Downloading artifact ${ARTIFACT_URI}"
gcloud storage cp "$ARTIFACT_URI" "$ARCHIVE_PATH"

log "Expanding artifact into ${RELEASE_DIR}"
rm -rf "$RELEASE_DIR"
install -d -o "$SERVICE_USER" -g "$SERVICE_USER" "$RELEASE_DIR"
tar -xzf "$ARCHIVE_PATH" -C "$RELEASE_DIR"
chown -R "$SERVICE_USER:$SERVICE_USER" "$RELEASE_DIR"
chmod +x "${RELEASE_DIR}/TankMapfServer.x86_64"

log "Switching current release"
if [ -d "$CURRENT_ROOT" ] && [ ! -L "$CURRENT_ROOT" ]; then
  rm -rf "$CURRENT_ROOT"
fi
ln -sfn "$RELEASE_DIR" "$CURRENT_ROOT"
chown -h "$SERVICE_USER:$SERVICE_USER" "$CURRENT_ROOT"

log "Writing runtime environment"
cat >"$RUNTIME_ENV_FILE" <<EOF
RELEASE_SHA=${RELEASE_SHA}
MAP_FILE=${MAP_FILE}
ALGORITHM=${ALGORITHM}
SESSION_CODE=${SESSION_CODE}
MAX_PLAYERS=${MAX_PLAYERS}
ENEMY_MULTIPLIER=${ENEMY_MULTIPLIER}
TANK_ENEMY_MULTIPLIER=${ENEMY_MULTIPLIER}
EOF
chmod 0640 "$RUNTIME_ENV_FILE"

log "Starting Unity dedicated server service"
systemctl daemon-reload
systemctl enable tank-mapf-server.service
systemctl enable tank-mapf-server-web.service
systemctl restart tank-mapf-server.service
systemctl restart tank-mapf-server-web.service

log "Publishing session to registry"
SESSION_BODY="$(PUBLIC_IP="$PUBLIC_IP" GAME_PORT="$GAME_PORT" WEB_HOST="$WEB_HOST" WEB_GAME_PORT_PUBLIC="$WEB_GAME_PORT_PUBLIC" WEB_TRANSPORT="$WEB_TRANSPORT" WEB_PUBLIC_BASE_URL="$WEB_PUBLIC_BASE_URL" MAP_FILE="$MAP_FILE" ALGORITHM="$ALGORITHM" MAX_PLAYERS="$MAX_PLAYERS" ENEMY_MULTIPLIER="$ENEMY_MULTIPLIER" SESSION_CODE="$SESSION_CODE" python3 - <<'PY'
import json
import os

session_code = os.environ["SESSION_CODE"]
payload = {
    "code": session_code,
    "host": os.environ["PUBLIC_IP"],
    "gamePort": int(os.environ["GAME_PORT"]),
    "transport": "udp",
    "webHost": os.environ["WEB_HOST"],
    "webGamePort": int(os.environ["WEB_GAME_PORT_PUBLIC"]),
    "webTransport": os.environ["WEB_TRANSPORT"],
    "webUrl": f"{os.environ['WEB_PUBLIC_BASE_URL'].rstrip('/')}/play?session={session_code}",
    "map": os.environ["MAP_FILE"],
    "algorithm": os.environ["ALGORITHM"],
    "maxPlayers": int(os.environ["MAX_PLAYERS"]),
    "enemyMultiplier": int(os.environ["ENEMY_MULTIPLIER"]),
    "expiresInSeconds": 7200,
}
print(json.dumps(payload))
PY
)"

curl -fsS \
  -X POST \
  -H "Authorization: Bearer ${REGISTRY_ADMIN_TOKEN}" \
  -H "Content-Type: application/json" \
  --data "$SESSION_BODY" \
  "$REGISTRY_URL" >/dev/null

log "Deploy complete: ${REGISTRY_PUBLIC_BASE_URL}/s/${SESSION_CODE}"
