# Plan chẩn đoán & sửa lỗi "Mất kết nối với host" (Internet / WebGL)

> Trạng thái: foundation kết nối CHƯA ổn định. Không bắt đầu Phase B/C/D của
> `multiplayer_internet_lobby_workflow_plan_v2.md` cho tới khi 1 client WebGL
> connect được và **giữ** kết nối qua màn chờ + lúc server load game scene.

---

## ✅ CẬP NHẬT — ĐÃ XÁC ĐỊNH NGUYÊN NHÂN (browser console), commit `d18576d`

Browser console của user **bác bỏ H1 (scene-sync)**. Log thật:
```
WebSockets were used even though they're not selected in NetworkManager...
[LAN] Connecting to 35.240.203.91:7778 via websocket → Connected → Disconnected
[LAN] Connecting to 35.240.203.91:7778 via udp        → (warning) → Disconnected   ← UDP trên WebGL!
... lặp lại, luân phiên ws/udp
```

**Nguyên nhân thật = client TỰ đá kết nối của mình (không phải server/scene-sync):**
1. **Forced-UDP trên WebGL:** retry path `RetryConnect → DoConnect(_pendingIp="host:7778")`
   re-parse chuỗi raw "host:port" → `transportMode` mất, default về **UDP**. WebGL không
   có UDP ⇒ một nửa số lần thử chết chắc + warning "WebSockets were used…".
2. **RetryConnect đá session đang sống:** `BeginClientConnection:687` lên lịch
   `Invoke(RetryConnect, 0.5f)`; khi nó fire lúc client ĐÃ connect → `Shutdown()` chính
   connection vừa thành công ⇒ "Connected → Disconnected". Đây là lý do "20 lần mới được 1"
   (chỉ khi timing né được retry mới giữ được).
3. Vòng auto-join lặp: `TryAutoJoinInternetSession` (MenuViewBootstrap:119) chạy mỗi lần
   Menu load + retry loop ⇒ liên tục reconnect.

**Fix đã áp (commit `d18576d`):**
- WebGL **luôn** `UseWebSockets=true` (NetworkManagerFactory.ConfigureTransport +
  BeginClientConnection) — không bao giờ rơi về UDP.
- `OnClientConnected`: `CancelInvoke(RetryConnect)` + clear `_pendingIp` + set `_connected`.
- `BeginClientConnection` & `BeginAutoJoin`: bail-out nếu đã connected (không tear-down
  session sống).

→ Compile sạch (Unity MCP, 0 error). Chờ CI deploy rồi smoke test. Nếu vẫn drop sau fix này
thì mới quay lại H1 (mục 5) — nhưng bằng chứng console cho thấy không cần.

---

---

## 1. Đã FIX và xác nhận trên production (build `51fdc18`)

| Vấn đề | Bằng chứng đã hết |
|---|---|
| Zombie state: process 99% CPU, mất hết socket, 7778 ngừng listen | Sau deploy: 7778 listen ổn định (fd=8), CPU ~6%, qua nhiều chu kỳ connect/disconnect vẫn listen |
| `Receive queue is full (128)` → drop sau khi load scene | Không còn xuất hiện trong run mới (đã nâng `MaxPacketQueueSize=128→1024` cả 2 phía; throttle input RPC 144fps→30Hz) |
| Grace-timer load scene với 0 player | Log mới: "All clients disconnected — timers reset, waiting for fresh connections" |
| SyncLoop spin/spam khi 0 client | Đã guard `BroadcastWorldState` bỏ qua khi `ConnectedClientsIds.Count == 0` |

→ Các lỗi trên là THẬT và đã được loại bỏ, nhưng chúng **che** một lỗi sâu hơn bên dưới.

---

## 2. Triệu chứng còn lại (chính xác, từ log production session `83FC4B`)

Pattern lặp lại sau **một** lần user bấm JOIN:

```
[DedicatedServer] Client connected: 1            ← qua approval (phải có session code đúng → CLIENT THẬT)
[DedicatedServer] Client disconnected: 1         ← drop NGAY: không load scene, không queue-full
[DedicatedServer] All clients disconnected — timers reset
[Netcode] Incomplete connection request message given config - possible NetworkConfig mismatch.
[DedicatedServer] Client disconnected: 2         ← connection KHÁC, message thiếu payload
... (lặp lại với client 3/4, 5/6)
```

Hai loại connection xen kẽ:
- **Lẻ (1,3,5):** qua được `ConnectionApproval` ⇒ message ĐẦY ĐỦ + session code khớp ⇒ **đây là client WebGL thật của user** (có thể tự retry vài lần). Connect xong **drop tức thì**.
- **Chẵn (2,4,6):** `ConnectionRequestMessage.cs:129` — message quá ngắn để chứa `ConfigHash + ConnectionData`. Vì port 7778 mở `0.0.0.0/0`, nhiều khả năng là **scanner/bot internet** gửi gói rác. *Cần xác nhận, không phải client thật (xem mục 5-H3).*

Phía client (LanLobbyController.cs:592-602): user thấy "Mất kết nối với host" ⇔ **`NetworkManager.DisconnectReason` RỖNG**. Approval-reject luôn set reason ⇒ ra "Connection rejected: …". Reason rỗng ⇒ **drop ở tầng transport / NGO đóng kết nối không kèm lý do**, sau khi đã connect.

---

## 3. Đã LOẠI TRỪ (đừng tốn thời gian lại)

- **Queue overflow:** đã nâng 1024 cả client (qua `NetworkManagerFactory.Ensure` tại `EnsureNetworkManager` LanLobbyController.cs:738) lẫn server. Run mới không còn warning.
- **Scene build-index mismatch:** `BuildServer.cs` và `BuildWebClient.cs` dùng scene list Y HỆT, cùng thứ tự (Menu=0, MapF_TankTest=1, MapF_TankTest_PIBT=2).
- **Prefab GlobalObjectIdHash:** `Assets/Resources/LanBridgePrefab.prefab` + `.meta` đều committed ⇒ hash ổn định giữa 2 build job (WebGL/Linux từ cùng commit).
- **Session code mismatch:** client lẻ qua được approval ⇒ session code đúng.
- **Build parity:** server + web cùng SHA `51fdc18` (đã `readlink` xác nhận trên VM).

---

## 4. Phân tích đường đi sau khi connect

`EnableSceneManagement = true` (NetworkManagerFactory.cs:40). Hệ quả: ngay sau approval, NGO `NetworkSceneManager` chạy **SceneSynchronization** đồng bộ scene đang active của server xuống client.

- Server dedicated boot vào **Menu** (index 0) và chỉ `LoadScene(MapF_TankTest)` khi **≥2 player** (`DedicatedServerBootstrap.MinPlayersToStart`). Với 1 client, server **ở yên trong Menu**.
- Client connect ⇒ NGO sync scene "Menu" của server. Client cũng đang ở "Menu" (auto-join từ trang `/play`).
- Client `OnClientConnected` (LanLobbyController.cs:559) chỉ set status "Đã kết nối! Chờ host…" rồi **chờ** server drive scene load. Client KHÔNG tự shutdown ở đây.

⇒ Cửa sổ chết nằm ở **giai đoạn NGO synchronization / giữ kết nối idle trong Menu qua WebSocket internet**, trước khi game scene được load.

---

## 5. Giả thuyết còn lại (xếp hạng) + cách xác nhận

### H1 — NGO scene-synchronization fail/stall qua WebSocket (CAO NHẤT)
`EnableSceneManagement=true` bắt client hoàn tất handshake sync scene Menu. Qua WebSocket internet (latency, TCP), handshake này có thể stall/timeout ⇒ NGO đóng kết nối, reason rỗng.
- **Xác nhận:** browser console (mục 6) sẽ in log NGO `SceneEvent`/`Synchronize` + lý do disconnect. Hoặc test với server đã ở SẴN trong game scene (xem mục 7-B2).
- **Lưu ý kiến trúc:** game này **không** dùng NetworkObject/NetworkTransform trong scene để sync world — tất cả qua `CustomMessagingManager` + ghost (LanClientView). NetworkObject DUY NHẤT là `LanBridgePrefab`. Vậy NGO scene management gần như chỉ để "client load MapF khi server load" — một tiện ích nhỏ nhưng kéo theo toàn bộ sự mong manh của scene-sync.

### H2 — NGO connection/synchronization timeout
`NetworkConfig.ClientConnectionBufferTimeout` (mặc định ~10s) hoặc heartbeat. Nếu client không hoàn tất synchronization trong ngưỡng ⇒ server kick, reason rỗng.
- **Xác nhận:** đo khoảng cách thời gian connect→disconnect (cần log có timestamp / browser console).

### H3 — "Incomplete connection request" là NOISE internet (không phải client thật)
Port 7778 mở `0.0.0.0/0`. Scanner gửi gói rác → đúng nhánh `ConnectionRequestMessage.cs:125-129`.
- **Xác nhận:** tạm thu hẹp firewall game-port về IP của user (sửa `allowed_game_sources` trong terraform.tfvars / hoặc rule tạm), rồi thử lại: nếu các dòng "Incomplete…" biến mất nhưng "connected→drop" vẫn còn ⇒ H3 đúng (noise), tập trung H1/H2.

---

## 6. BẰNG CHỨNG BẮT BUỘC PHẢI LẤY TIẾP — browser console

Suốt quá trình debug ta MÙ phía client. Đây là nửa còn thiếu và quyết định.

Các bước cho user:
1. Mở `http://35.240.203.91/play?session=<CODE>` (tạo phòng mới để có server boot sạch).
2. Bấm **F12** → tab **Console** TRƯỚC khi JOIN.
3. JOIN, đợi tới khi hiện "Mất kết nối với host".
4. Copy toàn bộ dòng có `[Netcode]`, `[LAN]`, `UnityTransport`, `SceneEvent`, `disconnect`, `WebSocket`, và bất kỳ exception (đỏ) nào → gửi lại.

Đặc biệt cần tìm: NGO in lý do disconnect phía client, có thấy `SceneEventType` nào không, có exception khi parse `MsgInitWorld`/`MsgWorldState` không.

---

## 7. Hướng FIX theo giả thuyết

### A. (Ưu tiên nếu H1 đúng) Bỏ NGO scene management, tự load scene
- `EnableSceneManagement = false`.
- Server báo client load game scene bằng **custom message / ClientRpc** ("load `MapF_TankTest`"); client tự `SceneManager.LoadScene` cục bộ.
- Bridge (`LanBridgePrefab`, PlayerPrefab) phải sống sót qua scene load: kiểm tra NetworkObject persistence khi tắt scene management (có thể cần `DontDestroyOnLoad` hoặc despawn/respawn có chủ đích).
- **Lợi:** loại bỏ hoàn toàn handshake scene-sync mong manh qua WebSocket — phù hợp vì world state đã đi qua CustomMessaging chứ không qua scene NetworkObjects.
- **Rủi ro:** cần xử lý vòng đời bridge + LanClientView qua scene load. Là thay đổi kiến trúc, chỉ làm sau khi browser console xác nhận H1.

### B. Tăng độ bền handshake (rẻ, làm trước để thử)
- B1: Tăng `NetworkConfig.ClientConnectionBufferTimeout` (vd 30s) trong `NetworkManagerFactory.Ensure`.
- B2: Cho server **load thẳng game scene khi client ĐẦU TIÊN connect** (bỏ chờ 2 player ở chế độ internet tạm thời) để loại trừ: nếu connect-rồi-vào-game OK ⇒ lỗi nằm ở giai đoạn idle-trong-Menu (củng cố H1/H2); nếu vẫn drop ⇒ lỗi ở chính bước sync/scene-load.

### C. Instrument để hết mù (làm cùng A/B)
- Surface `DisconnectReason` rõ hơn + bật `NetworkConfig.NetworkLogLevel = Developer` tạm trên client build để browser console in chi tiết.
- Thêm log phía server quanh `OnClientConnected` (clientId + có phải scene đang ở Menu) để phân biệt client thật vs noise theo thời điểm.

### D. Giảm noise (vận hành, không phải fix gốc)
- Cân nhắc thu hẹp `allowed_game_sources` khi demo, hoặc chấp nhận log noise. Không ảnh hưởng client thật.

---

## 8. Thứ tự thực thi đề xuất

1. **Lấy browser console** (mục 6) — gating, không code gì thêm tới khi có.
2. Song song: B2 (load game scene ngay khi client đầu tiên vào, bản thử) để khoanh vùng nhanh giai đoạn lỗi.
3. Phân loại theo console:
   - Có log scene-sync/timeout ⇒ làm **A** (bỏ NGO scene management) + B1 (tăng timeout).
   - Có exception parse custom message ⇒ sửa serialization tương ứng.
   - "Incomplete…" trùng thời điểm client thật ⇒ điều tra payload WebGL UTP (H3 thành nghi phạm thật).
4. Deploy → smoke 1 client giữ kết nối → rồi 2 client → mới mở Phase B.

---

## 9. Liên hệ Phase v2
Plan này là **tiền đề bắt buộc** cho `multiplayer_internet_lobby_workflow_plan_v2.md`. Lobby (slot list, owner START, tank picker) đều giả định client connect & giữ được. Fix A (bỏ NGO scene management, tự điều khiển scene) cũng làm Phase B/C dễ hơn vì luồng vào lobby → game sẽ do mình kiểm soát, không phụ thuộc NGO scene-sync.
```
