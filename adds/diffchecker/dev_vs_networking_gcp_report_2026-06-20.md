# Báo cáo diff: `dev` vs `networking-gcp` vs `networking-gcp-backup`

Ngày kiểm tra: 2026-06-20  
Repo: `D:\2025.2\DATN\projectY`  
Phạm vi: so sánh code sau khi chạy `git fetch --all --prune`.

## 1. Cách kiểm tra

Để tránh ảnh hưởng các thay đổi local đang có trong worktree, báo cáo này so sánh trực tiếp remote-tracking refs:

- `origin/dev`
- `origin/networking-gcp`
- `origin/networking-gcp-backup`

Worktree lúc kiểm tra đang ở `dev` và có thay đổi local/untracked, nên không checkout hoặc reset nhánh nào.

Các lệnh chính đã dùng:

```powershell
git -c safe.directory=D:/2025.2/DATN/projectY fetch --all --prune
git -c safe.directory=D:/2025.2/DATN/projectY rev-parse origin/dev origin/networking-gcp origin/networking-gcp-backup
git -c safe.directory=D:/2025.2/DATN/projectY rev-list --left-right --count origin/dev...origin/networking-gcp
git -c safe.directory=D:/2025.2/DATN/projectY rev-list --left-right --count origin/dev...origin/networking-gcp-backup
git -c safe.directory=D:/2025.2/DATN/projectY rev-list --left-right --count origin/networking-gcp...origin/networking-gcp-backup
git -c safe.directory=D:/2025.2/DATN/projectY diff --name-status origin/dev..origin/networking-gcp
git -c safe.directory=D:/2025.2/DATN/projectY diff --name-status origin/dev..origin/networking-gcp-backup
git -c safe.directory=D:/2025.2/DATN/projectY diff --name-status origin/networking-gcp-backup..origin/networking-gcp
```

## 2. Trạng thái commit

| Nhánh | Commit |
| --- | --- |
| `origin/dev` | `273ad4b9782fbb3d612f183e98c9b04a513fb763` |
| `origin/networking-gcp` | `616973c95f35645b5c109d5c85beff54e16b33ce` |
| `origin/networking-gcp-backup` | `9f35c9be3bf79c086effd45eaa0d83c20d4ebb38` |

Merge-base của `origin/dev` với cả hai nhánh networking là:

```text
9f35c9be3bf79c086effd45eaa0d83c20d4ebb38
```

Kết luận quan hệ nhánh:

- `origin/networking-gcp-backup` chính là điểm tách nhánh chung.
- `origin/networking-gcp` = `origin/networking-gcp-backup` + 1 commit riêng.
- `origin/dev` đã đi tiếp 8 commit so với `origin/networking-gcp-backup`.

Ahead/behind:

| So sánh | Bên trái | Bên phải | Diễn giải |
| --- | ---: | ---: | --- |
| `origin/dev...origin/networking-gcp` | 8 | 1 | `dev` có 8 commit riêng, `networking-gcp` có 1 commit riêng |
| `origin/dev...origin/networking-gcp-backup` | 8 | 0 | `backup` chưa có 8 commit mới của `dev` |
| `origin/networking-gcp...origin/networking-gcp-backup` | 1 | 0 | `networking-gcp` mới hơn `backup` đúng 1 commit |

Commit riêng duy nhất của `networking-gcp`:

```text
616973c fix: pin 2d.animation 13.0.4, add free-disk-space for WebGL CI, migrate deploy URLs to https/domain
```

## 3. Thống kê diff trực tiếp

| So sánh | Thống kê |
| --- | --- |
| `origin/dev..origin/networking-gcp` | 24 files changed, 145 insertions, 3166 deletions |
| `origin/dev..origin/networking-gcp-backup` | 22 files changed, 131 insertions, 3166 deletions |
| `origin/networking-gcp-backup..origin/networking-gcp` | 4 files changed, 22 insertions, 8 deletions |

Hai nhánh networking gần như giống nhau. Khác biệt giữa chúng chỉ nằm ở workflow CI/GCP và package pin.

## 4. `networking-gcp-backup` khác `dev` như thế nào

`networking-gcp-backup` là bản cũ hơn `dev` 8 commit. So với `dev`, backup thiếu các phần sau:

### 4.1. Thiếu mode PIBT-C++ / PIBT-TCP

Các file có ở `dev` nhưng không có ở `networking-gcp-backup`:

```text
Assets/Scripts/GridEnemyAgentPIBT_TCP.cs
Assets/Scripts/GridEnemyAgentPIBT_TCP.cs.meta
Assets/Scripts/MapScenarioBootstrapPIBT_TCP.cs
Assets/Scripts/MapScenarioBootstrapPIBT_TCP.cs.meta
Assets/Scripts/PIBTTcpClient.cs
Assets/Scripts/PIBTTcpClient.cs.meta
```

Ảnh hưởng:

- `dev` có đường chạy PIBT qua TCP/server, dùng algorithm key `PIBT_TCP`.
- `backup` chỉ còn A* và PIBT local trong backtest/menu.
- Nếu checkout/chạy theo backup, phần test PIBT-C++ qua TCP sẽ không tồn tại.

### 4.2. Backtest trên `dev` đã mở rộng sang 3 thuật toán

Các file backtest khác nhau:

```text
Assets/Scripts/Backtest/BacktestConfigUI.cs
Assets/Scripts/Backtest/BacktestMode.cs
Assets/Scripts/Backtest/BacktestResultChart.cs
Assets/Scripts/Backtest/BacktestRunner.cs
Assets/Scripts/Backtest/DynamicObstacleSpawner.cs
Tools/backtest_plot_report.py
```

Khác biệt chính:

- `dev` hiển thị/chạy `AStar`, `PIBT`, `PIBT_TCP`.
- `backup` chỉ hiển thị/chạy `AStar`, `PIBT`.
- Chart/report trên `dev` có thêm series `PIBT-C++`.
- Số run trong UI trên `dev` tính theo `reps * 3`, còn backup tính `reps * 2`.

### 4.3. Menu và bootstrap map trên `dev` có logic chọn PIBT-TCP

Các file khác nhau:

```text
Assets/Scripts/MapTankTestBootstrap.cs
Assets/Scripts/MenuViewBootstrap.cs
```

Khác biệt chính:

- `dev` dùng `SelectedAlgorithm` để phân biệt `PIBT_TCP`.
- `dev` có nút mode thứ ba `PIBT-TCP` trong menu chọn map.
- `dev` dispatch sang `MapScenarioBootstrapPIBT_TCP` khi chọn mode TCP.
- `backup` chỉ chọn giữa A* và PIBT local.

### 4.4. `dev` có thêm một số runtime controller

Các file có ở `dev` nhưng không có ở backup:

```text
Assets/Scripts/Multiplayer/RelayManager.cs
Assets/Scripts/Multiplayer/RelayManager.cs.meta
Assets/Scripts/PauseMenuController.cs
Assets/Scripts/PauseMenuController.cs.meta
```

Ảnh hưởng:

- `dev` có thêm code quản lý Relay/Pause menu.
- `backup` thiếu các class này, nên nếu scene/script khác đang reference thì có thể phát sinh lỗi compile hoặc missing script khi quay về backup.

### 4.5. Package Unity khác nhau

`origin/dev`:

```json
"com.unity.2d.animation": "13.0.4"
"com.unity.ai.navigation": "2.0.12"
"com.unity.cinemachine": "2.10.7"
"com.unity.collab-proxy": "2.12.4"
"com.unity.netcode.gameobjects": "2.11.2"
"com.unity.services.authentication": "3.6.1"
"com.unity.services.core": "1.16.0"
"com.unity.services.relay": "1.0.5"
"com.unity.timeline": "1.8.12"
```

`origin/networking-gcp-backup`:

```json
"com.unity.2d.animation": "14.0.4"
"com.unity.ai.navigation": "2.0.10"
"com.unity.cinemachine": "2.10.5"
"com.unity.collab-proxy": "2.11.3"
"com.unity.netcode.gameobjects": "2.1.1"
"com.unity.sdk.linux-x86_64": "1.1.0"
"com.unity.timeline": "1.8.10"
"com.unity.toolchain.win-x86_64-linux": "1.1.0"
```

Điểm đáng chú ý:

- `dev` đã nâng Netcode lên `2.11.2`.
- `dev` có `com.unity.services.authentication`, `com.unity.services.core`, `com.unity.services.relay`.
- `backup` có Linux SDK/toolchain package nhưng thiếu Unity Services Relay/Auth/Core.
- `backup` vẫn dùng `com.unity.2d.animation` `14.0.4`, khác với `dev`.

## 5. `networking-gcp` khác `networking-gcp-backup` như thế nào

`networking-gcp` chỉ hơn backup 1 commit:

```text
616973c fix: pin 2d.animation 13.0.4, add free-disk-space for WebGL CI, migrate deploy URLs to https/domain
```

Các file đổi:

```text
.github/workflows/deploy-gcp.yml
.github/workflows/unity-build.yml
Packages/manifest.json
Packages/packages-lock.json
```

Khác biệt chính:

- Thêm biến workflow `APP_DOMAIN`.
- Chuyển deploy/check URL từ IP HTTP sang domain HTTPS.
- Health check đổi từ `/healthz` sang `/create` và kiểm tra text `tank mapf`.
- WebGL artifact check đổi từ `.wasm`/`.data` sang `.wasm.br`/`.data.br`.
- Workflow `unity-build.yml` thêm step `jlumbroso/free-disk-space@main` cho build WebGL.
- Package `com.unity.2d.animation` được pin từ `14.0.4` xuống `13.0.4`, trùng với `dev`.

## 6. `networking-gcp` khác `dev` như thế nào

`networking-gcp` = backup + 1 commit CI/package, nên nó vẫn thiếu gần như toàn bộ 8 commit mới của `dev`.

So với `dev`, `networking-gcp` vẫn thiếu:

- PIBT-TCP runtime:
  - `GridEnemyAgentPIBT_TCP.cs`
  - `MapScenarioBootstrapPIBT_TCP.cs`
  - `PIBTTcpClient.cs`
- Backtest 3 thuật toán `AStar` / `PIBT` / `PIBT_TCP`.
- Menu chọn mode thứ ba `PIBT-TCP`.
- `RelayManager.cs`.
- `PauseMenuController.cs`.
- Package Unity Services Relay/Auth/Core và Netcode `2.11.2`.

Nhưng `networking-gcp` có thêm so với `dev`:

- Workflow dùng `APP_DOMAIN`.
- URL deploy/check chuyển sang HTTPS domain.
- WebGL CI free disk space step.
- Kiểm tra artifact nén `.wasm.br` và `.data.br`.

Lưu ý quan trọng: `origin/dev` hiện vẫn dùng deploy target dạng IP HTTP trong `.github/workflows/deploy-gcp.yml` và `.github/workflows/unity-build.yml`; commit HTTPS/domain mới chỉ nằm trên `origin/networking-gcp`.

## 7. Danh sách file diff trực tiếp

### 7.1. `origin/dev..origin/networking-gcp`

```text
M	.github/workflows/deploy-gcp.yml
M	.github/workflows/unity-build.yml
M	Assets/Scripts/Backtest/BacktestConfigUI.cs
M	Assets/Scripts/Backtest/BacktestMode.cs
M	Assets/Scripts/Backtest/BacktestResultChart.cs
M	Assets/Scripts/Backtest/BacktestRunner.cs
M	Assets/Scripts/Backtest/DynamicObstacleSpawner.cs
D	Assets/Scripts/GridEnemyAgentPIBT_TCP.cs
D	Assets/Scripts/GridEnemyAgentPIBT_TCP.cs.meta
D	Assets/Scripts/MapScenarioBootstrapPIBT_TCP.cs
D	Assets/Scripts/MapScenarioBootstrapPIBT_TCP.cs.meta
M	Assets/Scripts/MapTankTestBootstrap.cs
M	Assets/Scripts/MenuViewBootstrap.cs
D	Assets/Scripts/Multiplayer/RelayManager.cs
D	Assets/Scripts/Multiplayer/RelayManager.cs.meta
D	Assets/Scripts/PIBTTcpClient.cs
D	Assets/Scripts/PIBTTcpClient.cs.meta
D	Assets/Scripts/PauseMenuController.cs
D	Assets/Scripts/PauseMenuController.cs.meta
D	Assets/_Recovery/0 (1).unity
D	Assets/_Recovery/0 (1).unity.meta
M	Packages/manifest.json
M	Packages/packages-lock.json
M	Tools/backtest_plot_report.py
```

### 7.2. `origin/dev..origin/networking-gcp-backup`

```text
M	Assets/Scripts/Backtest/BacktestConfigUI.cs
M	Assets/Scripts/Backtest/BacktestMode.cs
M	Assets/Scripts/Backtest/BacktestResultChart.cs
M	Assets/Scripts/Backtest/BacktestRunner.cs
M	Assets/Scripts/Backtest/DynamicObstacleSpawner.cs
D	Assets/Scripts/GridEnemyAgentPIBT_TCP.cs
D	Assets/Scripts/GridEnemyAgentPIBT_TCP.cs.meta
D	Assets/Scripts/MapScenarioBootstrapPIBT_TCP.cs
D	Assets/Scripts/MapScenarioBootstrapPIBT_TCP.cs.meta
M	Assets/Scripts/MapTankTestBootstrap.cs
M	Assets/Scripts/MenuViewBootstrap.cs
D	Assets/Scripts/Multiplayer/RelayManager.cs
D	Assets/Scripts/Multiplayer/RelayManager.cs.meta
D	Assets/Scripts/PIBTTcpClient.cs
D	Assets/Scripts/PIBTTcpClient.cs.meta
D	Assets/Scripts/PauseMenuController.cs
D	Assets/Scripts/PauseMenuController.cs.meta
D	Assets/_Recovery/0 (1).unity
D	Assets/_Recovery/0 (1).unity.meta
M	Packages/manifest.json
M	Packages/packages-lock.json
M	Tools/backtest_plot_report.py
```

### 7.3. `origin/networking-gcp-backup..origin/networking-gcp`

```text
M	.github/workflows/deploy-gcp.yml
M	.github/workflows/unity-build.yml
M	Packages/manifest.json
M	Packages/packages-lock.json
```

## 8. Kết luận

Nếu mục tiêu là lấy code mới nhất để phát triển tiếp, `origin/dev` đang là nhánh đầy đủ hơn về gameplay/backtest/runtime. Hai nhánh networking là nhánh cũ hơn: `networking-gcp-backup` là baseline cũ, còn `networking-gcp` chỉ thêm đúng 1 commit về CI/GCP HTTPS/domain và pin package.

Nếu muốn hợp nhất, hướng hợp lý là lấy `origin/dev` làm nền, rồi đưa commit `616973c` từ `networking-gcp` sang. Cần xử lý kỹ conflict ở:

- `.github/workflows/deploy-gcp.yml`
- `.github/workflows/unity-build.yml`
- `Packages/manifest.json`
- `Packages/packages-lock.json`

Rủi ro chính khi merge là package Unity/Netcode/Relay: `dev` dùng Netcode `2.11.2` và Unity Services Relay/Auth/Core, còn `networking-gcp` đang ở Netcode `2.1.1` và không có các package services này. Không nên lấy nguyên manifest của `networking-gcp` đè lên `dev` nếu muốn giữ các tính năng networking/runtime mới của `dev`.
