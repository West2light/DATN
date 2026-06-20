# V2 - Kế hoạch triển khai multiplayer Internet theo phương án B

Ngày lập: 2026-06-10  
Branch hiện tại: `networking-gcp`  
Phương án đã chốt: VM GCP chạy Unity dedicated server + lightweight invite/session registry HTTP chạy cùng VM.  
Mục tiêu tài liệu: chia nhỏ công việc để model thấp hơn có thể implement tuần tự, ít suy diễn, có file list và tiêu chí nghiệm thu rõ ràng.

## 0. Quyết định kiến trúc

### 0.1. Chốt lựa chọn

Project sẽ đi theo mô hình:

```text
Player Client
  |
  | 1. Nhập invite link hoặc session code
  v
HTTP invite/session registry
  |
  | 2. Trả về host, gamePort, sessionCode, map, algorithm
  v
Unity Client
  |
  | 3. Kết nối Unity Transport tới host:gamePort
  v
GCP VM
  |-- Unity dedicated server: UDP 7777
  |-- Invite registry HTTP: TCP 8080
```

### 0.2. Vì sao dùng registry HTTP trên cùng VM

- Dễ triển khai MVP: một VM, một static IP, hai process systemd.
- Không cần Cloud Run trong phase đầu.
- Registry chỉ phục vụ HTTP nhẹ: tạo session, đọc session, render trang invite, health check.
- Unity game server vẫn dùng Unity Transport port riêng, không bị ép chạy qua HTTP.
- Khi đã ổn có thể tách registry sang Cloud Run sau, nhưng không nên làm ngay.

### 0.3. Port và endpoint chốt cho MVP

- Game server: `udp:7777`
- Registry HTTP: `tcp:8080`
- SSH: `tcp:22`, chỉ mở theo IP cá nhân nếu có thể
- Invite web MVP: `http://<STATIC_IP>:8080/s/<SESSION_CODE>`
- Join code MVP: `<SESSION_CODE>`
- Direct endpoint fallback: `<STATIC_IP>:7777`

### 0.4. Công nghệ chốt

- Game: Unity 6, Netcode for GameObjects, Unity Transport.
- Dedicated server: Unity Linux dedicated server build.
- Registry: Python 3 standard library, không dùng Flask/FastAPI ở MVP.
- VM service manager: `systemd`.
- Artifact store: GCS bucket `tank-mapf-artifacts-<project-id>` hoặc tên tương đương.
- IaC: Terraform Google provider.
- CI/CD: GitHub Actions + Google Workload Identity Federation.

## 1. Baseline project hiện tại

Các file đang là điểm neo:

- `Packages/manifest.json`
  - Có `com.unity.netcode.gameobjects` `2.1.1`.
- `Assets/Scripts/Multiplayer/LanLobbyController.cs`
  - Đang có `GamePort = 7777`.
  - Host dùng `StartHost()`.
  - Client dùng `StartClient()`.
  - `UnityTransport.SetConnectionData(...)`.
- `Assets/Scripts/Multiplayer/LanDiscovery.cs`
  - UDP broadcast `47776`.
  - Chỉ dùng cho LAN, không dùng cho Internet.
- `Assets/Scripts/Multiplayer/LanSessionManager.cs`
  - Lưu `IsActive`, `IsServer`, `PlayerCount`, `MapFile`, `Algorithm`.
- `Assets/Scripts/Multiplayer/LanNetworkBridge.cs`
  - Owner gửi input bằng `ServerRpc`.
  - Có `Slot`, `VariantIndex`.
- `Assets/Scripts/Multiplayer/LanGameCoordinator.cs`
  - Server-authoritative world state.
  - Broadcast 30 Hz qua `CustomMessagingManager`.
- `Assets/Scripts/Multiplayer/LanClientView.cs`
  - Client render ghosts.
  - Hiện có logic ép `ownSlot` khỏi `0`, vì đang giả định slot 0 là host.
- `Assets/Scripts/MapTankTestBootstrap.cs`
  - Nếu `LanSessionManager.IsActive` và `IsServer`, server spawn tanks/enemies.
  - Nếu client, thêm `LanClientView`.
- `ProjectSettings/EditorBuildSettings.asset`
  - Có `Menu`, `MapF_TankTest`, `MapF_TankTest_PIBT`.

Rủi ro chính cần xử lý trước CI/CD:

- Dedicated server dùng `StartServer()`, không phải `StartHost()`.
- Dedicated server không có host player, nên client đầu tiên có thể là slot 0.
- `LanSessionManager.PlayerCount` hiện được set từ lobby host; dedicated server cần cách set riêng.
- Registry HTTP không thay thế Unity transport. Nó chỉ trả metadata để client biết kết nối tới đâu.

## 2. Cấu trúc thư mục mục tiêu

Tạo thêm các vùng sau trong repo:

```text
adds/
  networking_gcp_deployment_plan.md
  networking_gcp_deployment_plan_v2.md
  networking_gcp_runbook.md              # tạo sau khi deploy thật

Assets/
  Editor/
    BuildServer.cs
  Scripts/
    Multiplayer/
      NetworkEndpointConfig.cs
      NetworkLaunchArgs.cs
      DedicatedServerBootstrap.cs
      InternetJoinParser.cs
      InternetSessionClient.cs
      InternetLobbyController.cs

services/
  invite-registry/
    app.py
    README.md
    sample.sessions.json
    systemd/
      tank-mapf-registry.service

infra/
  gcp/
    versions.tf
    providers.tf
    variables.tf
    locals.tf
    main.tf
    firewall.tf
    iam.tf
    storage.tf
    outputs.tf
    terraform.tfvars.example
    scripts/
      startup.sh
      install-server.sh
      deploy-release.sh
    systemd/
      tank-mapf-server.service.tpl
      tank-mapf-registry.service.tpl

.github/
  workflows/
    terraform-plan.yml
    terraform-apply.yml
    unity-server-build.yml
    deploy-game-server.yml
```

Quy tắc:

- Không đổi tên file `Lan*` trong phase đầu. Thêm wrapper/lớp mới để giảm rủi ro.
- Không xóa LAN discovery. LAN cũ phải tiếp tục chạy.
- Không commit `.vscode/settings.json` nếu đó chỉ là local setting.
- Với file trong `Assets/`, Unity sẽ cần `.meta`. Nếu tạo file ngoài Unity Editor, kiểm tra Unity tự sinh hoặc tạo `.meta` đúng chuẩn sau.

## 3. Milestone tổng quan

```text
M0  - Chuẩn bị local và khóa baseline
M1  - Tách cấu hình endpoint/session khỏi LAN naming
M2  - Thêm connection approval bằng session code
M3  - Join Internet bằng IP:port và link parser local
M4  - Dedicated server local bằng StartServer()
M5  - Fix slot/client ownership cho dedicated server
M6  - Build Linux dedicated server local
M7  - Invite/session registry HTTP local
M8  - Client đọc session registry HTTP
M9  - Terraform GCP base: static IP, firewall, VM, buckets
M10 - VM bootstrap: systemd server + registry
M11 - GitHub Actions Terraform plan/apply
M12 - GitHub Actions Unity build
M13 - GitHub Actions deploy artifact lên VM
M14 - End-to-end Internet test và runbook
```

Mỗi milestone phải merge/checkpoint riêng. Không làm M9-M13 nếu M4-M8 chưa pass local.

## 4. M0 - Chuẩn bị local và khóa baseline

### Mục tiêu

Xác nhận repo ở branch đúng, ghi nhận trạng thái LAN hiện tại, tạo các folder chưa có mà chưa thay code gameplay.

### Việc cần làm

1. Chạy:

```powershell
git -c safe.directory=D:/2025.2/DATN/projectY status --short --branch
git -c safe.directory=D:/2025.2/DATN/projectY rev-parse --short HEAD
```

2. Đảm bảo đang ở `networking-gcp`.

3. Tạo thư mục nếu chưa có:

```text
services/invite-registry/
infra/gcp/
.github/workflows/
Assets/Editor/
```

4. Ghi lại baseline vào `adds/networking_gcp_runbook.md` sau khi có triển khai thật. Ở M0 chưa cần tạo runbook nếu chỉ đang chuẩn bị code.

### Không được làm

- Không sửa `.vscode/settings.json`.
- Không xóa `LanDiscovery`.
- Không đổi `GamePort` trước khi có config chung.

### Done khi

- `git status` chỉ có các file plan/folder mới dự kiến.
- LAN code cũ chưa bị đụng tới.

## 5. M1 - Tách cấu hình endpoint/session khỏi LAN naming

### Mục tiêu

Tạo lớp cấu hình chung để LAN, Internet và dedicated server dùng cùng dữ liệu.

### File cần tạo

`Assets/Scripts/Multiplayer/NetworkEndpointConfig.cs`

Nội dung tối thiểu:

```csharp
using System;

[Serializable]
public struct NetworkEndpointConfig
{
    public string host;
    public ushort port;
    public string sessionCode;
    public string mapFile;
    public string algorithm;
    public int maxPlayers;
    public bool isDedicatedServer;

    public static NetworkEndpointConfig DefaultLan(string mapFile, string algorithm)
    {
        return new NetworkEndpointConfig
        {
            host = "0.0.0.0",
            port = 7777,
            sessionCode = "",
            mapFile = mapFile,
            algorithm = algorithm,
            maxPlayers = 8,
            isDedicatedServer = false
        };
    }
}
```

`Assets/Scripts/Multiplayer/NetworkLaunchArgs.cs`

Yêu cầu:

- Có hàm `Parse(string[] args)`.
- Đọc được:
  - `--server`
  - `--host`
  - `--port`
  - `--map`
  - `--algorithm`
  - `--maxPlayers`
  - `--sessionCode`
  - `--registryUrl`
- Đọc fallback env:
  - `TANK_HOST`
  - `TANK_PORT`
  - `TANK_MAP`
  - `TANK_ALGORITHM`
  - `TANK_MAX_PLAYERS`
  - `TANK_SESSION_CODE`
  - `TANK_REGISTRY_URL`

### File cần sửa

`Assets/Scripts/Multiplayer/LanSessionManager.cs`

Thêm các field/properties:

```csharp
public static bool IsDedicatedServer { get; private set; }
public static string SessionCode { get; private set; } = "";
public static string RegistryUrl { get; private set; } = "";
public static ushort GamePort { get; private set; } = 7777;
public static int MaxPlayers { get; private set; } = 8;
```

Thêm method:

```csharp
public static void ActivateInternetClient(NetworkEndpointConfig cfg)
public static void ActivateDedicatedServer(NetworkEndpointConfig cfg)
```

### Done khi

- Unity compile không lỗi.
- LAN host/client cũ vẫn gọi `ActivateHost`/`ActivateClient` như trước.
- Không có behavior change nếu chưa dùng Internet flow.

## 6. M2 - Connection approval bằng session code

### Mục tiêu

Server chỉ cho client vào nếu session code đúng. Đây là lớp bảo vệ tối thiểu khi mở public port.

### File cần tạo

`Assets/Scripts/Multiplayer/JoinApprovalPayload.cs`

Nội dung tối thiểu:

```csharp
using System;
using UnityEngine;

[Serializable]
public class JoinApprovalPayload
{
    public string sessionCode;
    public string clientVersion;
    public int variantIndex;

    public static byte[] ToBytes(JoinApprovalPayload payload)
    {
        return System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
    }

    public static bool TryFromBytes(byte[] bytes, out JoinApprovalPayload payload)
    {
        payload = null;
        try
        {
            string json = System.Text.Encoding.UTF8.GetString(bytes);
            payload = JsonUtility.FromJson<JoinApprovalPayload>(json);
            return payload != null;
        }
        catch
        {
            return false;
        }
    }
}
```

### File cần sửa

`Assets/Scripts/Multiplayer/LanLobbyController.cs`

Trong `EnsureNetworkManager()` hoặc helper mới:

- Set `nm.NetworkConfig.ConnectionApproval = true` khi session code không rỗng hoặc khi dedicated mode.
- Set callback server-side:

```csharp
nm.ConnectionApprovalCallback = ApprovalCheck;
```

Tạo method server-side:

```csharp
private static void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request,
    NetworkManager.ConnectionApprovalResponse response)
{
    // Nếu session code server rỗng, cho phép để LAN cũ không bị hỏng.
    // Nếu có session code, parse payload và so sánh.
    // Nếu sai: response.Approved = false; response.Reason = "Invalid session code";
    // Nếu đúng: response.Approved = true; response.CreatePlayerObject = true;
}
```

Client trước `StartClient()`:

```csharp
NetworkManager.Singleton.NetworkConfig.ConnectionData =
    JoinApprovalPayload.ToBytes(new JoinApprovalPayload {
        sessionCode = LanSessionManager.SessionCode,
        clientVersion = Application.version,
        variantIndex = LanSessionManager.LocalVariantIndex
    });
```

### Done khi

- LAN cũ không có session code vẫn join được.
- Khi server có `SessionCode = "ABC123"`, client đúng code join được.
- Client sai code bị disconnect và UI hiện lỗi rõ.

### Test thủ công

1. Set server session code tạm trong code hoặc debug path.
2. Join với đúng code.
3. Join với sai code.
4. Kiểm tra log có reason reject.

## 7. M3 - Join Internet bằng IP:port và parser link local

### Mục tiêu

Client có thể join server public bằng endpoint hoặc link, chưa cần registry HTTP.

### File cần tạo

`Assets/Scripts/Multiplayer/InternetJoinParser.cs`

Yêu cầu input/output:

- Input `1.2.3.4:7777`
  - host `1.2.3.4`
  - port `7777`
- Input `tankmapf://join?host=1.2.3.4&port=7777&session=ABC`
  - host `1.2.3.4`
  - port `7777`
  - session `ABC`
- Input `http://1.2.3.4:8080/s/ABC`
  - session `ABC`
  - registry URL `http://1.2.3.4:8080`
- Input `ABC123`
  - session `ABC123`
  - cần registry URL mặc định từ config/env/user input.

Không dùng regex phức tạp nếu không cần. Ưu tiên `Uri.TryCreate`.

### File cần sửa

`Assets/Scripts/Multiplayer/LanLobbyController.cs`

Thêm UI tối thiểu:

- Button `JOIN INTERNET`.
- Input text nhận endpoint/link/code.
- Button `CONNECT`.

Không cần polish UI ở milestone này. Mục tiêu là chạy được.

### Done khi

- Client nhập `127.0.0.1:7777` và connect được tới local server/host.
- Client nhập `tankmapf://join?host=127.0.0.1&port=7777&session=TEST` parse đúng.
- LAN buttons cũ vẫn hoạt động.

## 8. M4 - Dedicated server local bằng StartServer()

### Mục tiêu

Chạy server local không có player host.

### File cần tạo

`Assets/Scripts/Multiplayer/DedicatedServerBootstrap.cs`

Yêu cầu:

- Chạy ở scene đầu hoặc object runtime được tạo bằng `[RuntimeInitializeOnLoadMethod]`.
- Nếu command line có `--server`, tạo `NetworkManager`.
- Gắn `UnityTransport`.
- Set host bind `0.0.0.0`.
- Set port từ args/env.
- Set `LanSessionManager.ActivateDedicatedServer(cfg)`.
- Gọi `NetworkManager.Singleton.StartServer()`.
- Load scene `LanSessionManager.GameScene` sau khi server start.

Pseudo flow:

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
private static void AutoStart()
{
    var args = NetworkLaunchArgs.Parse(Environment.GetCommandLineArgs());
    if (!args.isDedicatedServer) return;

    DedicatedServerBootstrapRuntime.Start(args);
}
```

Có thể tránh static phức tạp bằng cách tạo GameObject runtime:

```csharp
var go = new GameObject("DedicatedServerBootstrap");
UnityEngine.Object.DontDestroyOnLoad(go);
go.AddComponent<DedicatedServerBootstrap>().StartWith(args);
```

### File nên refactor nhẹ

`LanLobbyController.EnsureNetworkManager()` đang private. Không copy-paste quá nhiều nếu có thể:

- Tạo helper mới `NetworkManagerFactory.cs`.
- Hoặc trong M4 copy logic tối thiểu, sau đó M5 dọn lại.

Nếu model thấp dễ lỗi, chọn copy logic tối thiểu trước để có server chạy.

### Done khi

Trong Unity Editor hoặc build dev, chạy command line giả lập:

```bash
TankMapfServer.exe -batchmode -nographics --server --port 7777 --map random-32-32-10.map --algorithm AStar --maxPlayers 8 --sessionCode TEST123
```

Kỳ vọng log:

```text
[DedicatedServer] Starting on 0.0.0.0:7777
[DedicatedServer] StartServer ok
[DedicatedServer] Loading MapF_TankTest
```

## 9. M5 - Fix slot/client ownership cho dedicated server

### Mục tiêu

Dedicated server không còn giả định slot 0 là host. Client đầu tiên có thể sở hữu slot 0.

### File cần sửa

`Assets/Scripts/Multiplayer/LanClientView.cs`

Hiện có đoạn logic đại ý:

```csharp
// CLIENT is never slot 0 in host-mode NGO.
if (ownSlot == 0 && playerCount > 1)
{
    ownSlot = Mathf.Min(1, playerCount - 1);
}
```

Cần đổi thành:

- Nếu `LanSessionManager.IsDedicatedServer == true`, không ép khỏi slot 0.
- Nếu host-mode cũ, giữ logic bảo vệ slot 0 như cũ.

Pseudo:

```csharp
if (!LanSessionManager.IsDedicatedServer && ownSlot == 0 && playerCount > 1)
{
    ownSlot = Mathf.Min(1, playerCount - 1);
}
```

`Assets/Scripts/MapTankTestBootstrap.cs`

Cần kiểm tra các đoạn:

- Khi `LanSessionManager.IsServer` true, spawn số tank theo `PlayerCount`.
- Dedicated server cần biết `PlayerCount` trước khi scene spawn.
- MVP có thể set `PlayerCount = MaxPlayers` hoặc số người đang connected tại lúc load scene.

Khuyến nghị MVP:

- Dedicated server chờ `MinPlayersToStart` hoặc timeout rồi mới load scene.
- Khi load scene, set `LanSessionManager.PlayerCount = NetworkManager.Singleton.ConnectedClients.Count`.
- Nếu chưa đủ player thì vẫn có thể start với số player hiện tại.

`Assets/Scripts/Multiplayer/LanGameCoordinator.cs`

Kiểm tra `TryLink()`:

- Sort bridges theo `OwnerClientId` vẫn ổn.
- Với dedicated server, bridge đầu tiên là client đầu tiên, slot 0.
- Không gọi `ApplyHostInput()` trong dedicated mode.

### Done khi

- Dedicated server local start.
- Client 1 join và được slot 0.
- Client 2 join và được slot 1.
- Cả hai client điều khiển đúng tank của mình.
- LAN host-mode cũ vẫn hoạt động.

## 10. M6 - Build Linux dedicated server local

### Mục tiêu

Tạo build server artifact để CI/CD dùng lại.

### File cần tạo

`Assets/Editor/BuildServer.cs`

Yêu cầu:

- Method static `BuildLinuxServer()`.
- Dùng scenes:
  - `Assets/Scenes/MapF_TankTest.unity`
  - `Assets/Scenes/MapF_TankTest_PIBT.unity`
  - Có thể include `Menu` nếu bootstrap đang phụ thuộc scene đầu, nhưng server nên vào scene game trực tiếp.
- Output:
  - `Builds/LinuxServer/TankMapfServer.x86_64`
- Build target:
  - `BuildTarget.StandaloneLinux64`
- Build options:
  - server/headless option phù hợp Unity 6.
- CLI build nên dùng thêm:
  - `-buildTarget Linux64`
  - `-standaloneBuildSubtarget Server`

### Command local mẫu

Điều chỉnh path Unity theo máy:

```powershell
"& C:\\Program Files\\Unity\\Hub\\Editor\\6000.3.10f1\\Editor\\Unity.exe" `
  -batchmode `
  -nographics `
  -quit `
  -projectPath "D:\\2025.2\\DATN\\projectY" `
  -executeMethod BuildServer.BuildLinuxServer `
  -buildTarget Linux64 `
  -standaloneBuildSubtarget Server `
  -logFile "Builds\\server-build.log"
```

### Done khi

- Có file `Builds/LinuxServer/TankMapfServer.x86_64`.
- Có thể nén thành `tank-mapf-server-<sha>.tar.gz`.
- Log build không có compile error.

## 11. M7 - Invite/session registry HTTP local

### Mục tiêu

Tạo service HTTP nhẹ để lưu và trả session metadata.

### File cần tạo

`services/invite-registry/app.py`

Không dùng dependency ngoài. Dùng:

- `http.server`
- `json`
- `os`
- `time`
- `secrets`
- `urllib.parse`

### API tối thiểu

`GET /healthz`

Response:

```json
{"ok":true}
```

`POST /api/sessions`

Header:

```text
Authorization: Bearer <REGISTRY_ADMIN_TOKEN>
Content-Type: application/json
```

Body:

```json
{
  "code": "ABC123",
  "host": "1.2.3.4",
  "gamePort": 7777,
  "map": "random-32-32-10.map",
  "algorithm": "PIBT",
  "maxPlayers": 8,
  "expiresInSeconds": 7200
}
```

Response:

```json
{
  "code": "ABC123",
  "joinUrl": "http://1.2.3.4:8080/s/ABC123"
}
```

`GET /api/sessions/<code>`

Response:

```json
{
  "code": "ABC123",
  "host": "1.2.3.4",
  "gamePort": 7777,
  "map": "random-32-32-10.map",
  "algorithm": "PIBT",
  "maxPlayers": 8,
  "expiresAt": 1790000000
}
```

`GET /s/<code>`

Response HTML đơn giản:

```html
<h1>Tank MAPF Session ABC123</h1>
<p>Endpoint: 1.2.3.4:7777</p>
<p>Session code: ABC123</p>
<p>Open game and paste this code/link.</p>
```

### Storage MVP

File JSON:

```text
/var/lib/tank-mapf/sessions.json
```

Local dev fallback:

```text
services/invite-registry/data/sessions.json
```

Env vars:

```text
REGISTRY_HOST=0.0.0.0
REGISTRY_PORT=8080
REGISTRY_PUBLIC_BASE_URL=http://127.0.0.1:8080
REGISTRY_DATA_FILE=services/invite-registry/data/sessions.json
REGISTRY_ADMIN_TOKEN=dev-token
```

Ghi chú local dev trên máy hiện tại:

- `127.0.0.1:8080` đang bị `mcp-for-unity` chiếm.
- Giữ `8080` cho deploy VM/GCP.
- Khi test local, đổi sang `18080`.

### Test local

```powershell
$env:REGISTRY_ADMIN_TOKEN="dev-token"
$env:REGISTRY_PORT="18080"
$env:REGISTRY_PUBLIC_BASE_URL="http://127.0.0.1:18080"
python services/invite-registry/app.py
```

Trong terminal khác:

```powershell
curl http://127.0.0.1:18080/healthz
curl -X POST http://127.0.0.1:18080/api/sessions `
  -H "Authorization: Bearer dev-token" `
  -H "Content-Type: application/json" `
  -d "{\"code\":\"ABC123\",\"host\":\"127.0.0.1\",\"gamePort\":7777,\"map\":\"random-32-32-10.map\",\"algorithm\":\"PIBT\",\"maxPlayers\":8,\"expiresInSeconds\":7200}"
curl http://127.0.0.1:18080/api/sessions/ABC123
```

### Done khi

- `/healthz` trả ok.
- POST tạo session được.
- GET session trả đúng host/port/code.
- `/s/ABC123` hiển thị HTML.
- Không cần pip install.

## 12. M8 - Client đọc session registry HTTP

### Mục tiêu

Unity client có thể nhập code/link, gọi registry, nhận endpoint, rồi connect.

### File cần tạo

`Assets/Scripts/Multiplayer/InternetSessionClient.cs`

Yêu cầu:

- Dùng `UnityWebRequest`.
- Method coroutine:

```csharp
public IEnumerator ResolveSession(string registryBaseUrl, string code,
    Action<NetworkEndpointConfig> onSuccess,
    Action<string> onError)
```

- Parse JSON response bằng `JsonUtility`.
- Trả `NetworkEndpointConfig` có:
  - `host`
  - `port`
  - `sessionCode`
  - `mapFile`
  - `algorithm`
  - `maxPlayers`

### File cần sửa

`InternetJoinParser.cs`

- Nếu input là `http://.../s/<code>`, extract:
  - registry base URL
  - code

`LanLobbyController.cs` hoặc `InternetLobbyController.cs`

- Nếu input có host/port trực tiếp, connect ngay.
- Nếu input chỉ có code/link, gọi `InternetSessionClient.ResolveSession`.
- Sau khi resolve:
  - `LanSessionManager.ActivateInternetClient(cfg)`
  - set `UnityTransport.SetConnectionData(cfg.host, cfg.port)`
  - set connection approval payload
  - `StartClient()`

### Done khi

- Registry local chạy.
- Client nhập `http://127.0.0.1:18080/s/ABC123`.
- Client resolve ra `127.0.0.1:7777`.
- Client gọi `StartClient()`.

## 13. M9 - Terraform GCP base

### Mục tiêu

Terraform tạo hạ tầng tối thiểu cho VM + registry + artifact bucket.

### File cần tạo

`infra/gcp/versions.tf`

```hcl
terraform {
  required_version = ">= 1.7.0"

  required_providers {
    google = {
      source  = "hashicorp/google"
      version = "~> 6.0"
    }
  }
}
```

`infra/gcp/providers.tf`

```hcl
provider "google" {
  project = var.project_id
  region  = var.region
  zone    = var.zone
}
```

`infra/gcp/variables.tf`

Biến bắt buộc:

```hcl
variable "project_id" { type = string }
variable "region" { type = string }
variable "zone" { type = string }
variable "machine_type" { type = string, default = "e2-medium" }
variable "game_port" { type = number, default = 7777 }
variable "registry_port" { type = number, default = 8080 }
variable "allowed_game_sources" { type = list(string), default = ["0.0.0.0/0"] }
variable "allowed_registry_sources" { type = list(string), default = ["0.0.0.0/0"] }
variable "allowed_ssh_sources" { type = list(string), default = [] }
variable "server_session_code" { type = string, sensitive = true }
variable "registry_admin_token" { type = string, sensitive = true }
```

`infra/gcp/main.tf`

Tài nguyên:

- `google_compute_network`
- `google_compute_subnetwork`
- `google_compute_address`
- `google_compute_instance`

VM labels/tags:

```hcl
tags = ["tank-mapf-server"]
```

`infra/gcp/firewall.tf`

Rules:

- allow UDP game port:
  - target tag `tank-mapf-server`
  - protocol `udp`
  - ports `[var.game_port]`
- allow TCP registry:
  - protocol `tcp`
  - ports `[var.registry_port]`
- allow SSH only if `allowed_ssh_sources` not empty.

`infra/gcp/storage.tf`

- `google_storage_bucket` artifact bucket.
- Uniform bucket-level access enabled.

`infra/gcp/iam.tf`

- service account for VM.
- grant bucket object viewer/admin as needed.

`infra/gcp/outputs.tf`

Output:

```hcl
output "server_static_ip" {}
output "game_endpoint" {}
output "registry_base_url" {}
output "invite_url" {}
output "artifact_bucket" {}
```

### Done khi

Local:

```powershell
cd infra/gcp
terraform fmt -recursive
terraform init
terraform validate
terraform plan
```

Plan không lỗi.

## 14. M10 - VM bootstrap: systemd server + registry

### Mục tiêu

VM sau khi tạo có sẵn user, folder, Python registry service, Unity server service placeholder.

### File cần tạo

`infra/gcp/scripts/startup.sh`

Nhiệm vụ:

- Tạo user `tankmapf`.
- Tạo folder:
  - `/opt/tank-mapf/releases`
  - `/opt/tank-mapf/current`
  - `/etc/tank-mapf`
  - `/var/lib/tank-mapf`
  - `/var/log/tank-mapf`
- Cài package:
  - `python3`
  - `curl`
  - `unzip`
  - `tar`
- Ghi `/etc/tank-mapf/server.env`.
- Cài systemd service registry.
- Enable/start registry.
- Cài systemd service game server nhưng chưa start nếu chưa có artifact.

`infra/gcp/systemd/tank-mapf-registry.service.tpl`

```ini
[Unit]
Description=Tank MAPF Invite Registry
After=network-online.target
Wants=network-online.target

[Service]
User=tankmapf
WorkingDirectory=/opt/tank-mapf/registry
EnvironmentFile=/etc/tank-mapf/server.env
ExecStart=/usr/bin/python3 /opt/tank-mapf/registry/app.py
Restart=always
RestartSec=3

[Install]
WantedBy=multi-user.target
```

`infra/gcp/systemd/tank-mapf-server.service.tpl`

```ini
[Unit]
Description=Tank MAPF Unity Dedicated Server
After=network-online.target tank-mapf-registry.service
Wants=network-online.target

[Service]
User=tankmapf
WorkingDirectory=/opt/tank-mapf/current
EnvironmentFile=/etc/tank-mapf/server.env
ExecStart=/opt/tank-mapf/current/TankMapfServer.x86_64 -batchmode -nographics --server --port ${GAME_PORT} --map ${MAP_FILE} --algorithm ${ALGORITHM} --maxPlayers ${MAX_PLAYERS} --sessionCode ${SESSION_CODE} --registryUrl ${REGISTRY_PUBLIC_BASE_URL}
Restart=always
RestartSec=5
KillSignal=SIGINT

[Install]
WantedBy=multi-user.target
```

`infra/gcp/scripts/deploy-release.sh`

Input env:

```text
ARTIFACT_URI=gs://bucket/path/tank-mapf-server-sha.tar.gz
RELEASE_SHA=...
```

Nhiệm vụ:

- Tải artifact từ GCS.
- Giải nén vào `/opt/tank-mapf/releases/$RELEASE_SHA`.
- Symlink `/opt/tank-mapf/current`.
- Copy registry app vào `/opt/tank-mapf/registry`.
- Restart registry.
- Restart game server.
- POST session vào registry:

```bash
curl -X POST "http://127.0.0.1:${REGISTRY_PORT}/api/sessions" \
  -H "Authorization: Bearer ${REGISTRY_ADMIN_TOKEN}" \
  -H "Content-Type: application/json" \
  -d "{\"code\":\"${SESSION_CODE}\",\"host\":\"${PUBLIC_IP}\",\"gamePort\":${GAME_PORT},\"map\":\"${MAP_FILE}\",\"algorithm\":\"${ALGORITHM}\",\"maxPlayers\":${MAX_PLAYERS},\"expiresInSeconds\":7200}"
```

### Done khi

Trên VM:

```bash
systemctl status tank-mapf-registry
curl http://127.0.0.1:8080/healthz
journalctl -u tank-mapf-registry -n 50 --no-pager
```

Registry chạy ổn trước khi game server artifact tồn tại.

## 15. M11 - GitHub Actions Terraform plan/apply

### Mục tiêu

CI có thể chạy Terraform plan/apply bằng OIDC, không dùng long-lived JSON key.

### GCP cần chuẩn bị trước

Trong GCP project:

- Billing enabled.
- APIs enabled:
  - `compute.googleapis.com`
  - `iam.googleapis.com`
  - `iamcredentials.googleapis.com`
  - `cloudresourcemanager.googleapis.com`
  - `sts.googleapis.com`
  - `storage.googleapis.com`
- Workload Identity Pool.
- OIDC provider trỏ tới GitHub.
- Deploy service account.
- IAM binding cho repo GitHub cụ thể.

### GitHub variables/secrets

Repository variables:

```text
GCP_PROJECT_ID
GCP_PROJECT_NUMBER
GCP_REGION
GCP_ZONE
GCP_WORKLOAD_IDENTITY_PROVIDER
GCP_DEPLOY_SERVICE_ACCOUNT
TF_STATE_BUCKET
```

Repository secrets:

```text
TF_VAR_server_session_code
TF_VAR_registry_admin_token
```

Không đưa secret vào `terraform.tfvars.example`.

### File cần tạo

`.github/workflows/terraform-plan.yml`

Trigger:

- pull request
- push vào `networking-gcp`
- manual

Steps:

1. checkout.
2. auth GCP bằng `google-github-actions/auth`.
3. setup terraform.
4. `terraform fmt -check -recursive`.
5. `terraform init`.
6. `terraform validate`.
7. `terraform plan`.

`.github/workflows/terraform-apply.yml`

Trigger:

- `workflow_dispatch` only trong MVP.

Steps tương tự plan, sau đó:

```bash
terraform apply -auto-approve
```

### Done khi

- Plan workflow pass trên PR/push.
- Apply workflow tạo được VM khi chạy manual.
- Output hiển thị static IP, registry URL, invite URL.

## 16. M12 - GitHub Actions Unity server build

### Mục tiêu

Tạo server artifact tự động từ GitHub Actions.

### Runner lựa chọn

Khuyến nghị MVP:

- Dùng self-hosted runner trên máy có Unity 6000.3.10f1 và license đã active.
- Lý do: Unity license trên hosted runner mất thời gian xử lý và dễ lỗi.

Nếu dùng hosted runner:

- Cần Unity activation/license secrets.
- Cần action build Unity phù hợp hoặc cài Unity thủ công.

### File cần tạo

`.github/workflows/unity-server-build.yml`

Trigger:

- push vào `networking-gcp`
- manual `workflow_dispatch`

Steps:

1. checkout.
2. restore Unity cache nếu có.
3. run Unity batchmode:

```bash
Unity \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "$GITHUB_WORKSPACE" \
  -executeMethod BuildServer.BuildLinuxServer \
  -buildTarget Linux64 \
  -standaloneBuildSubtarget Server \
  -logFile Builds/server-build.log
```

4. tar artifact:

```bash
tar -czf tank-mapf-server-${GITHUB_SHA}.tar.gz -C Builds/LinuxServer .
```

5. upload GitHub artifact.
6. auth GCP.
7. upload to GCS:

```bash
gcloud storage cp tank-mapf-server-${GITHUB_SHA}.tar.gz gs://${ARTIFACT_BUCKET}/server/${GITHUB_SHA}/
```

### Done khi

- Workflow tạo GitHub artifact.
- GCS có file:

```text
gs://<artifact-bucket>/server/<sha>/tank-mapf-server-<sha>.tar.gz
```

## 17. M13 - GitHub Actions deploy artifact lên VM

### Mục tiêu

Deploy artifact đã build lên VM và restart systemd services.

### File cần tạo

`.github/workflows/deploy-game-server.yml`

Trigger:

- `workflow_dispatch` với input:
  - `release_sha`
  - `map_file`
  - `algorithm`
  - `session_code`
  - `max_players`

Steps:

1. checkout.
2. auth GCP.
3. lấy Terraform output hoặc dùng labels tìm VM.
4. upload/copy deploy scripts nếu cần.
5. SSH vào VM bằng `gcloud compute ssh`.
6. chạy:

```bash
sudo ARTIFACT_URI="gs://.../server/${RELEASE_SHA}/tank-mapf-server-${RELEASE_SHA}.tar.gz" \
  RELEASE_SHA="${RELEASE_SHA}" \
  MAP_FILE="${MAP_FILE}" \
  ALGORITHM="${ALGORITHM}" \
  SESSION_CODE="${SESSION_CODE}" \
  MAX_PLAYERS="${MAX_PLAYERS}" \
  /opt/tank-mapf/scripts/deploy-release.sh
```

7. smoke test:

```bash
curl http://<STATIC_IP>:8080/healthz
curl http://<STATIC_IP>:8080/api/sessions/<SESSION_CODE>
```

8. check server service:

```bash
gcloud compute ssh ... --command "systemctl is-active tank-mapf-server"
```

### Done khi

- Registry trả session mới.
- `tank-mapf-server` active.
- `journalctl -u tank-mapf-server -n 100` không có crash loop.
- Client ở ngoài mạng LAN có thể resolve invite link.

## 18. M14 - End-to-end Internet test và runbook

### Mục tiêu

Chứng minh toàn bộ pipeline chạy từ build đến bạn bè join qua Internet.

### Test matrix

Test tối thiểu:

1. Local registry + local server + local client.
2. GCP registry + GCP server + client cùng mạng nhà.
3. GCP registry + GCP server + client từ mạng khác.
4. Sai session code bị reject.
5. Server restart, registry vẫn trả session hiện tại.
6. Deploy release mới, invite URL mới hoạt động.

### File cần tạo

`adds/networking_gcp_runbook.md`

Nội dung:

- Cách chạy local dedicated server.
- Cách chạy registry local.
- Cách chạy Terraform plan/apply.
- Cách build server artifact.
- Cách deploy artifact.
- Cách lấy invite URL.
- Cách xem log:

```bash
journalctl -u tank-mapf-server -f
journalctl -u tank-mapf-registry -f
```

- Cách stop/start:

```bash
sudo systemctl stop tank-mapf-server
sudo systemctl start tank-mapf-server
sudo systemctl restart tank-mapf-registry
```

- Cách tắt VM hoặc destroy hạ tầng để tránh chi phí.

### Done khi

- Runbook đủ để một người khác làm lại deploy.
- Có ít nhất một invite URL thật đã test.
- Có ghi chú lỗi thường gặp và cách xử lý.

## 19. Thứ tự implement khuyến nghị cho model thấp hơn

Không giao một model làm toàn bộ. Giao từng batch nhỏ:

### Batch A - Unity local foundation

Scope:

- M1, M2, M3.

Prompt giao việc:

```text
Implement M1-M3 trong adds/networking_gcp_deployment_plan_v2.md. Chỉ sửa Assets/Scripts/Multiplayer và giữ LAN flow cũ hoạt động. Không đụng Terraform/CI. Sau khi sửa, báo file changed và compile-risk.
```

### Batch B - Dedicated server local

Scope:

- M4, M5.

Prompt giao việc:

```text
Implement M4-M5. Tạo DedicatedServerBootstrap để chạy StartServer bằng command line --server. Fix slot logic để dedicated server cho phép client đầu tiên là slot 0. Không làm registry, Terraform hay GitHub Actions.
```

### Batch C - Build server

Scope:

- M6.

Prompt giao việc:

```text
Implement M6. Tạo Assets/Editor/BuildServer.cs để build Linux dedicated server. Không sửa gameplay/networking ngoài build script. Ghi command chạy local vào output.
```

### Batch D - Registry local

Scope:

- M7, M8.

Prompt giao việc:

```text
Implement M7-M8. Tạo services/invite-registry/app.py bằng Python standard library và Unity client resolver bằng UnityWebRequest. Không làm Terraform/CI. Test registry local bằng curl.
```

### Batch E - Terraform

Scope:

- M9, M10.

Prompt giao việc:

```text
Implement M9-M10. Tạo infra/gcp Terraform và scripts bootstrap/systemd cho VM. Không chạy terraform apply nếu chưa được yêu cầu. Chỉ chạy fmt/validate nếu Terraform CLI có sẵn.
```

### Batch F - GitHub Actions

Scope:

- M11, M12, M13.

Prompt giao việc:

```text
Implement M11-M13. Tạo workflow Terraform plan/apply, Unity server build, deploy-game-server. Dùng google-github-actions/auth với Workload Identity Federation. Không đưa secrets thật vào repo.
```

### Batch G - Runbook và nghiệm thu

Scope:

- M14.

Prompt giao việc:

```text
Implement M14 sau khi deploy đã chạy thật. Viết adds/networking_gcp_runbook.md từ kết quả thực tế, gồm endpoint, lệnh log, lệnh restart, lệnh stop VM và lỗi đã gặp.
```

## 20. Definition of Done toàn dự án

Hoàn thành khi:

- LAN cũ vẫn hoạt động.
- Dedicated server chạy bằng `StartServer()` không cần host player.
- Client nhập invite link hoặc session code để lấy endpoint từ registry.
- Client ở mạng khác join được game trên GCP.
- GCP hạ tầng được tạo bằng Terraform.
- Server artifact được build bằng GitHub Actions.
- Deploy artifact lên VM bằng GitHub Actions.
- Registry có `/healthz`, `/api/sessions/<code>`, `/s/<code>`.
- Public game port có session-code approval.
- Có runbook vận hành và cách tắt tài nguyên để tránh chi phí.

## 21. Các lỗi dễ gặp và cách tránh

1. Chỉ mở TCP 7777, client không connect được  
   Mở `udp:7777` cho game port vì Unity Transport thường dùng UDP.

2. Registry chạy nhưng game không chạy  
   Kiểm tra `systemctl status tank-mapf-server` và artifact đã giải nén vào `/opt/tank-mapf/current`.

3. Client đầu tiên bị sai slot  
   Kiểm tra `LanClientView` không ép `ownSlot` khỏi 0 trong dedicated mode.

4. Session code đúng nhưng vẫn bị reject  
   Kiểm tra client có set `NetworkConfig.ConnectionData` trước `StartClient()`.

5. Build server thiếu asset/reference  
   Những đoạn `AssetDatabase.LoadAssetAtPath` không tồn tại trong runtime build. Cần đảm bảo asset runtime nằm trong Resources hoặc được serialize trong scene/prefab.

6. GitHub Actions không auth được GCP  
   Kiểm tra Workload Identity Provider string, repo condition, service account IAM binding và `permissions: id-token: write`.

7. Terraform apply tạo VM nhưng startup script chưa chạy xong  
   SSH vào VM và xem:

```bash
sudo journalctl -u google-startup-scripts.service -n 200 --no-pager
```

8. Mở public registry không có admin guard  
   `POST /api/sessions` phải yêu cầu `Authorization: Bearer <token>`. `GET /api/sessions/<code>` có thể public.

## 22. Nguồn tham khảo kỹ thuật

- Unity Dedicated Server build command line: https://docs.unity3d.com/6000.4/Documentation/Manual/dedicated-server-build.html
- Google Cloud Compute Engine với Terraform: https://docs.cloud.google.com/compute/docs/terraform
- Terraform `google_compute_firewall`: https://registry.terraform.io/providers/hashicorp/google/latest/docs/resources/compute_firewall
- Google Cloud VPC firewall rules: https://docs.cloud.google.com/firewall/docs/using-firewalls
- Google Workload Identity Federation cho deployment pipelines: https://docs.cloud.google.com/iam/docs/workload-identity-federation-with-deployment-pipelines
- GitHub OIDC với Google Cloud: https://docs.github.com/actions/deployment/security-hardening-your-deployments/configuring-openid-connect-in-google-cloud-platform
- `google-github-actions/auth`: https://github.com/google-github-actions/auth
