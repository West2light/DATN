# PLAN: Fix PIBT đứng yên trên các map không phải Alpha32

Ngày lập: 2026-06-22  
Phạm vi: `PIBT` local/backtest trong `MapF_TankTest_PIBT`, tập trung các map `Mansion`, `Chantry`, `Gallows`, `Maze128`. Không trộn với bug transport/TCP trừ khi log chứng minh cùng nguyên nhân.

## 1. Triệu chứng

- `Alpha32` chạy tốt: enemy tank di chuyển tìm và bắn `Base Eagle`.
- Các map khác: tank có thể di chuyển một đoạn ngắn, xoay, rồi đứng yên; một số lần quay ra ngoài hoặc không tiến tới Base.
- Đây không giống lỗi vật lý/collider đơn thuần, vì tank vẫn nhận update ban đầu nhưng sau đó mất đường đi hợp lệ hoặc planner không còn sinh action có ích.

## 2. Giả thuyết root cause chính

### H1 - Spawn và Base không cùng vùng liên thông

`BacktestRunner` dùng cùng scene PIBT cho nhiều map có cấu trúc rất khác nhau: `Alpha32` là map mở nhỏ, còn `Mansion`, `Chantry`, `Gallows`, `Maze128` có nhiều tường/hành lang/vùng bị tách.

Trong code hiện tại có dấu hiệu rủi ro:

- `MapTankTestBootstrap.ComputeEnemySpawnCells()` tạo enemy spawn bằng toạ độ local theo `BuildWidth/BuildHeight`, ví dụ `(c - 2, 1)`, `(1, r - 2)`, nhưng không cộng `BuildStartX/BuildStartY`.
- `MapScenarioBootstrapPIBT.TryResolveEagleSpawnCell()` trong backtest đặt Eagle gần tâm build window bằng `BuildStartX + BuildWidth / 2`, `BuildStartY + BuildHeight / 2`.
- `MapScenarioBootstrapPIBT.SpawnEnemies()` gọi `mapLoader.TryFindWalkableNear(enemySpawnCells[i])` trực tiếp.
- `MapLoader.TryFindWalkableNear()` chỉ tìm ô walkable gần nhất theo bán kính, không kiểm tra cùng connected component với Eagle, không ràng buộc trong build window.

Kết quả có thể xảy ra: enemy spawn hợp lệ về mặt `IsWalkable`, Eagle cũng hợp lệ, nhưng hai bên nằm ở hai vùng không có đường đi. Alpha32 che lỗi này vì map đủ mở.

### H2 - Goal resolver chọn ô gần Eagle nhưng không đảm bảo reachable từ agent

`GridEnemyAgentPIBT.ReplanPath()` gọi:

- `startCell = mapLoader.WorldToCell(...)`
- `goalCell = ResolveGoalCell(eagleCell, blockedCells, startCell)`
- `GridPIBTPathfinder.TryFindPath(...)`

`ResolveGoalCell()` chỉ tránh blocked/friendly và tìm ô `PIBTPlanner.IsAgentWalkable()`, nhưng không chứng minh ô đó cùng component với `startCell`. Nếu goal ở component khác, pathfinder fail lặp lại, tank đứng yên hoặc chỉ recovery cục bộ.

### H3 - Destructible fallback không cứu được lỗi component

`TryFindDestructibleOnPath()` chỉ hữu ích khi có đường tới goal nếu coi destructible là passable. Nếu start và Eagle nằm trong hai component tách hoàn toàn, hoặc spawn bị resolve sang vùng phụ, fallback không tìm được mục tiêu phá tường có ý nghĩa.

### H4 - Dynamic friendly blocked cells làm map hẹp dễ nghẽn hơn

Trên map hành lang, nhiều tank gần nhau có thể làm `BuildDynamicBlockedCells()` khiến goal hoặc corridor bị coi là blocked. Đây có thể là nguyên nhân phụ sau khi đã đảm bảo spawn/base cùng component.

## 3. Không sửa vội trước khi có log này

Trước khi đổi behavior, thêm diagnostics tối thiểu để xác nhận:

- Map label, map file, `BuildStartX`, `BuildStartY`, `BuildWidth`, `BuildHeight`.
- Eagle preferred cell, Eagle resolved cell.
- Enemy preferred spawn cell, resolved spawn cell, khoảng cách tới Eagle.
- Connected component id/size của Eagle và từng enemy spawn.
- Với mỗi agent: `startCell`, `goalCell`, `resolvedGoal`, path length, fail reason, có destructible target hay không.
- Khi tank đứng yên quá N giây: in state `hasPath`, `pathIndex`, `currentPath.Count`, `isShooting`, recovery reason.

Log nên ghi vào `adds/output/pibt_non_alpha_map_diag_*.log` hoặc CSV nhỏ để so sánh từng map.

## 4. Kế hoạch sửa theo milestone

### M0 - Chẩn đoán component/reachability

- Thêm helper debug-only trong `MapLoader` hoặc utility riêng để flood-fill component từ một cell walkable.
- Tạo log một lần sau khi spawn Eagle/enemies trong `MapScenarioBootstrapPIBT`.
- Chạy backtest một lượt ngắn cho từng map `Mansion`, `Chantry`, `Gallows`, `Maze128` với algo `PIBT`.

Điều kiện pass M0: biết rõ mỗi enemy có cùng component với Eagle hay không. Nếu khác component, không cần đoán sang movement/collider.

### M1 - Sửa spawn cell về đúng hệ toạ độ build window

- Trong `ComputeEnemySpawnCells()`, các điểm local như `(c - 2, 1)` phải được đổi thành map cell global:
  - `new Vector2Int(mapLoader.BuildStartX + localX, mapLoader.BuildStartY + localY)`
- Áp dụng cùng nguyên tắc cho các spawn phụ phát sinh từ player/enemy layout nếu đang dùng local build coord.
- Không dùng `TryFindWalkableNear()` toàn map cho enemy spawn trong backtest nếu có thể rơi ra ngoài vùng build.

Điều kiện pass M1: preferred/resolved enemy spawn nằm trong build window và không bị lệch về góc global `(0,0)` khi map có offset/crop.

### M2 - Spawn Eagle và enemies cùng connected component

- Chọn Eagle cell trước, sau đó resolve enemy spawn bằng helper mới:
  - `TryFindWalkableNearInSameComponent(preferredCell, eagleCell, out result)`
- Nếu preferred spawn không cùng component, tìm nearest walkable trong component của Eagle.
- Nếu component của Eagle quá nhỏ, chọn lại Eagle trong component walkable lớn nhất của build window.
- Với map lớn/hành lang, ưu tiên component có kích thước đủ lớn và nhiều lối ra, tương tự logic `TryFindWalkableWithSpace()`.

Điều kiện pass M2: mọi enemy spawn trong backtest PIBT cùng component với Eagle, hoặc log ghi rõ agent bị loại vì không có spawn hợp lệ.

### M3 - Goal resolver phải component-safe

- Trong `GridEnemyAgentPIBT.ResolveGoalCell()`, chỉ chọn candidate goal reachable từ `startCell`.
- Nếu Eagle ở component khác, trả fail diagnostic rõ ràng thay vì lặp replan im lặng.
- Cân nhắc cache component map theo map load để không flood-fill mỗi frame.

Điều kiện pass M3: path fail vì unreachable component không còn bị nuốt im lặng; tank không đứng yên mà không có reason.

### M4 - Chuẩn hoá fail reason của pathfinder

- Đổi `GridPIBTPathfinder.TryFindPath(...)` hoặc thêm overload trả diagnostic:
  - `StartNotWalkable`
  - `GoalNotWalkable`
  - `DifferentComponent`
  - `NoPathAfterPIBT`
  - `BlockedByDynamicAgents`
  - `Timeout`
- Log fail reason theo agent với throttle để tránh spam console.

Điều kiện pass M4: khi tank không di chuyển, log chỉ ra nguyên nhân cụ thể trong 1-2 dòng/agent.

### M5 - Recovery cho map hẹp sau khi component đúng

- Nếu path length bằng 0 hoặc agent không tăng cell visited trong N giây, thử:
  - bỏ dynamic blocked cells tạm thời trong một lần replan,
  - chọn staging goal gần Eagle cùng component,
  - hoặc chọn corridor waypoint trung gian thay vì goal sát Eagle.
- Không ưu tiên sửa recovery trước M1-M3, vì recovery không thể cứu spawn/goal khác component.

Điều kiện pass M5: trên map hẹp, agent không đứng yên vĩnh viễn chỉ vì tank bạn chặn một ô hẹp.

### M6 - Kiểm tra collider/physics là điều kiện phụ

- Kiểm tra prefab enemy có `BoxCollider2D`/`CapsuleCollider2D` và `Rigidbody2D` đúng mode.
- Xác nhận tank không đè nhau bất thường bằng physics layer/collider, nhưng không coi đây là root cause nếu diagnostics báo `DifferentComponent` hoặc `NoPath`.
- Collider chỉ được sửa nếu log cho thấy tank có path hợp lệ nhưng bị kẹt do overlap/blocked vật lý.

Điều kiện pass M6: không còn overlap phản vật lý rõ ràng, nhưng sửa collider không được che lấp lỗi planner.

## 5. Tiêu chí nghiệm thu

- `Alpha32` giữ nguyên hành vi tốt hiện tại.
- Trên `Mansion`, `Chantry`, `Gallows`, `Maze128`, mỗi enemy PIBT phải có một trong hai kết quả rõ ràng:
  - di chuyển về phía Eagle và tăng `cellsVisited`, hoặc
  - bị skip/fail với diagnostic cụ thể, không đứng yên im lặng.
- Trong backtest 30-60 giây/map, không còn pattern nhiều tank `cellsVisited <= 1` khi không bắn và không có fail reason.
- Base Eagle phải bị tiếp cận/bắn trên các map có component đủ lớn.
- Có artifact log/CSV trong `adds/output` hoặc `BacktestResults` để đối chiếu trước/sau.

## 6. Thứ tự triển khai đề xuất

1. M0: thêm diagnostics component và path fail.
2. M1: sửa offset spawn cell theo `BuildStartX/BuildStartY`.
3. M2: bắt buộc spawn Eagle/enemy cùng component.
4. M3-M4: component-safe goal resolver và fail reason rõ.
5. M5: recovery cho hành lang hẹp nếu vẫn còn đứng yên.
6. M6: rà collider/physics sau cùng để xử lý overlap thật.

## 7. Rủi ro khi sửa

- Nếu chỉ tăng recovery hoặc chỉnh collider, bug có thể vẫn tồn tại vì agent không có path hợp lệ từ đầu.
- Nếu chọn component lớn nhất mà không xét vị trí Eagle, có thể làm Base spawn xa/khó tiếp cận khác thiết kế hiện tại.
- Nếu flood-fill mỗi replan không cache, map lớn như `Maze128` có thể làm backtest chậm.
- Nếu dynamic blocked cells bị bỏ hoàn toàn, tank có thể đè logic lên nhau dù physics không overlap.

## 8. Kết luận kỹ thuật

Hướng sửa ưu tiên là làm spawn và goal của PIBT component-aware. Alpha32 không tái hiện lỗi vì map nhỏ và liên thông; các map còn lại cần đảm bảo từ lúc spawn rằng enemy và Eagle nằm trong cùng vùng đi được, sau đó mới xử lý planner/recovery/collider.
