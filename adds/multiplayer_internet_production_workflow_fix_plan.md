# Plan: Fix workflow Multiplayer Internet trên production (đúng mục 2) + tái dùng scene chọn tank

Ngày lập: 2026-06-16
Repo: `F:\DATN`  ·  Nhánh: `networking-gcp`
Production: `http://35.240.203.91/`
Tiếp nối: `adds/multiplayer_internet_lobby_workflow_plan.md` (mục 2 = workflow đích) và `adds/multiplayer_internet_lobby_workflow_plan_v2.md` (PHASE D còn dở).

---

## 0. Tóm tắt — vì sao có plan này

Hạ tầng lobby **đã xong** (commit `eca85ce` "Phase B+C: owner-driven start + waiting lobby UI"). Nhưng **luồng menu trên production vẫn là luồng cũ** (PHASE D của v2 chưa làm), nên trải nghiệm sai so với sơ đồ mục 2. Plan này là phần **còn lại**: sửa menu, bỏ bước chọn tank trùng lặp, thêm chỗ nhập room code, fix copy link, và làm sạch vài footgun.

Trọng tâm theo yêu cầu: **tái dùng các màn UI đã có sẵn** (màn "SELECT YOUR TANK", màn map/mode, overlay lobby) — KHÔNG dựng scene mới — để dựng đúng workflow mục 2.

---

## 1. Hiện trạng production vs. mong muốn (ánh xạ theo ảnh)

| Ảnh | Hiện tại trên production | Sai ở đâu | Đúng theo mục 2 |
|-----|--------------------------|-----------|------------------|
| #1 | Nút **"MULTIPLAYER LAN"** ở menu chính | Nhãn sai (đây là Internet, không phải LAN) | Nút **"MULTIPLAYER INTERNET"** |
| #2 | Click → vào thẳng màn **SELECT YOUR TANK** | Chọn tank **trước** map/mode và **trước** khi tạo room | Không có bước này trong luồng Internet; tank chọn **trong lobby** |
| #3 | Chọn tank xong → màn **"LAN — SELECT MAP & MODE"** | Tiêu đề "LAN"; chỉ tới được sau khi đã chọn tank | **"INTERNET — SELECT MAP & MODE"**, là bước đầu của nhánh HOST |
| #5 | Popup lobby lại bắt **"Chọn màu tank"** lần nữa | **Lặp** chọn tank (đã chọn ở #2); chữ status đè lên list người chơi | Chỉ chọn tank 1 lần ở đây; layout không đè |
| #5 | **COPY LINK** không copy được gì vào clipboard | `GUIUtility.systemCopyBuffer` không chạy trên WebGL **+ site là HTTP** (xem §4.3) | Copy link chạy được trên HTTP |
| — | User 2 muốn vào cùng phòng: **không có ô nhập room code** | Trên WebGL chỉ có đường "tạo room + auto-join"; không có nhánh JOIN | Nhánh **JOIN** nhập invite link / room code |

### 1.1 Nguyên nhân trong code (đã verify)

- **Nhãn & luồng** — `MenuViewBootstrap.cs`:
  - Nút menu: `BtnHost` label `"MULTIPLAYER  LAN"`, onClick `_isLanMode = true; ShowScreen(Screen.Outfit)` (dòng ~246–249).
  - NEXT của Outfit: `ShowScreen(_isLanMode ? Screen.LanMapSelect : Screen.MapSelect)` (dòng ~366) → **ép đi qua màn chọn tank**.
  - Màn map/mode: `BuildScreenLanMapSelect` tiêu đề `"LAN  —  SELECT MAP & MODE"` (dòng ~758).
  - Click A*/PIBT → `HandleLanModeSelected` → WebGL gọi `CreateInternetRoomAndJoin` → `LanLobbyController.ShowAndJoin(...)` (dòng ~837–888). **Luôn tạo room + auto-join. Không có nhánh JOIN.**
- **Lặp chọn tank** — overlay lobby `LanLobbyController.BuildLobby` đã có picker "Chọn màu tank" (`PickVariant` → `SubmitVariant`). Vì đã chọn ở Outfit (#2) nên ở lobby (#5) là lần 2 → người dùng thấy lặp.
- **Không có chỗ nhập code** — `LanLobbyController` có sẵn `Screen.Choose` (HOST/JOIN) và `Screen.Joining` (ô nhập "IP / IP:port / invite link / session code"), **nhưng** trên WebGL ta luôn đi đường `ShowAndJoin` → `BeginAutoJoin` → bỏ qua Choose/Joining. → user 2 không bao giờ thấy ô nhập.
- **Copy link** — `LanLobbyController.CopyInvite` (dòng ~614): `GUIUtility.systemCopyBuffer = link;`. Không có `.jslib` clipboard (đã grep: `find Assets -name *.jslib` → rỗng).
- **ownSlot footgun** — `LanClientView.OnReceiveInitWorld` (dòng ~147, ~159–163): default `int ownSlot = Mathf.Min(1, playerCount - 1)` và fallback ép về `Min(1, playerCount-1)` khi nghi race → vẫn là nguồn gốc tiềm ẩn "2 người chung 1 tank" khi >2 người.

### 1.2 Cái gì ĐÃ XONG — không làm lại

- Lobby networked: slot list, READY toggle, owner-only START — `LanLobbyController.Screen.Lobby` + `RefreshLobby`.
- RPC lobby: `LanNetworkBridge.Ready`, `VariantIndex`, `SubmitReady`, `SubmitVariant`, `RequestStartServerRpc`, `ResolveOwnerClientId`.
- Server không auto-load; chỉ load khi owner START — `DedicatedServerBootstrap.BeginGameplayScene` (gọi từ `RequestStartServerRpc`).
- Spawn = số client thực: `MapTankTestBootstrap.SpawnAllLanPlayers` đọc `NetworkManager.ConnectedClients.Count` (dedicated) và set lại `PlayerCount`.
- Link đủ bridge + resend init khi link tăng: `LanGameCoordinator.TryLink` / `SendInitWorldMsg`.
- Variant per-player nguồn sự thật = `bridge.VariantIndex` (init message đọc từ đây; `ApplyVariantToTank`).
- Tank sprites có sẵn cho WebGL runtime: `Assets/Resources/TankSprites/tankBody_*.png` → lobby picker tái dùng được sprite thật.

> Kết luận: đây **không** phải làm lại lobby. Đây là **PHASE D + dư nợ C4 (clipboard) + A2 (ownSlot)** của v2, grounded theo code thật.

---

## 2. Workflow đích (mục 2) — bản WebGL cụ thể

```text
Main Menu
  └─ [MULTIPLAYER INTERNET]
        └─ INTERNET ENTRY  (màn mới, nhỏ)
             ├─ [HOST GAME] ──► INTERNET — SELECT MAP & MODE   (tái dùng màn LanMapSelect, đổi tiêu đề)
             │                     └─ click A*/PIBT ─► CreateInternetRoomAndJoin
             │                                          └─► WAITING LOBBY (owner)
             │
             └─ [JOIN ROOM] ───► JOIN INTERNET ROOM  (tái dùng Screen.Joining của lobby)
                                   nhập invite link / room code
                                   └─ resolve qua registry ─► connect ─► WAITING LOBBY (client)

WAITING LOBBY  (tái dùng LanLobbyController.Screen.Lobby — đổi tiêu đề "MULTIPLAYER INTERNET")
  · Chọn Tank Body (mỗi người)  ← tái dùng style màn SELECT YOUR TANK
  · Room Code + Invite Link + COPY  (fix clipboard HTTP)
  · READY (từng người)
  · Slot list (n / MaxPlayers, 2–8)
  · Owner bấm START khi ≥ 2 đã Ready
        └─► Game bắt đầu: mỗi người 1 tank, enemy = 6 × n

WebGL mở invite link (?session=CODE)  ─► đi thẳng nhánh JOIN ─► WAITING LOBBY
```

Không có bước chọn tank trước map/mode. Người JOIN không chọn map/mode (lấy từ registry).

---

## 3. Brainstorm — tái dùng scene/screen sẵn có (không dựng scene mới)

**Phát hiện cốt lõi:** "màn chọn tank" KHÔNG phải scene riêng — nó là `MenuViewBootstrap._screenOutfit`, dựng **procedural** trong scene `Menu`. Tương tự, map/mode và lobby đều procedural. Vậy "tái dùng scene" = **tái dùng code dựng UI sẵn có và đổi routing**, không tạo `.unity` mới.

Ba khối tái dùng:

1. **Khối chọn tank (`BuildScreenOutfit` / `BuildVariantSwatches` / `SelectVariant`)**
   → giữ nguyên cho **Single Play**. Với Internet: **không** route qua đây nữa.
   → **Tái dùng *visual* của nó cho picker trong lobby**: thay hàng swatch trơn (Image #5) bằng preview sprite + tên màu + ring chọn, load sprite từ `Resources/TankSprites`. Đây là cách "tận dụng màn chọn tank" mà vẫn đúng mục 2 (chọn tank trong lobby).

2. **Khối map/mode (`BuildScreenLanMapSelect` + `BuildLanMapCard`)**
   → tái dùng nguyên, chỉ **đổi tiêu đề** "LAN" → "INTERNET" và **đổi nút Back** về INTERNET ENTRY. Đây là bước đầu của nhánh HOST.

3. **Overlay lobby (`LanLobbyController`)**
   → tái dùng nguyên hạ tầng. `Screen.Joining` (ô nhập) tái dùng cho nhánh JOIN; `Screen.Lobby` là Waiting Lobby. Chỉ đổi tiêu đề, sửa layout đè, thêm static entry mở thẳng Join.

**Quyết định thiết kế (ĐÃ CHỐT):**
- Bỏ hẳn bước Outfit full-screen khỏi luồng Internet; **picker trong lobby bố trí 2 cột giống màn SELECT YOUR TANK**:
  - **Cột trái = PREVIEW**: ô preview tank lớn + tên màu. **Sprite preview ĐỔI MÀU theo swatch đang chọn** (bắt buộc, không còn là tùy chọn).
  - **Cột phải = slot list + hàng swatch màu + room code/COPY + READY/START**.
- → khử lặp (chọn 1 lần), đúng mục 2 (chọn trong lobby), tái dùng tối đa visual + sprite của Outfit. Người dùng vẫn "chọn tank và thấy mô hình đổi màu" như cũ.

```text
┌ MULTIPLAYER INTERNET ────────────────────┐
│ random-32-32 · A* · tối đa 8 người         │
├──────────┬─────────────────────────────────┤
│ PREVIEW  │ Người chơi (2/8)                │
│ ┌──────┐ │  ★ #0 [bạn]  ✓ sẵn sàng         │
│ │ TANK │ │    #1        … chưa             │
│ └──────┘ │                                 │
│  Blue    │ Chọn màu: [B][R][G][D][S]       │
│          │ Mã: AEE017          [COPY LINK] │
├──────────┴─────────────────────────────────┤
│ [✔ SẴN SÀNG]   [▶ START (≥2)]   [CANCEL]   │
└─────────────────────────────────────────────┘
   ↑ preview đổi màu ngay khi bấm swatch
```

---

## 4. Thay đổi từng file (grounded theo dòng thực tế)

### 4.1 `Assets/Scripts/MenuViewBootstrap.cs` — tách Host/Join, bỏ Outfit khỏi Internet

- **Đổi nhãn nút** (dòng ~246–248): `"MULTIPLAYER  LAN"` → `"MULTIPLAYER  INTERNET"`.
- **Đổi onClick nút** (dòng ~249): thay `ShowScreen(Screen.Outfit)` → `ShowScreen(Screen.InternetEntry)`.
- **Thêm `Screen.InternetEntry`** vào enum (dòng 77) và `_screenInternet` GameObject; hiển thị trong `ShowScreen` (dòng ~910).
- **Hàm mới `BuildScreenInternetEntry()`** (mượn helper `MakeButton/MakePanel/MakeText` có sẵn):
  - 2 nút lớn:
    - `[HOST GAME]` → `ShowScreen(Screen.LanMapSelect)` (không qua Outfit).
    - `[JOIN ROOM]` → mở lobby ở chế độ nhập code (xem 4.2): `LanLobbyController.ShowJoinPrompt(DefaultMapFile, "AStar", GetRuntimeRegistryBaseUrl())`.
  - 1 nút Back → `ShowScreen(Screen.Main)`.
- **Đổi tiêu đề màn map/mode** (`BuildScreenLanMapSelect`, dòng ~758): `"LAN  —  SELECT MAP & MODE"` → `"INTERNET  —  SELECT MAP & MODE"`; nút Back (dòng ~781) `ShowScreen(Screen.Outfit)` → `ShowScreen(Screen.InternetEntry)`.
- **NEXT của Outfit** (dòng ~366): bỏ nhánh LAN — chỉ còn Single Play: `ShowScreen(Screen.MapSelect)`. Có thể bỏ field `_isLanMode` (không còn ai set true cho Internet) hoặc giữ cho desktop LAN (xem §6).
- **`TryAutoJoinInternetSession`** (dòng ~122): giữ nguyên — auto-join từ `?session=` vẫn dẫn vào lobby (đúng nhánh JOIN). Đảm bảo truyền registry base URL từ `Application.absoluteURL`.
- **Desktop/Editor (non-WebGL):** `HandleLanModeSelected` (dòng ~845) vẫn gọi `LanLobbyController.Show(...)` (có sẵn Choose Host/Join) — không phá LAN flow. Nhánh Host/Join ở menu chủ yếu phục vụ WebGL.

### 4.2 `Assets/Scripts/Multiplayer/LanLobbyController.cs` — entry JOIN, tiêu đề, layout, picker, START≥2

- **Static entry mới `ShowJoinPrompt(mapFile, algorithm, registryUrl)`**: như `Show` nhưng sau `Build()` → `SwitchTo(Screen.Joining)` (không auto-join). Seed `LanSessionManager` registry = `registryUrl` để code trống resolve được (xem 4.4).
- **Tiêu đề** (`BuildHeader`, dòng ~216): `"MULTIPLAYER  LAN"` → trên WebGL hiển thị `"MULTIPLAYER  INTERNET"` (branch `#if UNITY_WEBGL && !UNITY_EDITOR`, giữ "LAN" cho desktop). Sub-line (dòng ~221) giữ `map · algo · tối đa N người`.
- **Bố trí lại lobby 2 cột (ĐÃ CHỐT — fix luôn layout đè Image #5):** layout hiện tại 1 cột, `_statusTxt` (y=-88, h=56) đè `_lobbyBox` (y=-84). Thay bằng 2 cột:
  - **Mở rộng panel:** `PW` 520 → ~720 (giữ `PH` ~480, hoặc tăng nhẹ nếu cần). Cập nhật các mốc bottom-anchored của footer (`BtnFull` dùng `FullW` nên tự co theo `PW`).
  - **Cột trái PREVIEW** (mới): ô `Image` preview + `Text` tên màu, đặt ở nửa trái của `_lobbyBox`. Đây là phần "tái dùng SELECT YOUR TANK".
  - **Cột phải:** dời `Slots` (slot list), `Picker` (hàng swatch), `Code` + `COPY LINK` sang nửa phải; bỏ/thu gọn `_statusTxt` ở scene Lobby (status 1 dòng sát header hoặc ẩn) để không đè.
- **Preview sprite ĐỔI MÀU (bắt buộc):**
  - Thêm field `Image _lobbyTankPreview` + `Text _lobbyTankName`.
  - Load sprite: WebGL dùng `Resources.Load<Sprite>("TankSprites/tankBody_<color>")` (bộ file đã xác nhận tồn tại); Editor có thể `AssetDatabase`. Tái dùng đúng cơ chế `MenuViewBootstrap.LoadSprite`/`SelectVariant` — cân nhắc tách helper dùng chung để không lặp code.
  - Map index→file: 0 blue, 1 red, 2 green, 3 dark, 4 sand (khớp `Variants[0..4]` của `MenuViewBootstrap` và `VariantColors` của lobby).
  - Cập nhật preview + tên + **ring chọn** quanh swatch trong `PickVariant`; và đồng bộ từ `local.VariantIndex.Value` mỗi tick trong `RefreshLobby` (để đúng khi server xác nhận / khi seed từ PlayerPrefs).
- **Swatch:** thêm tên màu dưới mỗi ô + ring chọn (giống `_variantRings` ở Outfit). `VariantName` đã có sẵn (đổi label nếu muốn).
- **START ≥2 (mục 2.0):** `RefreshLobby` (dòng ~585) đang cho start khi `bridges.Count >= 1 && readyCount == bridges.Count` (owner 1 mình cũng start được). Đổi điều kiện UI: `allReady = bridges.Count >= MinStart && readyCount == bridges.Count` với `MinStart = 2` (production); cho override `MinStart=1` trong Editor để test solo. Label khi chưa đủ: `"START  ({readyCount}/{bridges.Count} · cần ≥2)"`.
  - Đồng bộ server: `LanNetworkBridge.RequestStartServerRpc` (dòng ~229) đổi `if (nm.ConnectedClients.Count < 1)` → `< MinStart` (cùng giá trị; có thể đọc từ `LanSessionManager`). Giữ check "mọi bridge Ready".
- **Copy link:** `CopyInvite` (dòng ~614) gọi `WebClipboard.Copy(link)` thay cho `GUIUtility.systemCopyBuffer` (xem 4.3).

### 4.3 Clipboard WebGL (mới) — và lưu ý HTTP

> **Nguyên nhân gốc copy fail trên production:** `GUIUtility.systemCopyBuffer` không hoạt động trên WebGL. Quan trọng hơn: site chạy **HTTP** (`http://35.240.203.91`), KHÔNG phải HTTPS → `navigator.clipboard.writeText` bị chặn ("insecure context", chỉ chạy ở HTTPS/localhost). **Bắt buộc có fallback `document.execCommand('copy')`** (chạy được trên HTTP trong user-gesture click) thì copy mới hoạt động trên production hiện tại.

- **`Assets/Plugins/WebGL/WebClipboard.jslib`** (mới):
  ```javascript
  mergeInto(LibraryManager.library, {
    JsCopyToClipboard: function (strPtr) {
      var str = UTF8ToString(strPtr);
      try {
        if (window.isSecureContext && navigator.clipboard && navigator.clipboard.writeText) {
          navigator.clipboard.writeText(str);
          return;
        }
      } catch (e) { /* fall through to execCommand */ }
      try {
        var ta = document.createElement('textarea');
        ta.value = str;
        ta.style.position = 'fixed'; ta.style.opacity = '0';
        document.body.appendChild(ta);
        ta.focus(); ta.select();
        document.execCommand('copy');
        document.body.removeChild(ta);
      } catch (e) { console.warn('[WebClipboard] copy failed', e); }
    }
  });
  ```
- **`Assets/Scripts/Multiplayer/WebClipboard.cs`** (mới):
  ```csharp
  using UnityEngine;
  using System.Runtime.InteropServices;
  public static class WebClipboard {
  #if UNITY_WEBGL && !UNITY_EDITOR
      [DllImport("__Internal")] private static extern void JsCopyToClipboard(string s);
  #endif
      public static void Copy(string s) {
          if (string.IsNullOrEmpty(s)) return;
  #if UNITY_WEBGL && !UNITY_EDITOR
          JsCopyToClipboard(s);
  #else
          GUIUtility.systemCopyBuffer = s;
  #endif
      }
  }
  ```
- KHÔNG đụng `BuildWebClient.cs`/font/scale (xem §6). `.jslib` là plugin tự nhận khi build WebGL.

### 4.4 JOIN bằng room code — seed registry URL

> Khi user 2 gõ mã trống (vd `AEE017`), `InternetJoinParser.TryParse` set `config.registryUrl = fallbackRegistryUrl`. Nhánh `DoConnect` truyền `LanSessionManager.RegistryUrl` làm fallback — **nhưng nếu mở join từ menu thì RegistryUrl đang rỗng** → `ResolveSessionAndConnect` báo "Registry URL is missing".

- Fix: trên WebGL, registry của mã trống = **origin trang hiện tại**. Hai cách (chọn 1):
  - (a) `ShowJoinPrompt(... , registryUrl)` seed `LanSessionManager` registry trước khi vào `Screen.Joining`; hoặc
  - (b) trong `DoConnect`, nếu mã trống và `LanSessionManager.RegistryUrl` rỗng → derive từ `Application.absoluteURL` (giống `MenuViewBootstrap.GetRuntimeRegistryBaseUrl`).
- Khuyến nghị (a): rõ ràng, không rải logic URL.

### 4.5 `Assets/Scripts/Multiplayer/LanClientView.cs` — ownSlot strict (hardening per-player tank)

- `OnReceiveInitWorld` (dòng ~147): đổi default `int ownSlot = Mathf.Min(1, playerCount - 1)` → `int ownSlot = -1`.
- Vòng đọc slot (dòng ~149–154): chỉ set `ownSlot = i` khi `ownerClientId == myClientId`.
- Bỏ fallback ép `Min(1, playerCount-1)` (dòng ~159–163). Nếu sau vòng lặp `ownSlot < 0` (LocalClientId chưa gán — race): **không** `InitGhosts` với slot đoán; log warning và **chờ init resend** (server đã `ResendInit` khi link tăng / variant đổi — `LanGameCoordinator`). Có thể đặt cờ để áp dụng ngay khi init kế tới.
- Đây là dư nợ A2 của v2 — quan trọng khi N>2 để không 2 client trùng slot 0.

---

## 5. Milestones (nhỏ, test được, push prod từng bước)

> Mỗi MS = 1 commit gọn. Gom theo nhóm rồi mới build/deploy WebGL để tiết kiệm CI (~10–20').

| MS | Nội dung | File | Done khi |
|----|----------|------|----------|
| **M1** | Đổi nhãn + INTERNET ENTRY (Host/Join) + đổi tiêu đề màn map/mode; Host bỏ qua Outfit | `MenuViewBootstrap.cs` | Menu hiện "MULTIPLAYER INTERNET"; click → 2 nút Host/Join; Host → INTERNET map/mode → tạo room → lobby (không qua chọn tank) |
| **M2** | Nhánh JOIN nhập room code/link từ menu | `LanLobbyController.cs` (`ShowJoinPrompt`), `MenuViewBootstrap.cs`, seed registry (§4.4) | User 2 bấm JOIN → ô nhập → gõ `AEE017` → resolve → vào đúng lobby |
| **M3** | Copy link chạy trên HTTP | `WebClipboard.jslib` + `WebClipboard.cs` (mới), `LanLobbyController.CopyInvite` | Bấm COPY LINK → link vào clipboard (test trên production HTTP) |
| **M4** | Lobby 2 cột: cột PREVIEW (sprite đổi màu) + cột slot/swatch/code; START≥2 | `LanLobbyController.cs` | #5 hết đè chữ; **bấm swatch → preview tank đổi màu + tên + ring**; START chỉ bật khi ≥2 ready; chọn tank 1 lần (không còn bước Outfit) |
| **M5** | ownSlot strict (hardening) | `LanClientView.cs` | N=2 và N=3 đều mỗi người 1 slot riêng, không client nào rơi vào slot 0 sai |
| **M6** | Verify production 2 tab + scale | — | Theo §7 |

Phụ thuộc: `M1 → M2 → M3 → M4 → M5 → M6`. M1–M4 đều "visible workflow"; M5 là robustness.

---

## 6. Ràng buộc & rủi ro

- **KHÔNG đụng** fix font/glyph/scale WebGL: `UiFontProvider.cs`, `NotoSans-Regular.ttf`, `BuildWebClient.cs`, `CanvasScaler`. Mọi thay đổi UI chỉ là **text/layout/routing/clipboard**.
- **HTTP, không HTTPS:** đây là lý do copy fail; bắt buộc fallback `execCommand` (§4.3). Nếu sau này lên HTTPS thì `navigator.clipboard` mới chạy thẳng.
- **Giữ LAN flow Editor/Desktop:** non-WebGL vẫn dùng `LanLobbyController.Show` (Choose Host/Join + nhập IP). Nhánh Host/Join ở menu là cải tiến WebGL; đừng phá đường desktop.
- **Giữ readiness gate (M2 cũ)** và `DedicatedServerBootstrap` owner-start nguyên vẹn — chỉ chỉnh `MinStart` cho khớp ≥2.
- **Input chạy trong lobby:** `LanNetworkBridge.Update()` vẫn sample input ở scene lobby (server `_serverTank==null` nên vô hại, client prediction đã null-guard). Không bắt buộc sửa cho MVP.
- **Owner rời lobby:** chưa xử lý chuyển owner — tối thiểu nếu owner rời thì client về menu / báo room đóng (để E2 của v2; ngoài phạm vi MVP này).
- **Một server dùng chung:** mỗi lần tạo room vẫn restart service (M6 plan trước). Multi-room thật để sau.

---

## 7. Test plan (đúng sơ đồ mục 2)

**T-prod (2 tab):**
```text
Tab A (owner): Menu → MULTIPLAYER INTERNET → HOST → Alpha-32 → A*
   → vào Waiting Lobby; KHÔNG bị hỏi chọn tank trước đó
   → thấy Room Code + COPY LINK (bấm copy → dán ra notepad kiểm tra)
Tab B (ẩn danh): Menu → MULTIPLAYER INTERNET → JOIN → dán link HOẶC gõ room code
   → vào ĐÚNG lobby của A (thấy 2 slot)
A & B mỗi người chọn màu tank khác nhau → READY
A bấm START (chỉ bật khi cả 2 ready)
   → cả hai vào game: 2 tank riêng, camera bám tank mình, body đúng màu, enemy = 12 (=6×2)
```
**T-scale:** mở thêm 1–2 tab (3–4 người) → mỗi người 1 tank, enemy = 6×n, không ai trùng slot.
**T-regress:** lặp với Chantry + PIBT; kiểm font/UI WebGL & readiness gate vẫn nguyên.

---

## 8. Định nghĩa "Done"

- Menu hiển thị **MULTIPLAYER INTERNET**; vào là chọn **HOST** hoặc **JOIN** (có ô nhập room code/link).
- Luồng Internet **không còn bước chọn tank trước map/mode**; tank chọn **một lần** trong lobby, bố trí **2 cột giống SELECT YOUR TANK**, và **preview mô hình tank đổi màu theo swatch** đang chọn.
- **COPY LINK chạy trên production HTTP** (qua `execCommand` fallback).
- Lobby (#5) **hết đè chữ**; START chỉ bật khi **≥2 đã Ready**; owner-only.
- Vào game 2–8 người: **mỗi người 1 tank riêng**, điều khiển độc lập, camera đúng, body đúng màu, enemy = 6×n; **không còn 2 người chung 1 tank**.
- Giữ nguyên fix font/UI WebGL, readiness gate, và LAN flow Editor/Desktop.
