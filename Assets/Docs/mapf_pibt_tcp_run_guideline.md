# Guideline chạy mode `PIBT_TCP`

Tài liệu này hướng dẫn cách chạy mode `PIBT_TCP` theo trạng thái code hiện tại của repo.

Phạm vi:

- Build và chạy `pibt_tcp_server` trong WSL.
- Mở scene TCP trong Unity.
- Kiểm tra các cấu hình cần có trước khi bấm Play.

Không bao gồm test tự động. Tài liệu này được viết từ việc đọc code hiện tại và kiểm tra build.

## 1. Thành phần hiện có

### Unity

- Scene chạy mode TCP:
  - `Assets/Scenes/MapF_TankTest_PIBT_TCP.unity`
- Bootstrap scene:
  - `Assets/Scripts/PibtTcp/MapScenarioBootstrapPIBTTcp.cs`
- TCP client:
  - `Assets/Scripts/PibtTcp/PibtTcpClient.cs`
- Enemy runtime TCP:
  - `Assets/Scripts/PibtTcp/GridEnemyAgentPIBTTcp.cs`
- Grid/map cũ vẫn được tái sử dụng:
  - `Assets/Scripts/MapLoader.cs`
  - `Assets/Scripts/MapTankTestBootstrap.cs`

### WSL C++

- Server entrypoint:
  - `/home/west2light/projectY/src/tcp_server_main.cpp`
- TCP server:
  - `/home/west2light/projectY/src/PibtTcpServer.cpp`
- Build target:
  - `pibt_tcp_server`

## 2. Lưu ý quan trọng trước khi chạy

1. Menu hiện chưa route sang scene TCP.
   Vì vậy để chạy mode này, nên mở trực tiếp scene `MapF_TankTest_PIBT_TCP.unity` trong Unity Editor.

2. `MapLoader` vẫn là script dựng map chính.
   Scene TCP không có script tạo map riêng; nó dùng lại `MapLoader.LoadAndBuild()` như A* và PIBT C#.

3. TCP bootstrap mặc định connect tới:

```text
host = 127.0.0.1
port = 7777
timeout = 100 ms
```

Các giá trị này nằm trên component `MapScenarioBootstrapPIBTTcp`.

4. Repo WSL hiện là source tree độc lập.
   Khi build server, chạy lệnh trong:

```text
/home/west2light/projectY
```

## 3. Build server trong WSL

Mở WSL Ubuntu, vào thư mục source:

```bash
cd /home/west2light/projectY
```

Generate build files:

```bash
cmake -B build ./ -DCMAKE_BUILD_TYPE=Release
```

Build server:

```bash
cmake --build build -j --target pibt_tcp_server
```

Nếu muốn build cả binary cũ `lifelong` luôn:

```bash
cmake --build build -j --target pibt_tcp_server lifelong
```

## 4. Chạy server

Trong WSL:

```bash
cd /home/west2light/projectY
./build/pibt_tcp_server --host 127.0.0.1 --port 7777
```

Khi server lên đúng, log kỳ vọng:

```text
[pibt_tcp_server] listening on 127.0.0.1:7777
```

Server hiện dùng TCP JSON line.
Các message chính:

- `hello`
- `plan_step`
- `shutdown`

## 5. Mở scene trong Unity

Trong Unity Editor:

1. Mở scene:
   `Assets/Scenes/MapF_TankTest_PIBT_TCP.unity`
2. Chọn object chứa `MapTankTestBootstrap`, `MapLoader`, `MapScenarioBootstrapPIBTTcp`.
3. Kiểm tra `MapScenarioBootstrapPIBTTcp`:
   - `tcpHost = 127.0.0.1`
   - `tcpPort = 7777`
   - `tcpTimeout = 100`
4. Kiểm tra `MapLoader.mapFileName` nếu muốn đổi map.
   `MapLoader` vẫn đọc `.map` từ `Assets/MapData`.
5. Bấm Play.

## 6. Luồng chạy trong scene

Khi bấm Play, flow hiện tại là:

1. `MapTankTestBootstrap.Start()`
2. `mapLoader.LoadAndBuild()`
3. `MapTankTestBootstrap.SpawnScenario()`
4. Nếu scene có `MapScenarioBootstrapPIBTTcp`, bootstrap TCP được ưu tiên
5. `MapScenarioBootstrapPIBTTcp.SpawnScenario()`
6. Spawn Eagle + enemy
7. Tạo `PibtTcpClient`
8. Gửi `hello` tới server
9. Trong runtime, `GridEnemyAgentPIBTTcp` gửi `plan_step`

## 7. Dấu hiệu chạy đúng

### Ở WSL server

Khi Unity kết nối và gửi request, log thường sẽ có dạng:

```text
[pibt_tcp_server] client connected
[pibt_tcp_server] hello session=...
[pibt_tcp_server] plan_step session=... requestId=... agents=...
```

### Ở Unity Console

Các log quan trọng cần tìm:

- `[PibtTcpClient] Connected: server=..., planner=...`
- `[MapScenarioBootstrapPIBTTcp] TCP session started: ...`
- `[PibtTcpClient] req=... latency=... compute=... timeout=...`

## 8. Nếu mở scene nhưng enemy không chạy

Kiểm tra theo thứ tự:

1. Server WSL đã chạy chưa.
2. `tcpHost` và `tcpPort` trong `MapScenarioBootstrapPIBTTcp` có khớp không.
3. Scene đang mở có đúng là `MapF_TankTest_PIBT_TCP.unity` không.
4. Object bootstrap có đúng là `MapScenarioBootstrapPIBTTcp`, không phải `MapScenarioBootstrapPIBT`.
5. Console Unity có báo `ConnectAndHello failed` hoặc `plan_step timed out` không.
6. WSL server có log `client connected` không.

## 9. Nếu muốn đổi map

Có 2 cách:

1. Đổi trực tiếp `MapLoader.mapFileName` trong scene.
2. Dùng cơ chế `PlayerPrefs.SelectedMapFile` mà `MapLoader` đã hỗ trợ.

Trong mode TCP hiện tại, vẫn nên ưu tiên kiểm tra chạy ổn với map nhỏ trước, ví dụ:

```text
random-32-32-10.map
```

## 10. Những gì guide này chưa giả định

- Không giả định menu đã có nút vào mode TCP.
- Không giả định LAN flow đã tích hợp mode TCP.
- Không giả định backtest đã route đầy đủ qua TCP scene.
- Không giả định server và Unity ở hai máy khác nhau; guide hiện viết cho local Windows + WSL.

## 11. Lệnh chạy nhanh

### WSL

```bash
cd /home/west2light/projectY
cmake -B build ./ -DCMAKE_BUILD_TYPE=Release
cmake --build build -j --target pibt_tcp_server
./build/pibt_tcp_server --host 127.0.0.1 --port 7777
```

### Unity

```text
Open Assets/Scenes/MapF_TankTest_PIBT_TCP.unity
Check MapScenarioBootstrapPIBTTcp host/port
Press Play
```

## 12. Ghi chú hiện trạng

- Mã hiện tại cho thấy mode TCP đã có nhiều phần hơn Sprint 01:
  - Unity đã có `PibtTcpClient`, `PibtTcpSessionState`, `GridEnemyAgentPIBTTcp`, `MapScenarioBootstrapPIBTTcp`
  - WSL `CMakeLists.txt` đã nối thêm `PlannerSession.cpp` và `UnityStartKitAdapter.cpp`
- Vì vậy khi gặp lỗi runtime, nên đối chiếu code hiện tại trước, không nên dựa hoàn toàn vào plan Sprint cũ.
