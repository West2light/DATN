# PLAN: Điều tra lỗi Enemy PIBT TCP đứng/xoay sai hướng

Ngày: 2026-06-22
Phạm vi: `Assets/Scripts/GridEnemyAgentPIBT_TCP.cs`, `Assets/Scripts/MapScenarioBootstrapPIBT_TCP.cs`, `Assets/Scripts/PIBTTcpClient.cs`, C++ `pibt_tcp_server`.

## Hiện tượng

- Trong backtest/PIBT TCP, chỉ một phần enemy tìm và bắn được Eagle Base.
- Một số tank xoay một lúc rồi đứng yên, hoặc quay ra ngoài hướng mục tiêu.
- Ảnh runtime cho thấy các nhóm enemy ở rìa map có dấu vết di chuyển ngắn rồi kẹt, giống lỗi cũ “3 con bắn, các con còn lại đứng”.

## Nhận định ban đầu

Không kết luận ngay là lỗi planner C++. Lỗi cũ từng được xác nhận là do trạng thái runtime Unity bị kẹt: agent đã chuyển sang bắn nhưng vẫn giữ pending/executing move, làm gate team-level không gửi plan mới. Checkout hiện tại không còn route copy/instrumentation cũ, nên cần tái tạo trace trong route chính.

Ngoài ra, sau lỗi socket vừa gặp, cần kiểm tra contract TCP trước: Unity đang kỳ vọng `{"type":"plan_result","actions":[...]}`. Nếu server trả JSON malformed hoặc key sai như `{action:[...]}`, Unity sẽ dừng toàn đội qua `StopAgentsForConnectionFailure`.

## Giả thuyết ưu tiên

1. **Protocol/response malformed từ C++ server**
   - Dấu hiệu: `Unexpected plan_step response`, response không có `"type":"plan_result"` hoặc `"actions"`.
   - Hệ quả: `_serverReady=false`, tất cả agent bị `StopMovement()`, nhìn giống đứng yên.

2. **Shooting state làm mất nhịp movement**
   - `GridEnemyAgentPIBT_TCP.Update()` đang `return` ngay khi có `shootTarget`, nhưng vẫn giữ target cũ.
   - Khi hết line-of-sight hoặc nhận `W`, agent có thể tiếp tục xử lý target cũ/cell hiện tại, dẫn tới đứng hoặc xoay không có tiến triển.

3. **Orientation Unity và orientation server lệch quy ước**
   - `ReadAgentOrientation()` hiện map `up => 3`, trong khi `OrientationToDelta()` map `1 => up`, `3 => down`.
   - Nếu quy ước C++ là `0=right,1=up,2=left,3=down`, trạng thái ban đầu của tank đang bị đảo trục Y, dễ gây quay ra ngoài.

4. **Action `W`/`CR`/`CCR` vẫn gọi `SetNextTarget(cell hiện tại)`**
   - `SetNextTarget()` luôn bật `_hasTarget=true`.
   - Với action không tiến lên, local steering vẫn có thể cố đi tới chính cell đang đứng hoặc giữ target không hữu ích.

5. **Thiếu stuck/recovery trong TCP driver**
   - `GridEnemyAgentPIBT_TCP` đơn giản hơn `GridEnemyAgentPIBT`: không có progress tracking, scuff recovery, reverse recovery, destructible handling.
   - Khi bị vật cản hoặc lệch cell, agent không tự recover mà chỉ chờ plan mới.

## Pha 1: Thu thập trace tối thiểu

Mục tiêu: biết chính xác lỗi nằm ở server response, mapping action, hay movement runtime.

Việc cần làm:
- Thêm log mỗi `plan_step`: `requestId`, `agentId`, `loc`, `orientation`, `goalLoc`, `action`, `nextCell`, `currentCell`, `hasTarget`, `shootTarget`.
- Log một dòng summary dạng: `moving`, `shootEagle`, `shootPlayer`, `waiting`, `sameCell`, `unknownAction`, `parseError`.
- Ghi response raw của C++ server vào file dưới `adds/output/` khi parse lỗi, tránh chỉ nhìn Console bị cắt.
- Chụp lại 1 backtest seed có lỗi, lưu screenshot và log path.

Tiêu chí PASS:
- Có đủ dữ liệu để chỉ ra agent đứng vì parse lỗi, vì server trả `W/CR/CCR`, vì orientation sai, hay vì local steering không tiến.

## Pha 2: Kiểm tra contract C++ TCP

Mục tiêu: loại bỏ lỗi server trả JSON sai.

Việc cần làm:
- Gửi một `hello` + `plan_step` mẫu bằng script hoặc Unity log, so sánh raw response.
- Yêu cầu response đúng dạng:
  ```json
  {"type":"plan_result","sessionId":"...","requestId":0,"actions":[{"id":0,"action":"FW","nextLoc":123}]}
  ```
- Nếu C++ đang trả key `action` thay vì `actions`, thiếu quote, hoặc log/debug text trộn vào JSON, sửa ở C++ trước.
- Đảm bảo mỗi response là đúng một JSON object trên đúng một dòng `\n`.

Tiêu chí PASS:
- Không còn `Unexpected plan_step response`.
- Không còn response có `shutdown_ack` dính sau `plan_result`.

## Pha 3: Xác minh orientation và flat index

Mục tiêu: chặn lỗi tank quay ra ngoài do mapping sai.

Việc cần làm:
- So sánh quy ước orientation của C++ với Unity:
  - Unity hiện gửi orientation từ `ReadAgentOrientation()`.
  - Unity tự apply action bằng `OrientationToDelta()`.
- Kiểm tra khả nghi hiện tại: `ReadAgentOrientation()` trả `up => 3`, nhưng `OrientationToDelta(3)` lại là `down`.
- Tạo test 4 hướng: tank nhìn right/up/left/down, gửi loc và action `FW`, kiểm tra next cell.

Tiêu chí PASS:
- `ReadAgentOrientation()` và `OrientationToDelta()` dùng cùng quy ước với C++.
- Tank không còn quay ngược hoặc đi ra ngoài do orientation ban đầu sai.

## Pha 4: Vá runtime movement TCP

Mục tiêu: tránh đứng/xoay vô hạn khi action không tạo cell mới hoặc khi chuyển trạng thái bắn.

Việc cần làm:
- Không gọi `SetNextTarget()` cho action `W` nếu next cell là current cell; thay bằng lệnh stop/clear target rõ ràng.
- Khi có `shootTarget`, clear target movement hiện tại và reset accumulator steering.
- Khi rời trạng thái shooting, yêu cầu plan mới sớm thay vì tiếp tục target cũ.
- Thêm progress/stuck detection tối thiểu từ `GridEnemyAgentPIBT`: nếu agent không đổi cell trong N giây khi đang có target thì clear target và request/retry plan.

Tiêu chí PASS:
- Agent đứng chỉ khi server trả `W` có lý do hoặc đang bắn.
- Agent không giữ trạng thái xoay tại chỗ quá ngưỡng mà không có plan mới.

## Pha 5: Backtest xác nhận

Mục tiêu: chứng minh lỗi không tái diễn bằng dữ liệu, không chỉ quan sát ảnh.

Việc cần làm:
- Chạy lại backtest cùng map/seed gây lỗi.
- Lưu log raw + summary vào `adds/output/`.
- So sánh:
  - số enemy có `shootEagle=true`;
  - số enemy bị `sameCell` liên tục;
  - số parse/protocol error;
  - thời gian tới khi Eagle bị phát hiện/bắn.

Tiêu chí PASS:
- Không còn đội hình chỉ 3 con hoạt động trong khi các con khác đứng/xoay vô hạn.
- Không còn parse error trong TCP.
- Ít nhất mỗi enemy có trace rõ: đang bắn, đang chờ action hợp lệ, hoặc đang di chuyển tới cell mới.

## Thứ tự thực hiện đề xuất

1. Implement trace nhẹ ở Unity, chưa đổi behavior.
2. Chạy 1 backtest ngắn để phân loại lỗi.
3. Nếu response malformed: sửa C++ server trước.
4. Nếu response đúng nhưng hướng sai: sửa orientation mapping.
5. Nếu response và hướng đúng nhưng vẫn kẹt: sửa shooting/target/stuck state trong `GridEnemyAgentPIBT_TCP`.
6. Chạy backtest xác nhận và lưu artifact.
