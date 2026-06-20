# Plan khắc phục regression UI production sau commit WebGL

Ngày lập: 2026-06-14
Repo: `D:\2025.2\DATN\projectY`
Commit production đang nghi vấn: `81a4cc2 Fix WebGL UI rendering and deploy checks`

## 1. Bối cảnh

Sau khi push commit `81a4cc2`, production WebGL xuất hiện các lỗi UI/runtime:

1. Main menu mất title `TANK MAPF` trên card.
2. Một số nút `BACK` không còn dấu quay lại bên trái.
3. Màn tạo/join room trên production đang hiển thị link dạng `http://35.240.203.91/s/<CODE>` và lobby không đúng workflow mong muốn.
4. Backtest trên production cuộn chuột chỉ zoom in, không zoom out.
5. Panel realtime metrics góc phải trên trong backtest không hiển thị hoặc không cập nhật đúng.

Đính chính sau kiểm tra thêm:

- Bản WebGL chạy local của người dùng không có lỗi zoom in/out trong backtest.
- Bản WebGL chạy local cũng hiển thị panel realtime góc phải đúng.
- Vì vậy hai lỗi backtest này không được xem là lỗi logic local trước khi chứng minh ngược lại. Trọng tâm phải chuyển sang production artifact, deploy, CI build, hoặc khác biệt runtime giữa artifact local và artifact đang serve trên VM.

Yêu cầu ràng buộc:

- Không tự ý đổi tiếp font asset nếu chưa chứng minh nguyên nhân.
- Không phá các fix đã có trong `adds/webgl_production_ui_rendering_fix_plan.md`.
- Mỗi thay đổi UI production phải kiểm tra bằng local WebGL build/screenshot trước khi commit/push.

## 2. Audit nhanh commit `81a4cc2`

Commit `81a4cc2` đã sửa các nhóm sau:

```text
Assets/Scripts/MenuViewBootstrap.cs
Assets/Scripts/Multiplayer/LanLobbyController.cs
Assets/Scripts/Backtest/BacktestConfigUI.cs
Assets/Scripts/Backtest/BacktestResultChart.cs
Assets/Scripts/Backtest/BacktestRunner.cs
Assets/Scripts/UiFontProvider.cs
Assets/Resources/Fonts/NotoSans-Regular.ttf
Assets/Editor/BuildWebClient.cs
.github/workflows/*
```

Điểm đáng chú ý:

- `MenuViewBootstrap.MakeText(...)` đổi từ `LegacyRuntime.ttf` sang `UiFontProvider.GetDefaultFont()`.
- `LanLobbyController.F()` đổi từ `LegacyRuntime.ttf` sang `UiFontProvider.GetDefaultFont()`.
- Backtest overlay/result chart đổi sang `UiFontProvider.GetDefaultFont()`.
- `BacktestCameraController.cs` không đổi trong commit, nên lỗi wheel zoom có thể do WebGL/browser input hoặc overlay/event-system, không phải diff trực tiếp ở file camera.
- `InternetSessionClient.CreateRoom(...)` hiện đang ưu tiên dựng link `/{s}/{code}` khi response có `code`, nên UI production hiển thị `/s/854B67`. Đây là bug cụ thể với invite link playable.

Kiểm tra artifact ngày 2026-06-14 cho thấy local build và production build không giống nhau:

```text
Local Builds/WebGL/index.html SHA256:
0B20D33E3AE0CDF2E4BC00E1CFD9FD6A346EB617C1161714E50DD9A0E4F8F64F

Production index.html SHA256:
0B20D33E3AE0CDF2E4BC00E1CFD9FD6A346EB617C1161714E50DD9A0E4F8F64F

Local Builds/WebGL/Build/WebGL.loader.js SHA256:
B893E6B59CC0298BB96127AF2C821B7C294CED66D95E42449568E4E0AC345BE9

Production Build/WebGL.loader.js SHA256:
551F9705C64ACBB1529C7A65C58D4AD4AE8894ED6CAB7F22EAB3216646923CA5

Local Builds/WebGL/Build/WebGL.wasm SHA256:
8FD7A60F84CB817B30CCF9CC08FA8622C36C1FCB9DF0FDF91DAB0F49C744F01A

Production Build/WebGL.wasm SHA256:
74860A684FC343DB8D44D15D8C0EDE433CEB2F361B142B3A28CAF05BA2BAB10F

Local Builds/WebGL/Build/WebGL.data SHA256:
8F25117930B4DAB9209D421EF2C5BDCE73A12568795978BDC597704CFB4E1E41

Production Build/WebGL.data SHA256:
CBDA4BAF0FAF0B0468918F37D0494FB4886FFA293B7128974B250B391F761431
```

Kết luận tạm thời:

- `index.html` giống nhau.
- `loader.js`, `wasm`, `data` khác nhau.
- Nếu local WebGL pass còn production fail, phải ưu tiên điều tra artifact CI/deploy trước khi sửa code backtest.

## 3. Nguyên tắc fix

1. Phục hồi visual trước, multiplayer workflow sau.
2. Không rollback toàn bộ commit `81a4cc2` vì commit đó cũng chứa fix minimap, font tiếng Việt và deploy smoke check.
3. Không đổi `NotoSans-Regular.ttf` nếu chưa có bằng chứng font asset thiếu glyph.
4. Với các icon đơn giản như mũi tên back, không phụ thuộc glyph font nữa. Dùng UI shape/Image hoặc ASCII fallback để WebGL không mất ký tự.
5. Tách commit thành các nhóm nhỏ:
   - `fix-webgl-menu-regression`
   - `fix-webgl-room-link`
   - `fix-backtest-webgl-controls`

## 4. Phase A - Chẩn đoán production/local trước khi sửa

### A1. Kiểm tra production artifact đúng commit nào

Mục tiêu: xác nhận production đang chạy đúng build từ `81a4cc2` hay build cũ/cached.

Lệnh:

```powershell
git -c safe.directory=D:/2025.2/DATN/projectY rev-parse --short HEAD
curl http://35.240.203.91/
curl -I http://35.240.203.91/Build/WebGL.wasm
curl -I http://35.240.203.91/Build/WebGL.data
gcloud compute ssh tank-mapf-server --zone asia-southeast1-b --project tankmapf --command "readlink -f /var/www/tank-mapf-web/current && ls -la /var/www/tank-mapf-web/current"
```

Nếu WebGL artifact không phải commit mới nhất, fix code sẽ không phản ánh ngay. Khi đó cần redeploy đúng artifact trước khi kết luận.

### A2. Build WebGL local và chụp lại

Không commit thêm nếu chưa có các ảnh local này:

```text
1_main_menu.png
2_tank_select.png
3_lan_map_select.png
4_room_create_lobby.png
5_backtest_running.png
```

Mục tiêu ảnh:

- `TANK MAPF` visible.
- Back button có dấu hoặc icon quay lại.
- Màn chọn tank không mất chữ.
- Màn room không hiện link `/s/<code>` như link chính.
- Backtest realtime panel visible.

## 5. Phase B - Fix main menu mất `TANK MAPF`

### Hiện tượng

Production screenshot chỉ còn subtitle `Multi-Agent Pathfinding`, title `TANK MAPF` không hiển thị.

### Giả thuyết cần kiểm tra

1. Text title có `FontStyle.Bold` với dynamic font WebGL và bị render fail riêng.
2. Text title bị che/sorting/order sau khi đổi font provider.
3. Text title bị scale/clip ở WebGL 1366x768.
4. Font asset load được nhưng glyph atlas cho title chưa build đúng lúc.

### Fix đề xuất theo mức ít rủi ro

1. Giữ nguyên font asset, nhưng tạo title bằng Text riêng:

```text
TANK MAPF
font = UiFontProvider.GetDefaultFont()
fontStyle = FontStyle.Normal nếu Bold gây mất glyph
fontSize giữ hoặc giảm nhẹ nếu bị clip
supportRichText = false
raycastTarget = false
```

2. Đưa title lên cuối cùng trong hierarchy card hoặc gọi `transform.SetAsLastSibling()`.
3. Thêm debug-only smoke text ở WebGL local để kiểm tra title glyph trước khi deploy.
4. Nếu vẫn mất, chỉ riêng title dùng `LegacyRuntime.ttf` vì title là ASCII, không ảnh hưởng fix tiếng Việt. Không dùng lại LegacyRuntime cho các dòng tiếng Việt/ký tự đặc biệt.

Definition of done:

- `TANK MAPF` hiển thị ở main menu trên WebGL local.
- Production sau deploy không còn blank title.

## 6. Phase C - Fix back button mất dấu quay lại

### Hiện tượng

Production screenshot nút `BACK` không còn ký tự `←`.

### Nguyên tắc

Không tiếp tục phụ thuộc glyph `←` trong font. Dù NotoSans có/không có glyph, UI production không nên mất icon vì font fallback.

### Fix đề xuất

Tạo helper dùng chung:

```text
MakeBackButton(parent, name, position, size, onClick)
```

Bên trong:

- Label chỉ là ASCII `BACK`.
- Icon quay lại là UI shape/Image riêng, không phải text glyph:
  - một line ngang;
  - hai line chéo tạo đầu mũi tên;
  - hoặc sprite/texture 1 màu tạo procedural.

Áp dụng cho:

```text
MenuViewBootstrap.BuildScreenOutfit
MenuViewBootstrap.BuildScreenMapSelect
MenuViewBootstrap.BuildScreenLanMapSelect
LanLobbyController join-row back mini button nếu cần
Backtest/result modal nếu có nút quay lại
```

Definition of done:

- Back button luôn có icon quay lại dù font thiếu glyph.
- Không còn text `← BACK` phụ thuộc font trong UI production chính.

## 7. Phase D - Fix tạo room/link lobby sai workflow

### Bug cụ thể đã thấy

`InternetSessionClient.CreateRoom(...)` hiện:

```csharp
string joinTarget = !string.IsNullOrWhiteSpace(response?.code)
    ? $"{normalizedBaseUrl}/s/{response.code.Trim()}"
    : ...
```

Vì vậy nếu registry trả `code`, client luôn dùng `/s/<CODE>`, bỏ qua `webUrl/joinUrl`.

### Fix đề xuất

1. Mở rộng `CreateRoomResponse`:

```csharp
public string code;
public string joinUrl;
public string webUrl;
public string status;
```

2. Ưu tiên target:

```text
response.webUrl
response.joinUrl nếu là playable URL
normalizedBaseUrl + "/play?session=" + code
fallback registry lookup chỉ dùng nội bộ
```

3. Registry `/s/<code>`:

- Nếu session có `webUrl`, redirect 302 sang `webUrl`.
- Nếu không có `webUrl`, render page fallback có nút `PLAY IN BROWSER`.

4. UI lobby:

- Không hiển thị `/s/<code>` như invite chính.
- Hiển thị:

```text
Room Code: 854B67
Invite Link: http://35.240.203.91/play?session=854B67
```

5. Không auto join trước khi room ready:

- Sau `POST /api/rooms`, registry/helper phải đảm bảo WebSocket server active/listen.
- UI hiển thị progress `Creating room -> Waiting server -> Joining`.

Definition of done:

- Copy link gửi bạn mở trực tiếp WebGL.
- URL có `?session=<code>` auto join.
- Không còn link invite chính dạng `/s/<code>` trên UI.

## 8. Phase E - Fix lobby Internet chưa đúng với Engine/local

### Hiện trạng production

Production đang vào overlay `LanLobbyController` với text:

```text
Connecting to 35.240.203.91:7778...
JOIN
CANCEL
```

Engine/local còn có screen chọn vai trò:

```text
Chọn vai trò của bạn:
JOIN (nhập IP host)
HOST GAME
CANCEL
```

### Quyết định

Không nên ép production Internet giống hệt LAN Engine, vì production không nhập IP LAN nữa. Cần tách 2 mode:

```text
LAN local:
  Host Game
  Join nhập IP host

Internet production:
  Create Room
  Room Code
  Invite Link
  Waiting Lobby
  Ready
  Start
```

### Fix ngắn hạn

- Đổi status production thành rõ nghĩa hơn:

```text
Creating internet room...
Room ready: 854B67
Connecting to websocket server...
```

- Sau khi room tạo xong, show room code/link trước khi connect hoặc trong lúc connect.
- Nếu connect fail, giữ code/link trên màn để người dùng retry/copy, không mất ngữ cảnh.

### Fix đầy đủ

- Tách `InternetLobbyController` khỏi `LanLobbyController`.
- LAN overlay giữ nguyên cho Editor/Desktop.
- WebGL dùng Internet overlay riêng.

## 9. Phase F - Fix backtest scroll zoom in/out trên WebGL

### Hiện tượng

Production backtest cuộn chuột chỉ zoom in, không zoom out.

### Đính chính quan trọng

Local WebGL build không bị lỗi này. Không sửa `BacktestCameraController` ngay ở bước đầu.

Ưu tiên:

1. Xác nhận production đang serve đúng artifact nào.
2. So sánh hash local verified artifact với artifact production.
3. Nếu khác, redeploy đúng artifact local đã pass hoặc build lại bằng cùng Unity version/settings.
4. Chỉ sửa code wheel nếu artifact giống nhau nhưng production vẫn lỗi.

### Giả thuyết

1. Production đang chạy artifact khác bản local đã pass.
2. CI build dùng Unity/settings/package cache khác local.
3. Deploy web bị partial update: `index.html` mới nhưng `Build/*.wasm/data/js` không cùng artifact.
4. Browser/runtime production khác local chỉ sau khi xác nhận artifact giống nhau.
5. Nếu artifact giống nhau, lúc đó mới xét `Input.GetAxis("Mouse ScrollWheel")` trên WebGL/browser có dấu không ổn định.
6. Camera zoom formula nhân khá mạnh:

```csharp
_cam.orthographicSize * (1f - scroll * _zoomSpeed * 10f)
```

Với `_zoomSpeed = 0.12`, hệ số là `1 - 1.2 * scroll`, dễ quá nhạy.

### Fix đề xuất

Nhánh ưu tiên 1 - artifact/deploy:

1. Tạo manifest hash khi build WebGL:

```text
Builds/WebGL/build-manifest.json
index.html sha256
Build/WebGL.loader.js sha256
Build/WebGL.framework.js sha256
Build/WebGL.wasm sha256
Build/WebGL.data sha256
gitCommit
buildTime
unityVersion
```

2. Deploy manifest cùng WebGL.
3. Smoke test production so sánh manifest/hash sau deploy.
4. Nếu local artifact đã pass, upload/deploy chính artifact đó thay vì để CI rebuild khác.

Nhánh fallback chỉ dùng nếu artifact giống nhau nhưng production vẫn lỗi:

1. Dùng helper normalize wheel:

```text
float axis = Input.GetAxis("Mouse ScrollWheel")
float delta = Input.mouseScrollDelta.y
chọn giá trị có magnitude lớn hơn
clamp về [-1, 1]
```

2. Đổi công thức zoom sang exponential ổn định:

```text
factor = Mathf.Pow(1.12f, -normalizedScroll)
orthographicSize = Clamp(oldSize * factor)
```

3. Thêm phím fallback:

```text
Q / E hoặc - / + để zoom out/in
```

4. Thêm debug text tạm trong local WebGL:

```text
wheelAxis=...
mouseDelta=...
ortho=...
```

5. Nếu browser vẫn chỉ gửi một chiều, thêm nút UI `+` và `-` góc dưới/phải cho backtest.

Definition of done:

- Production serve đúng artifact đã pass ở local, có manifest/hash chứng minh.
- WebGL local và production đều zoom in/out bằng wheel.
- Fallback keyboard hoặc UI buttons hoạt động.

## 10. Phase G - Fix backtest realtime panel

### Hiện tượng

Panel realtime góc phải trên không hiển thị hoặc không cập nhật.

### Đính chính quan trọng

Local WebGL build hiển thị realtime panel đúng. Không sửa layout/panel trước khi xác nhận production artifact.

Ưu tiên:

1. So sánh production `Build/WebGL.data` với local artifact đã pass.
2. Kiểm tra production console/log nếu có lỗi font/UI/canvas.
3. Redeploy đúng artifact local verified nếu hash khác.
4. Chỉ sửa `BacktestRunner.BuildOverlayUI()` nếu cùng artifact vẫn fail production.

### Kiểm tra code hiện tại

`BacktestRunner.BuildOverlayUI()` tạo:

```text
BacktestOverlay canvas sortingOrder = 200
RealtimePanel anchor top-right
_realtimeText.text = ""
```

`UpdateRealtimePanel(job)` chỉ set text trong vòng chạy mỗi frame.

### Giả thuyết

1. Production đang chạy artifact khác bản local pass.
2. Production artifact bị partial deploy hoặc stale `Build/*.data`.
3. Nếu artifact giống nhau: panel có tạo nhưng bị canvas khác che.
4. Nếu artifact giống nhau: text rỗng lúc loading, người dùng nhìn giai đoạn chưa update.
5. Nếu artifact giống nhau: panel nằm ngoài visible area do scale/WebGL canvas size.
6. Nếu artifact giống nhau: text/font không render trong WebGL.
7. Nếu artifact giống nhau: backtest scene reload phá overlay/camera event theo thứ tự.

### Fix đề xuất

1. Khi tạo panel, set text mặc định:

```text
Preparing backtest...
```

2. Tăng kích thước panel và đưa vào trong safe margin:

```text
anchoredPosition = (-20, -52)
sizeDelta = (220, 132)
```

3. Tăng `sortingOrder` nếu bị che:

```text
BacktestOverlay sortingOrder = 500
```

4. Trong `SetProgress(...)`, gọi cập nhật panel status tối thiểu để không rỗng.
5. Thêm log hoặc debug visual nếu `_realtimeText == null`.
6. Không ẩn panel cho đến khi result chart đã show xong.

Definition of done:

- Panel visible ngay khi bắt đầu backtest.
- Metrics cập nhật mỗi frame hoặc tối thiểu mỗi run.
- Không bị che bởi chart/config overlay.

## 11. Phase H - Verification gate trước commit/push

Không push nếu thiếu một trong các gate sau:

### Local code gate

```powershell
git -c safe.directory=D:/2025.2/DATN/projectY diff --check
git -c safe.directory=D:/2025.2/DATN/projectY status --short --branch
```

### Unity/WebGL gate

Build WebGL local:

```powershell
& "<UnityPath>\Unity.exe" `
  -batchmode `
  -quit `
  -projectPath "D:\2025.2\DATN\projectY" `
  -executeMethod BuildWebClient.BuildWebGL `
  -logFile "Logs\webgl-ui-regression-fix.log"
```

Serve local:

```powershell
cd D:\2025.2\DATN\projectY\Builds\WebGL
python -m http.server 9090
```

Manual screenshot checklist:

```text
Main menu: TANK MAPF visible
Tank select: back icon visible
Map select/LAN map select: back icon visible, minimaps visible
Room create: invite link is /play?session=<code>
Backtest config: checkbox green square still OK
Backtest running: wheel zoom in/out works
Backtest running: realtime panel visible and updating
```

### Production gate sau deploy

```powershell
curl http://35.240.203.91/
curl http://35.240.203.91:8080/healthz
curl -I http://35.240.203.91/Build/WebGL.wasm
curl http://35.240.203.91/api/sessions/<CODE>
```

## 12. Thứ tự làm khuyến nghị

1. Fix invite link `/play?session=<code>` vì đây là bug rõ nhất và ảnh hưởng multiplayer.
2. Fix icon back bằng UI shape, không phụ thuộc font.
3. Fix `TANK MAPF` title và verify local WebGL.
4. Fix backtest wheel normalization.
5. Fix realtime panel visibility/update.
6. Build WebGL local, chụp ảnh, chỉ sau đó mới commit/push.

## 13. Tiêu chí hoàn thành

Plan này hoàn thành khi production đạt:

- `TANK MAPF` hiển thị lại.
- Back button có icon quay lại ổn định.
- Room creation hiển thị code 6 ký tự và link `/play?session=<code>`.
- Invite link mở được WebGL và auto join đúng session.
- Backtest wheel zoom in/out được.
- Backtest realtime panel góc phải hiển thị và cập nhật.
- Không thay font asset nếu không có bằng chứng cần thay.
