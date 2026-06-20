# Kế hoạch triển khai multiplayer Internet trên GCP

Ngày lập: 2026-06-10  
Branch hiện tại: `networking-gcp`  
Mục tiêu: chuyển hệ thống multiplayer hiện tại từ LAN-only sang mô hình có server public trên GCP, có Terraform quản lý hạ tầng, GitHub Actions CI/CD, và có link/code để mời bạn bè ngoài mạng LAN tham gia.

## 1. Tóm tắt quyết định

Hướng đề xuất cho project hiện tại là:

1. Giữ Unity Netcode for GameObjects + Unity Transport làm nền tảng networking.
2. Tách chế độ LAN hiện tại khỏi chế độ Internet bằng một lớp cấu hình endpoint chung.
3. Xây một Unity Linux Dedicated Server chạy trên GCP Compute Engine VM.
4. Dùng Terraform tạo VM, static IP, firewall rule mở port game, service account, bucket state và artifact path.
5. Dùng GitHub Actions để build Unity server, chạy Terraform plan/apply, upload artifact và restart service trên VM.
6. MVP invite link dùng dạng `tankmapf://join?host=<public-ip>&port=7777&session=<code>` hoặc web link `https://<domain>/s/<code>` trỏ về cùng endpoint.

Không nên chọn Cloud Run làm game server chính ở MVP vì luồng hiện tại dùng Unity Transport dạng kết nối realtime thấp trễ trên port game cố định, thường là UDP. Cloud Run phù hợp cho HTTP/WebSocket/gRPC và có request timeout/session-affinity tradeoff; nó có thể dùng cho invite/session registry HTTP, không phải thay thế trực tiếp game transport.

## 2. Trạng thái project hiện tại

Các điểm đã có sẵn:

- Unity 6 project, `Packages/manifest.json` có `com.unity.netcode.gameobjects` `2.1.1`.
- Multiplayer code nằm trong `Assets/Scripts/Multiplayer/`.
- `LanLobbyController` đang tạo `NetworkManager` runtime, gắn `UnityTransport`, host bind `0.0.0.0:7777`, client nhập IP và gọi `StartClient()`.
- `LanDiscovery` dùng UDP broadcast port `47776`, signature `TANK_MAPF_HOST:<port>`, nên chỉ tự tìm host trong cùng LAN.
- `LanNetworkBridge` gửi input qua `ServerRpc`, server broadcast world state 30 Hz qua `CustomMessagingManager`.
- `LanGameCoordinator` là server-authoritative: server giữ tank/enemy/eagle thật, client chủ yếu render ghost và prediction.
- `MapTankTestBootstrap` đã có nhánh xử lý multiplayer: server spawn player tanks + enemies, client vào spectator/ghost flow.
- Build settings có `Menu`, `MapF_TankTest`, `MapF_TankTest_PIBT`.

Vấn đề chính:

- LAN discovery không đi qua Internet vì broadcast không vượt router/NAT.
- Host hiện tại là một player host, chưa phải dedicated server không người chơi.
- Một số logic đang giả định host là slot 0; dedicated server sẽ không có host player.
- Client hiện chỉ join bằng IP thủ công hoặc IP tìm thấy trong LAN, chưa có invite link/session code.
- Chưa có build pipeline server headless/Linux, chưa có hạ tầng GCP, chưa có deploy tự động.

## 3. Kiến trúc mục tiêu

```text
Friend PC / Player Client
        |
        | tankmapf://join?... hoặc Join Code
        v
Unity Client
        |
        | Unity Transport, game port 7777
        v
GCP Compute Engine VM
        |
        | systemd service
        v
Unity Dedicated Server build
        |
        | authoritative simulation
        v
MapF_TankTest / MapF_TankTest_PIBT
```

Phần quản trị:

```text
GitHub push / manual dispatch
        |
        v
GitHub Actions
        |-- Unity server build
        |-- Terraform plan/apply
        |-- Upload artifact
        |-- Restart VM service
        v
GCP: static IP + firewall + VM + logs/artifacts
```

## 4. Brainstorm các phương án

### Phương án A: Single Compute Engine VM, static IP, Unity Dedicated Server

Đây là phương án MVP nên làm trước.

Ưu điểm:

- Khớp nhất với code hiện tại vì Unity Transport có thể mở port game trực tiếp.
- Dễ debug bằng SSH, journalctl, logs Unity.
- Terraform đơn giản: VM + firewall + static IP.
- Bạn bè chỉ cần join public IP/port hoặc link chứa endpoint.
- Phù hợp thesis/demo 2-8 người chơi.

Nhược điểm:

- Một VM là single point of failure.
- Cần tự quản lý update, restart, log rotation.
- Nếu mở port public thì cần join token/rate limit để tránh người lạ vào.

### Phương án B: VM + lightweight invite/session registry HTTP

Mở rộng từ phương án A.

- Game server vẫn chạy trên VM qua UDP/TCP port game.
- Thêm service HTTP nhỏ để sinh link `https://<domain>/s/<code>`.
- Session registry trả về `host`, `port`, `map`, `algorithm`, `expiresAt`.
- Có thể chạy service HTTP cùng VM, hoặc Cloud Run vì đây là HTTP-only.

Ưu điểm:

- Link mời đẹp hơn IP thô.
- Có thể đổi IP/port phía sau mà link/code không đổi trong thời gian session sống.
- Dễ thêm expiry, password, player limit.

Nhược điểm:

- Thêm một service nhỏ và một luồng deploy nữa.
- Nếu muốn click link mở game trực tiếp cần custom protocol handler trên máy người chơi.

### Phương án C: GKE / Agones / multi-session server fleet

Không nên làm ngay cho project hiện tại.

- Phù hợp nếu có nhiều phòng chơi song song, scale dynamic, matchmaking.
- Chi phí và vận hành cao hơn nhiều so với nhu cầu demo.
- Cần container hóa server, readiness/health, session allocator.

### Phương án D: Unity Relay / Lobby service

Đáng cân nhắc nếu mục tiêu là vượt NAT nhanh và không muốn tự vận hành VM.

- Giảm phần GCP/Terraform.
- Nhưng đi lệch yêu cầu hiện tại là deploy lên GCP bằng Terraform/CI/CD.
- Có thể dùng làm fallback nếu đường truyền UDP direct qua public VM gặp vấn đề.

## 5. Các thay đổi Unity cần làm

### 5.1. Tách "LAN" khỏi "Internet"

Giữ code LAN hiện tại để không phá demo cũ, nhưng thêm lớp config chung:

- `NetworkEndpointConfig`
  - `string host`
  - `ushort port`
  - `string sessionCode`
  - `string mapFile`
  - `string algorithm`
  - `bool isLanDiscovery`
  - `bool isDedicatedServer`

- `NetworkLaunchArgs`
  - đọc command line: `--server`, `--port 7777`, `--map random-32-32-10.map`, `--algorithm PIBT`, `--maxPlayers 8`
  - đọc env vars fallback: `TANK_PORT`, `TANK_MAP`, `TANK_ALGORITHM`, `TANK_SESSION_CODE`

Tên class `Lan*` có thể giữ trong phase đầu để giảm rủi ro, nhưng flow mới nên gọi qua API trung lập như `MultiplayerSessionManager`.

### 5.2. Dedicated server bootstrap

Thêm `DedicatedServerBootstrap`:

- Chạy sớm khi build server/headless.
- Tạo `NetworkManager` giống `LanLobbyController.EnsureNetworkManager()`.
- Gắn `UnityTransport.SetConnectionData("0.0.0.0", port)`.
- Dùng `StartServer()` thay vì `StartHost()`.
- Load scene game sau khi đủ người chơi hoặc sau timeout cấu hình.
- Không spawn tank cho host vì dedicated server không phải player.

Các điểm cần sửa:

- `LanSessionManager` thêm `IsDedicatedServer` hoặc đổi thành session state chung.
- `LanClientView.OnReceiveInitWorld()` không được ép client khỏi slot 0 trong dedicated mode. Slot 0 có thể là client đầu tiên.
- `LanGameCoordinator.ApplyHostInput()` chỉ dùng cho host-mode cũ; dedicated mode không gọi.
- `MapTankTestBootstrap` cần tách logic `server has local player` và `server authoritative without local player`.

### 5.3. Internet lobby

Thêm hoặc mở rộng UI:

- `Host LAN` giữ nguyên.
- `Join LAN` giữ discovery cũ.
- `Join Internet` nhận:
  - link `tankmapf://join?...`
  - hoặc `IP:PORT`
  - hoặc session code nếu có registry.

Client parse link rồi gọi `UnityTransport.SetConnectionData(host, port)` và `StartClient()`.

### 5.4. Invite link

MVP link trực tiếp:

```text
tankmapf://join?host=<STATIC_IP>&port=7777&map=random-32-32-10.map&algo=PIBT&session=<SESSION_CODE>
```

MVP đơn giản hơn để test ngay:

```text
<STATIC_IP>:7777
```

Bản tốt hơn:

```text
https://tank-mapf.example.com/s/<SESSION_CODE>
```

Trang web này hiển thị nút copy endpoint và có thể redirect sang `tankmapf://join?...` nếu máy đã đăng ký custom protocol.

### 5.5. Join token tối thiểu

Không nên chỉ mở public IP trống. Thêm token/session code:

- Server nhận `sessionCode` qua command line/env.
- Client gửi `sessionCode` trong connection approval payload của NGO.
- Server reject nếu sai, hết hạn, hoặc quá `MaxPlayers`.

## 6. Terraform hạ tầng GCP

Đề xuất cấu trúc repo:

```text
infra/
  gcp/
    backend.tf
    providers.tf
    variables.tf
    main.tf
    firewall.tf
    outputs.tf
    startup.sh
    systemd/
      tank-mapf.service.tpl
```

Tài nguyên MVP:

- `google_compute_address` cho static external IPv4.
- `google_compute_instance` hoặc instance template cho VM Ubuntu.
- `google_compute_firewall`:
  - allow game port: `udp:7777` từ Internet hoặc danh sách IP bạn bè.
  - optional health/admin HTTP: `tcp:8080` nếu có invite/status service.
  - SSH `tcp:22` nên khóa theo IP cá nhân, không mở `0.0.0.0/0` nếu không cần.
- `google_service_account` cho VM runtime.
- GCS bucket cho Terraform remote state.
- Optional Artifact Registry hoặc GCS bucket để chứa server build artifact.
- Optional Cloud DNS nếu có domain.

Biến Terraform nên có:

```hcl
project_id
region
zone
machine_type
game_port
allowed_game_sources
allowed_ssh_sources
server_artifact_uri
session_code
```

Output quan trọng:

```hcl
server_static_ip
game_endpoint
invite_url
ssh_command
```

## 7. CI/CD với GitHub Actions

### 7.1. Authentication

Dùng GitHub OIDC + GCP Workload Identity Federation, tránh lưu service account key JSON dài hạn trong GitHub Secrets.

Secrets/variables cần có:

- `GCP_PROJECT_ID`
- `GCP_PROJECT_NUMBER`
- `GCP_WORKLOAD_IDENTITY_PROVIDER`
- `GCP_DEPLOY_SERVICE_ACCOUNT`
- Unity license secrets nếu dùng hosted runner để build Unity.

### 7.2. Workflow đề xuất

```text
.github/workflows/
  terraform-plan.yml
  terraform-apply.yml
  unity-server-build.yml
  deploy-game-server.yml
```

`terraform-plan.yml`:

- Trigger: pull request vào `networking-gcp` hoặc `main`.
- Steps: checkout, auth GCP, setup terraform, `terraform fmt -check`, `terraform validate`, `terraform plan`.

`terraform-apply.yml`:

- Trigger: manual `workflow_dispatch` hoặc push vào protected branch.
- Dùng GitHub Environment approval.
- Chạy `terraform apply`.

`unity-server-build.yml`:

- Trigger: push vào `networking-gcp`, tag release, hoặc manual.
- Build Linux Dedicated Server.
- Upload artifact `.tar.gz`.
- Có thể dùng self-hosted runner để tránh vấn đề Unity licensing trên hosted runner.

`deploy-game-server.yml`:

- Trigger: sau khi build server thành công.
- Upload artifact lên GCS/Artifact Registry.
- SSH hoặc OS Login vào VM, tải artifact mới, giải nén vào `/opt/tank-mapf/releases/<sha>`.
- Update symlink `/opt/tank-mapf/current`.
- Restart `systemd` service `tank-mapf.service`.
- Smoke test: kiểm tra process, port listening, log không có exception trong 30-60 giây đầu.

## 8. Build server Unity

Thêm script editor:

```text
Assets/Editor/BuildServer.cs
```

Nhiệm vụ:

- Build target: Linux x86_64.
- Subtarget: Dedicated Server nếu Unity version hỗ trợ trong CLI.
- Scenes: `Menu` không cần cho server; ưu tiên `MapF_TankTest`, `MapF_TankTest_PIBT`.
- Output: `Builds/LinuxServer/TankMapfServer.x86_64`.
- Define symbols: `DEDICATED_SERVER`, `UNITY_SERVER` nếu cần.

Runtime command mẫu:

```bash
./TankMapfServer.x86_64 \
  -batchmode -nographics \
  --server \
  --port 7777 \
  --map random-32-32-10.map \
  --algorithm PIBT \
  --maxPlayers 8 \
  --sessionCode "$SESSION_CODE"
```

Systemd service mẫu:

```ini
[Unit]
Description=Tank MAPF Unity Dedicated Server
After=network-online.target
Wants=network-online.target

[Service]
User=tankmapf
WorkingDirectory=/opt/tank-mapf/current
EnvironmentFile=/etc/tank-mapf/server.env
ExecStart=/opt/tank-mapf/current/TankMapfServer.x86_64 -batchmode -nographics --server --port ${GAME_PORT} --map ${MAP_FILE} --algorithm ${ALGORITHM} --maxPlayers ${MAX_PLAYERS} --sessionCode ${SESSION_CODE}
Restart=always
RestartSec=5
KillSignal=SIGINT

[Install]
WantedBy=multi-user.target
```

## 9. Bảo mật và vận hành

MVP demo có thể mở `udp:7777` từ `0.0.0.0/0`, nhưng nên có các chốt sau:

- Connection approval payload có `sessionCode`.
- `MaxPlayers` không vượt 8.
- Server reject client sai version/protocol.
- Log join/leave/client id/IP.
- SSH chỉ mở cho IP cá nhân hoặc dùng OS Login/IAP.
- Không lưu GCP key JSON trong repo hoặc GitHub secrets nếu dùng được OIDC.
- Terraform state nằm trong GCS bucket riêng, bật versioning nếu có thể.
- Tạo budget alert trước khi để VM chạy lâu.
- Có workflow/manual step để stop VM hoặc `terraform destroy` sau demo.

## 10. Lộ trình triển khai

### Phase 0: Chốt baseline hiện tại

Mục tiêu:

- Xác nhận LAN flow vẫn chạy trên branch `networking-gcp`.
- Ghi rõ port `7777`, discovery `47776`, scenes, NGO version.

Done khi:

- 2 máy cùng LAN vẫn host/join được.
- Không phá `LanLobbyController` hiện tại.

### Phase 1: Join Internet bằng IP trực tiếp

Mục tiêu:

- Thêm UI `Join Internet`.
- Parse `IP:PORT`.
- Không dùng broadcast.
- Reuse `UnityTransport.SetConnectionData(host, port)`.

Done khi:

- Trong cùng máy/local network, client join bằng IP thủ công vẫn được.
- LAN discovery cũ vẫn còn.

### Phase 2: Dedicated server local

Mục tiêu:

- Thêm `DedicatedServerBootstrap`.
- Server chạy `StartServer()` không có host player.
- Client đầu tiên có thể là slot 0.
- Scene server spawn tank cho từng connected client.

Done khi:

- Chạy server local bằng `-batchmode -nographics --server`.
- Hai client editor/build join vào server local.
- AStar và PIBT scene đều vào game được.

### Phase 3: GCP single VM bằng Terraform

Mục tiêu:

- `infra/gcp` tạo static IP, VM, firewall.
- VM có startup script tạo user, thư mục `/opt/tank-mapf`, systemd service placeholder.

Done khi:

- `terraform apply` tạo được VM.
- Output có `game_endpoint`.
- Có thể SSH/OS Login và thấy service file.

### Phase 4: CI build + deploy

Mục tiêu:

- GitHub Actions build Linux Dedicated Server.
- Upload artifact.
- Deploy artifact lên VM và restart service.

Done khi:

- Push branch tạo artifact server.
- Manual deploy cập nhật VM.
- `journalctl -u tank-mapf` thấy server listen port `7777`.

### Phase 5: Invite link/session code

Mục tiêu:

- Server sinh hoặc nhận `SESSION_CODE`.
- Client join bằng link/code.
- Optional web link đẹp nếu có domain.

Done khi:

- Người chơi ở mạng khác nhập link/code và vào được phòng.
- Sai code bị reject rõ ràng.
- Link hết hạn hoặc đổi session không cho join.

### Phase 6: Hardening cho demo Internet

Mục tiêu:

- Smoke tests, logs, restart policy, budget/stop VM.
- Basic metrics: connected players, tick/world-state rate, disconnect count.

Done khi:

- Test từ ít nhất 2 mạng khác nhau.
- Có hướng dẫn vận hành ngắn: start/stop server, lấy invite link, xem log, rollback.

## 11. Rủi ro kỹ thuật cần xử lý sớm

1. Dedicated server phá giả định host slot 0  
   Cần sửa `LanClientView` và coordinator để slot 0 có thể là client thật.

2. Scene flow hiện tại phụ thuộc host bấm Start Game  
   Dedicated server cần auto-start hoặc có lobby server-side riêng.

3. Unity headless build có thể thiếu serialized references  
   Các fallback `AssetDatabase.LoadAssetAtPath` chỉ chạy trong Editor. Build server phải có references runtime hoặc Resources path hợp lệ.

4. Transport protocol/firewall không khớp  
   Nếu Unity Transport đang dùng UDP thì firewall phải mở `udp:7777`, không chỉ TCP.

5. Invite link không tự mở game nếu chưa đăng ký protocol handler  
   MVP nên chấp nhận copy endpoint/code trước; custom URI handler làm sau.

6. Chi phí VM chạy 24/7  
   Demo nên có workflow stop/start hoặc hủy hạ tầng khi không dùng.

## 12. Checklist file cần tạo/sửa khi bắt đầu code

Unity:

- `Assets/Scripts/Multiplayer/NetworkEndpointConfig.cs`
- `Assets/Scripts/Multiplayer/NetworkLaunchArgs.cs`
- `Assets/Scripts/Multiplayer/DedicatedServerBootstrap.cs`
- `Assets/Scripts/Multiplayer/InternetLobbyController.cs`
- `Assets/Scripts/Multiplayer/LanSessionManager.cs`
- `Assets/Scripts/Multiplayer/LanClientView.cs`
- `Assets/Scripts/Multiplayer/LanGameCoordinator.cs`
- `Assets/Scripts/MapTankTestBootstrap.cs`
- `Assets/Editor/BuildServer.cs`

Infra:

- `infra/gcp/backend.tf`
- `infra/gcp/providers.tf`
- `infra/gcp/main.tf`
- `infra/gcp/firewall.tf`
- `infra/gcp/variables.tf`
- `infra/gcp/outputs.tf`
- `infra/gcp/startup.sh`
- `infra/gcp/systemd/tank-mapf.service.tpl`

CI/CD:

- `.github/workflows/terraform-plan.yml`
- `.github/workflows/terraform-apply.yml`
- `.github/workflows/unity-server-build.yml`
- `.github/workflows/deploy-game-server.yml`

Docs:

- `adds/networking_gcp_runbook.md` sau khi có triển khai thật.

## 13. Thứ tự ưu tiên thực tế

Nếu muốn đi nhanh nhất để mời bạn bè ngoài mạng:

1. Làm `Join Internet` bằng public IP trước.
2. Làm dedicated server local.
3. Deploy dedicated server lên một VM GCP bằng tay một lần để xác thực port/transport.
4. Sau khi kết nối Internet chạy được, mới đóng gói Terraform.
5. Sau Terraform ổn định, mới thêm CI/CD.
6. Invite link đẹp và session registry làm sau cùng.

Lý do: rủi ro lớn nhất không nằm ở Terraform mà nằm ở việc biến host-mode hiện tại thành dedicated server đúng nghĩa. Nếu phần server local chưa chạy, CI/CD và GCP chỉ tự động hóa một artifact chưa dùng được.

## 14. Nguồn tham khảo chính

- Unity Dedicated Server manual: https://docs.unity3d.com/6000.2/Documentation/Manual/dedicated-server.html
- Google Cloud Terraform VM quickstart: https://docs.cloud.google.com/docs/terraform/create-vm-instance
- Google Cloud VPC firewall rules: https://docs.cloud.google.com/firewall/docs/firewalls
- Google Cloud Workload Identity Federation: https://docs.cloud.google.com/iam/docs/workload-identity-federation
- GitHub Actions OIDC with GCP: https://docs.github.com/en/actions/how-tos/secure-your-work/security-harden-deployments/oidc-in-google-cloud-platform
- google-github-actions/auth: https://github.com/google-github-actions/auth
- Cloud Run WebSockets note, useful for invite/session HTTP service but not the primary Unity UDP game server: https://docs.cloud.google.com/run/docs/triggering/websockets
