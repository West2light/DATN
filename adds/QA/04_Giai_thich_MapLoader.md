# Giải thích `MapLoader.cs` — dễ hiểu, từng hàm

> File: `Assets/Scripts/MapLoader.cs` (764 dòng).
> Đây là **nền móng** của cả đồ án: mọi thứ (spawn, tìm đường, bắn đạn) đều gọi qua nó.
> Nhiệm vụ một câu: **biến file text `.map` thành lưới ô + gạch trong thế giới game, rồi cho phần còn lại hỏi "ô này đi được không / ô này nằm ở đâu trong world"**.

---

## 0. Bức tranh tổng thể (đọc cái này trước khi vào chi tiết)

`MapLoader` làm đúng 3 việc, theo thứ tự:

1. **Đọc** file `.map` → dựng `char[][] grid` (mảng 2 chiều các ký tự `.` và `@`).
2. **Dựng** (build) gạch/tường thành GameObject trong scene, thêm collider chặn xe + hitbox cho đạn, và 4 bức tường bao quanh map.
3. **Phục vụ tra cứu** cho code khác: 5 hàm public quan trọng nhất là
   - `IsWalkable(cell)` — ô này đi được không?
   - `CellToWorld(cell)` — ô (x,y) trong lưới nằm ở toạ độ world nào?
   - `WorldToCell(pos)` — toạ độ world này rơi vào ô nào?
   - `TryFindWalkableNear(cell, out result)` — ô mong muốn bị chặn, tìm giúp ô đi được gần nhất.
   - `TryFindAvailableSpawnNear / InRange` — như trên nhưng còn tránh chồng chỗ khi spawn nhiều xe.

**Hệ toạ độ ô (nhớ kỹ, hay bị hỏi):** gốc ở **góc trên-trái**, `x` tăng sang **phải**, `y` tăng xuống **dưới** — khớp đúng thứ tự dòng trong file `.map`. Còn world thì tâm map ở `(0,0)`, mặt phẳng XY, `Z = 0`.

---

## 1. Các thuộc tính cấu hình (đầu file, dòng 14–67)

Đây là các biến chỉnh được trong Inspector:

| Biến | Ý nghĩa |
|---|---|
| `mapFileName` | Tên file `.map` sẽ nạp (mặc định `random-32-32-10.map`). |
| `tileSize` | Kích thước 1 ô = bao nhiêu đơn vị world (mặc định 1). |
| `buildOnStart` | Tự dựng map khi vào Play hay không. |
| `tilesParent` | GameObject cha để chứa toàn bộ gạch sinh ra (để dễ xoá/gom). |
| `maxBuildWidth/Height`, `mapOffsetX/Y` | **Cửa sổ dựng** (build window): chỉ dựng một phần map thay vì toàn bộ. Để 0 = dựng hết. Hữu ích với map lớn 251×180. |
| `walkableCells` | Những ký tự coi là "đi được" (mặc định chỉ `.`). |
| `groundSprite`, `obstacleSprite`... | Ảnh gạch nền / tường / cây / nước. |
| `...SpritePath` | Đường dẫn asset để tự nạp sprite khi ô Inspector bỏ trống (chỉ trong Editor). |

**Dữ liệu nội bộ quan trọng:**
- `char[][] grid` — **trái tim của class**. `grid[y][x]` là ký tự tại ô (x,y). `.` = đi được, `@` = tường.
- `width`, `height` — kích thước map gốc.
- `buildStartX/Y`, `buildWidth/Height` — vùng thực sự được dựng (sau khi áp cửa sổ build).
- `_destructibleCells` — tập ô bị chặn **tạm thời** (thùng động sinh ra lúc chơi), để phân biệt với tường cố định.

**Các thuộc tính `public ... =>` (dòng 59–67):** chỉ là cổng đọc (read-only) để code ngoài biết trạng thái: `IsLoaded`, `IsLoading`, `LastLoadError`... — không chứa logic.

---

## 2. Vòng đời nạp map

### `Start()` (74) và `Reset()` (69)
- `Reset()`: khi gắn component lần đầu, tự đặt `tilesParent = chính nó`.
- `Start()`: nếu bật `buildOnStart` thì gọi `LoadAndBuild()`.

### `LoadAndBuild()` — cửa chính (dòng 82), có `[ContextMenu("Load Map Now")]`
Đây là hàm bạn gọi để nạp map. `[ContextMenu]` nghĩa là **chuột phải vào component trong Inspector → "Load Map Now"** là chạy được, **không cần bấm Play**.

Trình tự:
1. Nếu đang nạp dở thì thoát (chống gọi chồng).
2. `ClearExistingTiles()` — xoá gạch cũ.
3. `LoadDefaultSprites()` — nạp ảnh gạch.
4. Đọc `PlayerPrefs["SelectedMapFile"]` — nếu menu đã chọn map khác thì **ghi đè** `mapFileName`. Đây là cách menu chọn map truyền lệnh xuống.
5. **Rẽ nhánh theo nền tảng:**
   - **WebGL** (chạy trên trình duyệt): không đọc được file trực tiếp → phải tải qua HTTP bằng coroutine `LoadAndBuildFromStreamingAssetsUrl(...)`.
   - **PC/Editor**: đọc thẳng file từ `StreamingAssets/MapData/` (hoặc `Application.dataPath/MapData/` dự phòng). Không thấy file → `FailLoad(...)`.
6. Đọc file thành `grid` rồi chạy coroutine `FinalizeLoadedMapCoroutine()`.

> **Vì sao dùng coroutine?** Dựng vài nghìn ô trong 1 frame sẽ làm đơ (freeze) WebGL. Coroutine chia việc ra nhiều frame.

### `LoadAndBuildFromStreamingAssetsUrl(url)` (119) — nhánh WebGL
Gửi `UnityWebRequest` tải file `.map` qua mạng. Thành công thì đọc text → `grid`. Lỗi/rỗng thì `FailLoad`.

### `BuildWebGlMapUrl(fileName)` (159)
Ghép URL đầy đủ tới file map dựa trên địa chỉ trang web hiện tại. Chỉ phục vụ WebGL.

### `FinalizeLoadedMapCoroutine()` (141) — hoàn tất sau khi có `grid`
Đây là chuỗi 6 bước "hậu kỳ", **thứ tự này hay bị hỏi khi demo**:
1. `ComputeBuildWindow()` — tính vùng cần dựng.
2. `BuildTilesCoroutine()` — dựng gạch/tường (rải qua nhiều frame).
3. `CreateMapBounds()` — dựng 4 tường bao.
4. `StaticBatchingUtility.Combine(...)` — gộp gạch tĩnh lại để **tối ưu hiệu năng render**.
5. `FitCamera()` — chỉnh camera vừa khung map.
6. Đặt lại cờ trạng thái (`_isLoading = false`).

### `FailLoad(message)` (152)
Gặp lỗi: tắt cờ đang-nạp, lưu thông báo lỗi, `Debug.LogError`.

---

## 3. Đọc file `.map` → mảng ký tự

### `ReadMapFile(path)` (477) / `ReadMapText(text)` (482)
Hai lối vào: một đọc từ file (PC), một đọc từ chuỗi (WebGL). `ReadMapText` chuẩn hoá xuống dòng (`\r\n`, `\r` → `\n`) rồi tách dòng. Cả hai đều gọi `ReadMapLines`.

### `ReadMapLines(lines)` (489) — bộ đọc lõi
Định dạng file `.map` chuẩn MovingAI có 4 dòng header rồi tới bản đồ:
```
type octile
height 32
width 32
map
................@@@....   ← từ dòng 5 trở đi là bản đồ
```
Hàm này:
1. Kiểm tra tối thiểu 4 dòng, nếu không → ném lỗi.
2. Lấy `height` từ dòng 2, `width` từ dòng 3 (qua `ParseHeaderInt`).
3. Bắt buộc dòng 4 phải là chữ `map`.
4. Duyệt từng dòng bản đồ, đổ vào `grid[y][x]`. **Ô thiếu ký tự (dòng ngắn) được coi là tường `@`** — phòng thủ chống file lỗi.

### `ParseHeaderInt(line, key)` (518)
Tách dòng kiểu `height 32` → kiểm tra từ khoá đúng (`height`) → trả về số 32. Sai định dạng thì ném lỗi.

---

## 4. Tính vùng dựng & dựng gạch

### `ComputeBuildWindow()` (529)
Từ `mapOffset` và `maxBuildWidth/Height`, tính ra `buildStartX/Y` và `buildWidth/Height` — tức "dựng từ ô nào, rộng bao nhiêu". Mặc định (offset 0, max 0) = **dựng toàn bộ map**. Cơ chế này cho phép cắt một góc map lớn để test nhanh.

### `BuildTilesCoroutine()` (539)
1. `CreateGroundBackground()` — trải 1 tấm nền cỏ lớn phủ cả map (rẻ hơn trải từng ô cỏ).
2. Duyệt mọi ô trong build window. **Chỉ ô KHÔNG đi được (tường) mới tạo GameObject** `CreateTile(...)`; ô cỏ đã có nền chung nên bỏ qua → tiết kiệm rất nhiều object.
3. Cứ 150 ô lại `yield return null` (nghỉ sang frame sau) để **không đơ WebGL**.

### `CreateGroundBackground()` (566)
Tạo 1 GameObject "Ground" duy nhất, kéo giãn (scale) sprite cỏ cho phủ kín map, đặt `sortingOrder = -1` (nằm dưới cùng).

### `CreateTile(cell, mapCell)` (587)
Tạo 1 ô tường:
- Đặt vị trí bằng `CellToWorld(mapCell)`, scale theo `tileSize`.
- Gán sprite (`PickSprite`) và màu (`PickColor`), `sortingOrder = 2` (nổi trên nền).
- **Nếu là tường:** gán layer chặn di chuyển (`Walls`, dự phòng `ObstaclesMovement`), thêm `BoxCollider2D` để xe không đi xuyên, và gọi `AddBulletHitbox` để đạn bắn trúng được.

### `AddBulletHitbox(tile)` (608)
Tạo 1 GameObject con `BulletHitbox` trên layer `Hittable`, với `BoxCollider2D` kiểu **trigger**. Đây là chỗ `Bullet.OnTriggerEnter2D` nhận diện va chạm. → Tường có **2 collider**: 1 để chặn xe, 1 (trigger) để ăn đạn.

### `CreateMapBounds()` (619) + `CreateBoundary(...)` (630)
Dựng 4 bức tường vô hình (Top/Bottom/Left/Right) bao quanh map để xe **không chạy ra ngoài**. Mỗi tường cũng có hitbox đạn.

### `ResolveLayer(name, fallback)` (642)
Trả về id layer theo tên; nếu layer chính chưa tồn tại trong project thì dùng layer dự phòng. → Đây là lý do code chịu được cả tên layer mới `Walls` lẫn tên cũ `ObstaclesMovement`.

---

## 5. Chuyển đổi toạ độ (phần hay dùng nhất)

### `CellToWorld(cell)` (298) — ô → world
Công thức: lấy tâm map làm gốc `(0,0)`, tính lệch của ô so với ô đầu build window. Chú ý dấu: `x` cộng sang phải, `y` **trừ** (vì y-ô tăng xuống dưới nhưng y-world tăng lên trên). **Mọi chỗ cần đặt vật thể theo ô đều phải gọi hàm này**, đừng tự tính lại.

### `WorldToCell(position)` (309) — world → ô
Phép ngược lại của `CellToWorld`, dùng `RoundToInt` để quy toạ độ world về ô gần nhất.

> Hai hàm này là **một cặp nghịch đảo**. `WorldToCell(CellToWorld(c)) == c`. Nếu bạn sửa một cái phải sửa cái kia cho khớp.

---

## 6. Tra cứu ô & tìm chỗ spawn

### `IsWalkable(cell)` (168)
Ô có nằm trong map **và** ký tự thuộc `walkableCells` không? → `true/false`. Đây là hàm A\* gọi nhiều nhất.

### `IsInside(cell)` (173) / `IsInsideBuildWindow(cell)` (178)
`IsInside`: ô có nằm trong toàn bộ map. `IsInsideBuildWindow`: ô có nằm trong vùng đã dựng (chặt hơn).

### `TryFindWalkableNear(preferred, out result)` (340)
Ô mong muốn đi được thì trả luôn. Nếu không, **quét vòng tròn lan dần** (radius 1, 2, 3...) tìm ô đi được gần nhất. Dùng khi spawn rơi trúng tường.

### `TryFindWalkableWithSpace(preferred, minRegionSize, minNeighbors, out result)` (371)
Nghiêm ngặt hơn: ô spawn phải đi được **và** có ≥ `minNeighbors` lối thoát trực tiếp **và** thuộc vùng liên thông ≥ `minRegionSize` ô. → Tránh spawn player vào một hốc cụt không có đường ra.

### `TryFindAvailableSpawnNear(...)` (190) và `TryFindAvailableSpawnInRange(...)` (248)
Phiên bản "spawn nhiều xe cùng lúc". Khác `TryFindWalkableNear` ở chỗ chúng **tôn trọng danh sách ô đã đặt trước** (`reservedCells`) và ép khoảng cách tối thiểu, nên nhiều yêu cầu spawn **không dồn vào cùng một ô**. `InRange` còn giới hạn spawn trong một vành khoảng cách quanh điểm tham chiếu (ví dụ: địch xuất hiện cách Eagle từ min→max ô). Có tối ưu: nếu ô đề xuất đã hợp lệ thì dùng luôn, không quét cả map (quan trọng trên map 251×180 với ~48 địch).

### Các hàm đếm phụ trợ
- `CountWalkableNeighbors(cell)` (397): đếm ô đi được liền kề **4 hướng**.
- `CountWalkableBuildNeighbors(cell)` (407): như trên nhưng chỉ tính ô trong build window.
- `ConflictsWithReserved(...)` (421): ô ứng viên có quá gần ô đã đặt trước không.
- `CountConnectedWalkable(start, maxCount)` (431): **BFS loang** đếm số ô đi được nối liền nhau, dừng sớm khi đủ `maxCount` (để không quét thừa). Đây là cái đảm bảo "vùng đủ rộng".

---

## 7. Sửa lưới lúc chạy (cho chướng ngại động)

Nhóm hàm này để **thùng động** (dynamic obstacle) thêm/bớt lúc đang chơi:
- `MarkCellBlocked(cell)` (319): đặt ô thành tường `@` trong `grid`.
- `MarkCellDestructible(cell)` (325): chặn ô **và** ghi vào `_destructibleCells` (đánh dấu "chặn tạm").
- `IsDestructibleBlocked(cell)` (331): ô này có phải chướng ngại tạm không.
- `UnmarkCellBlocked(cell)` (333): trả ô về đi được `.` và xoá khỏi tập tạm.

> Nhờ nhóm này mà A\*/PIBT khi replan sẽ "thấy" thùng mới xuất hiện và né — chính là cơ chế **chế độ động** trong phần backtest.

---

## 8. Hình ảnh & dọn dẹp

- `FitCamera()` (456): chỉnh `orthographicSize` của camera cho vừa map, có hệ số `padding = 0.85` để zoom gần hơn chút.
- `PickSprite(cell, mapCell)` (648): chọn ảnh theo ký tự ô (`.` cỏ, xen kẽ cỏ 2 cho đỡ đơn điệu; `T` cây, `W` nước, `S` đầm lầy, còn lại tường). Có nhiều lớp dự phòng nếu sprite null.
- `PickColor(cell)` (662): nhuộm màu nước (xanh) / đầm lầy (tím), còn lại trắng.
- `IsCellWalkable(char)` (669): ký tự có thuộc `walkableCells` không (bản dùng cho `char`, khác `IsWalkable` dùng cho `cell`).
- `ClearExistingTiles()` (675): xoá hết gạch cũ + reset tập chướng ngại tạm. Trong Play dùng `Destroy`, ngoài Play dùng `DestroyImmediate`.
- `GetFallbackSprite()` (699): tạo 1 sprite trắng 1×1 khi mọi ảnh khác đều thiếu — để map không bao giờ "vô hình".

---

## 9. Nạp sprite theo nền tảng

- `LoadSpriteAsset(path)` (714, chỉ Editor): thử nạp sprite theo đường dẫn asset; nếu là Tile hay atlas thì bóc sprite con ra.
- `LoadDefaultSprites()` (741): **Editor** nạp qua `AssetDatabase` theo path; **bản build** nạp qua `Resources.Load` từ thư mục `Assets/Resources/MapTiles/...`.
- `SpriteFromResources(path)` (756): nạp sprite (hoặc texture rồi tự cắt thành sprite) từ `Resources` — dùng ở bản build.

> Đây là chỗ giải thích vì sao có cả `...SpritePath` (Editor) lẫn thư mục `Resources` (build): hai đường nạp ảnh cho hai môi trường khác nhau.

---

## 10. Câu hỏi phản biện có thể gặp

**"Vì sao chỉ tạo GameObject cho tường mà không tạo cho ô cỏ?"**
→ Ô cỏ đã có 1 tấm nền chung phủ cả map (`CreateGroundBackground`). Tạo GameObject cho từng ô cỏ (hàng nghìn ô) là lãng phí render vô ích; chỉ tường mới cần collider nên mới cần object riêng.

**"Vì sao tường có 2 collider?"**
→ Một `BoxCollider2D` đặc trên layer `Walls` để **chặn xe di chuyển**; một collider con kiểu **trigger** trên layer `Hittable` để **đạn bắn trúng**. Tách ra để va chạm di chuyển và va chạm đạn không lẫn nhau.

**"Toạ độ ô và world quy đổi ra sao?"**
→ Qua đúng cặp `CellToWorld` / `WorldToCell`. Gốc ô ở góc trên-trái, y tăng xuống; world lấy tâm map làm `(0,0)`, nên khi đổi y phải đảo dấu.

**"Chế độ động (thùng xuất hiện lúc chơi) làm sao thuật toán biết mà né?"**
→ `DynamicObstacleSpawner` gọi `MarkCellBlocked/Destructible` để sửa `grid`; lần replan kế tiếp, `IsWalkable` trả `false` cho ô đó nên A\*/PIBT tự vòng tránh.

**"Vì sao phải dùng coroutine để dựng map?"**
→ Bản WebGL không có đa luồng thật; dựng vài nghìn object trong 1 frame sẽ đơ trình duyệt. Coroutine rải việc ra (150 ô/frame) giữ cho game mượt.

**"Map lớn 251×180 có bị chậm không?"**
→ Có build window để dựng một phần; chỉ tường mới sinh object; gạch được `StaticBatchingUtility.Combine` để gộp draw call; và các hàm tìm spawn có nhánh nhanh tránh quét toàn map.
