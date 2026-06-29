# Plan v2: Hoàn thiện Multiplayer Internet (2–8 người) — roadmap milestone nhỏ

Ngày lập: 2026-06-14
Repo: `D:\2025.2\DATN\projectY` · Nhánh: `networking-gcp`
Tiếp nối:
- `adds/multiplayer_internet_workflow_plan.md` (M0–M6 gốc)
- `adds/multiplayer_internet_lobby_workflow_plan.md` (v1 — kiến trúc lobby + 2 bug)

Plan v1 đã mô tả ĐÚNG kiến trúc và root cause. Plan v2 này **không đổi mục tiêu**, chỉ **chia nhỏ thành các milestone độc lập, test được từng bước**, sắp xếp theo rủi ro để về đích "nhiều người (2–8) chơi với nhau qua Internet, mỗi người 1 tank riêng".

## 0. Mục tiêu cuối

Người A mở `http://35.240.203.91/` → tạo room → vào Waiting Lobby. Người B…H mở invite link/nhập code → vào cùng lobby. Mỗi người chọn tank body riêng, Ready. Owner bấm START (khi ≥2 ready) → tất cả vào cùng game, **mỗi người điều khiển 1 tank riêng**, camera bám tank của mình, enemy = 6 × số người.

Đã xong (nền): M1 invite link, M2 readiness gate, M4-lite 2-player gating (commit `3de25b1`, `4895c20`).

## 1. Nguyên tắc chia milestone

1. **Mỗi milestone là một thay đổi nhỏ, có tiêu chí Done rõ và cách test cụ thể.**
2. **Fix bug lõi trước feature.** Bug "2 người 1 tank" (Phase A) ưu tiên cao nhất — test được tại chỗ bằng LAN, và có thể lên production sớm với gating hiện có.
3. **Test ở Editor/LAN trước khi đốt chu kỳ CI.** Phần lớn logic networked (spawn, link, ownSlot, sync lobby) là transport-agnostic → kiểm trong harness LAN local. Chỉ push CI để smoke production theo từng *phase* (không từng milestone).
4. **Không phá LAN flow cũ** (Editor/Desktop host) và **không đụng** fix font/UI WebGL (`UiFontProvider`, `NotoSans`, `BuildWebClient`, CanvasScaler — xem `adds/webgl_production_ui_rendering_fix_plan.md`).

## 2. Chiến lược test (giảm phụ thuộc CI)

| Tầng | Cách | Bắt được gì | Tốc độ |
|---|---|---|---|
| **T1 — LAN host trong Editor + 1–2 standalone build client (cùng PC/LAN)** | Editor `HOST GAME`; build Desktop client `JOIN` bằng IP | spawn N tank, link bridge, ownSlot, camera, variant, sync lobby NetworkList | nhanh, không CI |
| **T1b — Unity 6 Multiplayer Play Mode (nếu bật được)** | virtual players trong Editor | như T1 nhưng không cần build client | rất nhanh |
| **T2 — Local dedicated build (Windows standalone chạy `--dedicated-server`) + Editor/standalone client** | mô phỏng đúng mô hình "server không có người chơi, owner = client đầu" | logic dedicated/owner/start | trung bình |
| **T3 — Production smoke (sau mỗi phase)** | push → CI rebuild WebGL+server (~10–20') → mở 2–8 tab/ẩn danh | WS transport thật, end-to-end | chậm |

Ghi chú: đường dedicated chỉ kích hoạt khi có arg `--dedicated-server` (`NetworkLaunchArgs.Parse`). Editor không có arg đó → dùng T1 (LAN host) cho logic chung, T2 cho riêng phần dedicated/owner.

## 3. Roadmap milestone

Sắp theo phase. Mỗi phase push CI 1 lần để smoke production.

### PHASE A — Fix "2 người 1 tank" (bug lõi, test LAN, lên prod sớm)

> ✅ **HOÀN TẤT & ĐÃ VERIFY TRÊN PRODUCTION** (2 máy, 2 mạng khác nhau, mỗi người 1 tank riêng).
> Commits: `ca7e710` (A1+A2 spawn/ownSlot), `c67d1dd` (grace reset), `51fdc18` (transport
> queue + input throttle + framerate cap), `d18576d` (WebGL force-WebSocket + chặn vòng lặp
> self-disconnect). A3 (variant per-player) đã có sẵn qua `bridge.VariantIndex`. A4 smoke prod ✅.
>
> Mục tiêu phase: với N client connect (LAN hoặc internet) **trước khi vào game**, mỗi client có 1 tank riêng. Không cần lobby UI. Có thể ship lên prod cùng gating M4-lite hiện có để chữa bug 2 người ngay.

**A1 — Spawn đúng số tank = số client thực tế.**
- Sửa `MapTankTestBootstrap.SpawnAllLanPlayers()`: lấy `n` từ số client đang connect tại thời điểm scene load (server đọc `NetworkManager.ConnectedClients.Count`, hoặc số bridge đã spawned), không dùng `LanSessionManager.PlayerCount` cũ/stale. Cập nhật `PlayerCount` = n trước khi spawn để `EnemyCount` đúng.
- Test (T1): LAN host (editor) + 1 standalone client → server spawn **2 tank**; log `[Coordinator] 2 tanks ... 2 bridges`.
- Done: số tank == số client mọi lần.

**A2 — Link đủ bridge + ownSlot nghiêm ngặt.**
- `LanGameCoordinator.TryLink()`: đảm bảo `_serverTanks.Count >= bridges` để `n=Min(...)` link hết (đến từ A1).
- `LanClientView.OnReceiveInitWorld()`: bỏ default `ownSlot = Mathf.Min(1, playerCount-1)` → đặt `ownSlot = -1`, chỉ set khi `ownerClientId == LocalClientId`; nếu chưa khớp thì giữ -1, chờ init message kế (server resend khi link tăng). Camera/own-ghost chỉ gán khi ownSlot hợp lệ.
- Test (T1): 2 và **3** client → mỗi client điều khiển đúng tank của mình, camera bám tank mình, không ai điều khiển chung.
- Done: N=2 và N=3 đều đúng own-tank/own-camera.

**A3 — Tank body (variant) đúng theo từng người.**
- Variant theo từng client đã có (`bridge.VariantIndex`, gửi qua init). Đảm bảo mỗi client gửi variant của mình (`SendVariantServerRpc` lúc spawn bridge) và init message phản ánh đúng per-slot.
- Test (T1): mỗi client để variant khác nhau (tạm qua PlayerPrefs khác máy) → ghosts đúng màu trên mọi máy.
- Done: màu tank từng người khớp trên tất cả client.

**A4 — Smoke production Phase A.**
- Push (CI rebuild) → mở 2 tab: cùng vào game (nhờ gating M4-lite), **2 tank riêng**. Thử 3 tab.
- Done: trên prod, 2–3 người mỗi người 1 tank. (Workflow vẫn cũ, nhưng bug lõi đã hết.)

### PHASE B — Start tường minh (owner-driven), bỏ auto-load

> Mục tiêu: khử hẳn race; server chỉ vào game khi owner ra lệnh. Chưa cần UI đẹp.
>
> **DEVIATION (quyết định khi triển khai):** KHÔNG tạo `InternetRoomLobby` NetworkObject mới.
> Thay vào đó **tái dùng `LanNetworkBridge`** (đã sync `Slot`/`VariantIndex`, đã chứng minh chạy
> end-to-end qua 2 mạng): thêm `NetworkVariable<bool> Ready`, owner = client có `OwnerClientId`
> nhỏ nhất (derived, không cần state mới), start qua `RequestStartServerRpc` trên bridge.
> Lý do: vừa ổn định kết nối, tránh thêm NetworkObject mới (rủi ro vòng đời scene-management +
> đăng ký prefab 2 phía). State lobby cho UI (Phase C) = scan `FindObjectsByType<LanNetworkBridge>`.

**B1 — InternetRoomLobby (server state) + owner.**
- Thêm `Assets/Scripts/Multiplayer/InternetRoomLobby.cs` (NetworkBehaviour). Server spawn 1 instance sau `StartServer` (trong `DedicatedServerBootstrap`, hoặc cả LAN host).
- State: `NetworkVariable<ulong> OwnerClientId` (gán = client đầu connect), `NetworkList<LobbySlot> Slots` (clientId, variantIndex, ready, isOwner). Cập nhật trên `OnClientConnected/Disconnected`.
- Test (T1/T2): client connect thấy `Slots` đồng bộ; owner = người đầu.
- Done: slot list + owner sync đúng khi vào/ra.

**B2 — RequestStartServerRpc + bỏ auto-load dedicated.**
- `DedicatedServerBootstrap`: nếu `IsDedicatedServer` → KHÔNG auto-load (bỏ MinPlayersToStart/grace ở nhánh này; giữ làm dev fallback chỉ editor/debug).
- `InternetRoomLobby.RequestStartServerRpc()`: chỉ owner; validate `connected>=2` và mọi slot `ready`; rồi `LanSessionManager.PlayerCount = connected` và load scene.
- Test (T2): local dedicated build + 2 client; chỉ owner trigger được start; <2 không start.
- Done: server chỉ vào game khi owner start hợp lệ; số người chốt = số client lúc start.

**B3 — Ready + variant qua lobby.**
- RPC: `SetReadyServerRpc(bool)`, `SetVariantServerRpc(int)` (có thể tái dùng cái trên bridge). Server mirror variant slot → `bridge.VariantIndex` để path init/spawn cũ không đổi.
- Test (T1/T2): toggle ready/variant phản ánh trong `Slots` trên mọi client.
- Done: ready/variant sync; start chặn khi chưa đủ ready.

**B4 — Smoke production Phase B (UI tạm).**
- Push. Test 2 tab với UI tối thiểu (nút Ready/Start tạm) → owner start, cả 2 vào game, mỗi người 1 tank.
- Done: end-to-end start tường minh chạy trên prod.

### PHASE C — Waiting Lobby UI (client)

> Mục tiêu: UI lobby đúng như sơ đồ, 2–8 slot. Chỉ text/layout/clipboard (không đụng font/scale).

**C1 — Slot list UI (2–8).**
- `InternetRoomLobbyController` (hoặc mở rộng `LanLobbyController`): render slot từ `Slots`, số slot = `MaxPlayers` (≤8), slot trống = "Empty"; cuộn/giãn khi 8.
- Test (T1): vào/ra thấy slot cập nhật realtime.
- Done: bảng người chơi đúng số, đúng tên/owner.

**C2 — Tank picker + Ready trong lobby.**
- Picker 5 body unlocked → `SetVariantServerRpc`; nút `READY` → `SetReadyServerRpc`; cột Tank/Status cập nhật từ `Slots`.
- Done: đổi tank + ready phản ánh ngay cho mọi người.

**C3 — Nút START (owner-only) + CANCEL/Leave.**
- START enabled chỉ owner và khi ≥2 ready (mọi người ready); CANCEL rời lobby (shutdown client, về menu).
- Done: client thường không thấy/không bấm được START.

**C4 — Room Code + Invite Link + Copy.**
- Hiển thị code (`LanSessionManager.SessionCode`) + link `/play?session=<code>`; nút COPY dùng jslib WebGL (`navigator.clipboard.writeText`), fallback `GUIUtility.systemCopyBuffer` ngoài WebGL. Tách commit riêng nếu cần thêm `.jslib`.
- Done: copy link mở được trên máy khác → vào đúng lobby.

**C5 — Smoke production Phase C.**
- Push. Test 2–4 tab: lobby đẹp, chọn tank, ready, owner start.
- Done: workflow lobby hoàn chỉnh trên prod.

### PHASE D — Tái cấu trúc menu (Host vs Join, chuyển chọn tank)

> Mục tiêu: vào lobby đúng từ 2 nhánh; bỏ chọn tank trước khi tạo room.

**D1 — Tách nhánh Host / Join ở menu.**
- `MenuViewBootstrap`: Host = `INTERNET — SELECT MAP & MODE` → create → vào lobby (KHÔNG auto game). Join = màn `JOIN INTERNET ROOM` nhận invite link / room code / IP → resolve → connect → lobby.
- Done: 2 nhánh dẫn về cùng Waiting Lobby.

**D2 — Bỏ chọn tank pre-create cho Internet.**
- Outfit screen không quyết định tank cho Internet; tank chọn trong lobby (C2). Variant nguồn sự thật = lobby/bridge, không đọc PlayerPrefs lúc spawn cho Internet (`MapTankTestBootstrap.ApplyTankVariant`).
- Done: không còn bước chọn tank trước map/mode ở luồng Internet.

**D3 — WebGL auto-join từ invite link → lobby.**
- `TryAutoJoinInternetSession` (`MenuViewBootstrap:122`) dẫn vào nhánh Join → Waiting Lobby (không auto game).
- Done: mở link trên tab khác → vào thẳng lobby.

**D4 — Smoke production Phase D.**
- Push. Test đúng sơ đồ: Host tạo → lobby; Join bằng link/code → lobby; không nhập IP; không chọn tank trước.
- Done: workflow khớp 100% sơ đồ người dùng.

### PHASE E — Verify quy mô + edge case

**E1 — Scale 3–8 người.** Mở 3–8 tab/máy: mỗi người 1 tank, enemy = 6×n (12–48), camera đúng, không lag chết.
**E2 — Owner rời / client rời.** Owner rời lobby → chuyển owner hoặc đóng room; client rời → slot cập nhật.
**E3 — Phòng đầy / hết hạn.** Đủ `MaxPlayers` → client thứ N+1 bị từ chối với message rõ; session expire xử lý gọn.
**E4 — Chốt regression.** A*/PIBT, random/Chantry; readiness gate + font/UI vẫn nguyên.

## 4. Phụ thuộc & thứ tự

```text
A1 → A2 → A3 → A4(prod)         # fix bug lõi, ship sớm
                 │
                 ▼
B1 → B2 → B3 → B4(prod)         # start tường minh
                 │
                 ▼
C1 → C2 → C3 → C4 → C5(prod)    # lobby UI
                 │
                 ▼
D1 → D2 → D3 → D4(prod)         # menu flow
                 │
                 ▼
E1..E4                          # verify 2–8 + edge
```

- Phase A độc lập, giá trị cao nhất, lên prod được ngay với gating cũ.
- Phase B cần A (spawn đúng) để start tường minh có ý nghĩa.
- Phase C cần B (state có sẵn để render).
- Phase D cần C (lobby để dẫn vào).

## 5. Bảng tổng hợp milestone

| MS | Nội dung | File chính | Test | CI? |
|----|----------|-----------|------|-----|
| A1 | Spawn = số client thực | MapTankTestBootstrap | T1 | cuối phase |
| A2 | Link đủ bridge + ownSlot strict | LanGameCoordinator, LanClientView | T1 (N=2,3) | cuối phase |
| A3 | Variant per-player | LanClientView, LanNetworkBridge | T1 | cuối phase |
| A4 | Smoke prod | — | T3 | ✅ push |
| B1 | InternetRoomLobby + owner | InternetRoomLobby (mới), DedicatedServerBootstrap | T1/T2 | cuối phase |
| B2 | RequestStart + bỏ auto-load | DedicatedServerBootstrap, InternetRoomLobby | T2 | cuối phase |
| B3 | Ready + variant qua lobby | InternetRoomLobby, LanNetworkBridge | T1/T2 | cuối phase |
| B4 | Smoke prod | — | T3 | ✅ push |
| C1 | Slot list UI 2–8 | InternetRoomLobbyController (mới)/LanLobbyController | T1 | cuối phase |
| C2 | Tank picker + Ready | LanLobbyController | T1 | cuối phase |
| C3 | START owner-only + Cancel | LanLobbyController | T1/T2 | cuối phase |
| C4 | Code + Link + Copy | LanLobbyController, .jslib | T1 | cuối phase |
| C5 | Smoke prod | — | T3 | ✅ push |
| D1 | Tách Host/Join | MenuViewBootstrap | T1 | cuối phase |
| D2 | Bỏ chọn tank pre-create | MenuViewBootstrap, MapTankTestBootstrap | T1 | cuối phase |
| D3 | Auto-join link → lobby | MenuViewBootstrap | T3 | cuối phase |
| D4 | Smoke prod | — | T3 | ✅ push |
| E1–E4 | Verify 2–8 + edge | — | T3 | — |

## 6. Rủi ro & lưu ý

- **Bridge input chạy trong scene lobby**: `LanNetworkBridge.Update()` sample input mỗi frame kể cả ở lobby (server `_serverTank==null` nên vô hại, client prediction null-guarded). Nên gate input cho tới khi game start để sạch — minor.
- **Spawn InternetRoomLobby**: cần đăng ký network prefab + server spawn sau `StartServer`; clients nhận `NetworkList`. Kiểm `EnableSceneManagement=true` không nuốt object qua scene load.
- **Owner trong mô hình dedicated** = client đầu tiên, không phải server. Phải test bằng T2 (local dedicated build) vì Editor không chạy nhánh dedicated.
- **Clipboard WebGL** cần `.jslib`; tách commit riêng, không đụng `BuildWebClient.cs` trừ khi bắt buộc.
- **Mỗi phase 1 lần push CI** (~10–20'); gộp trọn milestone của phase rồi mới push để tiết kiệm.
- **Giữ readiness gate (M2)** và LAN flow Editor/Desktop nguyên vẹn.
- **Late-join giữa game** chưa hỗ trợ — chốt người trước Start là đủ MVP.

## 7. Định nghĩa "Done" toàn cục

- 2–8 người qua Internet: Host tạo room → lobby; Join bằng link/code → lobby; mỗi người chọn tank riêng + Ready; owner START khi ≥2 ready.
- Trong game: mỗi người 1 tank riêng, điều khiển độc lập, camera đúng, body đúng màu, enemy = 6×n.
- Không còn "2 người 1 tank"; không còn "Mất kết nối với host" cho lỗi có nguyên nhân cụ thể.
- Readiness gate + fix font/UI WebGL giữ nguyên.
