# Tank MAPF - Web Play Plan v1

Ngày lập: 2026-06-11  
Trạng thái nền: sau M13, native Unity client đã kết nối được tới dedicated server GCP qua invite registry.

## Kết luận hạ tầng

Không hủy VM hiện tại.

VM `tank-mapf-server` đang có đủ các phần có giá trị để tái sử dụng:

- Static IP: `35.240.203.91`
- Registry HTTP đang chạy: `http://35.240.203.91:8080`
- Dedicated server native UDP đang chạy: `35.240.203.91:7777`
- Artifact bucket, startup script, deploy script, systemd service và firewall Terraform đã có
- Session invite hiện tại đã resolve được và native client đã connect thành công

Việc chơi trực tiếp trên web không thay thế toàn bộ hạ tầng này. Web chỉ cần thêm một lane mới:

- WebGL client được host bằng HTTP trên VM hoặc GCS
- Dedicated server WebSocket dùng port riêng, ví dụ `7778`
- Registry trả thêm endpoint web để WebGL client auto join

## Vì sao không thể chỉ mở IP là chơi ngay

Build Linux server hiện tại đang phục vụ native Unity client qua Unity Transport UDP port `7777`. Trình duyệt/WebGL không dùng UDP trực tiếp theo kiểu native client. Vì vậy nếu chỉ tạo trang HTML chứa WebGL nhưng server vẫn chỉ nghe UDP thì browser sẽ load game nhưng không join được multiplayer.

Repo hiện tại đã có package phù hợp:

- `Packages/manifest.json` dùng `com.unity.netcode.gameobjects` `2.1.1`
- `Library/PackageCache/.../UnityTransport.cs` có `UseWebSockets`
- `UnityTransport` hiện đang được tạo trong `Assets/Scripts/Multiplayer/NetworkManagerFactory.cs`

Do đó hướng đúng là mở rộng code hiện tại để cấu hình transport mode:

- Native/Desktop: UDP `35.240.203.91:7777`
- WebGL/Browser: WebSocket `ws://35.240.203.91:7778` cho MVP
- Production sau này: `https://domain` + `wss://domain/ws` hoặc `wss://domain:7778`

## Mục tiêu UX

Mục tiêu MVP:

- Người chơi mở `http://35.240.203.91/` thấy giao diện game WebGL.
- Link mời dạng `http://35.240.203.91/play?session=nhuhai123` hoặc `http://35.240.203.91/s/nhuhai123` có thể tự resolve session.
- Browser client tự connect tới WebSocket server.
- Native Unity client vẫn tiếp tục dùng link hiện tại qua registry và UDP `7777`.

Mục tiêu chơi chung cần hiểu chính xác:

- Web PC + Web laptop: chơi chung được nếu cả hai cùng join WebSocket server `35.240.203.91:7778` với cùng session.
- Native PC + Native laptop: chơi chung được như hiện tại nếu cả hai cùng join UDP server `35.240.203.91:7777`.
- Native PC qua UDP `7777` + Web laptop qua WebSocket `7778`: không chơi chung cùng một trận trong MVP nếu đó là 2 server process khác nhau.
- Native PC + Web laptop muốn chơi chung: native client phải có tùy chọn join bằng WebSocket và cùng kết nối vào WebSocket server `35.240.203.91:7778`.

Mục tiêu chọn map/chế độ:

- Web vẫn có thể dùng lại menu chọn map/chế độ giống local, nhưng lựa chọn đó phải tạo hoặc cập nhật session trên server.
- Nếu chỉ dùng link cố định hiện tại, session sẽ chỉ chạy map/chế độ đã được server start bằng env/launch args, ví dụ `random-32-32-10.map` + `AStar`.
- Để người chơi chọn như local, web cần flow `Create Room`: chọn map, chọn mode, server WebSocket start/restart với đúng map/mode, registry trả invite link cho room đó.
- Giai đoạn MVP nên hỗ trợ 1 web room active tại một thời điểm trên VM. Nhiều room cùng lúc cần milestone riêng để spawn nhiều server process/port.

Mục tiêu production:

- Dùng domain riêng thay vì IP.
- Dùng HTTPS/WSS để tránh mixed content và browser security issue.
- CI/CD build và deploy cả Linux dedicated server lẫn WebGL client.

## Kiến trúc đề xuất

```text
Internet players
  |
  | HTTP 80 for MVP, HTTPS 443 later
  v
GCP VM 35.240.203.91
  |
  |-- Caddy/Nginx/static server
  |     serves WebGL build at /
  |
  |-- invite registry :8080
  |     /healthz
  |     /api/sessions/{code}
  |     /s/{code}
  |
  |-- native dedicated server
  |     UDP :7777
  |
  |-- web dedicated server
        WebSocket TCP :7778
```

Không nên cố gắng cho cùng một process vừa nghe UDP `7777` vừa nghe WebSocket `7778` ở giai đoạn đầu. Cách ít rủi ro hơn là chạy 2 systemd service từ cùng artifact server:

- `tank-mapf-server.service`: native UDP, giữ nguyên để không phá luồng đã chạy.
- `tank-mapf-server-web.service`: WebSocket, port `7778`, phục vụ WebGL.

Hai service có thể dùng cùng map/algorithm/session code trong MVP. Nếu sau này cần native và web vào đúng cùng một trận, cần thiết kế shared session/game instance kỹ hơn. Giai đoạn đầu nên ưu tiên web chạy được end-to-end.

Quyết định cross-play cho plan này:

- Luồng không rủi ro: giữ UDP `7777` cho native client hiện tại, thêm WebSocket `7778` cho browser.
- Luồng chơi chung native + web: coi WebSocket `7778` là endpoint multiplayer chung cho cả WebGL và native client khi người chơi chọn chế độ online web-compatible.
- Không dùng đồng thời UDP `7777` và WebSocket `7778` cho cùng một trận, trừ khi sau này có kiến trúc server hỗ trợ multi-transport trong cùng game instance.

## Biến cấu hình mới

Đề xuất thêm các biến ở local, Terraform và deploy script:

```text
GAME_PORT=7777
WEB_GAME_PORT=7778
REGISTRY_PORT=8080
WEB_HTTP_PORT=80
TRANSPORT_MODE=udp
WEB_TRANSPORT_MODE=websocket
WEB_PUBLIC_BASE_URL=http://35.240.203.91
WEB_SOCKET_PUBLIC_HOST=35.240.203.91
WEB_SOCKET_PUBLIC_PORT=7778
```

Quy ước transport:

```text
udp
websocket
```

Không dùng nhiều tên như `ws`, `web`, `browser` trong code core. Parser có thể nhận alias, nhưng internal enum/string nên chỉ giữ `udp` và `websocket`.

## Milestone M14 - Chốt thiết kế Web lane

Mục tiêu:

- Ghi rõ native UDP lane và web WebSocket lane.
- Không đụng VM destruction.
- Xác nhận file nào cần sửa trước khi code.

File cần đọc:

- `Assets/Scripts/Multiplayer/NetworkManagerFactory.cs`
- `Assets/Scripts/Multiplayer/DedicatedServerBootstrap.cs`
- `Assets/Scripts/Multiplayer/NetworkLaunchArgs.cs`
- `Assets/Scripts/Multiplayer/NetworkEndpointConfig.cs`
- `Assets/Scripts/Multiplayer/InternetSessionClient.cs`
- `Assets/Scripts/Multiplayer/InternetJoinParser.cs`
- `Assets/Editor/BuildServer.cs`
- `services/invite-registry/app.py`
- `infra/gcp/variables.tf`
- `infra/gcp/firewall.tf`
- `infra/gcp/scripts/deploy-release.sh`
- `infra/gcp/scripts/startup.sh`

Definition of done:

- Plan này đã được commit hoặc ít nhất được lưu trong `adds/`.
- Không có thay đổi Terraform apply phá VM hiện tại.
- Native path vẫn được coi là baseline cần giữ.

## Milestone M15 - Thêm transport mode trong Unity runtime

Mục tiêu:

- Unity code biết server/client đang chạy UDP hay WebSocket.
- `NetworkManagerFactory` cấu hình `UnityTransport.UseWebSockets` đúng theo mode.

Các bước implement:

1. Tạo enum hoặc static helper mới, ví dụ `NetworkTransportMode`.

```csharp
public enum NetworkTransportMode
{
    Udp,
    WebSocket
}
```

2. Mở rộng `NetworkEndpointConfig` thêm field:

```csharp
public NetworkTransportMode transportMode;
```

3. Mở rộng `NetworkLaunchArgs` nhận:

```text
--transport udp
--transport websocket
```

4. Mở rộng `LanSessionManager` hoặc state hiện tại để lưu transport mode active.

5. Sửa `NetworkManagerFactory.Ensure(...)`:

```csharp
public static bool Ensure(
    string bindAddress,
    ushort port,
    bool isServer,
    NetworkTransportMode transportMode,
    out NetworkManager networkManager)
```

6. Sau khi lấy/tạo `UnityTransport`, set:

```csharp
transport.SetConnectionData(bindAddress, port);
transport.UseWebSockets = transportMode == NetworkTransportMode.WebSocket;
```

7. Giữ overload cũ nếu nhiều callsite còn dùng:

```csharp
public static bool Ensure(string bindAddress, ushort port, bool isServer, out NetworkManager networkManager)
{
    return Ensure(bindAddress, port, isServer, NetworkTransportMode.Udp, out networkManager);
}
```

8. Với client WebGL, default transport nên là WebSocket:

```csharp
#if UNITY_WEBGL && !UNITY_EDITOR
    defaultTransport = NetworkTransportMode.WebSocket;
#else
    defaultTransport = NetworkTransportMode.Udp;
#endif
```

Definition of done:

- Native Unity Editor client vẫn connect được UDP như hiện tại.
- WebGL build path có thể chọn WebSocket mà không phá native.
- Native desktop client có thể chọn WebSocket endpoint nếu cần chơi chung với browser.
- Console không có compile error.

## Milestone M16 - Thêm WebSocket dedicated server service

Mục tiêu:

- Cùng VM chạy thêm server process riêng cho WebGL.
- Native server UDP vẫn giữ nguyên.

Các bước implement local:

1. Sửa `DedicatedServerBootstrap` để đọc transport mode từ launch args.
2. Log rõ mode khi server start:

```text
[DedicatedServer] Starting websocket on 0.0.0.0:7778
```

3. Thêm systemd template mới:

```text
infra/gcp/systemd/tank-mapf-server-web.service.tpl
```

Nội dung dự kiến:

```ini
[Unit]
Description=Tank MAPF WebSocket Dedicated Server
After=network-online.target tank-mapf-registry.service
Wants=network-online.target

[Service]
User=tankmapf
Group=tankmapf
WorkingDirectory=/opt/tank-mapf/current
EnvironmentFile=/etc/tank-mapf/server.env
EnvironmentFile=-/etc/tank-mapf/runtime.env
ExecStart=/opt/tank-mapf/current/TankMapfServer.x86_64 --server --port ${WEB_GAME_PORT} --transport websocket --map ${MAP_FILE} --algorithm ${ALGORITHM} --sessionCode ${SESSION_CODE} --maxPlayers ${MAX_PLAYERS} --registryUrl http://127.0.0.1:${REGISTRY_PORT}
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
```

4. Sửa startup script để ghi unit mới và enable service mới.
5. Sửa deploy script để restart cả hai service khi deploy artifact mới.

Definition of done:

- `systemctl status tank-mapf-server.service` vẫn active.
- `systemctl status tank-mapf-server-web.service` active sau deploy.
- VM nghe UDP `7777`, TCP `7778`, TCP `8080`.

Lệnh kiểm tra sau deploy:

```powershell
gcloud compute ssh tank-mapf-server --zone asia-southeast1-b --project tankmapf --command "sudo ss -lntup | egrep '7777|7778|8080'"
gcloud compute ssh tank-mapf-server --zone asia-southeast1-b --project tankmapf --command "sudo journalctl -u tank-mapf-server-web.service -n 80 --no-pager"
```

## Milestone M17 - Mở firewall Terraform cho web

Mục tiêu:

- Mở HTTP cho WebGL page.
- Mở WebSocket TCP port cho WebGL multiplayer.
- Giữ UDP port native.

Terraform variables thêm:

```hcl
variable "web_game_port" {
  type    = number
  default = 7778
}

variable "web_http_port" {
  type    = number
  default = 80
}

variable "allowed_web_game_sources" {
  type    = list(string)
  default = ["0.0.0.0/0"]
}

variable "allowed_web_http_sources" {
  type    = list(string)
  default = ["0.0.0.0/0"]
}
```

Firewall resource thêm:

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

Output thêm:

```hcl
output "web_url" {
  value = "http://${google_compute_address.server_static_ip.address}"
}

output "web_game_endpoint" {
  value = "${google_compute_address.server_static_ip.address}:${var.web_game_port}"
}
```

Lệnh chạy:

```powershell
cd D:\2025.2\DATN\projectY\infra\gcp
$env:TF_VAR_project_id="tankmapf"
$env:TF_VAR_region="asia-southeast1"
$env:TF_VAR_zone="asia-southeast1-b"
$env:TF_VAR_server_session_code="NHUHAI123"
$env:TF_VAR_registry_admin_token="nhuhai123456789"
terraform fmt
terraform validate
terraform plan
terraform apply
terraform output
```

Definition of done:

- `terraform apply` không recreate VM.
- Output vẫn giữ static IP `35.240.203.91`.
- Có output `web_url` và `web_game_endpoint`.

## Milestone M18 - Build WebGL client local

Mục tiêu:

- Có build script WebGL giống `BuildServer.cs`.
- Output vào `Builds/WebGL`.

File mới:

```text
Assets/Editor/BuildWebClient.cs
Assets/Editor/BuildWebClient.cs.meta
```

Build scenes dùng giống server:

```csharp
private static readonly string[] BuildScenes =
{
    "Assets/Scenes/Menu.unity",
    "Assets/Scenes/MapF_TankTest.unity",
    "Assets/Scenes/MapF_TankTest_PIBT.unity",
};
```

Build method:

```csharp
[MenuItem("Tools/Tank MAPF/Build WebGL Client")]
public static void BuildWebGL()
{
    var options = new BuildPlayerOptions
    {
        scenes = BuildScenes,
        locationPathName = "Builds/WebGL",
        target = BuildTarget.WebGL,
        options = BuildOptions.StrictMode,
    };
    BuildPipeline.BuildPlayer(options);
}
```

Batch command dự kiến:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Unity.exe" `
  -batchmode `
  -quit `
  -projectPath "D:\2025.2\DATN\projectY" `
  -executeMethod BuildWebClient.BuildWebGL `
  -logFile "Logs\webgl-build.log"
```

Definition of done:

- `Builds/WebGL/index.html` tồn tại.
- Build log không có error.
- Mở local qua static server được, không mở trực tiếp file HTML.

Local serve test:

```powershell
cd D:\2025.2\DATN\projectY\Builds\WebGL
python -m http.server 9090
```

Mở:

```text
http://127.0.0.1:9090/
```

## Milestone M19 - Host WebGL trên VM

Mục tiêu:

- Truy cập `http://35.240.203.91/` thấy game WebGL.

Hướng MVP đề xuất: dùng Nginx trên VM.

Startup script cần cài:

```bash
apt-get install -y nginx
```

Thư mục deploy:

```text
/var/www/tank-mapf-web/current
```

Nginx site:

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

Deploy script mới:

```text
infra/gcp/scripts/deploy-web.sh
```

Input:

```text
WEB_ARTIFACT_URI=gs://tankmapf-tank-mapf-artifacts/web/<sha>/tank-mapf-web-<sha>.tar.gz
WEB_RELEASE_SHA=<sha>
```

Các bước:

1. Download tar.gz từ GCS.
2. Extract vào `/var/www/tank-mapf-web/releases/<sha>`.
3. Symlink `/var/www/tank-mapf-web/current`.
4. Reload Nginx.

Definition of done:

- `curl http://35.240.203.91/` trả HTML của Unity WebGL.
- Browser load được page.
- DevTools Network không báo thiếu `.wasm`, `.data`, `.js`.

## Milestone M20 - Registry trả web endpoint

Mục tiêu:

- Session API cung cấp đủ dữ liệu cho cả native và web.

Payload hiện tại:

```json
{
  "code": "NHUHAI123",
  "host": "35.240.203.91",
  "gamePort": 7777,
  "map": "random-32-32-10.map",
  "algorithm": "AStar",
  "maxPlayers": 8
}
```

Payload mới đề xuất:

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
  "maxPlayers": 8,
  "expiresAt": 1781111111
}
```

Backward compatibility:

- Native client vẫn đọc `host` và `gamePort`.
- WebGL client ưu tiên `webHost`, `webGamePort`, `webTransport`.
- Nếu chưa có field web, WebGL client báo lỗi rõ: `Web endpoint not available for this session`.

Registry page `/s/{code}` cho MVP:

- Có thể redirect tới `/play?session={code}` khi WebGL đã host xong.
- Hoặc giữ HTML đơn giản nhưng thêm nút `Play in browser`.

Definition of done:

- `curl http://35.240.203.91:8080/api/sessions/nhuhai123` có field web.
- Native client cũ không bị hỏng.
- WebGL client có thể resolve session từ code/link.

## Milestone M21 - WebGL auto join từ URL

Mục tiêu:

- Browser mở link là vào flow join online, không cần copy link thủ công.

URL support:

```text
http://35.240.203.91/?session=nhuhai123
http://35.240.203.91/play?session=nhuhai123
http://35.240.203.91/s/nhuhai123
```

Các bước implement:

1. Tạo helper đọc query string cho WebGL, ví dụ `WebJoinBootstrap`.
2. Nếu có `session` query:
   - Resolve session bằng registry.
   - Set endpoint WebSocket.
   - Gọi flow join hiện tại.
3. Nếu không có query:
   - Hiện menu hiện tại, cho nhập link/session như native client.

Pseudo flow:

```text
Awake
  if UNITY_WEBGL && URL has session:
    registryUrl = "http://35.240.203.91:8080"
    GET /api/sessions/{code}
    endpoint = webHost:webGamePort
    transport = websocket
    start client
```

Lưu ý:

- Nếu page chạy qua `http://35.240.203.91`, registry HTTP và WebSocket `ws://` cùng scheme nên MVP không bị mixed content.
- Khi chuyển sang HTTPS, registry phải qua HTTPS và multiplayer phải qua WSS.

Definition of done:

- Mở URL có session thì WebGL tự connect.
- Console trong browser log rõ endpoint và transport.
- Unity console/build log không có exception.

## Milestone M21A - Chọn map/chế độ trên web và tạo room

Mục tiêu:

- Web player chọn được map và mode như menu local.
- Server WebSocket thật sự chạy đúng map/mode đã chọn.
- Invite link gửi cho laptop/bạn bè dẫn vào đúng room đó.

Map/mode hiện có trong local menu:

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

Ràng buộc kỹ thuật:

- `MapLoader` lấy map qua `PlayerPrefs["SelectedMapFile"]`.
- `LanSessionManager.GameScene` chọn `MapF_TankTest_PIBT` khi algorithm là `PIBT`, còn lại dùng `MapF_TankTest`.
- Dedicated server hiện lấy map/mode từ `--map` và `--algorithm`.
- Client không được tự quyết map/mode khác server; server session là nguồn sự thật.

Flow MVP đề xuất:

```text
Web home page
  -> chọn map
  -> chọn mode AStar/PIBT
  -> bấm Create Room
  -> registry/server-manager start hoặc restart tank-mapf-server-web với --map và --algorithm tương ứng
  -> registry publish session
  -> trả invite link /play?session=<code>
  -> PC/laptop mở link và cùng join WebSocket room
```

Implementation scope MVP:

1. Thêm whitelist map/mode trong registry hoặc server-manager, không nhận tên file tùy ý từ browser.
2. Thêm endpoint tạo web room, ví dụ:

```text
POST /api/web-rooms
{
  "map": "ht_chantry.map",
  "algorithm": "PIBT",
  "maxPlayers": 8
}
```

3. Endpoint trả:

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

4. Với MVP 1 room active:
   - ghi runtime env web: `WEB_MAP_FILE`, `WEB_ALGORITHM`, `WEB_SESSION_CODE`;
   - restart `tank-mapf-server-web.service`;
   - publish session mới vào registry.

5. UI WebGL:
   - nếu không có `session` trong URL, hiển thị menu chọn map/mode như local;
   - nếu có `session`, bỏ qua chọn map/mode và join thẳng room từ registry.

6. Native client nếu muốn chơi chung với web:
   - chọn join bằng invite link web;
   - dùng WebSocket transport;
   - lấy map/mode từ registry, không lấy từ lựa chọn local.

Definition of done:

- Chọn `ht_chantry.map` + `PIBT` trên web tạo được room mới.
- Laptop mở invite link vào đúng map/mode đã chọn.
- PC mở cùng invite link trên web vào cùng trận.
- Native client join WebSocket-compatible link vào cùng trận nếu đã implement native WebSocket mode.
- Registry từ chối map/mode không nằm trong whitelist.

## Milestone M22 - Manual deploy web end-to-end

Mục tiêu:

- Chưa cần GitHub Actions, làm deploy thủ công trước để khoanh lỗi.

Các bước:

1. Build Linux server mới có WebSocket support.
2. Upload server artifact lên GCS.
3. Deploy server artifact lên VM.
4. Build WebGL client.
5. Tar WebGL output.
6. Upload web artifact lên GCS.
7. Deploy web artifact lên VM.
8. Test browser từ máy ngoài mạng.

Lệnh mẫu upload:

```powershell
$sha = git rev-parse --short HEAD
tar -czf "Builds\tank-mapf-web-$sha.tar.gz" -C "Builds\WebGL" .
gcloud storage cp "Builds\tank-mapf-web-$sha.tar.gz" "gs://tankmapf-tank-mapf-artifacts/web/$sha/tank-mapf-web-$sha.tar.gz"
```

Lệnh deploy web:

```powershell
gcloud compute ssh tank-mapf-server `
  --zone asia-southeast1-b `
  --project tankmapf `
  --command "sudo WEB_ARTIFACT_URI=gs://tankmapf-tank-mapf-artifacts/web/$sha/tank-mapf-web-$sha.tar.gz WEB_RELEASE_SHA=$sha /opt/tank-mapf/scripts/deploy-web.sh"
```

Checklist test:

```powershell
curl http://35.240.203.91/
curl http://35.240.203.91:8080/healthz
curl http://35.240.203.91:8080/api/sessions/nhuhai123
```

Browser test:

```text
http://35.240.203.91/
http://35.240.203.91/?session=nhuhai123
```

Definition of done:

- Web page load hoàn chỉnh.
- WebGL client connect được tới `35.240.203.91:7778`.
- Native client vẫn connect được tới `35.240.203.91:7777`.

## Milestone M23 - GitHub Actions CI/CD cho server và web

Mục tiêu:

- Push lên branch/deploy tag thì tự build artifact.
- Deploy lên GCP bằng Workload Identity hoặc service account secret.

Workflow đề xuất:

```text
.github/workflows/deploy-gcp.yml
```

Jobs:

1. `build-server`
   - Checkout
   - Cache Library nếu ổn định
   - Unity build Linux dedicated server
   - Tar artifact
   - Upload GCS

2. `build-webgl`
   - Checkout
   - Unity build WebGL
   - Tar artifact
   - Upload GCS

3. `terraform-plan`
   - `terraform fmt -check`
   - `terraform validate`
   - `terraform plan`

4. `deploy`
   - SSH/GCloud command vào VM
   - Run `/opt/tank-mapf/scripts/deploy-release.sh`
   - Run `/opt/tank-mapf/scripts/deploy-web.sh`
   - Health check registry
   - Optional smoke check HTTP homepage

Secrets/vars cần chuẩn bị:

```text
GCP_PROJECT_ID=tankmapf
GCP_PROJECT_NUMBER=112059749535
GCP_REGION=asia-southeast1
GCP_ZONE=asia-southeast1-b
GCP_ARTIFACT_BUCKET=tankmapf-tank-mapf-artifacts
REGISTRY_ADMIN_TOKEN=<secret>
SERVER_SESSION_CODE=NHUHAI123
```

Không commit:

```text
registry_admin_token
terraform.tfvars chứa secret
service account json
```

Definition of done:

- GitHub Actions tạo được server artifact và web artifact.
- Deploy job cập nhật VM.
- Sau CI/CD, `http://35.240.203.91/` và registry health đều pass.

## Milestone M24 - Domain, HTTPS và WSS

Mục tiêu:

- Chuyển từ IP HTTP sang domain HTTPS.
- Tránh browser chặn mixed content.

Khi có domain, ví dụ:

```text
tankmapf.example.com
```

Hướng khuyến nghị:

- Trỏ A record về `35.240.203.91`.
- Dùng Caddy để tự cấp TLS, hoặc Nginx + Certbot.
- Host WebGL tại `https://tankmapf.example.com/`.
- Registry proxy qua `https://tankmapf.example.com/api/...`.
- WebSocket qua `wss://tankmapf.example.com/ws` hoặc `wss://tankmapf.example.com:7778`.

Production routing tốt hơn:

```text
https://tankmapf.example.com/              -> WebGL static
https://tankmapf.example.com/api/...       -> registry localhost:8080
wss://tankmapf.example.com/ws              -> web server localhost:7778
udp://tankmapf.example.com:7777            -> native client
```

Firewall production:

- Public: TCP `80`, TCP `443`, UDP `7777`
- Nếu WebSocket proxy qua `443`, có thể không cần public TCP `7778`
- SSH chỉ mở theo IP cá nhân, không mở `0.0.0.0/0`

Definition of done:

- `https://domain/` load WebGL.
- Browser WebGL connect bằng WSS.
- DevTools không có mixed content/security error.

## Thứ tự làm khuyến nghị

Không nhảy thẳng vào CI/CD. Thứ tự ít rủi ro:

1. M15: thêm transport mode trong Unity.
2. M16: chạy WebSocket dedicated server service local/VM.
3. M17: mở firewall Terraform.
4. M18: build WebGL local.
5. M19: host WebGL trên VM.
6. M20-M21: auto join bằng session link.
7. M22: manual deploy end-to-end.
8. M23: CI/CD.
9. M24: domain HTTPS/WSS.

## Rủi ro chính

- WebGL client không thể join UDP server hiện tại, bắt buộc có WebSocket lane.
- Nếu dùng HTTPS sau này mà WebSocket vẫn là `ws://`, browser sẽ chặn mixed content.
- Một VM chạy 2 Unity server process có thể tốn CPU/RAM hơn. Với MVP và ít người chơi vẫn chấp nhận được; nếu quá tải thì tăng machine type hoặc tách VM.
- Native UDP server và WebSocket server là 2 process khác nhau. Nếu cần tất cả người chơi native/web vào đúng một trận duy nhất, cần milestone riêng để hợp nhất transport hoặc quản lý session/game instance.
- WebGL build có thể lớn, cần serve đúng MIME type cho `.wasm`, `.data`, `.js`.

## Quyết định hiện tại

Giữ VM `tank-mapf-server`, tận dụng static IP `35.240.203.91`, registry và artifact bucket hiện tại.

Milestone tiếp theo nên là M15: thêm transport mode `udp/websocket` vào Unity runtime, vì đây là điều kiện bắt buộc trước khi WebGL client có thể chơi multiplayer qua browser.
