# Báo cáo thay đổi đã pull về trên nhánh `dev`

## 1. Phạm vi kiểm tra

- Thời điểm kiểm tra: `23/05/2026`.
- Nhánh hiện tại: `dev`.
- Trạng thái remote: `HEAD` đang trùng `origin/dev`.
- Mốc so sánh được dùng trong báo cáo: `origin/main..HEAD`.

Lý do chọn mốc này:

- Reflog hiện tại không cho thấy một lệnh `git pull` tường minh gần nhất.
- Tuy nhiên, nhánh `dev` đang chứa 9 commit mới hơn `origin/main` và đã đồng bộ với `origin/dev`, nên đây là tập thay đổi hợp lý để xem là “những gì đã pull về” trên nhánh làm việc hiện tại.

Lưu ý:

- File `.vscode/settings.json` đang có thay đổi cục bộ trong worktree, không được tính là nội dung vừa pull từ remote.

## 2. Tóm tắt nhanh

- Tổng số commit mới trên `dev` so với `origin/main`: `9`.
- Tổng số file khác biệt: `29`.
- File thêm mới: `21`.
- File chỉnh sửa: `8`.
- Trọng tâm thay đổi:
  - Bổ sung luồng pathfinding **LNS2** cho bài toán MAPF.
  - Tạo scene thử nghiệm mới `MapF_TankTest_LNS2`.
  - Bổ sung bootstrap gameplay, health bar, game over, menu scene.
  - Chỉnh va chạm đạn để tránh tự bắn trúng xe sở hữu.
  - Cập nhật build settings để chạy được các scene MAPF mới.

## 3. Dòng thời gian commit

| Commit | Thời gian | Nội dung chính |
|---|---|---|
| `7714fe0` | `22/05/2026 21:36` | Bổ sung tự fit camera trong `MapLoader`, cập nhật `MapTankTestBootstrap` để dựng map và spawn scenario rõ ràng hơn. |
| `54f9431` | `22/05/2026 21:42` | Đợt thêm lớn cho **LNS2**: scene mới, planner, pathfinder, agent LNS2, bootstrap LNS2, tài liệu thiết kế scene. |
| `4f2c3d8` | `22/05/2026 22:10` | Sửa `Bullet` và `Turret` để đạn bỏ qua collider của xe bắn ra và dùng mask va chạm rõ ràng. |
| `c8629c4` | `22/05/2026 22:14` | Chỉnh prefab `StaticEnemy` để khớp cấu hình máu/thanh máu của enemy. |
| `6bcf4e2` | `22/05/2026 23:09` | Bổ sung `HealthBarVisibilityController`, HUD máu, game over hook và mở rộng bootstrap cho cả scene thường lẫn scene LNS2. |
| `bdbb969` | `22/05/2026 23:13` | Nâng bootstrap để đặt Eagle gần player tốt hơn và hoàn thiện logic spawn. |
| `5a47067` | `22/05/2026 23:20` | Thêm `MapGameOverController` và nối luồng quay lại menu khi player/base chết. |
| `5600537` | `22/05/2026 23:33` | Thêm `MenuViewBootstrap` và cập nhật `EditorBuildSettings` để đưa các scene MAPF vào build. |
| `a1e8730` | `22/05/2026 23:40` | Hoàn thiện thêm giao diện menu được dựng runtime bởi `MenuViewBootstrap`. |

## 4. File mới được tạo và mục đích

### 4.1. File mới phục vụ tính năng/chạy game

| File | Mục đích |
|---|---|
| `Assets/Docs/mapf_lns2_scene_plan.md` | Tài liệu mô tả kiến trúc scene `MapF_TankTest_LNS2`, thuật toán LNS2, dependency graph và luồng bootstrap. |
| `Assets/Scenes/MapF_TankTest_LNS2.unity` | Scene thử nghiệm mới để chạy bài toán MAPF với thuật toán LNS2 thay vì A* thuần. |
| `Assets/Scripts/LNS2Planner.cs` | Static planner trung tâm cho LNS2: quản lý flow grid, heuristic cache, đăng ký agent và vòng lặp Frank-Wolfe. |
| `Assets/Scripts/GridLNS2Pathfinder.cs` | Wrapper pathfinding dùng LNS2, trả path theo dạng `List<Vector2Int>` để các agent có thể gọi trực tiếp. |
| `Assets/Scripts/GridEnemyAgentLNS2.cs` | Enemy agent mới dùng LNS2 thay cho `GridEnemyAgent`, có replan, bám đường, line-of-sight và recovery khi bị kẹt. |
| `Assets/Scripts/MapScenarioBootstrapLNS2.cs` | Bootstrap riêng cho scene LNS2: spawn Eagle, enemy, khởi tạo planner, cấu hình target và HUD liên quan. |
| `Assets/Scripts/Ai/AIPatrolLNS2PathBehaviour.cs` | `AIBehaviour` cho nhánh AI cũ nhưng chuyển sang điều hướng bằng LNS2. |
| `Assets/Scripts/HealthBarVisibilityController.cs` | Điều khiển ẩn/hiện thanh máu theo thời gian bằng `CanvasGroup`. |
| `Assets/Scripts/MapGameOverController.cs` | Hiển thị overlay `GAME OVER`, chờ một khoảng ngắn rồi reset save và quay lại scene menu. |
| `Assets/Scripts/MenuViewBootstrap.cs` | Dựng/chỉnh UI menu lúc runtime, cấu hình nút `START` và `EXIT`, đồng thời định tuyến vào scene `MapF_TankTest`. |
| `DATN.slnx` | File solution mới để mở workspace/phần script Unity thuận tiện hơn trong IDE hỗ trợ `.slnx`. |

### 4.2. File `.meta` mới của Unity

Các file sau được tạo mới để Unity quản lý GUID và import settings cho asset/script mới:

- `Assets/Docs/mapf_lns2_scene_plan.md.meta`
- `Assets/Scenes/MapF_TankTest_LNS2.unity.meta`
- `Assets/Scripts/Ai/AIPatrolLNS2PathBehaviour.cs.meta`
- `Assets/Scripts/GridEnemyAgentLNS2.cs.meta`
- `Assets/Scripts/GridLNS2Pathfinder.cs.meta`
- `Assets/Scripts/HealthBarVisibilityController.cs.meta`
- `Assets/Scripts/LNS2Planner.cs.meta`
- `Assets/Scripts/MapGameOverController.cs.meta`
- `Assets/Scripts/MapScenarioBootstrapLNS2.cs.meta`
- `Assets/Scripts/MenuViewBootstrap.cs.meta`

## 5. File đã chỉnh sửa và ý nghĩa thay đổi

| File | Thay đổi chính | Mục đích |
|---|---|---|
| `.vscode/settings.json` | Chỉnh cấu hình VS Code trong repo. | Hỗ trợ môi trường làm việc; không ảnh hưởng trực tiếp gameplay. |
| `Assets/Prefabs/StaticEnemy.prefab` | Điều chỉnh prefab enemy. | Đồng bộ enemy với thay đổi về máu/thanh máu trong gameplay mới. |
| `Assets/Scripts/Bullet.cs` | Thêm `hitDetectionMask`, truyền `owner`, bỏ qua collider của chính xe bắn ra. | Tránh đạn tự va vào tank sở hữu, lọc va chạm ổn định hơn. |
| `Assets/Scripts/Turret.cs` | Xác định `tankRoot` và truyền vào `Bullet.Initialize(...)`. | Hoàn thiện cơ chế bullet ownership để hỗ trợ sửa lỗi va chạm đạn. |
| `Assets/Scripts/MapLoader.cs` | Thêm `FitCamera()` sau khi dựng map. | Tự căn camera theo kích thước map, giúp scene MAPF hiển thị gọn hơn. |
| `Assets/Scripts/MapScenarioBootstrap.cs` | Bổ sung logic đặt Eagle gần player, HUD máu base, đếm enemy, chuyển scene kế tiếp và hook game over. | Nâng scene MAPF gốc thành flow chơi hoàn chỉnh hơn, không chỉ dừng ở spawn AI. |
| `Assets/Scripts/MapTankTestBootstrap.cs` | Cập nhật luồng `Start()`, spawn player, gắn health bar player, hook game over và ưu tiên bootstrap LNS2 nếu có. | Cho phép cùng một bootstrap có thể dựng scene MAPF gốc hoặc scene LNS2. |
| `ProjectSettings/EditorBuildSettings.asset` | Thêm `Assets/Scenes/MapF_TankTest.unity` và `Assets/Scenes/MapF_TankTest_LNS2.unity` vào build settings. | Cho phép chạy trực tiếp các scene MAPF từ build/menu. |

Ghi chú:

- Bảng trên chỉ mô tả file chỉnh sửa theo mục đích. `Turret.cs` xuất hiện như một thay đổi nhỏ nhưng có vai trò quan trọng vì là đầu nối giữa tank và bullet.

## 6. Nhóm thay đổi theo chức năng

### 6.1. Nhóm LNS2 / MAPF

- Thêm đầy đủ stack LNS2 từ planner, pathfinder, agent tới bootstrap scene.
- Bổ sung scene riêng `MapF_TankTest_LNS2` để so sánh hoặc thử nghiệm với scene MAPF cũ.
- Tài liệu `Assets/Docs/mapf_lns2_scene_plan.md` cho thấy đây không phải patch nhỏ mà là một nhánh thử nghiệm/triển khai thuật toán khá hoàn chỉnh.

### 6.2. Nhóm HUD và vòng lặp gameplay

- Có thêm thanh máu cho player và Eagle.
- Có thêm bộ đếm enemy còn sống.
- Khi player hoặc Eagle chết, game hiển thị `GAME OVER` rồi tự quay lại menu.
- Khi hạ hết enemy ở scene thường, bootstrap có thể tự chuyển sang scene kế tiếp `MapF_TankTest_LNS2`.

### 6.3. Nhóm menu và khả năng chạy scene

- `MenuViewBootstrap` chỉnh menu runtime thay vì phụ thuộc hoàn toàn vào scene UI tĩnh.
- `EditorBuildSettings.asset` đã đưa các scene MAPF vào danh sách build, nghĩa là luồng menu -> play scene đã được nối chính thức.

### 6.4. Nhóm sửa lỗi combat

- Đạn đã có khái niệm “owner”.
- Viên đạn sẽ bỏ qua collider của chính tank bắn ra.
- Va chạm được lọc bằng `LayerMask`, giảm rủi ro đụng phải collider không mong muốn như blocker hoặc vật thể trung gian không cần xử lý.

## 7. Kết luận

Đợt thay đổi này không phải chỉ là sửa lỗi lẻ tẻ. Đây là một cụm cập nhật khá lớn để:

1. Đưa thuật toán **LNS2** vào repo như một hướng pathfinding MAPF mới.
2. Tạo scene thử nghiệm riêng để chạy và so sánh.
3. Hoàn thiện gameplay loop với HUD, game over, chuyển scene và menu.
4. Sửa lỗi va chạm đạn để combat ổn định hơn.

Nếu cần tách sâu hơn nữa, bước tiếp theo nên là viết thêm một báo cáo phụ theo từng commit hoặc theo từng file script, kèm diff chi tiết cho từng hàm quan trọng.
