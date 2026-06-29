# Plan chuyển Multiplayer LAN thành Multiplayer Internet

Ngày lập: 2026-06-14  
Repo: `D:\2025.2\DATN\projectY`  
Nhánh đang xem: `networking-gcp`

## 1. Mục tiêu

Chuyển workflow người chơi từ `Multiplayer LAN` sang `Multiplayer Internet` trên bản WebGL production GCP:

```text
Main Menu
  -> Multiplayer Internet
     -> Chọn tank
     -> Chọn map + mode
     -> Tạo room Internet
     -> Sinh mã 6 ký tự + link mời
     -> Host và bạn bè vào Waiting Lobby
     -> Người chơi chọn tank / Ready
     -> Host bấm Start khi đủ điều kiện
     -> Game bắt đầu
```

Mục tiêu thực tế của MVP:

- Người chơi A mở `http://35.240.203.91/`, chọn map/mode và tạo room.
- Hệ thống sinh room code 6 ký tự, ví dụ `A7C9F2`.
- UI hiển thị nút copy `Room Code` và copy `Invite Link`.
- Người chơi B mở invite link từ máy khác / mạng khác và vào đúng lobby.
- Server không tự bắt đầu game ngay khi client đầu tiên kết nối.
- Host chỉ start khi điều kiện ready đạt, đề xuất tối thiểu `>= 2` người chơi và mọi người đã ready.
- LAN local hiện có vẫn giữ được, nhưng UI chính nên ưu tiên Internet vì production là WebGL/GCP.

## 2. Phát hiện từ code hiện tại

Các phần đã có sẵn:

- `Assets/Scripts/Multiplayer/NetworkTransportMode.cs` đã có `Udp` và `WebSocket`.
- `Assets/Scripts/Multiplayer/InternetSessionClient.cs` đã có `ResolveSession` và `CreateRoom`.
- `Assets/Scripts/MenuViewBootstrap.cs` trong WebGL đã gọi `POST /api/rooms` khi chọn map/mode.
- `services/invite-registry/app.py` đã có endpoint `/api/rooms`.
- `infra/gcp/scripts/startup.sh` đã tạo helper `/usr/local/bin/tank-mapf-create-room`.
- GCP đã có WebGL HTTP, registry và WebSocket server lane.

Các điểm đang lệch workflow:

1. UI vẫn gọi là `MULTIPLAYER LAN`, `LAN — SELECT MAP & MODE`, `LanLobbyController`, nên người chơi production bị dẫn vào mental model LAN.
2. `InternetSessionClient.CreateRoom` ưu tiên trả join target dạng `/s/<code>` khi response có `code`. Link này dùng được bên trong Unity để resolve session, nhưng khi gửi cho bạn bè mở trực tiếp trên browser thì Nginx đang proxy `/s/` sang registry HTML, không mở thẳng WebGL.
3. Link mời playable nên là `/play?session=<code>` hoặc `/?session=<code>`, không phải chỉ `/s/<code>`.
4. `DedicatedServerBootstrap` hiện tự load gameplay scene sau client đầu tiên trong khoảng `SceneStartDelaySeconds = 3f`. Điều này không phù hợp với Waiting Lobby có slot, ready và host start.
5. Room creation hiện restart service rồi publish session gần như ngay lập tức. Client WebGL có thể join trước khi WebSocket server thật sự listen xong, dẫn tới trạng thái "Mất kết nối với host".
6. Chưa có state phòng rõ ràng: `creating`, `warming`, `open`, `starting`, `in_game`, `failed`, `closed`.
7. Chưa có server-side lobby state để sync danh sách slot, tank body, ready, host/owner.

Kiểm tra production nhanh ngày 2026-06-14:

- `http://35.240.203.91:8080/healthz` trả `{"ok": true}`.
- `http://35.240.203.91/` trả HTTP 200.
- `http://35.240.203.91/create` đang có trang create room HTML.
- Session mẫu `NHUHAI123` hiện trả 404, có thể do hết hạn hoặc không còn được publish.

## 2.1. Ràng buộc không đụng phần font/hiển thị WebGL đã fix

Phần font chữ, glyph tiếng Việt, dấu gạch, mũi tên, mini map preview, checkbox backtest và scale WebGL đã được xử lý theo:

```text
adds/webgl_production_ui_rendering_fix_plan.md
```

Milestone Multiplayer Internet không được tự ý thay các phần sau:

```text
Assets/Scripts/UiFontProvider.cs
Assets/Resources/Fonts/NotoSans-Regular.ttf
Assets/Resources/Fonts/NotoSans-Regular.ttf.meta
Assets/Editor/BuildWebClient.cs
CanvasScaler/referenceResolution nếu không có lỗi UI Internet cụ thể
```

Các thay đổi UI trong plan này chỉ nên giới hạn vào copy/text và layout workflow multiplayer:

- đổi nhãn `LAN` thành `Internet` ở các màn multiplayer phù hợp;
- thêm room code, invite link, copy button, ready/start;
- thêm status/progress tạo room;
- không thay font family, font asset, font fallback hoặc các fix WebGL rendering đã ổn.

Nếu trong quá trình implement cần sửa font để xử lý một lỗi mới, phải tách thành ticket/commit riêng và xác nhận trước.

## 3. Quyết định UX đề xuất

### 3.1 Main Menu

Đổi text chính:

```text
SINGLE PLAY
MULTIPLAYER INTERNET
MULTIPLAYER LAN        (nhỏ hơn, Advanced/Local)
BACKTEST
```

Nếu muốn giữ UI gọn cho production WebGL:

- WebGL build: chỉ hiện `MULTIPLAYER INTERNET`.
- Editor/Desktop build: hiện cả `MULTIPLAYER INTERNET` và `MULTIPLAYER LAN`.

### 3.2 Chọn Map / Mode

Đổi title:

```text
INTERNET — SELECT MAP & MODE
```

Thêm subtext ngắn:

```text
Tạo room online · gửi code hoặc link cho bạn bè
```

Khi bấm `A*` hoặc `PIBT`:

- Không join ngay lập tức.
- Hiện modal/progress:

```text
ĐANG TẠO ROOM
[1/4] Sinh mã phòng
[2/4] Cập nhật map/mode trên server
[3/4] Chờ WebSocket server sẵn sàng
[4/4] Mở lobby
```

Nếu lỗi, hiển thị lỗi cụ thể:

- `Create room helper not found`
- `WebSocket server not ready`
- `Session not found`
- `Connection rejected: Invalid session code`
- `Connection timeout to host:port`

Không chỉ hiện chung chung `Mất kết nối với host`.

### 3.3 Waiting Lobby

Lobby Internet nên thay thế overlay LAN hiện tại bằng layout rõ ràng:

```text
MULTIPLAYER INTERNET
Map: Chantry · Mode: PIBT · Server: Singapore/GCP

Room Code: A7C9F2        [COPY CODE]
Invite Link: /play?session=A7C9F2  [COPY LINK]

Players (2/4)
┌────┬──────────────┬───────────┬────────┐
│ #  │ Player       │ Tank      │ Status │
├────┼──────────────┼───────────┼────────┤
│ 1  │ You (Host)   │ Blue      │ Ready  │
│ 2  │ Friend       │ Green     │ Ready  │
│ 3  │ Empty        │ -         │ -      │
│ 4  │ Empty        │ -         │ -      │
└────┴──────────────┴───────────┴────────┘

[CHANGE TANK] [READY] [START] [CANCEL]
```

Quy tắc:

- Người tạo room là `room owner`, không phải dedicated server.
- `START` chỉ enabled với owner.
- `START` enabled khi `connectedPlayers >= 2` và mọi người đã ready.
- Có thể thêm override dev `Start solo` chỉ trong Editor hoặc build debug, không hiện production.
- Người join bằng link không cần chọn map/mode nữa; map/mode lấy từ registry/server.

### 3.4 Join Room

Thêm màn `JOIN INTERNET ROOM`:

Input nhận 3 dạng:

```text
A7C9F2
http://35.240.203.91/play?session=A7C9F2
http://35.240.203.91/s/A7C9F2
```

Parser chuẩn hóa về `sessionCode`, sau đó gọi registry để lấy endpoint.

## 4. Quyết định link và mã phòng

### 4.1 Room code 6 ký tự

Code hiện tại dùng `secrets.token_hex(3).upper()`, tạo đúng 6 ký tự hex, ví dụ `A1B2C3`.

Khuyến nghị cho UI:

- Tiếp tục dùng 6 ký tự cho MVP.
- Sau đó có thể đổi sang alphabet ít nhầm lẫn:

```text
ABCDEFGHJKLMNPQRSTUVWXYZ23456789
```

Tránh `I`, `O`, `0`, `1` nếu người chơi phải đọc code qua chat/voice.

### 4.2 Invite link chuẩn

Chuẩn hóa:

```text
Playable invite:
http://35.240.203.91/play?session=A7C9F2

Short lookup:
http://35.240.203.91/s/A7C9F2
```

Quy tắc:

- Nút `COPY LINK` trong Unity phải copy `webUrl`, không copy `/s/<code>` nếu `/s/` chưa redirect.
- Registry `/s/<code>` nên `302 redirect` sang `webUrl` nếu session có `webUrl`.
- Nếu không redirect, trang `/s/<code>` phải có nút lớn `PLAY IN BROWSER`, nhưng đây chỉ là fallback.

File cần sửa:

- `InternetSessionClient.CreateRoom`: ưu tiên `response.webUrl`, rồi mới `joinUrl`, cuối cùng mới tự dựng `/play?session=<code>`.
- `services/invite-registry/app.py`: `handle_session_page` nên redirect sang `webUrl` cho browser flow.
- `infra/gcp/scripts/startup.sh`: đảm bảo Nginx route `/play` fallback về WebGL `index.html`.

## 5. Quyết định backend / room lifecycle

### 5.1 Không publish room trước khi server sẵn sàng

`POST /api/rooms` hiện gọi helper restart service rồi đọc session. Cần siết lại:

1. Validate map/mode theo whitelist.
2. Generate code.
3. Ghi `/etc/tank-mapf/runtime.env`.
4. Restart `tank-mapf-server-web.service`.
5. Chờ service active.
6. Chờ TCP `WEB_GAME_PORT` listen.
7. Tốt hơn: chờ log `StartServer ok` hoặc health marker do server ghi.
8. Chỉ publish session sau khi WebSocket server đã ready.
9. Response trả `status=open`, `code`, `webUrl`, `webHost`, `webGamePort`, `webTransport`.

Nếu bước 5-7 fail:

- Trả HTTP 503 hoặc 500 với message cụ thể.
- Không trả invite link giả.
- UI hiển thị lỗi tạo room, không auto join.

### 5.2 Thêm trạng thái room

Payload registry/session đề xuất:

```json
{
  "code": "A7C9F2",
  "status": "open",
  "host": "35.240.203.91",
  "gamePort": 7777,
  "transport": "udp",
  "webHost": "35.240.203.91",
  "webGamePort": 7778,
  "webTransport": "websocket",
  "webUrl": "http://35.240.203.91/play?session=A7C9F2",
  "map": "ht_chantry.map",
  "algorithm": "PIBT",
  "maxPlayers": 4,
  "expiresAt": 1781111111
}
```

Trạng thái:

```text
creating  server đang chuẩn bị
open      có thể join lobby
starting  owner đã bấm start
in_game   scene gameplay đã load
failed    tạo room thất bại
closed    hết hạn hoặc bị hủy
```

MVP có thể chỉ cần `open` và `failed`, nhưng nên thiết kế enum từ đầu.

## 6. Quyết định server lobby

Hiện `DedicatedServerBootstrap` tự load scene gameplay sau client đầu tiên. Cần đổi cho Internet:

### 6.1 Tách LAN host và Internet dedicated server

Giữ local LAN flow:

```text
Editor/Desktop LAN
  StartHost()
  Host bấm Start
```

Thêm Internet dedicated flow:

```text
WebGL/Internet
  Clients connect to headless server
  Server stays in lobby state
  First client becomes room owner
  Clients send tank variant + ready via ServerRpc
  Owner sends StartRoomServerRpc()
  Server loads gameplay scene for all clients
```

### 6.2 Component mới đề xuất

Thêm các file:

```text
Assets/Scripts/Multiplayer/InternetRoomLobbyState.cs
Assets/Scripts/Multiplayer/InternetRoomLobbyController.cs
Assets/Scripts/Multiplayer/InternetRoomSlot.cs
```

Vai trò:

- `InternetRoomLobbyState`: server-side state, danh sách client, owner, ready, tank variant.
- `InternetRoomLobbyController`: UI lobby trên client, render slots, copy code/link, ready/start.
- `InternetRoomSlot`: dữ liệu slot sync qua NetworkVariable hoặc custom message.

Nếu muốn ít file hơn trong MVP, có thể mở rộng `LanNetworkBridge` thêm ready/owner RPC, nhưng về lâu dài nên tách khỏi chữ `Lan`.

### 6.3 Không auto start gameplay

Sửa `DedicatedServerBootstrap`:

- Nếu `LanSessionManager.TransportMode == WebSocket` hoặc `IsDedicatedServer == true`, không dùng `SceneStartDelaySeconds`.
- Chỉ load scene khi nhận `StartRoomServerRpc` hợp lệ từ owner.
- Nếu room không ai vào sau timeout, server vẫn idle hoặc registry expire session.

## 7. Milestone triển khai

### M0 - Chẩn đoán production hiện tại

Mục tiêu:

- Chứng minh lỗi đến từ link, endpoint, server readiness hay approval/session code.

Lệnh kiểm tra:

```powershell
curl http://35.240.203.91:8080/healthz
curl http://35.240.203.91/create
curl http://35.240.203.91/Build/WebGL.wasm -I
gcloud compute ssh tank-mapf-server --zone asia-southeast1-b --project tankmapf --command "sudo systemctl status tank-mapf-server-web.service --no-pager"
gcloud compute ssh tank-mapf-server --zone asia-southeast1-b --project tankmapf --command "sudo ss -lntup | egrep '7778|8080|80'"
gcloud compute ssh tank-mapf-server --zone asia-southeast1-b --project tankmapf --command "sudo journalctl -u tank-mapf-server-web.service -n 120 --no-pager"
```

Done khi:

- Biết server có listen TCP `7778` sau khi tạo room hay không.
- Biết client bị reject vì session code, hay timeout vì server chưa ready.

### M1 - Sửa invite link trước

Mục tiêu:

- Link copy gửi bạn bè mở trực tiếp được WebGL và auto join.

Sửa:

- `InternetSessionClient.CreateRoom` ưu tiên `webUrl`.
- Registry `/s/<code>` redirect sang `/play?session=<code>` nếu có `webUrl`.
- UI copy link dùng `/play?session=<code>`.

Done khi:

- Mở `http://35.240.203.91/play?session=<code>` load WebGL.
- `Application.absoluteURL` parse được session.
- Bạn bè không còn rơi vào trang registry HTML.

### M2 - Room creation readiness gate

Mục tiêu:

- Không auto join trước khi WebSocket server sẵn sàng.

Sửa:

- `/usr/local/bin/tank-mapf-create-room` chờ `tank-mapf-server-web.service` active.
- Chờ port `7778` listen.
- Chờ log `StartServer ok` hoặc tạo health marker.
- `POST /api/rooms` chỉ trả success sau ready.
- UI có progress và timeout rõ ràng.

Done khi:

- Sau bấm map/mode, client chỉ join khi room ready.
- Không còn lỗi "Mất kết nối với host" do join quá sớm.

### M3 - Đổi UI từ LAN sang Internet

Mục tiêu:

- Production workflow đúng mental model Internet.

Sửa:

- Main menu: `MULTIPLAYER INTERNET`.
- Map screen title: `INTERNET — SELECT MAP & MODE`.
- Lobby title: `MULTIPLAYER INTERNET`.
- Status text dùng `room`, `code`, `invite link`, `server`, không dùng `IP của bạn`.
- Giữ LAN local ở mục phụ hoặc chỉ hiện trong non-WebGL build.

Done khi:

- Người chơi không cần hiểu LAN/IP để chơi production.
- Có màn join bằng code/link.

### M4 - Waiting Lobby + Ready + Host Start

Mục tiêu:

- Đúng workflow trong sơ đồ người dùng gửi.

Sửa:

- Thêm room owner là client đầu tiên.
- Sync slot list, tank variant, ready.
- Thêm nút `COPY CODE`, `COPY LINK`, `READY`, `START`.
- Server không auto load gameplay scene.
- Owner bấm Start mới gọi server load scene.

Done khi:

- Host tạo room, bạn vào link, cả hai thấy nhau trong lobby.
- Cả hai ready.
- Host bấm Start, cả hai vào cùng gameplay scene.

### M5 - Verify production end-to-end

Checklist:

```text
PC A tạo room random + AStar
PC B mở invite link và vào lobby
PC A/B đổi tank body, ready
PC A bấm Start
Hai máy thấy đúng số player, enemy = 6 x player
PC A tạo room Chantry + PIBT
PC B vào đúng map/mode
```

Smoke commands:

```powershell
curl http://35.240.203.91/
curl http://35.240.203.91:8080/healthz
curl http://35.240.203.91/api/sessions/<CODE>
```

GCP logs:

```powershell
gcloud compute ssh tank-mapf-server --zone asia-southeast1-b --project tankmapf --command "sudo journalctl -u tank-mapf-server-web.service -n 160 --no-pager"
gcloud compute ssh tank-mapf-server --zone asia-southeast1-b --project tankmapf --command "sudo journalctl -u tank-mapf-registry.service -n 160 --no-pager"
```

### M6 - Sau MVP

Nâng cấp sau khi MVP ổn:

- Multi-room thật sự: spawn nhiều server process / dynamic ports hoặc container per room.
- HTTPS/WSS qua domain để không phải dùng `ws://IP:7778`.
- Rate limit `/api/rooms`.
- Room password/private room.
- Hiển thị ping/region.
- Spectator/reconnect.

## 8. Thứ tự ưu tiên khuyến nghị

Không nên làm lobby phức tạp trước khi sửa link và readiness. Thứ tự ít rủi ro:

1. M1: sửa invite link playable.
2. M2: chờ server ready trước khi trả create room success.
3. M3: đổi UI text và button từ LAN sang Internet.
4. M4: thêm Waiting Lobby, Ready, Host Start.
5. M5: test production hai máy.

Lý do:

- Nếu link vẫn sai, bạn bè không vào được dù lobby đẹp.
- Nếu server chưa ready, host vẫn bị disconnect dù UI đúng.
- Nếu server vẫn auto start sau client đầu tiên, workflow ready/start không thể đúng.

## 9. File dự kiến cần sửa

Unity runtime:

```text
Assets/Scripts/MenuViewBootstrap.cs
Assets/Scripts/Multiplayer/InternetSessionClient.cs
Assets/Scripts/Multiplayer/InternetJoinParser.cs
Assets/Scripts/Multiplayer/LanLobbyController.cs
Assets/Scripts/Multiplayer/DedicatedServerBootstrap.cs
Assets/Scripts/Multiplayer/LanNetworkBridge.cs
```

Có thể thêm:

```text
Assets/Scripts/Multiplayer/InternetRoomLobbyController.cs
Assets/Scripts/Multiplayer/InternetRoomLobbyState.cs
Assets/Scripts/Multiplayer/InternetRoomSlot.cs
```

Registry/infra:

```text
services/invite-registry/app.py
infra/gcp/scripts/startup.sh
infra/gcp/scripts/deploy-release.sh
infra/gcp/systemd/tank-mapf-server-web.service.tpl
.github/workflows/deploy-gcp.yml
.github/workflows/unity-build.yml
```

## 10. Tiêu chí hoàn thành

MVP chỉ được coi là xong khi:

- WebGL production tạo được room mới.
- Room code 6 ký tự hiển thị trong UI.
- Copy invite link mở được WebGL trực tiếp trên máy khác.
- Người join không cần nhập IP.
- Waiting Lobby hiển thị tối thiểu 2 player.
- Ready/start chạy theo owner, không tự start sau client đầu tiên.
- Game scene vào đúng map/mode.
- Không còn thông báo chung chung `Mất kết nối với host` cho lỗi có nguyên nhân cụ thể.
