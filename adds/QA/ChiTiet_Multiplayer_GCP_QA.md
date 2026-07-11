# Hỏi Đáp Chuyên Sâu: LAN / Multiplayer & Triển Khai GCP

Tài liệu này giải thích tầng **nhiều người chơi (multiplayer)** của đồ án và **cách đưa server lên đám mây Google Cloud (GCP)**. Đây là phần "kỹ thuật hệ thống" bổ trợ cho phần lõi MAPF — cho phép nhiều người cùng phòng thủ Eagle trên nhiều máy, qua LAN hoặc Internet. Trình bày theo mạch: kiến trúc tổng thể → ba chế độ kết nối → các file cốt lõi → triển khai GCP → phản biện.

> Nền tảng mạng: **Unity Netcode for GameObjects (NGO)** + `UnityTransport`. Toàn bộ file nằm ở `Assets/Scripts/Multiplayer/`.

---

## 1. Kiến trúc cốt lõi: Server toàn quyền (Server-Authoritative)

Đây là quyết định thiết kế quan trọng nhất, chi phối mọi thứ còn lại.

**Mô hình:** **một máy chủ (server) mô phỏng toàn bộ trận đấu** — vật lý xe tăng, AI địch, đạn, máu Eagle. Các máy khách (client) **không tự tính gì cả**; chúng chỉ làm 2 việc:
1. **Gửi lệnh bấm phím** (input) lên server.
2. **Nhận vị trí mọi vật** từ server rồi vẽ lại (vẽ "bóng ma" — ghost).

```
[Client A] --input-->  [SERVER: mô phỏng thật]  --world state 30Hz-->  [Client A, B, C vẽ ghost]
[Client B] --input-->
```

> **Tại sao chọn server-authoritative thay vì mỗi máy tự tính?** Vì AI MAPF (A\*/PIBT) phải chạy **một nơi duy nhất** để mọi người thấy **cùng một trận**. Nếu mỗi máy tự chạy AI, các con địch sẽ đi khác nhau trên từng màn hình → loạn. Server toàn quyền đảm bảo **một sự thật duy nhất**, và cũng chống gian lận (client không thể "chế" vị trí). Đổi lại: có độ trễ một nhịp mạng, được che bằng **dự đoán cục bộ** (client prediction) cho xe của chính mình.

### Hai kênh truyền, hai mục đích
- **World state (vị trí, máu)** — gửi **30 lần/giây** qua `CustomMessagingManager` (kênh tùy biến), kiểu "ảnh chụp liên tục". Rớt một gói cũng không sao vì gói sau đè lên.
- **Sự kiện một-lần (bắn đạn, nổ, thắng/thua)** — gửi qua `[ClientRpc]`, **độ tin cậy cao (Reliable)** vì mất là hỏng (ví dụ bỏ lỡ hiệu ứng nổ).

> **Tại sao tách 2 kênh?** Vị trí thì gửi liên tục nên mất 1 gói không sao — ưu tiên tốc độ. Sự kiện bắn/nổ chỉ xảy ra 1 lần — bắt buộc phải tới nơi. Dùng đúng kiểu truyền cho đúng loại dữ liệu.

---

## 2. Ba chế độ kết nối (điểm dễ bị hỏi "chạy Internet thật không?")

Cùng một lõi game, nhưng có **3 cách hai máy tìm thấy nhau**:

| Chế độ | Cách tìm nhau | File chính | Dùng khi |
|---|---|---|---|
| **LAN** | Server phát sóng UDP broadcast, client dò | `LanDiscovery` | Cùng mạng Wi-Fi/LAN (demo tại chỗ) |
| **Relay** | Unity Relay cấp mã 6 ký tự, hai bên nhập mã | `RelayManager` | Internet không cần IP tĩnh, không mở cổng |
| **Dedicated + Registry** | Server chạy 24/7 trên GCP, client nhập mã phòng | `InternetSessionClient` + `DedicatedServerBootstrap` | Internet "production", link mời |

### 2.1. LAN — `LanDiscovery` (UDP broadcast)
Server **phát sóng** mỗi giây một gói `"TANK_MAPF_HOST:<port>:<ip>"` ra mọi card mạng vật lý (dòng 62). Client **lắng nghe** trên cổng 47776, nghe thấy thì lấy IP host và kết nối (dòng 153).

> **Điểm tinh tế:** code **lọc bỏ card mạng ảo** (WSL, VMware, VPN, Hyper-V — dòng 27) vì IP của chúng máy khác trong LAN không với tới được. Nếu không lọc, client hay "thấy host" ở một IP ảo rồi kết nối thất bại. Đây là bug LAN kinh điển đã được xử lý.

### 2.2. Relay — `RelayManager` (Unity Relay service)
Dùng dịch vụ Relay của Unity: host tạo "allocation" nhận **mã join 6 ký tự** (dòng 34), client nhập mã để vào. Traffic đi vòng qua server Relay của Unity nên **không cần IP công khai hay mở cổng router** — tiện cho Internet gia đình sau NAT.

### 2.3. Internet Production — Registry + Dedicated Server
Đây là chế độ "xịn" nhất, gắn với GCP (mục 5):
- Một **server headless** chạy 24/7 trên máy ảo GCP (`DedicatedServerBootstrap`).
- Một **dịch vụ registry** (HTTP, cổng 8080) ánh xạ **mã phòng → host:port**.
- Client nhập link mời `http://<IP>:8080/s/<CODE>` → `InternetSessionClient.ResolveSession` gọi API registry (dòng 33) lấy về địa chỉ server thật → kết nối.
- `InternetJoinParser` (dòng 5) hiểu nhiều dạng link: `host:port` trực tiếp, URI `tankmapf://...`, hay link registry `/s/CODE`.

> **Câu trả lời trung thực khi thầy hỏi "Internet chạy thật chưa?":** *"LAN chạy ổn định. Internet em dựng qua 2 đường: Relay của Unity (không cần IP tĩnh) và dedicated server trên GCP có registry mã phòng. Cả hai đã dựng và test được, nhưng độ ổn định còn tùy mạng."* — Đừng khẳng định chắc cái chưa test kỹ.

---

## 3. Tầng truyền tải: UDP hay WebSocket? (dễ bị hỏi ở bản Web)

`NetworkManagerFactory` + `NetworkTransportMode` quyết định giao thức truyền:
- **UDP** — nhanh, dùng cho bản desktop (LAN, dedicated).
- **WebSocket** — bắt buộc cho **bản WebGL chạy trên trình duyệt**, vì trình duyệt **không mở được UDP**.

Điểm quan trọng trong `ConfigureTransport` (dòng 66): **WebGL luôn ép WebSocket**, không cho fallback về UDP (nếu fallback sẽ lỗi câm "WebSockets were used even though not selected"). Có `wss` (WebSocket bảo mật, TLS) khi qua HTTPS.

> **Một bug đã sửa cần biết (dòng 78-82):** hàng đợi gói mặc định (128) **tràn** khi vừa load scene vừa bắn world-state 30Hz qua WebSocket Internet → rớt kết nối ("Receive queue is full" → "Mất kết nối"). Đã tăng `MaxPacketQueueSize = 1024` (gấp 8 lần) để hấp thụ đợt dồn. Đây là câu trả lời sẵn nếu thầy hỏi "sao chơi web hay rớt".

---

## 4. Các file cốt lõi và vai trò

### `LanSessionManager` (trạng thái phiên tĩnh)
Một `static class` giữ mọi thông tin phiên **xuyên qua các lần load scene**: đang là server hay client, map nào, thuật toán nào, mã phiên, số người, số địch. Là "bộ nhớ chung" mà menu ghi vào và scene game đọc ra. `EnemyCount = EnemyMultiplier × PlayerCount` (dòng 69) — phòng càng đông địch càng nhiều.

### `LanNetworkBridge` (trái tim — mỗi người chơi một cái)
Đây là **NetworkObject "player prefab"**: NGO spawn một cái cho mỗi người kết nối. Nó là "đường dây" hai chiều của riêng người đó:
- **Input đi LÊN (dòng 276 `Update`):** chủ sở hữu (owner) đọc phím/chuột mỗi frame, nhưng **chỉ gửi lên server 30 lần/giây** (`SendInputServerRpc`). *Tại sao throttle?* Gửi mỗi frame (tới 144fps) làm **tràn hàng đợi nhận của server qua WebSocket → rớt kết nối**. Nút bắn được "chốt" (`_pendingShoot`) để cú bấm giữa 2 lần gửi không bị mất.
- **Dự đoán cục bộ (client prediction):** owner tự dự đoán chuyển động thân/nòng xe **ngay lập tức** (`PredictOwnMovement`/`PredictTurretAim`) để **không thấy trễ**, trong khi chờ server xác nhận.
- **Sự kiện đi XUỐNG (`[ClientRpc]`):** server bắn `SpawnBulletEffectClientRpc`, `SpawnExplosionClientRpc`, `BroadcastEventClientRpc` (thắng/thua) tới mọi client.
- **Sảnh chờ (lobby):** giữ `NetworkVariable` cho `Ready`, `VariantIndex` (màu xe), `Slot`. Chủ phòng (clientId nhỏ nhất) bấm START → `RequestStartServerRpc` kiểm tra mọi người đã Ready chưa rồi mới vào trận (dòng 257).

### `LanGameCoordinator` (nhạc trưởng phía server — chỉ chạy trên server)
- Ghép mỗi `bridge` với một xe tăng thật (`TryLink`, dòng 119), theo thứ tự clientId.
- Chạy **vòng lặp đồng bộ 30Hz** (`SyncLoop`, dòng 172) gói toàn bộ vị trí + máu (players, enemies, eagle) vào một `FastBufferWriter` rồi bắn cho mọi client (`BroadcastWorldState`, dòng 238).
- Route input của host qua đây để **chắc chắn input host chỉ vào xe slot 0** (dòng 365).

### `LanClientView` (mắt phía client)
Bên client, đọc gói world-state rồi **vẽ "bóng ma" (ghost)** cho mọi xe/địch/đạn/Eagle theo đúng vị trí server gửi. Nó cũng lo dự đoán cục bộ cho xe của chính người chơi. (Client không có xe thật, chỉ có ghost — xe thật nằm ở server.)

### `DedicatedServerBootstrap` (server không màn hình cho GCP)
Chạy khi build server headless được khởi động với cờ `--server`:
- `AutoStart` (dòng 19) tự kích hoạt khi phát hiện cờ dòng lệnh.
- Đọc tham số từ **dòng lệnh HOẶC biến môi trường** qua `NetworkLaunchArgs` (`--map`, `--algorithm`, `--sessionCode`, `--maxPlayers`...) → linh hoạt cho cả CLI lẫn systemd.
- **Cap FPS về 60** (dòng 58): không cap thì server headless quay 100% CPU trên VM 2 nhân, bóp nghẹt registry/nginx.
- **Không tự vào game:** đợi chủ phòng bấm START mới load scene (dòng 107) — người chơi tụ ở lobby trước.

### `NetworkManagerFactory` — kiểm soát vào phòng (approval)
Khi có mã phiên (`SessionCode`), server bật `ConnectionApproval`: client phải gửi **đúng mã phiên** mới được vào (`ApprovalCheck`, dòng 111). Cũng chặn vào giữa trận ("Game is already in progress"). Đây là lớp bảo vệ phòng.

---

## 5. Triển khai lên GCP (`networking_gcp_deployment_plan_v3.md`)

Mục tiêu: đưa **dedicated server** lên một máy ảo Google Cloud để chơi Internet qua link mời. Kiến trúc hạ tầng dựng bằng **Terraform** (Infrastructure-as-Code), tự động hóa bằng **GitHub Actions CI/CD**.

### 5.1. Thành phần trên GCP
- **Project:** `tankmapf` (số hiệu 112059749535), vùng `asia-southeast1` (Singapore — gần VN, độ trễ thấp).
- **1 máy ảo (VM)** `e2-medium` chạy: server game (cổng 7777) + dịch vụ registry (cổng 8080).
- **IP tĩnh** để link mời không đổi.
- **Bucket lưu trữ (Cloud Storage)** chứa artifact build server.

### 5.2. Quy trình dựng bằng Terraform (M9)
Hạ tầng khai báo trong `infra/gcp`. Các bước:
```
gcloud auth login → enable APIs → set 2 secret (session code, admin token) qua env
→ terraform init → plan → apply
```
Sau `apply`, Terraform trả về `game_endpoint` (VD `35.240.203.91:7777`), `registry_base_url`, `invite_url`. Bí mật (session code) truyền qua **biến môi trường `TF_VAR_...`**, **không commit vào Git** — file `*.auto.tfvars` chỉ chứa giá trị không nhạy cảm.

### 5.3. Bootstrap VM (M10)
Script `startup.sh` + `deploy-release.sh` + hai **systemd service** (`tank-mapf-registry`, `tank-mapf-server`) để server tự khởi động và tự chạy lại khi VM reboot. Cấu hình đọc từ `/etc/tank-mapf/server.env` (map, thuật toán, mã phiên, cổng...).

### 5.4. CI/CD tự động (M11–M13)
- **M11 — Xác thực không mật khẩu (GitHub OIDC):** thay vì lưu khóa service account trong GitHub (rủi ro lộ), dùng **Workload Identity Federation** — GitHub Actions tự chứng minh danh tính với GCP qua OIDC, mượn quyền service account `github-deploy`. An toàn hơn hẳn.
- **M12 — Build server:** Unity build headless Linux (`-batchmode -nographics`, `BuildServer.BuildLinuxServer`), nén `.tar.gz`, đẩy lên bucket.
- **M13 — Deploy:** SSH vào VM, chạy `deploy-release.sh` kéo artifact mới về chạy. Client vào bằng link `http://<IP>:8080/s/<CODE>`.

### 5.5. Tiết kiệm chi phí
Có sẵn lệnh `stop`/`start` VM để tắt khi không dùng (không mất tiền compute), và `terraform destroy` để hủy toàn bộ khi xong.

> **Tại sao dùng Terraform + CI/CD thay vì bấm tay trên console?** Để **tái lập được (reproducible)**: toàn bộ hạ tầng dựng lại bằng một lệnh, không phụ thuộc "nhớ đã bấm gì". Đây cũng là tinh thần khoa học xuyên suốt đồ án — mọi thứ phải lặp lại được, kể cả hạ tầng.

---

## 6. Câu hỏi phản biện có thể gặp

**"Multiplayer chạy thật không, hay chỉ mô phỏng?"**
→ Chạy thật trên NGO. LAN ổn định (2 máy cùng mạng, đã test). Internet có 2 đường: Relay Unity và dedicated server GCP + registry. Đã dựng và chạy được; độ ổn định Internet còn tùy mạng — em không khẳng định quá cái chưa test kỹ.

**"Ai mô phỏng AI địch khi có nhiều người chơi?"**
→ Chỉ server. Mô hình server-authoritative: server chạy A\*/PIBT cho mọi con địch, rồi bắn vị trí xuống client 30Hz. Client chỉ vẽ lại. Nhờ vậy mọi người thấy cùng một trận và không gian lận được.

**"Độ trễ mạng làm xe giật không?"**
→ Có client prediction: xe của chính người chơi được dự đoán cục bộ ngay khi bấm phím, không chờ server. Input gửi lên server throttle 30Hz để không tràn hàng đợi. World-state cũng 30Hz. Người chơi cảm thấy mượt dù có độ trễ một nhịp.

**"Sao bản web hay báo mất kết nối?"**
→ Đã sửa: trình duyệt chỉ dùng WebSocket (không UDP), và hàng đợi gói mặc định tràn khi vừa load scene vừa sync 30Hz → đã tăng `MaxPacketQueueSize` lên 1024 và throttle input.

**"Vì sao phải làm dedicated server riêng, host-client chưa đủ à?"**
→ Host-client (một người vừa chơi vừa làm chủ) chỉ hợp LAN/phòng nhỏ. Dedicated server chạy 24/7 trên cloud cho phép ai cũng vào bằng link bất cứ lúc nào, không cần chủ phòng online — đúng mô hình game Internet thật, và chứng minh kiến trúc mở rộng được lên cloud.

**"Bảo mật phòng thế nào, ai cũng vào được à?"**
→ Có mã phiên (session code): `NetworkManagerFactory.ApprovalCheck` từ chối client không gửi đúng mã, và chặn vào giữa trận. Bí mật hạ tầng (admin token) truyền qua env, không commit Git.

**"Terraform và CI/CD để làm gì trong một đồ án game?"**
→ Để hạ tầng **tái lập được** bằng một lệnh và deploy tự động an toàn (GitHub OIDC không cần lưu khóa). Cùng tinh thần "mọi thứ phải lặp lại được" như phần thực nghiệm thuật toán.
