# Hỏi Đáp Chuyên Sâu: PIBT C++/TCP — Client Mạng & Tài Xế Câm

Tài liệu này giải thích **cặp đôi** tạo nên mode thứ ba của đồ án (PIBT C++/TCP):
- `PIBTTcpClient.cs` (501 dòng) — **đường dây điện thoại** nối Unity với server C++ bên ngoài.
- `GridEnemyAgentPIBT_TCP.cs` (299 dòng) — **tài xế câm**: không tự nghĩ đường, chỉ nghe lệnh "đi tới ô X" rồi lái tới.

> 📎 Đọc kèm `ChiTiet_PIBTPlanner_QA.md` (mode PIBT C#) để thấy vì sao cần thêm mode TCP.

---

## 0. Bức tranh tổng thể: chuỗi 4 mắt xích

Khác hẳn hai mode trước (tính đường **ngay trong Unity**), mode này đẩy toàn bộ việc tính đường **ra một chương trình C++ chạy riêng** (trên WSL Ubuntu). Luồng điều khiển đi qua 4 mắt xích:

```
[Server C++ PIBT]  ←TCP/JSON→  [PIBTTcpClient]  →  [Coordinator]  →  [GridEnemyAgentPIBT_TCP] → xe tăng
  (bộ não thật)                  (đường dây)         (nhạc trưởng)      (tài xế câm)
```

1. **Server C++** (`Server-PIBT-TeamNoMan-sSky`, bản đoạt giải LoRR 2024): nhận trạng thái mọi agent, chạy thuật toán PIBT thật, trả về hành động mỗi agent.
2. **`PIBTTcpClient`**: gói/mở gói JSON, gửi/nhận qua socket TCP.
3. **`MapScenarioBootstrapPIBT_TCP`** (Coordinator — file riêng, "nhạc trưởng"): mỗi tick gom vị trí mọi xe, gọi `client.PlanStep(...)`, rồi phát ô đích cho từng xe qua `agent.SetNextTarget(...)`.
4. **`GridEnemyAgentPIBT_TCP`**: nhận ô đích, lái xe tới đó cho mượt.

> **Đây là điểm mạnh nhất để khoe:** hai mode kia là em **port** thuật toán sang C#. Mode này **chạy thẳng chính đoạn code C++ đoạt giải**, không viết lại — chứng minh kiến trúc client-server tách rời, và cho phép đưa server lên cloud. (Trả lời sẵn cho câu "sao phải làm bản TCP, C# chưa đủ à?")

---

# PHẦN A — `PIBTTcpClient.cs` (đường dây điện thoại)

## A1. Đầu vào, Đầu ra và Giao thức (Workflow)

**Bản chất:** một client TCP nói chuyện bằng **JSON-lines** — mỗi thông điệp là một dòng JSON kết thúc bằng `\n`. Vòng đời một phiên:
```
hello  →  plan_step  →  plan_step  →  ...  →  shutdown
(bắt tay)   (xin nước đi mỗi tick)              (chào tạm biệt)
```

**Đầu vào:** cấu hình `host`/`port` (mặc định `127.0.0.1:7777` — trỏ tới server trong WSL2), và mỗi tick là mảng trạng thái agent `(id, loc, orientation, goalLoc)`.

**Đầu ra:** mảng **hành động** cho từng agent (ví dụ `"W"`=chờ, hoặc lệnh di chuyển), cộng `LastNextLocs` = ô kế tiếp server chỉ định cho mỗi agent.

## A2. Ba (bốn) loại thông điệp

### `Hello` (dòng 113) — bắt tay + kiểm tra khớp bản đồ
Gửi kích thước map + ký hiệu ô + số agent. Chờ `hello_ack`. Điểm quan trọng: **đối chiếu kích thước map server báo về** với map ta gửi đi (dòng 143-151). Nếu lệch → **hủy phiên ngay**.

> **Tại sao phải kiểm tra khớp?** Vì server C++ và Unity phải hiểu **cùng một hệ tọa độ phẳng** (`loc = y*width + x`). Nếu hai bên lệch chiều rộng map, mọi chỉ số ô sẽ trỏ sai chỗ → xe chạy loạn xạ. Đây chính là gốc của bug "chỉ 1/5 map chạy" từng gặp — nên bước validate này là bắt buộc.

### `PlanStep` (dòng 220) — trái tim, gọi mỗi tick
Gửi trạng thái toàn đội, nhận `plan_result`. Có xử lý lỗi nhiều tầng: timeout, thiếu `plan_result`, parse thất bại → trả `null` để coordinator biết mà xoay xở.

### `Shutdown` (dòng 86) / `SendShutdown` (dòng 213) — chào tạm biệt
Báo server dọn phiên. Có 2 kiểu: `Shutdown` chờ `shutdown_ack` (timeout ngắn 500ms để không treo Unity); `SendShutdown` bắn-rồi-quên dùng trong `OnDestroy` (không chờ, tránh kẹt luồng chính).

### `Reset` (dòng 170) — tái dùng kết nối cho ván mới
Xóa trạng thái server mà **không cần ngắt-nối lại** socket. Chơi ván mới nhanh hơn. Có fallback nếu server bản cũ không hỗ trợ.

## A3. Chi tiết truyền tải đáng chú ý

- **`SendLine`/`RecvLine` (dòng 300, 319):** gửi/đọc **từng dòng** kết thúc `\n`. `RecvLine` đọc từng byte tới khi gặp `\n`. Đơn giản, khớp đúng giao thức JSON-lines.
- **TLS tùy chọn (`ResolveEndpoint`, dòng 342):** nếu `host` là URL `https://...` thì tự bọc `SslStream` — để nói chuyện với server cloud sau reverse-proxy HTTPS. Còn `127.0.0.1` thì TCP trần.
- **Timeout khắp nơi:** connect/hello/plan mỗi loại một ngưỡng riêng. Vì đây chạy trên **luồng chính của Unity** — nếu không timeout, server treo là cả game đơ.
- **Parse 2 tầng (`ParseActions`, dòng 394):** thử `JsonUtility` trước; hỏng thì rơi xuống parser chuỗi thủ công (`ParseStringArray`). Ô nào server không trả hành động → mặc định `"W"` (chờ) cho an toàn.
- **`Escape` (dòng 490):** thoát ký tự đặc biệt khi tự ghép chuỗi JSON bằng tay (không dùng thư viện serialize nặng).

> **Tại sao tự ghép JSON bằng `StringBuilder` thay vì thư viện?** Để **nhẹ và nhanh** — thông điệp `plan_step` gửi mỗi tick với hàng chục agent; tránh cấp phát rác từ thư viện JSON đầy đủ. Format cố định nên ghép tay là đủ và tối ưu.

---

# PHẦN B — `GridEnemyAgentPIBT_TCP.cs` (tài xế câm)

## B1. Bản chất: xe không có não tìm đường

So với `GridEnemyAgentPIBT`, file này **cắt bỏ toàn bộ phần tính đường**. Nó không có `PIBTPlanner`, không A\*, không danh sách đường (`currentPath`). Nó chỉ giữ **đúng một ô đích** (`_currentTarget`) do coordinator gán, và mỗi frame lái xe tiến tới đó.

**API công khai cho coordinator gọi:**
- `SetNextTarget(cell)` (dòng 104): "đi tới ô này".
- `StopMovement()` (dòng 101): "dừng ngay" (ví dụ mất kết nối server).
- `HasCommittedAction()` (dòng 176): "đã làm xong lệnh trước chưa?" — để coordinator biết khi nào xin nước đi mới.
- `CurrentCell`, `MovementTarget`: cho coordinator đọc trạng thái.

> **Tại sao tách "tài xế" khỏi "bộ não"?** Vì bộ não (PIBT) chạy theo **bước rời rạc** ở server, còn xe tăng cần **chuyển động vật lý liên tục**. Giữa hai tick của server, xe vẫn phải nhích mượt tới ô đích. Tài xế này lấp khoảng trống đó: "server bảo đi ô X, tôi lo phần lái tới X cho mượt".

## B2. Lái xe: `SteerTowardTarget` (dòng 191)
Logic xoay-rồi-tiến **giống hệt** hai mode kia (4 mức dot-product, duty cycle, ngưỡng chạm ô co giãn — xem `ChiTiet_GridEnemyAgent_QA.md` mục 5). Tới ô đích thì **đứng chờ** coordinator gán ô mới. Gặp thùng phá được thì dừng bắn vỡ.

## B3. Cơ chế Bắn — giống hệt các mode khác
`GetShootingTarget`/`CanShootTarget` với `RaycastAll` nhìn xuyên đồng đội. **Bắn được xử lý cục bộ trong Unity, không hỏi server** — vì bắn là phản xạ tức thời theo tầm nhìn, không cần điều phối toàn cục.

## B4. Cứu hộ kẹt & "cú hích tham lam" NON-PIBT (dòng 110 — cần trung thực)

Xe TCP có thể kẹt (server chậm, hai xe chèn nhau). `TrackStuckAndRecover` xử lý 2 nấc:

1. **Kẹt nhẹ (`stuckTimeout` 2.5s):** đứng im dù đáng lẽ phải đi → bật cờ `NeedsForcedReplan` để coordinator xin server tính lại.
2. **Kẹt nặng (`fallbackTimeout` 4s):** **cú hích tham lam** (`GreedyStepTowardEagle`, dòng 151) — tự chọn ô hàng xóm gần Eagle nhất (Manhattan) rồi nhích một bước.

> ⚠️ **ĐIỂM PHẢN BIỆN QUAN TRỌNG:** cú hích tham lam này **KHÔNG phải PIBT** — nó là heuristic cục bộ 1 bước, bỏ qua phối hợp. Code **ghi log rõ ràng** (`FALLBACK greedy nudge`, dòng 146) đúng để **không nhầm** nó với hành vi PIBT thật.
> **Nói với thầy:** *"Khi server bế tắc quá lâu, em có một cú hích tham lam cục bộ để backtest vẫn thu được dữ liệu chuyển động thay vì xe đứng chết. Em đánh dấu rõ đây là fallback NON-PIBT, tách khỏi số liệu PIBT thuần."* — Thành thật chỗ này là điểm cộng, giấu đi mới là điểm trừ.

## B5. `CurrentCell` & hệ tọa độ phẳng
Xe báo vị trí bằng ô lưới; coordinator đổi sang chỉ số phẳng `loc = y*width + x` để gửi server C++. Hai bên phải chung quy ước này (nhắc lại lý do bước validate ở A2).

---

## C. Câu hỏi phản biện có thể gặp

**"Sao phải làm bản TCP C++, hai mode C# chưa đủ à?"**
→ Bản đoạt giải LoRR là C++ hiệu năng cao. TCP cho phép **tái dùng đúng đoạn code server đó** thay vì viết lại, và chứng minh kiến trúc client-server tách rời (đưa được lên cloud). Đây là mode "xịn nhất" vì chạy thuật toán gốc, không phải bản port.

**"Unity và server C++ đồng bộ trạng thái kiểu gì?"**
→ Giao thức JSON-lines qua TCP: `hello` (bắt tay + khớp kích thước map) → `plan_step` mỗi tick (gửi vị trí mọi agent, nhận hành động) → `shutdown`. Server là bên có thẩm quyền quyết định nước đi.

**"Nếu mất kết nối server thì sao?"**
→ Client có timeout mọi khâu để không treo Unity. Coordinator gọi `StopMovement()` cho các xe. Ngoài ra mỗi xe có fallback hích-tham-lam để không đứng chết hoàn toàn. (Kịch bản lùi khi demo: đã có sẵn dữ liệu backtest offline nếu server lỗi.)

**"Cú hích tham lam có làm sai lệch kết quả PIBT không?"**
→ Có nguy cơ, nên nó được **ghi log tách biệt** và chỉ kích hoạt khi kẹt > 4s — trường hợp hiếm. Nó tồn tại để backtest có dữ liệu thay vì mất trắng lượt, và được đánh dấu rõ là NON-PIBT để không trộn vào số liệu thuần.

**"Vì sao xe TCP không tự tính đường luôn cho nhanh?"**
→ Vì mục tiêu của mode này là **so sánh thuật toán PIBT C++ thật**. Nếu xe tự tính, ta không còn đo được server nữa. Xe cố tình "câm" để mọi quyết định đường đi đến từ server — giữ phép đo sạch.

**"Bắn cũng hỏi server à?"**
→ Không. Bắn xử lý cục bộ trong Unity (phản xạ theo tầm nhìn tức thời). Server chỉ lo điều phối **đường đi**. Tách vậy để giảm tải mạng và giữ phản xạ bắn nhạy.

**"Tại sao phải validate kích thước map ở bước hello?"**
→ Vì server và Unity dùng chung chỉ số phẳng `y*width+x`. Lệch width là mọi ô trỏ sai → xe chạy loạn. Đây từng là gốc lỗi "chỉ vài map chạy được", nên validate sớm là bắt buộc.
