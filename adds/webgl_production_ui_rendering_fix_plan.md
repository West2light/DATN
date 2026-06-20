# Plan xử lý lỗi UI WebGL production khác local

Ngày lập: 2026-06-13

## Mục tiêu

Khắc phục khác biệt giữa local và production WebGL ở màn `LAN — SELECT MAP & MODE`:

1. Tiếng Việt bị mất glyph hoặc mất chữ có dấu.
2. Mini map trong từng card không hiển thị trên production.
3. Các ký tự đặc biệt như `—`, `←`, `·`, `×` bị mất.

Kết quả cần đạt: bản WebGL chạy local bằng HTTP và bản production hiển thị giống nhau ở các viewport chính, ít nhất `1280x720` và `1920x1080`.

## Kết luận nhanh về WSL/local production link

Không bắt buộc copy build sang WSL ngay từ đầu.

Thứ tự hợp lý hơn:

1. Serve đúng bản `Builds/WebGL` local qua HTTP trước, vì Unity WebGL không nên kiểm tra bằng cách mở trực tiếp `index.html`.
2. Nếu lỗi đã tái hiện trên local WebGL thì sửa trong Unity code/assets trước, không cần WSL.
3. Chỉ dùng WSL/nginx khi cần kiểm tra lớp serving giống production: MIME type, `StreamingAssets`, symlink release, nginx route fallback, hoặc artifact `.tar.gz` sau khi deploy.
4. Nếu dùng WSL, không nhất thiết phải copy toàn bộ build; có thể mount trực tiếp `/mnt/d/2025.2/DATN/projectY/Builds/WebGL`. Copy sang WSL chỉ nên dùng khi muốn test đúng mô hình release folder như `/var/www/tank-mapf-web/releases/<sha>`.

## Ghi nhận từ code hiện tại

### Font/glyph

- `Assets/Scripts/MenuViewBootstrap.cs` đang tạo UI bằng `UnityEngine.UI.Text`.
- `MakeText(...)` dùng `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`.
- Các chuỗi bị lỗi đều chứa glyph ngoài ASCII hoặc tiếng Việt:
  - `LAN  —  SELECT MAP & MODE`
  - `Chọn map và chế độ AI · số enemy = 6 × số người chơi`
  - `← BACK`
  - `32 × 32  •  10% walls`
- Đây là dấu hiệu mạnh của lỗi font fallback: Editor/local native có thể render nhờ font hệ điều hành, nhưng WebGL production không có fallback đó.

### Mini map

- `BuildLanMapCard(...)` gọi `BuildMiniMapRawImage(...)`.
- `BuildMiniMapRawImage(...)` gọi `LoadMapGrid(...)`.
- `LoadMapGrid(...)` đọc map bằng `File.Exists(...)` và `File.ReadAllText(...)` từ `Application.streamingAssetsPath` hoặc `Application.dataPath`.
- Trên WebGL, đọc `StreamingAssets` bằng filesystem sync thường không ổn định hoặc không hoạt động; `MapLoader.cs` đã có hướng đúng hơn: dùng `UnityWebRequest.Get(...)` trong nhánh `UNITY_WEBGL && !UNITY_EDITOR`.
- Local `Builds/WebGL` hiện có `StreamingAssets/MapData/*.map`, nên cần kiểm tra production có đang serve các file này không. Nếu file có nhưng UI vẫn trống, nguyên nhân nằm ở cách đọc trong `MenuViewBootstrap`.

## Phase 0 - Baseline và tái hiện đúng môi trường

### 0.1 Chốt source và artifact đang so sánh

Ghi lại:

```powershell
git -c safe.directory=D:/2025.2/DATN/projectY status --short --branch
git -c safe.directory=D:/2025.2/DATN/projectY rev-parse HEAD
```

Trên production/GitHub Actions, ghi lại SHA artifact đang deploy. Không so sánh Editor với production nếu chưa chắc WebGL build cùng SHA.

### 0.2 Serve local WebGL bằng HTTP

Ưu tiên chạy ngay trên Windows:

```powershell
cd D:\2025.2\DATN\projectY\Builds\WebGL
python -m http.server 8081
```

Mở:

```text
http://127.0.0.1:8081/
```

Kiểm tra màn LAN map select, console browser và Network tab.

### 0.3 Kiểm tra production static assets

Kiểm tra các file bắt buộc:

```bash
curl -fsSI http://<production-ip>/
curl -fsSI http://<production-ip>/Build/WebGL.loader.js
curl -fsSI http://<production-ip>/Build/WebGL.wasm
curl -fsS  http://<production-ip>/StreamingAssets/MapData/random-32-32-10.map | head
curl -fsS  http://<production-ip>/StreamingAssets/MapData/ht_mansion_n.map | head
```

Kết luận:

- Nếu `/StreamingAssets/MapData/*.map` trả `404`: sửa deploy/package/nginx.
- Nếu file map tải được nhưng preview vẫn trống: sửa code đọc minimap WebGL.
- Nếu text ASCII hiện nhưng tiếng Việt/ký tự đặc biệt mất: sửa font asset.

## Phase 1 - Sửa font/glyph cho WebGL

### 1.1 Thêm font có đủ glyph

Thêm font `.ttf` có hỗ trợ tiếng Việt và ký tự UI vào repo, ví dụ:

```text
Assets/Resources/Fonts/NotoSans-Regular.ttf
Assets/Resources/Fonts/NotoSans-Bold.ttf
```

Nếu muốn giữ phong cách arcade cho tiêu đề, có thể dùng font display cho chữ ASCII, nhưng các dòng có tiếng Việt/ký tự đặc biệt phải dùng font đầy đủ glyph.

### 1.2 Tạo font provider dùng chung

Tạo helper, ví dụ:

```text
Assets/Scripts/UI/UiFontProvider.cs
```

Trách nhiệm:

- `Resources.Load<Font>("Fonts/NotoSans-Regular")`
- fallback về `LegacyRuntime.ttf` nếu thiếu asset
- log warning rõ nếu font chính không load được
- optional: hàm kiểm tra `HasCharacter(...)` cho bộ chuỗi menu quan trọng

### 1.3 Thay các điểm dùng LegacyRuntime ở UI WebGL

Ưu tiên trước:

- `Assets/Scripts/MenuViewBootstrap.cs`
- `Assets/Scripts/Multiplayer/LanLobbyController.cs`
- `Assets/Scripts/Multiplayer/LanClientView.cs`

Sau đó rà tiếp các UI runtime khác:

- `MapGameOverController.cs`
- `MapTankTestBootstrap.cs`
- `MapScenarioBootstrap.cs`
- `MapScenarioBootstrapPIBT.cs`
- `BacktestResultChart.cs`

### 1.4 Kiểm thử glyph

Chuỗi test tối thiểu:

```text
LAN — SELECT MAP & MODE
← BACK
Chọn map và chế độ AI · số enemy = 6 × số người chơi
32 × 32 • 10% walls
A* PIBT Alpha-32 Maze-128
```

Nếu vẫn mất glyph, không deploy. Lúc đó hoặc font chưa được include trong build, hoặc UI vẫn đang dùng `LegacyRuntime.ttf`.

## Phase 2 - Sửa mini map preview trên WebGL

### 2.1 Không dùng File.ReadAllText sync cho WebGL menu preview

Sửa `MenuViewBootstrap` theo một trong hai hướng.

Hướng khuyến nghị: dùng `UnityWebRequest` giống `MapLoader`.

- Đổi `BuildMiniMapRawImage(...)` thành flow async/coroutine.
- Khi build card, tạo panel preview trước.
- Trong WebGL, `StartCoroutine(LoadMiniMapGridAsync(...))`.
- Sau khi map text tải xong, parse và gắn `RawImage.texture`.
- Nếu request lỗi, hiển thị placeholder `?` và log URL lỗi.

Hướng thay thế: pre-bake preview texture.

- Tạo PNG minimap cho từng `.map` và import như Sprite.
- Menu chỉ hiển thị Sprite, không đọc `.map` runtime.
- Ít rủi ro runtime hơn, nhưng phải maintain thêm asset khi thêm map mới.

### 2.2 Reuse parser hiện có

Tách phần parse text thành hàm dùng chung:

```text
ParseMapGridText(string text) -> char[][]
```

Không duplicate logic parse giữa local filesystem và WebGL request.

### 2.3 Kiểm tra Network tab

Sau sửa, mỗi map preview phải có request thành công hoặc dùng data đã pre-bake:

```text
/StreamingAssets/MapData/random-32-32-10.map
/StreamingAssets/MapData/ht_mansion_n.map
/StreamingAssets/MapData/ht_chantry.map
/StreamingAssets/MapData/lt_gallowstemplar_n.map
/StreamingAssets/MapData/maze-128-128-10.map
```

## Phase 3 - Kiểm thử production-like local

### 3.1 Build lại WebGL

Build bằng Unity menu:

```text
Tools/Tank MAPF/Build WebGL Client
```

Hoặc bằng GitHub Actions nếu muốn artifact đúng CI.

### 3.2 Serve local không dùng WSL

```powershell
cd D:\2025.2\DATN\projectY\Builds\WebGL
python -m http.server 8081
```

Chụp lại màn:

- Main menu
- Outfit select
- LAN map select
- LAN lobby

### 3.3 Serve bằng WSL/nginx nếu cần parity production

Chỉ làm bước này nếu local HTTP đã ổn nhưng production vẫn lỗi, hoặc cần kiểm chứng nginx.

Gợi ý kiểm tra nhanh bằng WSL:

```bash
cd /mnt/d/2025.2/DATN/projectY/Builds/WebGL
python3 -m http.server 8082
```

Nếu muốn giống production hơn, tạo nginx root trỏ vào:

```text
/mnt/d/2025.2/DATN/projectY/Builds/WebGL
```

Không cần copy build trừ khi muốn test chính xác quy trình release:

```text
/var/www/tank-mapf-web/releases/<sha>
/var/www/tank-mapf-web/current -> releases/<sha>
```

## Phase 4 - Củng cố CI/deploy smoke test

Bổ sung smoke test trong deploy workflow, ngoài `curl -fsSI /`:

```bash
curl -fsS "${web_url}/StreamingAssets/MapData/random-32-32-10.map" | grep -q '^type'
curl -fsSI "${web_url}/Build/WebGL.wasm"
curl -fsSI "${web_url}/Build/WebGL.data"
```

Nếu sau này bật Brotli/gzip lại, kiểm tra thêm header:

```text
Content-Encoding
Content-Type: application/wasm
Content-Type: application/javascript
```

## Phase 5 - Deploy và xác nhận

1. Commit fix font + minimap.
2. Chạy build WebGL.
3. Kiểm tra local WebGL bằng HTTP.
4. Push branch `networking-gcp` hoặc chạy workflow build/deploy theo release SHA.
5. Mở production ở cùng viewport với local.
6. So sánh screenshot production với local WebGL, không so với Editor.

## Definition of Done

- Tiếng Việt hiển thị đủ: `Chọn map và chế độ AI`, `số người chơi`.
- Các ký tự `—`, `←`, `·`, `×`, `•` hiển thị đúng.
- Cả 5 mini map preview hiển thị trong LAN map select.
- Browser console không có lỗi tải `.map`, `.data`, `.wasm`, `.js`.
- Production route `/StreamingAssets/MapData/random-32-32-10.map` trả nội dung map hợp lệ.
- Screenshot local WebGL và production cùng SHA không còn khác biệt UI đáng kể.

## Thứ tự triển khai khuyến nghị

1. Fix font trước vì đây là nguyên nhân chung cho lỗi tiếng Việt, dấu gạch, mũi tên và ký tự nhân.
2. Fix minimap bằng `UnityWebRequest` hoặc pre-baked PNG.
3. Chạy local WebGL HTTP.
4. Chỉ dựng WSL/nginx nếu production vẫn khác sau khi local WebGL đã đúng.
5. Bổ sung smoke test asset vào deploy workflow để không tái phát lỗi thiếu `StreamingAssets`.
