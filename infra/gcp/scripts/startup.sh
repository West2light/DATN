#!/usr/bin/env bash
set -euo pipefail

export DEBIAN_FRONTEND=noninteractive

log() {
  echo "[tank-mapf-startup] $*"
}

retry() {
  local attempts="$1"
  shift
  local count=0
  until "$@"; do
    count=$((count + 1))
    if [ "$count" -ge "$attempts" ]; then
      return 1
    fi
    sleep 5
  done
}

write_file() {
  local path="$1"
  local owner="$2"
  local mode="$3"

  install -d "$(dirname "$path")"
  cat >"$path"
  chown "$owner" "$path"
  chmod "$mode" "$path"
}

install_google_cloud_cli() {
  if command -v gcloud >/dev/null 2>&1; then
    return 0
  fi

  log "Installing google-cloud-cli"
  install -d -m 0755 /usr/share/keyrings
  curl -fsSL https://packages.cloud.google.com/apt/doc/apt-key.gpg \
    | gpg --dearmor -o /usr/share/keyrings/cloud.google.gpg
  cat >/etc/apt/sources.list.d/google-cloud-sdk.list <<'EOF'
deb [signed-by=/usr/share/keyrings/cloud.google.gpg] https://packages.cloud.google.com/apt cloud-sdk main
EOF
  retry 5 apt-get update
  retry 5 apt-get install -y google-cloud-cli
}

log "Installing base packages"
retry 5 apt-get update
retry 5 apt-get install -y curl ca-certificates gnupg nginx python3 tar sudo
install_google_cloud_cli

if ! id -u ${service_user} >/dev/null 2>&1; then
  log "Creating service user ${service_user}"
  useradd --system --create-home --home-dir ${install_root} --shell /usr/sbin/nologin ${service_user}
fi

log "Creating directories"
install -d -o ${service_user} -g ${service_user} -m 0755 ${install_root}
install -d -o ${service_user} -g ${service_user} -m 0755 ${registry_root}
install -d -o ${service_user} -g ${service_user} -m 0755 ${releases_root}
install -d -o ${service_user} -g ${service_user} -m 0755 ${scripts_root}
install -d -o ${service_user} -g ${service_user} -m 0755 ${log_root}
install -d -o ${service_user} -g ${service_user} -m 0755 "$(dirname ${registry_data_file})"
install -d -o www-data -g www-data -m 0755 ${web_root}
install -d -o www-data -g www-data -m 0755 ${web_root}/releases
install -d -o root -g root -m 0755 /etc/tank-mapf

log "Writing registry app"
write_file "${registry_root}/app.py" "${service_user}:${service_user}" 0644 <<'__TANK_MAPF_REGISTRY_APP_PY__'
${registry_app_py}
__TANK_MAPF_REGISTRY_APP_PY__

log "Writing deploy script"
write_file "${scripts_root}/deploy-release.sh" "root:root" 0755 <<'__TANK_MAPF_DEPLOY_RELEASE_SH__'
${deploy_release_sh}
__TANK_MAPF_DEPLOY_RELEASE_SH__

log "Writing web deploy script"
write_file "${scripts_root}/deploy-web.sh" "root:root" 0755 <<'__TANK_MAPF_DEPLOY_WEB_SH__'
${deploy_web_sh}
__TANK_MAPF_DEPLOY_WEB_SH__

write_file "/usr/local/bin/tank-mapf-create-room" "root:root" 0755 <<'__TANK_MAPF_CREATE_ROOM_SH__'
#!/usr/bin/env bash
set -euo pipefail

SERVER_ENV_FILE=/etc/tank-mapf/server.env
RUNTIME_ENV_FILE=/etc/tank-mapf/runtime.env

# shellcheck disable=SC1090
source "$SERVER_ENV_FILE"

WEB_PUBLIC_BASE_URL="$${WEB_PUBLIC_BASE_URL:-http://$${PUBLIC_IP}}"

MAP_FILE=""
ALGORITHM=""
SESSION_CODE=""
MAX_PLAYERS="8"

while [ $# -gt 0 ]; do
  case "$1" in
    --map) MAP_FILE="$2"; shift 2 ;;
    --algorithm) ALGORITHM="$2"; shift 2 ;;
    --code) SESSION_CODE="$2"; shift 2 ;;
    --max-players) MAX_PLAYERS="$2"; shift 2 ;;
    *) echo "Unknown argument: $1" >&2; exit 1 ;;
  esac
done

if [ -z "$MAP_FILE" ] || [ -z "$ALGORITHM" ] || [ -z "$SESSION_CODE" ]; then
  echo "Usage: tank-mapf-create-room --map MAP --algorithm ALG --code CODE [--max-players N]" >&2
  exit 1
fi

export MAP_FILE ALGORITHM SESSION_CODE MAX_PLAYERS PUBLIC_IP GAME_PORT WEB_GAME_PORT WEB_PUBLIC_BASE_URL REGISTRY_ADMIN_TOKEN REGISTRY_PUBLIC_BASE_URL

cat >"$RUNTIME_ENV_FILE" <<EOF
RELEASE_SHA=
MAP_FILE=$${MAP_FILE}
ALGORITHM=$${ALGORITHM}
SESSION_CODE=$${SESSION_CODE}
MAX_PLAYERS=$${MAX_PLAYERS}
EOF
chmod 0640 "$RUNTIME_ENV_FILE"

systemctl restart tank-mapf-server.service
systemctl restart tank-mapf-server-web.service

python3 - <<PY
import json
import os
import subprocess

payload = {
    "code": os.environ["SESSION_CODE"],
    "host": os.environ["PUBLIC_IP"],
    "gamePort": int(os.environ["GAME_PORT"]),
    "transport": "udp",
    "webHost": os.environ["PUBLIC_IP"],
    "webGamePort": int(os.environ["WEB_GAME_PORT"]),
    "webTransport": "websocket",
    "webUrl": f"{os.environ['WEB_PUBLIC_BASE_URL'].rstrip('/')}/play?session={os.environ['SESSION_CODE']}",
    "map": os.environ["MAP_FILE"],
    "algorithm": os.environ["ALGORITHM"],
    "maxPlayers": int(os.environ["MAX_PLAYERS"]),
    "expiresInSeconds": 7200,
}
data = json.dumps(payload).encode("utf-8")
subprocess.run([
    "curl", "-fsS", "-X", "POST",
    "-H", f"Authorization: Bearer {os.environ['REGISTRY_ADMIN_TOKEN']}",
    "-H", "Content-Type: application/json",
    "--data-binary", "@-",
    f"{os.environ['REGISTRY_PUBLIC_BASE_URL'].rstrip('/')}/api/sessions",
], input=data, check=True)
print(f"{os.environ['REGISTRY_PUBLIC_BASE_URL'].rstrip('/')}/s/{os.environ['SESSION_CODE']}")
PY
__TANK_MAPF_CREATE_ROOM_SH__

cat >/etc/sudoers.d/tank-mapf-create-room <<'__TANK_MAPF_SUDOERS__'
tankmapf ALL=(root) NOPASSWD: /usr/local/bin/tank-mapf-create-room
__TANK_MAPF_SUDOERS__
chmod 0440 /etc/sudoers.d/tank-mapf-create-room

log "Writing environment files"
write_file "${server_env_file}" "root:root" 0640 <<__TANK_MAPF_SERVER_ENV__
GAME_PORT=${game_port}
REGISTRY_PORT=${registry_port}
WEB_GAME_PORT=${web_game_port}
PUBLIC_IP=${public_ip}
REGISTRY_PUBLIC_BASE_URL=${registry_base_url}
WEB_PUBLIC_BASE_URL=http://${public_ip}
MAP_FILE=${map_file}
ALGORITHM=${algorithm}
MAX_PLAYERS=${max_players}
SESSION_CODE=${session_code}
REGISTRY_ADMIN_TOKEN=${registry_admin_token}
REGISTRY_DATA_FILE=${registry_data_file}
__TANK_MAPF_SERVER_ENV__

if [ ! -f "${runtime_env_file}" ]; then
  write_file "${runtime_env_file}" "root:root" 0640 <<'__TANK_MAPF_RUNTIME_ENV__'
RELEASE_SHA=
__TANK_MAPF_RUNTIME_ENV__
fi

if [ ! -f "${registry_data_file}" ]; then
  log "Initializing registry data file"
  cat >"${registry_data_file}" <<'__TANK_MAPF_SESSIONS_JSON__'
{"sessions": {}, "updatedAt": 0}
__TANK_MAPF_SESSIONS_JSON__
  chown ${service_user}:${service_user} "${registry_data_file}"
  chmod 0644 "${registry_data_file}"
fi

log "Writing systemd units"
write_file "/etc/systemd/system/tank-mapf-registry.service" "root:root" 0644 <<'__TANK_MAPF_REGISTRY_SERVICE__'
${registry_service}
__TANK_MAPF_REGISTRY_SERVICE__

write_file "/etc/systemd/system/tank-mapf-server.service" "root:root" 0644 <<'__TANK_MAPF_SERVER_SERVICE__'
${server_service}
__TANK_MAPF_SERVER_SERVICE__

write_file "/etc/systemd/system/tank-mapf-server-web.service" "root:root" 0644 <<'__TANK_MAPF_SERVER_WEB_SERVICE__'
${web_server_service}
__TANK_MAPF_SERVER_WEB_SERVICE__

log "Writing nginx site"
install -d -o root -g root -m 0755 /etc/nginx/sites-available
install -d -o root -g root -m 0755 /etc/nginx/sites-enabled
cat >/etc/nginx/sites-available/tank-mapf-web <<'__TANK_MAPF_NGINX_SITE__'
server {
    listen 80 default_server;
    listen [::]:80 default_server;
    server_name _;

    root ${web_root}/current;
    index index.html;

    location ~* \.js\.br$ {
        gzip off;
        add_header Content-Encoding br always;
        default_type application/javascript;
        try_files $uri =404;
    }

    location ~* \.wasm\.br$ {
        gzip off;
        add_header Content-Encoding br always;
        default_type application/wasm;
        try_files $uri =404;
    }

    location ~* \.data\.br$ {
        gzip off;
        add_header Content-Encoding br always;
        default_type application/octet-stream;
        try_files $uri =404;
    }

    location ~* \.symbols\.json\.br$ {
        gzip off;
        add_header Content-Encoding br always;
        default_type application/json;
        try_files $uri =404;
    }

    location /api/sessions/ {
        proxy_pass http://127.0.0.1:${registry_port};
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    location = /api/rooms {
        proxy_pass http://127.0.0.1:${registry_port};
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    location /s/ {
        proxy_pass http://127.0.0.1:${registry_port};
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    location = /create {
        proxy_pass http://127.0.0.1:${registry_port};
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    location / {
        try_files $uri $uri/ /index.html;
    }
}
__TANK_MAPF_NGINX_SITE__
ln -sfn /etc/nginx/sites-available/tank-mapf-web /etc/nginx/sites-enabled/tank-mapf-web
rm -f /etc/nginx/sites-enabled/default

log "Reloading systemd"
systemctl daemon-reload
systemctl enable tank-mapf-registry.service
systemctl enable tank-mapf-server.service
systemctl enable tank-mapf-server-web.service
systemctl enable nginx
systemctl restart tank-mapf-registry.service
systemctl start tank-mapf-server.service || true
systemctl start tank-mapf-server-web.service || true
nginx -t
systemctl restart nginx

log "Startup bootstrap complete"
