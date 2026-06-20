#!/usr/bin/env bash
set -euo pipefail

log() {
  echo "[tank-mapf-web-deploy] $*"
}

require_env() {
  local name="$1"
  if [ -z "${!name:-}" ]; then
    echo "Missing required environment variable: $name" >&2
    exit 1
  fi
}

WEB_ROOT=/var/www/tank-mapf-web
RELEASES_ROOT="${WEB_ROOT}/releases"
CURRENT_ROOT="${WEB_ROOT}/current"
SERVICE_USER=www-data

require_env WEB_ARTIFACT_URI

WEB_RELEASE_SHA="${WEB_RELEASE_SHA:-manual-$(date +%Y%m%d%H%M%S)}"
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

ARCHIVE_PATH="${TMP_DIR}/web-release.tar.gz"
RELEASE_DIR="${RELEASES_ROOT}/${WEB_RELEASE_SHA}"

log "Downloading web artifact ${WEB_ARTIFACT_URI}"
gcloud storage cp "$WEB_ARTIFACT_URI" "$ARCHIVE_PATH"

log "Expanding web artifact into ${RELEASE_DIR}"
rm -rf "$RELEASE_DIR"
install -d -o "$SERVICE_USER" -g "$SERVICE_USER" "$RELEASE_DIR"
tar -xzf "$ARCHIVE_PATH" -C "$RELEASE_DIR"
chown -R "$SERVICE_USER:$SERVICE_USER" "$RELEASE_DIR"

log "Switching current web release"
if [ -d "$CURRENT_ROOT" ] && [ ! -L "$CURRENT_ROOT" ]; then
  rm -rf "$CURRENT_ROOT"
fi
ln -sfn "$RELEASE_DIR" "$CURRENT_ROOT"
chown -h "$SERVICE_USER:$SERVICE_USER" "$CURRENT_ROOT"

log "Validating nginx configuration"
nginx -t
systemctl enable nginx
systemctl reload nginx

log "Web deploy complete"
