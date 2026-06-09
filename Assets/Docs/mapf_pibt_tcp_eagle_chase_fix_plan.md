# Plan kiểm tra và khắc phục PIBT_TCP quay vòng khi tìm Eagle

## 1. Mục tiêu

Hoàn thiện mode `PIBT_TCP` để Enemy tìm và áp sát `EagleBase` nhanh, ổn định như scene `MapF_TankTest_PIBT`, thay vì đi được vài bước rồi quay vòng tại chỗ.

Phạm vi lần này:

- So sánh trực tiếp với scene `Assets/Scenes/MapF_TankTest_PIBT.unity`.
- Giữ map đơn giản `random-32-32-10.map`.
- Không thêm thùng gỗ, rào chắn, destructible obstacle hoặc layout kiểu `ht_mansion_n`.
- Chuẩn hóa scale map/camera theo scene PIBT C#.
- Kiểm tra riêng phần TCP/action model `FW/CR/CCR/W`.

## 2. Nhận định ban đầu

TCP đã gửi nhận ổn định sau fix timeout trước đó: `plan_result` có action và latency khoảng 90 ms. Vì vậy lỗi hiện tại không còn là mất kết nối TCP, mà nằm ở bridge từ action của server sang chuyển động Unity.

Các điểm nghi ngờ chính:

1. `PIBT_TCP` dùng action model có hướng (`orientation`) của C++ start-kit: `FW`, `CR`, `CCR`, `W`.
2. `PIBT C#` không chạy action xoay từng tick như vậy; nó nhận path dạng waypoint rồi dùng steering Unity để đi tới cell tiếp theo.
3. `MapLoader.CellToWorld()` đảo trục Y: grid `y` tăng xuống dưới, world `+Y` tăng lên trên. Nếu mapping facing giữa world và grid lệch một dấu, server sẽ liên tục trả `CR/CCR`.
4. Scene TCP hiện bật `spawnEagleNearPlayer: 1`, trong khi benchmark so sánh nên khóa Eagle về cell cố định như `eagleCell: (16,16)`.
5. Root enemy prefab không phải object thực sự di chuyển; `EnemyTank` child mới là transform được physics kéo đi. Mọi phép đo position/cell/facing phải lấy từ `tankController.tankMover.transform`, không lấy root.

## 3. Baseline scene cần giữ

`MapF_TankTest_PIBT_TCP` phải khớp các cấu hình chính của `MapF_TankTest_PIBT`:

- `MapLoader.mapFileName = random-32-32-10.map`
- `MapLoader.useSelectedMapFileOverride = false`
- `MapLoader.tileSize = 1`
- `MapLoader.maxBuildWidth = 0`
- `MapLoader.maxBuildHeight = 0`
- `MapLoader.mapOffsetX = 0`
- `MapLoader.mapOffsetY = 0`
- Camera orthographic/camera zoom dùng cùng tỷ lệ với scene PIBT C#.
- Enemy scale giữ gần `0.72`, không dùng scale/layout riêng của `ht_mansion_n`.
- Không spawn thêm obstacle runtime ngoài wall từ `.map` và collider của tank.

Đề xuất chỉnh scene trước khi debug action:

- Set `spawnEagleNearPlayer = false`.
- Dùng `eagleCell = (16,16)` để tạo mục tiêu ổn định ở giữa map.
- Giữ enemy spawn từ `MapTankTestBootstrap.ComputeEnemySpawnCells()` hoặc danh sách hiện có ở 4 góc/mid-edge.

## 4. Phase A - Instrument runtime để thấy chính xác vì sao quay

Thêm logging có giới hạn trong `GridEnemyAgentPIBTTcp` và `MapScenarioBootstrapPIBTTcp`, chỉ bật bằng bool debug để không spam console.

Log tối thiểu mỗi agent:

- `requestId`
- `agentId`
- `currentCell`
- `goalCell`
- `facingCell`
- `orientation` gửi lên server
- action nhận về: `FW/CR/CCR/W`
- `nextLoc`, `targetCell`
- world position của `tankMover`
- distance tới Eagle
- số tick liên tiếp chỉ nhận `CR/CCR`

Tiêu chí pass của Phase A:

- Tái hiện được vòng xoay và thấy rõ action sequence trước khi kẹt.
- Xác định được agent quay vì server trả `CR/CCR` lặp, hay vì Unity nhận `FW` nhưng steering không tới được cell.

## 5. Phase B - Test orientation mapping bằng unit/debug harness nhỏ

Tạo test logic không cần Play dài:

1. Đặt tank giả ở 1 cell, lần lượt set world rotation theo 4 hướng.
2. Gọi `GetFacingCell()` và `PibtTcpGridAdapter.DirectionToOrientation()`.
3. So với convention C++:
   - `0 = east`, loc `+1`
   - `1 = south`, loc `+cols`
   - `2 = west`, loc `-1`
   - `3 = north`, loc `-cols`
4. Kiểm tra `CR` trong C++ là `(orientation + 1) % 4`; trong Unity phải tương ứng xoay world clockwise từ east sang south.
5. Kiểm tra `CCR` là `(orientation - 1 + 4) % 4`; trong Unity phải xoay world counter-clockwise từ east sang north.

Tiêu chí pass của Phase B:

- Bảng mapping 4 hướng khớp tuyệt đối giữa Unity và start-kit.
- `FW nextLoc` từ server trỏ đúng cell phía trước theo hướng Unity đang nhìn.

Nếu fail:

- Sửa `GetFacingCell()` hoặc `DirectionToOrientation()`.
- Không sửa bằng cách đảo bừa `CR/CCR`; phải chứng minh bằng bảng 4 hướng.

## 6. Phase C - So sánh path intent với PIBT C#

Chạy cùng map, cùng Eagle cell, cùng spawn cells:

1. Scene `MapF_TankTest_PIBT`: ghi lại 10-20 cell đầu mà mỗi enemy đi qua.
2. Scene `MapF_TankTest_PIBT_TCP`: ghi lại action/cell đầu ra từ server.
3. Quy đổi action TCP thành cell sequence dự kiến.
4. So sánh Manhattan distance tới Eagle theo thời gian.

Tiêu chí pass:

- TCP không cần giống hệt path PIBT C#, nhưng distance tới Eagle phải giảm đều.
- Không có chuỗi `CR,CCR,CR,CCR` hoặc `CR,CR,CR,CR` kéo dài khi cell không đổi.

## 7. Phase D - Sửa execution policy của TCP agent

Nếu orientation đúng nhưng vẫn quay vòng, sửa runtime execution theo hướng sau:

1. Không apply action mới khi đang hoàn tất rotate hoặc đang đi dở cell.
2. Sau khi hoàn tất `CR/CCR`, cập nhật local facing/rotation state ngay, không chờ physics drift.
3. Khi nhận `FW`, đi tới `nextLoc` như waypoint, dùng steering giống `GridEnemyAgentPIBT`.
4. Chỉ gửi `currentCell` mới lên server khi tank đủ gần center cell.
5. Nếu server trả `CR/CCR` quá nhiều lần mà cell không đổi, kích hoạt diagnostic recovery:
   - force recompute facing từ `tankMover.transform.up`
   - reset pending action
   - gửi lại state sau một tick

Tiêu chí pass:

- Agent không bị reset rotation giữa chừng.
- Agent hoàn tất một cell move trước khi nhận move mới.
- `btCellsVisited` tăng đều.

## 8. Phase E - Sửa mục tiêu Eagle và scene parity

Chỉnh scene TCP để phục vụ benchmark đơn giản:

- `spawnEagleNearPlayer = false`
- `eagleCell = (16,16)`
- `mapFileName = random-32-32-10.map`
- `useSelectedMapFileOverride = false`
- Không bật placement phase tạo obstacle.
- Không thêm destructible obstacle runtime.

Sau đó chạy lại:

- Enemy từ góc trên phải tiến về trung tâm.
- Enemy từ góc dưới phải tiến về trung tâm.
- Enemy không được ưu tiên đi vòng quanh player nếu Eagle đang là mục tiêu.

## 9. Phase F - Camera và scale

Đối chiếu `MapF_TankTest_PIBT` và `MapF_TankTest_PIBT_TCP`:

- Camera orthographic size/tỷ lệ zoom phải thấy được gameplay giống nhau.
- `tileSize = 1`.
- `enemyScale = 0.72`.
- `playerScale = 1.3`.
- Không dùng scale hoặc obstacle profile dành cho mansion.

Nếu camera bị lệch:

- Ưu tiên giữ `MapTankTestBootstrap.cameraZoom = 0.7`.
- Để `MapLoader.FitCamera()` build map, sau đó `MapTankTestBootstrap.SetupCamera()` quản lý follow/zoom như scene PIBT C#.

## 10. Phase G - Validation cuối

Chạy server:

```bash
cd /home/west2light/projectY
cmake --build build -j --target pibt_tcp_server lifelong
./build/pibt_tcp_server --host 0.0.0.0 --port 7777
```

Chạy Unity scene:

```text
Assets/Scenes/MapF_TankTest_PIBT_TCP.unity
```

Pass criteria:

- Unity console không có `PlanStep exception`.
- Không có chuỗi timeout/stale response.
- Có `plan_result` đều, timeout = 0.
- Trong 10 giây đầu, ít nhất 4/6 enemy giảm khoảng cách tới Eagle.
- Không agent nào quay tại chỗ quá 2 giây nếu không bị tường hoặc enemy khác chặn.
- TCP scene dùng `random-32-32-10.map`, không dùng `ht_mansion_n`.
- Không có thùng gỗ/rào chắn/destructible obstacle được spawn thêm cho map đơn giản này.

## 11. Thứ tự implement đề xuất

1. Chuẩn hóa scene TCP: Eagle fixed center, map/camera parity.
2. Thêm debug trace có flag.
3. Viết orientation harness hoặc runtime assertion 4 hướng.
4. Sửa mapping nếu sai.
5. Sửa execution policy nếu action bị reset giữa chừng.
6. Chạy Play qua MCP, đọc console và chụp/snapshot vị trí enemy.
7. Cập nhật lại `mapf_pibt_tcp_run_guideline.md` vì timeout hiện đã là `1000`, không còn `100`.

