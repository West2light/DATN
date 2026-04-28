# Ý tưởng scene test xe tank với MAPF benchmark map

## Mục tiêu

Tạo thêm một scene riêng để test xe tank chạy trong bản đồ sinh từ file MAPF benchmark `Assets/MapData/random-32-32-10.map`, tận dụng các tile sprite đã có trong `Assets/Sprites/Tiles` thay vì chỉ render ô màu runtime.

Scene này nên dùng để kiểm tra nhanh:

- Tank di chuyển có va chạm đúng với obstacle `@` không.
- Camera theo tank có hoạt động ổn trên map lớn dạng grid không.
- Map loader có đọc đúng kích thước, vị trí và loại ô từ MAPF `.map` không.
- Sau này có thể dùng cùng scene để test pathfinding/MAPF nhiều agent.

## Scene đề xuất

Tạo scene mới:

```text
Assets/Scenes/MapF_TankTest.unity
```

Hierarchy gợi ý:

```text
MapF_TankTest
├── Main Camera
├── EventSystem
├── GridRoot
│   └── MapLoader
├── Player
├── PlayerCinemachine
└── TestUI hoặc DebugOverlay
```

## Cách dùng map data

File MAPF benchmark đang có:

```text
Assets/MapData/random-32-32-10.map
```

Format:

```text
type octile
height 32
width 32
map
.......@.........@@.......@.....
...
```

Ý nghĩa tối thiểu cần hỗ trợ:

| Ký tự | Ý nghĩa | Gameplay |
|---|---|---|
| `.` | Walkable/floor | Tank đi được |
| `@` | Obstacle/wall | Tank không đi xuyên qua |
| `T` | Tree/terrain obstacle nếu map có | Có thể coi là obstacle |
| `W` | Water nếu map có | Có thể coi là obstacle hoặc slow tile |
| `S` | Swamp nếu map có | Có thể coi là slow tile sau này |

Với map hiện tại `random-32-32-10.map`, chủ yếu cần xử lý `.` và `@`.

## Mapping từ ký tự map sang sprite tile

Nên mở rộng `MapLoader` để nhận sprite bằng field serialized thay vì hard-code màu:

```csharp
[Header("Tile Sprites")]
public Sprite groundSprite;
public Sprite obstacleSprite;
public Sprite treeSprite;
public Sprite waterSprite;
public Sprite swampSprite;
```

Mapping asset gợi ý:

| Ký tự | Sprite gợi ý |
|---|---|
| `.` | `Assets/Sprites/Tiles/Ground/tileGrass1.asset` hoặc `tileGrass2.asset` |
| `@` | dùng road/stone/sand tile nếu có tile phù hợp; nếu chưa có wall sprite thì dùng `tileSand1.asset` hoặc một obstacle prefab tạm |
| `T` | `Assets/Sprites/Tiles/Details/treeGreen_large.asset` |
| `W` | nếu có water tile thì dùng water; nếu chưa có thì tint xanh trên sprite trắng runtime |
| `S` | dùng sand/grass tint tím hoặc slow tile riêng |

Nếu muốn map nhìn tự nhiên hơn, có thể random nhẹ giữa `tileGrass1` và `tileGrass2` cho ô `.` bằng seed cố định theo tọa độ cell, ví dụ hash `(x, y)` để mỗi lần load ra cùng layout.

## Collider và physics

Luồng tạo tile nên tách rõ visual và collider:

- Ô `.`: có `SpriteRenderer`, không có collider.
- Ô `@`, `T`, `W`: có `SpriteRenderer` và `BoxCollider2D` static.
- Collider size bằng `tileSize`.
- Tất cả obstacle nên nằm trên layer đang được tank/player collision nhận diện.

Có thể gom collider obstacle sau này bằng CompositeCollider2D để giảm số collider nếu map lớn, nhưng scene test 32x32 chưa cần tối ưu phức tạp.

## Spawn tank trong map

Cần chọn một cell walkable để đặt `Player`:

- Cách đơn giản: tìm cell `.` đầu tiên ở gần góc trái trên, ví dụ scan từ `(0,0)`.
- Cách tốt hơn cho test: expose `Vector2Int playerSpawnCell` trong `MapLoader` hoặc script `MapTankTestBootstrap`.
- Khi scene start, convert cell sang world position bằng `CellToWorld(playerSpawnCell)` rồi đặt tank vào đó.

Ví dụ cấu hình:

```text
playerSpawnCell = (1, 1)
tileSize = 1
useXYPlane = true
```

Nếu cell spawn bị obstacle thì bootstrap nên tìm walkable gần nhất.

## Camera

Dùng lại camera setup hiện có:

- `Main Camera` + `CinemachineBrain`.
- `PlayerCinemachine` follow player tank.
- Camera confiner có thể bỏ trong bản test đầu tiên, hoặc tạo boundary theo kích thước map.

Với map 32x32, camera orthographic khoảng `8-12` là đủ để vừa nhìn tank vừa thấy đường đi.

## Hướng triển khai tối thiểu

1. Tạo scene `MapF_TankTest.unity` từ scene gameplay hiện có hoặc scene 2D trống.
2. Thêm GameObject `GridRoot` và attach `MapLoader`.
3. Sửa/viết `MapLoader` thật trong `Assets/Scripts` để:
   - Đọc file từ `Assets/MapData`.
   - Tạo tile bằng `SpriteRenderer` với sprite đã assign.
   - Add `BoxCollider2D` cho obstacle.
   - Cung cấp `CellToWorld`, `WorldToCell`, `IsWalkable`.
4. Đặt player tank vào một cell walkable sau khi map load.
5. Chạy scene và kiểm tra tank không đi xuyên obstacle.
6. Nếu ổn, thêm scene này vào Build Settings sau `Lvl2` hoặc chỉ để editor test.

## Script phụ nên có

Có thể thêm một script nhỏ `MapTankTestBootstrap`:

```text
MapTankTestBootstrap
- Tham chiếu MapLoader
- Tham chiếu Player transform
- playerSpawnCell
- Sau khi MapLoader.LoadAndBuild(), đặt player vào CellToWorld(playerSpawnCell)
- Nếu cell không walkable, tự tìm cell walkable gần nhất
```

Script này giúp `MapLoader` chỉ lo map, không phụ thuộc trực tiếp vào tank/player.

## Lưu ý với code `MapLoader` hiện tại trong docs

File đang mở `Assets/Docs/MapLoader.cs.md` hiện là bản ghi chú code, chưa phải script C# trong `Assets/Scripts`. Nếu muốn Unity chạy thật, cần tạo file:

```text
Assets/Scripts/MapLoader.cs
```

hoặc chuyển nội dung phù hợp từ docs sang script thật.

Ngoài ra trong bản docs hiện tại có đường dẫn:

```csharp
Path.Combine(Application.dataPath, "Mapdata", mapFileName)
```

Nhưng thư mục thật là:

```text
Assets/MapData
```

Vì vậy khi implement cần dùng đúng chữ hoa/thường:

```csharp
Path.Combine(Application.dataPath, "MapData", mapFileName)
```

Trên Windows có thể chưa lỗi vì filesystem thường không phân biệt hoa/thường, nhưng nên sửa đúng để tránh lỗi khi build/chạy trên môi trường khác.

## Mốc hoàn thành scene test

Scene được coi là đạt khi:

- Mở `MapF_TankTest.unity` không có error Console.
- Play Mode sinh đủ map 32x32 từ `random-32-32-10.map`.
- Tile walkable và obstacle hiển thị bằng sprite trong `Assets/Sprites/Tiles`.
- Tank spawn trên ô đi được.
- Tank chạy được trên ô `.` và bị chặn bởi ô `@`.
- Camera follow tank ổn định.
