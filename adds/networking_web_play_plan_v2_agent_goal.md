# Tank MAPF - Web Multiplayer Plan v2 for Agent Goal

Ngày lập: 2026-06-11  
Repo: `D:\2025.2\DATN\projectY`  
GCP project: `tankmapf`  
VM hiện tại: `tank-mapf-server`  
Static IP hiện tại: `35.240.203.91`

## Setgoal đề xuất

```text
Goal: Implement browser-playable multiplayer for Tank MAPF on the existing GCP VM without destroying the current working native deployment.

Current baseline:
- Native Unity client can connect to GCP dedicated server through invite registry.
- Registry health endpoint is live at http://35.240.203.91:8080/healthz.
- Existing native server uses UDP 7777.
- Existing registry uses TCP 8080.
- Existing GCP VM/static IP/artifact bucket/Terraform should be reused.

Target:
- A player can open http://35.240.203.91/ in a browser, load the Unity WebGL game, create or join a room, and play multiplayer with another browser player on another machine/network.
- Invite links should open the WebGL game and auto-join the correct session.
- Web room creator can choose map and mode like local: 5 maps x AStar/PIBT.
- Native UDP path must not be broken. Native + web cross-play is allowed only through the WebSocket-compatible endpoint.

Authority:
- Agent may edit Unity C# scripts, registry service, Terraform, systemd templates, deploy scripts, GitHub Actions, and docs.
- Agent may run Unity builds, Terraform plan/apply, gcloud, and VM deploy commands when credentials are already available.
- Agent must not destroy/recreate the VM, delete the static IP, reset git state, commit secrets, or remove user changes.
- Agent must checkpoint after every milestone with console/log/build evidence.

Primary implementation order:
M15 -> M16 -> M17 -> M18 -> M19 -> M20 -> M21 -> M21A -> M22 -> M23.

Completion:
- Web PC + web laptop can join the same WebSocket room.
- Invite link opens browser game and joins automatically.
- At least one non-default map and PIBT mode are verified.
- Native UDP still works or is explicitly documented as temporarily not verified.
```

## Câu trả lời thực tế

Chạy được, nhưng không nên làm kiểu một lần sửa toàn bộ rồi mới test. Đi đúng gate thì xác suất thành công cao:

1. Thêm WebSocket transport trong Unity.
2. Chạy được WebSocket dedicated server.
3. Mở firewall.
4. Build WebGL.
5. Host WebGL trên VM.
6. Auto join bằng invite link.
7. Thêm Create Room để chọn map/mode.
8. Sau khi manual end-to-end pass mới làm CI/CD.

Không hủy VM. VM hiện tại là baseline đúng và cần được tái sử dụng.

## Không được phá

- Không chạy `terraform destroy`.
- Không đổi tên hoặc recreate `google_compute_address.server_static_ip`.
- Không xóa VM `tank-mapf-server`.
- Không `git reset --hard`.
- Không commit hoặc in secret như `registry_admin_token`, service account JSON, private key.
- Không sửa xóa các file unrelated nếu không cần.
- Không dùng broad cleanup trong `Assets/`, `ProjectSettings/`, `Library/`.

Nếu Terraform plan báo recreate VM hoặc recreate static IP thì dừng apply và sửa plan.

## Source of Truth hiện tại

Các file cần đọc trước khi làm:

```text
CLAUDE.md
adds/networking_web_play_plan_v1.md
Assets/Scripts/Multiplayer/LanSessionManager.cs
Assets/Scripts/Multiplayer/LanLobbyController.cs
Assets/Scripts/Multiplayer/NetworkManagerFactory.cs
Assets/Scripts/Multiplayer/NetworkLaunchArgs.cs
Assets/Scripts/Multiplayer/NetworkEndpointConfig.cs
Assets/Scripts/Multiplayer/InternetSessionClient.cs
Assets/Scripts/Multiplayer/InternetJoinParser.cs
Assets/Scripts/MapTankTestBootstrap.cs
Assets/Scripts/MenuViewBootstrap.cs
Assets/Editor/BuildServer.cs
services/invite-registry/app.py
infra/gcp/variables.tf
infra/gcp/firewall.tf
infra/gcp/outputs.tf
infra/gcp/locals.tf
infra/gcp/scripts/startup.sh
infra/gcp/scripts/deploy-release.sh
infra/gcp/systemd/*.tpl
```

Known map/mode set:

```text
Maps:
- random-32-32-10.map
- ht_mansion_n.map
- ht_chantry.map
- lt_gallowstemplar_n.map
- maze-128-128-10.map

Modes:
- AStar
- PIBT
```

## Success Criteria

Minimum success:

- `http://35.240.203.91/` loads WebGL game.
- Browser A creates or joins a room.
- Browser B on another machine opens invite link and joins same game.
- Both players are visible/synchronized enough to play.
- Room can run `random-32-32-10.map` + `AStar`.
- Native UDP deployment remains available on `35.240.203.91:7777`.

Full success:

- Web Create Room supports all 5 maps and both modes.
- At least `ht_chantry.map` + `PIBT` is verified.
- Registry returns web endpoint fields.
- Web invite link auto-joins.
- CI/CD builds and deploys server + WebGL.
- Manual rollback path is documented.

## Verification Commands

Local:

```powershell
git status --short
terraform -chdir=infra/gcp fmt
terraform -chdir=infra/gcp validate
```

GCP/VM:

```powershell
curl http://35.240.203.91:8080/healthz
curl http://35.240.203.91:8080/api/sessions/nhuhai123
gcloud compute ssh tank-mapf-server --zone asia-southeast1-b --project tankmapf --command "sudo systemctl status tank-mapf-registry.service --no-pager"
gcloud compute ssh tank-mapf-server --zone asia-southeast1-b --project tankmapf --command "sudo systemctl status tank-mapf-server.service --no-pager"
gcloud compute ssh tank-mapf-server --zone asia-southeast1-b --project tankmapf --command "sudo ss -lntup | egrep '7777|7778|8080|80|443' || true"
```

Unity:

```powershell
# Use the installed Unity path in this machine if different.
& "C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Unity.exe" `
  -batchmode `
  -quit `
  -projectPath "D:\2025.2\DATN\projectY" `
  -executeMethod BuildServer.BuildLinuxServer `
  -logFile "Logs\server-build.log"
```

WebGL local serve:

```powershell
cd D:\2025.2\DATN\projectY\Builds\WebGL
python -m http.server 9090
```

## M14 - Baseline Check

Purpose:

- Confirm current native GCP lane still works before touching web.

Steps:

1. Run `git status --short`.
2. Verify registry:

```powershell
curl http://35.240.203.91:8080/healthz
```

3. Verify current session:

```powershell
curl http://35.240.203.91:8080/api/sessions/nhuhai123
```

4. Check VM services:

```powershell
gcloud compute ssh tank-mapf-server --zone asia-southeast1-b --project tankmapf --command "sudo systemctl is-active tank-mapf-registry.service; sudo systemctl is-active tank-mapf-server.service"
```

Done when:

- Registry returns `{"ok": true}`.
- Existing server service is active.
- Worktree state is noted.

Do not proceed if:

- VM is not reachable.
- Registry is down and cannot be restarted.
- Terraform state is missing.

## M15 - Add Transport Mode to Unity

Purpose:

- Make Unity choose UDP or WebSocket explicitly.

Files:

```text
Assets/Scripts/Multiplayer/NetworkTransportMode.cs
Assets/Scripts/Multiplayer/NetworkEndpointConfig.cs
Assets/Scripts/Multiplayer/NetworkLaunchArgs.cs
Assets/Scripts/Multiplayer/LanSessionManager.cs
Assets/Scripts/Multiplayer/NetworkManagerFactory.cs
Assets/Scripts/Multiplayer/LanLobbyController.cs
Assets/Scripts/Multiplayer/InternetSessionClient.cs
```

Implementation:

1. Add enum:

```csharp
public enum NetworkTransportMode
{
    Udp,
    WebSocket
}
```

2. Add `transportMode` to `NetworkEndpointConfig`.

3. Add `--transport` and `TANK_TRANSPORT` parsing in `NetworkLaunchArgs`.

4. Store transport mode in `LanSessionManager`.

5. Change `NetworkManagerFactory.Ensure(...)` so callers can pass transport mode.

6. For `UnityTransport`:

```csharp
transport.SetConnectionData(hostOrBindAddress, port);
transport.UseWebSockets = transportMode == NetworkTransportMode.WebSocket;
```

7. Keep old overloads defaulting to UDP so existing callsites do not break.

8. For WebGL client default:

```csharp
#if UNITY_WEBGL && !UNITY_EDITOR
NetworkTransportMode.WebSocket
#else
NetworkTransportMode.Udp
#endif
```

9. Update `BeginClientConnection` in `LanLobbyController` to apply endpoint transport mode before `StartClient()`.

10. Update logs:

```text
[LAN] Connecting to 35.240.203.91:7778 via WebSocket
[DedicatedServer] Starting websocket on 0.0.0.0:7778
```

Verification:

- Unity compiles.
- Native Editor join to current UDP session still works.
- No console errors from `NetworkManagerFactory`.

Rollback:

- Revert only the transport-mode files changed in this milestone.
- Do not touch Terraform or VM.

## M16 - Add WebSocket Dedicated Server Service

Purpose:

- Run a WebSocket-compatible dedicated server process for browsers.

Recommended ports:

```text
UDP native: 7777
TCP WebSocket: 7778
Registry: 8080
HTTP WebGL: 80
```

Files:

```text
infra/gcp/variables.tf
infra/gcp/locals.tf
infra/gcp/scripts/startup.sh
infra/gcp/scripts/deploy-release.sh
infra/gcp/systemd/tank-mapf-server-web.service.tpl
```

Implementation:

1. Add variables:

```hcl
variable "web_game_port" {
  type    = number
  default = 7778
}
```

2. Add env in startup:

```text
WEB_GAME_PORT=${web_game_port}
WEB_TRANSPORT=websocket
```

3. Add systemd unit `tank-mapf-server-web.service.tpl`.

4. Its `ExecStart` should run same binary with:

```text
--server
--port ${WEB_GAME_PORT}
--transport websocket
--map ${MAP_FILE}
--algorithm ${ALGORITHM}
--sessionCode ${SESSION_CODE}
--maxPlayers ${MAX_PLAYERS}
--registryUrl http://127.0.0.1:${REGISTRY_PORT}
```

5. Update startup script to write and enable the new unit.

6. Update deploy script to restart both:

```bash
systemctl restart tank-mapf-server.service
systemctl restart tank-mapf-server-web.service
```

7. Do not replace or remove `tank-mapf-server.service`.

Verification:

```powershell
gcloud compute ssh tank-mapf-server --zone asia-southeast1-b --project tankmapf --command "sudo systemctl status tank-mapf-server-web.service --no-pager"
gcloud compute ssh tank-mapf-server --zone asia-southeast1-b --project tankmapf --command "sudo ss -lntup | egrep '7777|7778|8080'"
```

Done when:

- Native service active.
- Web service active.
- `7778` is listening on TCP.

Rollback:

```powershell
gcloud compute ssh tank-mapf-server --zone asia-southeast1-b --project tankmapf --command "sudo systemctl disable --now tank-mapf-server-web.service || true"
```

## M17 - Terraform Firewall and Outputs

Purpose:

- Open browser HTTP and WebSocket ports.

Files:

```text
infra/gcp/variables.tf
infra/gcp/firewall.tf
infra/gcp/outputs.tf
infra/gcp/terraform.tfvars.example
```

Implementation:

1. Add:

```hcl
variable "web_http_port" {
  type    = number
  default = 80
}

variable "allowed_web_http_sources" {
  type    = list(string)
  default = ["0.0.0.0/0"]
}

variable "allowed_web_game_sources" {
  type    = list(string)
  default = ["0.0.0.0/0"]
}
```

2. Add firewall:

```hcl
resource "google_compute_firewall" "allow_web_game_tcp" {
  name    = "${local.name_prefix}-allow-web-game-tcp"
  network = google_compute_network.tank_mapf.name

  target_tags   = [local.instance_tag]
  source_ranges = var.allowed_web_game_sources

  allow {
    protocol = "tcp"
    ports    = [tostring(var.web_game_port)]
  }
}

resource "google_compute_firewall" "allow_web_http_tcp" {
  name    = "${local.name_prefix}-allow-web-http-tcp"
  network = google_compute_network.tank_mapf.name

  target_tags   = [local.instance_tag]
  source_ranges = var.allowed_web_http_sources

  allow {
    protocol = "tcp"
    ports    = [tostring(var.web_http_port)]
  }
}
```

3. Add outputs:

```hcl
output "web_url" {
  value = "http://${google_compute_address.server_static_ip.address}"
}

output "web_game_endpoint" {
  value = "${google_compute_address.server_static_ip.address}:${var.web_game_port}"
}
```

Commands:

```powershell
cd D:\2025.2\DATN\projectY\infra\gcp
$env:TF_VAR_project_id="tankmapf"
$env:TF_VAR_region="asia-southeast1"
$env:TF_VAR_zone="asia-southeast1-b"
$env:TF_VAR_server_session_code="NHUHAI123"
$env:TF_VAR_registry_admin_token="<do-not-commit>"
terraform fmt
terraform validate
terraform plan
```

Apply only if plan does not recreate VM/static IP:

```powershell
terraform apply
terraform output
```

Done when:

- Outputs include `web_url` and `web_game_endpoint`.
- VM IP remains `35.240.203.91`.

## M18 - Add WebGL Build Script

Purpose:

- Produce a WebGL client build under `Builds/WebGL`.

Files:

```text
Assets/Editor/BuildWebClient.cs
Assets/Editor/BuildWebClient.cs.meta
```

Implementation:

1. Create `BuildWebClient` with menu item:

```text
Tools/Tank MAPF/Build WebGL Client
```

2. Use scenes:

```text
Assets/Scenes/Menu.unity
Assets/Scenes/MapF_TankTest.unity
Assets/Scenes/MapF_TankTest_PIBT.unity
```

3. Build target:

```csharp
BuildTarget.WebGL
```

4. Output:

```text
Builds/WebGL
```

Command:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Unity.exe" `
  -batchmode `
  -quit `
  -projectPath "D:\2025.2\DATN\projectY" `
  -executeMethod BuildWebClient.BuildWebGL `
  -logFile "Logs\webgl-build.log"
```

Done when:

- `Builds/WebGL/index.html` exists.
- `Logs/webgl-build.log` has no build failure.
- Local static server can load index page.

## M19 - Host WebGL on Existing VM

Purpose:

- Make `http://35.240.203.91/` serve the WebGL build.

Recommended MVP:

- Use Nginx on same VM.
- Serve static files from `/var/www/tank-mapf-web/current`.

Files:

```text
infra/gcp/scripts/startup.sh
infra/gcp/scripts/deploy-web.sh
infra/gcp/locals.tf
```

Implementation:

1. Install Nginx in startup:

```bash
apt-get install -y nginx
```

2. Create dirs:

```text
/var/www/tank-mapf-web/releases
/var/www/tank-mapf-web/current
```

3. Add Nginx config:

```nginx
server {
    listen 80 default_server;
    server_name _;

    root /var/www/tank-mapf-web/current;
    index index.html;

    location / {
        try_files $uri $uri/ /index.html;
    }

    location /registry/ {
        proxy_pass http://127.0.0.1:8080/;
    }
}
```

4. Add `deploy-web.sh`.

Required env:

```text
WEB_ARTIFACT_URI
WEB_RELEASE_SHA
```

Deploy script steps:

- Download tar.gz from GCS.
- Extract to `/var/www/tank-mapf-web/releases/${WEB_RELEASE_SHA}`.
- Symlink `/var/www/tank-mapf-web/current`.
- Run `nginx -t`.
- Reload Nginx.

Verification:

```powershell
curl http://35.240.203.91/
```

Done when:

- Response contains WebGL `index.html`.
- Browser loads without missing `.wasm`, `.data`, `.js`.

## M20 - Extend Registry for Web Endpoint

Purpose:

- Session API must tell WebGL which WebSocket endpoint to use.

Files:

```text
services/invite-registry/app.py
Assets/Scripts/Multiplayer/InternetSessionClient.cs
```

Session response should include:

```json
{
  "code": "NHUHAI123",
  "host": "35.240.203.91",
  "gamePort": 7777,
  "transport": "udp",
  "webHost": "35.240.203.91",
  "webGamePort": 7778,
  "webTransport": "websocket",
  "webUrl": "http://35.240.203.91/play?session=NHUHAI123",
  "map": "random-32-32-10.map",
  "algorithm": "AStar",
  "maxPlayers": 8
}
```

Rules:

- Keep `host` and `gamePort` for backwards compatibility.
- WebGL client prefers `webHost`, `webGamePort`, `webTransport`.
- Native client can keep UDP unless user chooses web-compatible join.

Verification:

```powershell
curl http://35.240.203.91:8080/api/sessions/nhuhai123
```

Done when:

- JSON includes web fields.
- Existing native parser still works.

## M21 - WebGL Auto Join from URL

Purpose:

- Invite link opens game and joins session automatically.

Supported URLs:

```text
http://35.240.203.91/?session=nhuhai123
http://35.240.203.91/play?session=nhuhai123
http://35.240.203.91/s/nhuhai123
```

Implementation:

1. Add WebGL URL query reader.
2. If URL has `session`, resolve registry.
3. Use web endpoint fields.
4. Set transport mode WebSocket.
5. Start client.
6. If no session, show normal menu/create-room UI.

Done when:

- Browser opens invite link and connects without manual input.
- Browser console/Unity logs show WebSocket endpoint.

## M21A - Web Create Room for Map and Mode

Purpose:

- Player can choose map/mode on web like local.

MVP constraint:

- One active WebSocket room at a time on the VM.
- Creating a room restarts `tank-mapf-server-web.service`.
- Native UDP service remains untouched.

Registry/server-manager endpoint:

```text
POST /api/web-rooms
```

Request:

```json
{
  "map": "ht_chantry.map",
  "algorithm": "PIBT",
  "maxPlayers": 8
}
```

Response:

```json
{
  "code": "AB12CD",
  "joinUrl": "http://35.240.203.91/play?session=AB12CD",
  "map": "ht_chantry.map",
  "algorithm": "PIBT",
  "webHost": "35.240.203.91",
  "webGamePort": 7778,
  "webTransport": "websocket"
}
```

Security:

- Whitelist map names.
- Whitelist algorithms: `AStar`, `PIBT`.
- Do not allow arbitrary shell args from request body.
- If endpoint is public, protect room creation with a simple create token or rate limit.

Implementation option A, fastest:

- Registry app runs as service user.
- Registry writes a root-readable request file.
- A separate root-owned small server-manager script validates and restarts service via a tightly scoped sudoers rule.

Implementation option B, simpler for thesis demo but less clean:

- Keep room creation manual through deploy script/env for now.
- Web menu shows map/mode, but room creation calls a protected admin endpoint only available to you.

Recommended for agent:

- Implement option B first if time is limited.
- Only implement public room creation after web-to-web join already works.

Done when:

- Create room `ht_chantry.map` + `PIBT`.
- Invite link joins that map/mode on PC browser and laptop browser.
- Invalid map returns 400.
- Invalid algorithm returns 400.

## M22 - Manual End-to-End Deploy

Purpose:

- Prove the whole flow before CI/CD.

Steps:

1. Build Linux dedicated server.
2. Tar server artifact.
3. Upload to GCS.
4. Deploy server artifact.
5. Verify native service and web service.
6. Build WebGL.
7. Tar WebGL artifact.
8. Upload WebGL artifact.
9. Deploy WebGL artifact.
10. Test browser-to-browser multiplayer.

Server artifact example:

```powershell
$sha = git rev-parse --short HEAD
tar -czf "Builds\tank-mapf-server-$sha.tar.gz" -C "Builds\LinuxServer" .
gcloud storage cp "Builds\tank-mapf-server-$sha.tar.gz" "gs://tankmapf-tank-mapf-artifacts/server/$sha/tank-mapf-server-$sha.tar.gz"
```

Web artifact example:

```powershell
$sha = git rev-parse --short HEAD
tar -czf "Builds\tank-mapf-web-$sha.tar.gz" -C "Builds\WebGL" .
gcloud storage cp "Builds\tank-mapf-web-$sha.tar.gz" "gs://tankmapf-tank-mapf-artifacts/web/$sha/tank-mapf-web-$sha.tar.gz"
```

Smoke tests:

```powershell
curl http://35.240.203.91/
curl http://35.240.203.91:8080/healthz
curl http://35.240.203.91:8080/api/sessions/nhuhai123
```

Done when:

- PC browser joins.
- Laptop browser joins same room.
- Movement/gameplay sync is acceptable.
- `AStar` and at least one `PIBT` room are verified.

## M23 - GitHub Actions CI/CD

Purpose:

- Automate build and deploy only after manual deploy works.

Workflow:

```text
.github/workflows/deploy-gcp.yml
```

Jobs:

1. `build-server`
2. `build-webgl`
3. `terraform-check`
4. `deploy-server`
5. `deploy-web`
6. `smoke-test`

Required GitHub variables/secrets:

```text
GCP_PROJECT_ID=tankmapf
GCP_PROJECT_NUMBER=112059749535
GCP_REGION=asia-southeast1
GCP_ZONE=asia-southeast1-b
GCP_ARTIFACT_BUCKET=tankmapf-tank-mapf-artifacts
SERVER_SESSION_CODE=NHUHAI123
REGISTRY_ADMIN_TOKEN=<secret>
```

Authentication recommendation:

- Prefer Workload Identity Federation.
- If using service account JSON temporarily, store only in GitHub Secrets and never commit.

Done when:

- GitHub Actions can build and upload artifacts.
- Deploy updates VM.
- Smoke tests pass.

## M24 - Domain, HTTPS, and WSS

Purpose:

- Production-grade browser security.

This is optional for IP-only MVP.

Recommended routing:

```text
https://tankmapf.example.com/        -> WebGL static
https://tankmapf.example.com/api/... -> registry localhost:8080
wss://tankmapf.example.com/ws        -> WebSocket server localhost:7778
udp://tankmapf.example.com:7777      -> native UDP
```

Do this after web MVP works on HTTP/IP.

Done when:

- HTTPS WebGL loads.
- WebGL connects over WSS.
- Browser DevTools has no mixed content error.

## Agent Stop Conditions

Stop and ask user only if:

- GCP auth is missing and cannot be recovered by local `gcloud auth list`.
- Unity license/build cannot run in batch mode.
- Terraform state is missing or plan wants to recreate VM/static IP.
- A secret is required and not already present in environment/GitHub Secrets/local ignored tfvars.
- WebSocket transport API differs from current package and cannot compile after reasonable local inspection.

Do not stop for:

- Need to create files.
- Need to run Terraform plan.
- Need to run Unity build.
- Need to restart VM services.
- Need to adjust firewall, as long as no VM/static IP recreation is planned.

## Expected Final Report Format

When done, report:

```text
Implemented:
- ...

Verified:
- ...

URLs:
- Web: http://35.240.203.91/
- Registry: http://35.240.203.91:8080/healthz
- Example invite: ...

Not verified / remaining:
- ...

Files changed:
- ...
```

Keep the report short and include exact commands or logs only where they prove a gate.
