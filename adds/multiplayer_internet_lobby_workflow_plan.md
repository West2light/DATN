# Plan: Sửa workflow Multiplayer Internet + Waiting Lobby + fix per-player tank

Ngày lập: 2026-06-14
Repo: `D:\2025.2\DATN\projectY`
Nhánh: `networking-gcp`
Tiếp nối: `adds/multiplayer_internet_workflow_plan.md` (M0–M6)

## 0. Bối cảnh — cái gì đã xong

Đã ship và verify trên production `http://35.240.203.91/`:

- **M1 (invite link)**: link `/play?session=<code>` mở thẳng WebGL — OK.
- **M2 (readiness gate)**: helper `tank-mapf-create-room` chờ web service active + port 7778 listen + marker `StartServer ok` rồi mới publish session. Hết lỗi "Mất kết nối với host" lúc tạo room. (commit `3de25b1`)
- **M4-lite (2-player gating)**: `DedicatedServerBootstrap` không còn auto-load scene 3s sau client đầu; chờ đủ `MinPlayersToStart=2` rồi mới load (grace solo 25s). (commit `4895c20`)

Plan này giải quyết phần **còn lại và quan trọng nhất**: workflow đúng (Host/Join → Waiting Lobby → Ready → Start) và **fix triệt để bug 2 người điều khiển chung 1 tank**.

## 1. Hai lỗi hiện tại (đã truy ra nguyên nhân trong code)

### 1.1 Lỗi thứ tự workflow

Luồng hiện tại (WebGL):

```text
Main Menu
  -> Outfit (CHỌN TANK)        ← MenuViewBootstrap.SelectVariant → PlayerPrefs "MenuTankVariant"
  -> Map/Mode (A*/PIBT)        ← BuildLanMapCard → HandleLanModeSelected (MenuViewBootstrap:837)
  -> POST /api/rooms           ← CreateInternetRoomAndJoin (MenuViewBootstrap:849)
  -> ShowAndJoin → auto-join   ← LanLobbyController.BeginAutoJoin (LanLobbyController:414)
  -> vào thẳng game            (không có lobby chờ, không Ready, không Start)
```

Vấn đề:

1. Tank chọn **trước** map/mode và **trước** khi tạo room — sai so với workflow mong muốn (tank chọn trong lobby).
2. Không có phân nhánh **Host** (tạo room) vs **Join** (link/code/IP). Mọi người đi chung 1 đường create-and-join.
3. Không có **Waiting Lobby** thật: không thấy danh sách người chơi, không Ready, không nút Start của owner.
4. `LanLobbyController` hiện là overlay kiểu **LAN** (Host/Join/IP), không phải lobby networked có slot.

File liên quan: `MenuViewBootstrap.cs` (124–151 auto-join; 837–889 create flow), `LanLobbyController.cs` (toàn bộ), `LanSessionManager.cs` (24/31/37 đọc variant từ PlayerPrefs).

### 1.2 Lỗi 2 người điều khiển chung 1 tank — ROOT CAUSE

Chuỗi nhân quả (đã verify từ code):

1. `DedicatedServerBootstrap.BeginGameplayScene()` set `LanSessionManager.PlayerCount = ConnectedClients.Count` **một lần duy nhất** tại thời điểm load scene.
2. `MapTankTestBootstrap.SpawnAllLanPlayers()` (264–318) spawn đúng `n = LanSessionManager.PlayerCount` tank, **chỉ server-side**. Client không spawn tank thật, chỉ spawn ghost qua `LanClientView`.
3. `LanGameCoordinator.TryLink()` (97–127): `n = Mathf.Min(_bridges.Count, _serverTanks.Count)` → nếu chỉ 1 tank mà 2 bridge thì `n=1`, bridge[1] **không bao giờ được link** (`_serverTank = null`), input client 2 rơi vào null (`SendInputServerRpc`, LanNetworkBridge:227).
4. `LanClientView.OnReceiveInitWorld()` (136–162): default `int ownSlot = Mathf.Min(1, playerCount - 1)`. Khi `playerCount=1` → `ownSlot=0` cho **mọi** client → cả 2 camera bám `_playerGhosts[0]`.

Kết luận: với hành vi cũ (load scene khi mới 1 client), chỉ 1 tank spawn → 2 client cùng nhìn/điều khiển 1 tank. Đây CHÍNH XÁC là triệu chứng bạn thấy.

`MinPlayersToStart=2` (M4-lite) đã giảm nhẹ, nhưng:
- Vẫn còn **race**: nếu client 2 connect sau khi đã quyết định Start nhưng trước khi scene load xong → count cũ.
- `PlayerCount` đọc một lần, không re-read khi spawn.
- Fallback `Mathf.Min(1, playerCount-1)` là footgun tiềm ẩn.
- Per-player tank body (variant) chưa thật sự độc lập theo từng người trong lobby.

Workflow lobby + Start tường minh sẽ khử race hoàn toàn (chỉ Start khi tất cả đã ở trong lobby), nên đây vừa là feature vừa là fix bug #2.

## 2. Workflow đích (theo sơ đồ người dùng)

```text
Multiplayer Internet
├── HOST
│    ├── Chọn Map / Mode
│    ├── Create Room → Registry sinh code (đã có M1/M2)
│    └────────────────────────────┐
│                                  ▼
└── JOIN                       ┌─ WAITING LOBBY ─────────────┐
     ├── Click Invite Link  ──>│ · Chọn Tank Body (mỗi người) │
     ├── Nhập Room Code     ──>│ · Hiển thị Room Code + Link  │
     └── Nhập IP Address    ──>│ · Ready (từng player)        │
                               │ · Status 4 slots             │
                               └──────────────┬──────────────┘
                                              │
                              Owner bấm START (khi ≥2 đã Ready)
                                              │
                                         Game bắt đầu
```

### 2.0 Số lượng người chơi: 2–8

- Room hỗ trợ **2 đến 8 người chơi** (`MaxPlayers` mặc định 8, do helper `tank-mapf-create-room --max-players` truyền vào lúc tạo room).
- Số slot trong lobby = `MaxPlayers` của room (≤ 8), không cố định 4.
- Điều kiện START: **tối thiểu 2 người đã Ready**, tối đa `MaxPlayers`. Khi đủ `MaxPlayers` thì các slot còn lại đầy; không bắt buộc full mới start.
- Vào game: số tank player = số người connect lúc Start (2–8), mỗi người 1 tank. `EnemyCount = 6 × PlayerCount` (đã có ở `LanSessionManager:61`) → 12–48 enemy tùy số người.
- `MinPlayersToStart` (M4-lite, dùng cho dev fallback) giữ = 2; production dùng owner Start với điều kiện ≥2 ready.

### 2.1 Ánh xạ "Host" sang mô hình dedicated server

Production dùng **dedicated server headless** (không phải máy host chơi). Vì vậy:

- "Host" trong sơ đồ = **room owner** = **client đầu tiên** connect vào dedicated server (chính là người tạo room).
- Server giữ trạng thái lobby, không tự chơi, không tự start.
- Owner là người duy nhất thấy/bấm được nút START.
- Mọi người (kể cả owner) đều là client; mỗi client có 1 tank riêng khi vào game.

Giữ flow LAN cũ (Editor/Desktop: máy host chạy server, `StartHost()`) song song — không phá.

## 3. Kiến trúc giải pháp

### 3.1 Tách Host vs Join ở menu (sửa Bug #1)

`MenuViewBootstrap`:

- Màn **Outfit chọn tank bị bỏ khỏi luồng multiplayer** (hoặc giữ cho Single Play, nhưng KHÔNG quyết định tank cho Internet — tank chọn trong lobby).
- Multiplayer Internet tách 2 nhánh:
  - **HOST**: màn `INTERNET — SELECT MAP & MODE` → click A*/PIBT → `CreateInternetRoomAndJoin` (giữ) nhưng **vào Waiting Lobby thay vì auto-start game**.
  - **JOIN**: màn `JOIN INTERNET ROOM` nhận 3 input (invite link / room code / IP) → resolve → connect → vào Waiting Lobby.
- WebGL auto-join từ invite link (`Application.absoluteURL`) → đi thẳng nhánh JOIN → Waiting Lobby.

Tank variant không còn lấy từ PlayerPrefs lúc spawn; lấy từ lựa chọn trong lobby (sync qua network).

### 3.2 Waiting Lobby networked

Tận dụng hạ tầng sẵn có: mỗi client khi connect đã được server spawn 1 `LanNetworkBridge` (PlayerPrefab), đã có `NetworkVariable<int> VariantIndex`. Server giữ client ở lobby (`DedicatedServerBootstrap` "Waiting for clients").

Đề xuất thêm **1 NetworkBehaviour lobby singleton do server spawn** để UI render danh sách slot dễ dàng:

```text
Assets/Scripts/Multiplayer/InternetRoomLobby.cs        (NetworkBehaviour, server-spawned)
Assets/Scripts/Multiplayer/InternetRoomLobbyController.cs (UI client: slots, code/link, ready/start)
```

Data model (server là authority):

```text
struct LobbySlot : INetworkSerializable, IEquatable<LobbySlot>
{
    ulong clientId;
    int   variantIndex;   // tank body mỗi người chọn
    bool  ready;
    bool  isOwner;        // true cho slot client đầu tiên
}
NetworkList<LobbySlot> Slots;   // server ghi, mọi client đọc → render 4 slot
NetworkVariable<ulong> OwnerClientId;
```

RPC:

- `SetVariantServerRpc(int v)` — client đổi tank body trong lobby. (Có thể tái dùng `LanNetworkBridge.SendVariantServerRpc` đã có; lobby slot mirror lại để render.)
- `SetReadyServerRpc(bool r)` — client toggle Ready.
- `RequestStartServerRpc()` — **chỉ owner**; server validate (`connected >= 2` và mọi slot `ready`) rồi mới load scene.

Server cập nhật `Slots`/`OwnerClientId` trên các sự kiện `OnClientConnected` / `OnClientDisconnected` (gán owner = clientId đầu tiên; nếu owner rời thì chuyển owner cho client kế tiếp hoặc đóng room).

Lưu ý kết nối với gameplay: variant cuối cùng phải đến được init message. Hiện `LanGameCoordinator.SendInitWorldMsg()` đọc `_bridges[i].VariantIndex.Value`. Giữ nguồn sự thật của variant trên **bridge** (đã wired vào init + `ApplyVariantToTank`); lobby slot chỉ mirror để hiển thị. Tank picker trong lobby gọi `SendVariantServerRpc` → set `bridge.VariantIndex` → server cập nhật slot tương ứng.

### 3.3 UI Waiting Lobby (client)

Theo `adds/multiplayer_internet_workflow_plan.md` mục 3.3:

```text
MULTIPLAYER INTERNET
Map: <map> · Mode: <A*/PIBT> · Server: GCP

Room Code: A7C9F2        [COPY CODE]
Invite Link: http://35.240.203.91/play?session=A7C9F2  [COPY LINK]

Players (n/8)
┌────┬──────────────┬───────────┬────────┐
│ #  │ Player       │ Tank      │ Status │
│ 1  │ You (Owner)  │ Blue      │ Ready  │
│ 2  │ Friend       │ Green     │ Ready  │
│ 3  │ Friend 3     │ Red       │ ...    │
│ …  │ …            │ …         │ …      │
│ 8  │ Empty        │ -         │ -      │
└────┴──────────────┴───────────┴────────┘
(số slot = MaxPlayers của room, 2–8; slot trống hiển thị "Empty")
```

Quy tắc:
- Số slot render = `MaxPlayers` (≤ 8); slot chưa có người = "Empty". UI cuộn/giãn được khi 8 người.
- Tank picker (5 body unlocked) ngay trong lobby → `SetVariantServerRpc`.
- `READY` toggle → `SetReadyServerRpc`. UI cập nhật từ `Slots`.
- `START` chỉ enabled cho owner và khi **≥2 người đã Ready và mọi người đang trong lobby đều Ready** (không cần full 8). Owner-only `*`.
- Copy code / copy link: WebGL cần JS interop cho clipboard (`GUIUtility.systemCopyBuffer` không ổn định trên WebGL) — dùng `Application.ExternalEval`/jslib `navigator.clipboard.writeText`.
- Người join bằng link KHÔNG chọn map/mode; map/mode lấy từ registry/server.

Ràng buộc UI: **không** đụng font/glyph/scale theo `adds/webgl_production_ui_rendering_fix_plan.md` (UiFontProvider, NotoSans, BuildWebClient, CanvasScaler). Chỉ thêm copy/text/layout lobby.

### 3.4 Server không auto-load; owner Start

`DedicatedServerBootstrap`:

- Khi `IsDedicatedServer == true`: **bỏ** auto-load (cả `MinPlayersToStart` và grace) — chỉ load scene khi nhận `RequestStartServerRpc` hợp lệ từ owner.
- Giữ `MinPlayersToStart`/grace như **dev fallback chỉ khi build debug/editor** (cờ riêng), không bật trên production. (Hoặc bỏ hẳn auto-load, thêm "Start solo" dev-only trong lobby.)
- Khi nhận start: chốt số người = số client đang trong lobby (đã ready) rồi mới `SceneManager.LoadScene`.

### 3.5 Fix triệt để per-player tank (Bug #2)

1. **PlayerCount xác định, không race**: tại thời điểm owner Start, mọi client đã ở trong lobby. Server set `PlayerCount = số slot đang connect (ready)` ngay trước khi load. Không còn cửa sổ "load khi mới 1 client".
2. **Re-read khi spawn**: `MapTankTestBootstrap` spawn theo số bridge thực tế đang spawned trên server (hoặc theo `PlayerCount` đã chốt từ lobby), không đọc giá trị cũ.
3. **TryLink phải link đủ**: đảm bảo `_serverTanks.Count == số client` để `n = Min(bridges, tanks)` link hết. Nếu sau này hỗ trợ late-join, cần spawn tank động khi bridge mới tới.
4. **Bỏ footgun ownSlot**: trong `LanClientView.OnReceiveInitWorld`, thay default `Mathf.Min(1, playerCount-1)` bằng `-1` và chỉ set ghost/camera khi tìm được đúng `ownerClientId == LocalClientId`; nếu chưa khớp thì chờ init message kế (server resend khi link tăng).
5. **Variant per-player**: mỗi client chọn body riêng trong lobby → `bridge.VariantIndex` riêng → init message gửi đúng variant từng slot → mỗi ghost đúng màu.
6. **Camera per-client**: đã đúng nếu `OwnGhost` resolve đúng slot (`MapTankTestBootstrap.LateUpdate` 141–148). Chỉ cần (4) đảm bảo ownSlot đúng.

## 4. Thay đổi từng file (dự kiến)

Unity runtime:

```text
Assets/Scripts/MenuViewBootstrap.cs
  - Tách nhánh Host (map/mode → create → lobby) và Join (link/code/IP → lobby).
  - Bỏ quyết định tank ở Outfit cho luồng Internet; vào lobby mới chọn.
  - Sau CreateRoom: vào Waiting Lobby thay vì auto-start.

Assets/Scripts/Multiplayer/LanLobbyController.cs  (hoặc tách file mới)
  - Chuyển sang Waiting Lobby networked: slots, code/link, copy, ready, start.
  - Bỏ/giấu nhãn LAN/IP cho build WebGL; giữ cho Editor/Desktop.

Assets/Scripts/Multiplayer/InternetRoomLobby.cs            (MỚI - server state)
Assets/Scripts/Multiplayer/InternetRoomLobbyController.cs  (MỚI - client UI)  [tùy chọn tách khỏi LanLobbyController]

Assets/Scripts/Multiplayer/DedicatedServerBootstrap.cs
  - Bỏ auto-load trên dedicated; load scene chỉ khi RequestStart hợp lệ.
  - Chốt PlayerCount = số client ready ngay trước load.

Assets/Scripts/Multiplayer/LanNetworkBridge.cs
  - (Tùy chọn) thêm NetworkVariable<bool> Ready, hoặc để Ready ở InternetRoomLobby.
  - Tank picker gọi SendVariantServerRpc (đã có).

Assets/Scripts/Multiplayer/LanClientView.cs
  - Bỏ default ownSlot = Min(1, playerCount-1); dùng -1 + chỉ set khi khớp ownerClientId.

Assets/Scripts/MapTankTestBootstrap.cs
  - Spawn số tank = số client thực tế (không đọc PlayerCount cũ/stale).

Assets/Scripts/Multiplayer/LanSessionManager.cs
  - Variant lobby là nguồn sự thật (không đọc PlayerPrefs lúc spawn cho Internet).
```

Registry/infra: không bắt buộc cho lobby (đã có code/link/readiness). Chỉ đụng nếu thêm trạng thái room (`open/starting/in_game`) — để sau.

## 5. Milestones (tăng dần, test được từng bước)

### L1 — Tách Host/Join + vào lobby (không auto-start)
- Menu: Host (map/mode → create → lobby), Join (link/code/IP → lobby).
- Server bỏ auto-load; client connect xong dừng ở overlay "Waiting Lobby" tối thiểu (chưa cần slot đẹp).
- Done: tạo room không vào game ngay; mở 2 tab đều dừng ở lobby, thấy nhau (đếm số người).

### L2 — Slot list + Ready + owner Start
- `InternetRoomLobby` sync `Slots`, `OwnerClientId`.
- UI 4 slot, Ready toggle, START owner-only enabled khi ≥2 ready.
- `RequestStartServerRpc` → server load scene.
- Done: owner thấy START, client thường không; ready đủ thì start được.

### L3 — Tank picker trong lobby + per-player tank ĐÚNG
- Tank picker trong lobby → `SendVariantServerRpc`.
- Fix `LanClientView` ownSlot + spawn đúng số tank theo số client (2–8).
- Done (CHÍNH): n người (2–8) = **n tank riêng**, mỗi người điều khiển tank của mình, camera bám tank của mình, body màu theo lựa chọn từng người. Test ít nhất 2 và 3+ người để chắc không hardcode 2.

### L4 — Room Code + Invite Link + Copy trong lobby
- Hiển thị code + link, nút COPY (WebGL clipboard qua jslib).
- Done: copy link gửi máy khác mở vào đúng lobby.

### L5 — Verify production 2 máy / 2 tab
```text
Tab A: Internet → Host → random + AStar → lobby (owner)
Tab B (ẩn danh): mở invite link → vào cùng lobby
A & B chọn tank khác màu, Ready
A bấm START
Cả hai vào game: 2 tank riêng, enemy = 6 × số người (=12), camera đúng
Mở rộng: thêm 1–2 tab nữa (3–4 người) → mỗi người 1 tank, enemy = 6 × n
Lặp lại với Chantry + PIBT
```

## 6. Rủi ro & ràng buộc

- **KHÔNG đụng** font/glyph/scale WebGL đã fix (`UiFontProvider.cs`, `NotoSans-Regular.ttf`, `BuildWebClient.cs`, CanvasScaler). Mọi thay đổi UI chỉ là text/layout/clipboard.
- WebGL clipboard cần jslib interop; tách commit riêng nếu phát sinh.
- Late-join giữa chừng game chưa hỗ trợ — lobby chốt người trước Start là đủ cho MVP.
- Đổi `DedicatedServerBootstrap` phải giữ LAN host flow (Editor/Desktop) hoạt động: chỉ bỏ auto-load ở nhánh `IsDedicatedServer`.
- Owner rời lobby: cần xử lý chuyển owner hoặc đóng room (tối thiểu: nếu owner rời, hủy room / về menu).
- Mỗi lần tạo room hiện vẫn restart service (1 server dùng chung). Multi-room thật để M6 plan trước.

## 7. Tiêu chí hoàn thành

MVP coi là xong khi:

- Host: chọn map/mode → tạo room → vào Waiting Lobby (KHÔNG vào game ngay).
- Join bằng link/code → vào đúng lobby, không cần nhập IP, không chọn map/mode.
- Lobby hỗ trợ **2–8 người**, hiển thị slot theo `MaxPlayers`, room code + invite link + copy, mỗi người chọn tank body riêng và Ready.
- START chỉ của owner, enabled khi ≥2 người đã Ready (không cần full 8); server không tự start.
- Vào game với số người bất kỳ 2–8: **mỗi người 1 tank riêng**, điều khiển độc lập, camera bám tank của mình, body đúng màu từng người, enemy = 6 × số người (12–48).
- Không còn cảnh 2 người chung 1 tank.
- Giữ nguyên các fix font/UI WebGL và readiness gate đã có.
