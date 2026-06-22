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
${create_room_sh}
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
WEB_HOST=${public_ip}
WEB_GAME_PORT_PUBLIC=${web_game_port}
WEB_TRANSPORT=websocket
REGISTRY_INTERNAL_URL=http://127.0.0.1:${registry_port}
MAP_FILE=${map_file}
ALGORITHM=${algorithm}
MAX_PLAYERS=${max_players}
ENEMY_MULTIPLIER=3
TANK_ENEMY_MULTIPLIER=3
SESSION_CODE=${session_code}
REGISTRY_ADMIN_TOKEN=${registry_admin_token}
REGISTRY_DATA_FILE=${registry_data_file}
__TANK_MAPF_SERVER_ENV__

if [ ! -f "${runtime_env_file}" ]; then
  write_file "${runtime_env_file}" "root:root" 0640 <<'__TANK_MAPF_RUNTIME_ENV__'
RELEASE_SHA=
ENEMY_MULTIPLIER=3
TANK_ENEMY_MULTIPLIER=3
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
