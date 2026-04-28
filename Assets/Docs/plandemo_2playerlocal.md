## 🗺️ Tổng quan Gameplay

```
┌─────────────────────────────┐
│  [P1 Tank]      [P2 Tank]   │
│     ████  walls  ████       │
│   AI tanks di chuyển MAPF   │
│         🦅 ĐẠI BÀNG 🦅      │
│      (2 players bảo vệ)     │
└─────────────────────────────┘
Win: Diệt hết AI  |  Lose: Đại Bàng bị phá
```

---

## ⏱️ Kế hoạch theo giờ

### **🌅 Buổi sáng (Giờ 1–4): Core Game**

**Giờ 1 — Scene + Grid + Map**

- Grid 20×20, đặt tường tay hoặc random obstacle
- Prefab: `TankPlayer`, `TankAI`, `Bullet`, `Eagle`
- Camera top-down cố định

**Giờ 2 — Player Controller (2 người, 1 bàn phím)**

|           | Player 1 | Player 2 |
| --------- | -------- | -------- |
| Di chuyển | `WASD`   | `↑↓←→`   |
| Bắn       | `Space`  | `Enter`  |

```csharp
// Tank di chuyển theo cell (snap to grid)
// Quay hướng rồi mới di chuyển
// Cooldown bắn ~0.5s
```

**Giờ 3 — Bullet + HP System**

- Đạn bay thẳng, destroy khi chạm tường
- HP: Player = 3, AI = 2, Eagle = 1 (bị bắn 1 phát = thua)
- UI: Thanh HP góc màn hình, icon Eagle nhấp nháy khi bị tấn công

**Giờ 4 — Eagle + Win/Lose Logic**

```
Eagle bị bắn → GAME OVER (thua)
Tất cả AI bị tiêu diệt → WIN
P1 + P2 đều chết → GAME OVER
```

---

### **☀️ Buổi chiều (Giờ 5–8): AI + MAPF**

**Giờ 5 — A\* Baseline cho AI**

- Mỗi AI tank dùng A\* tìm đường đến Eagle
- Chạy lại path mỗi ~1 giây (dynamic replanning)
- Tới gần Eagle → **bắn**

**Giờ 6 — Cooperative Pathfinding (LNS2 simplified)**

```
Prioritized Planning:
  Agent 1 → A* bình thường
  Agent 2 → A* tránh path của Agent 1 theo timestep
  Agent 3 → tránh Agent 1 + 2
  ...
```

- Thêm **nút Switch** trên UI: `[A*] [LNS2]`
- Khi switch → respawn AI, chạy lại thuật toán mới

**Giờ 7 — AI Behavior Logic**

```
State Machine mỗi AI tank:
  MOVING   → đang di chuyển theo path đến Eagle
  SHOOTING → trong range bắn Eagle hoặc Player
  EVADING  → bị bắn gần → né đường
  BLOCKED  → replan path (LNS2 xử lý tốt hơn A*)
```

**Giờ 8 — Polish + Debug**

- Vẽ path AI bằng màu (A\* = đỏ, LNS2 = xanh)
- Thêm hiệu ứng nổ đơn giản khi tank chết
- Test 2 người chơi thực sự

---

### **🌙 Buổi tối (1–2 tiếng): Demo Ready**

- **Kịch bản demo rõ ràng:**
  1. Bật A\* → 4 AI tank lao vào Eagle **không phối hợp**, chen nhau, dễ block
  2. Bật LNS2 → 4 AI tank **chia hướng tấn công**, khó chặn hơn
- Chụp metrics: số va chạm giữa AI, thời gian AI đến Eagle
- Chuẩn bị 1 slide so sánh

---

## 📦 Cấu trúc code gợi ý

```
Assets/
├── Scripts/
│   ├── Grid/
│   │   ├── GridManager.cs       ← tạo & quản lý grid
│   │   └── Cell.cs              ← walkable, occupied
│   ├── Pathfinding/
│   │   ├── AStarSolver.cs       ← tìm đường độc lập
│   │   └── PrioritizedPlanner.cs← cooperative (LNS2 demo)
│   ├── Tank/
│   │   ├── TankPlayer.cs        ← input P1/P2
│   │   ├── TankAI.cs            ← state machine + follow path
│   │   └── Bullet.cs
│   └── Game/
│       ├── GameManager.cs       ← win/lose, switch AI mode
│       └── UIManager.cs         ← HP bar, metrics, nút switch
```

---

## ⚠️ Ưu tiên nếu hết giờ

| Bắt buộc                   | Có thể bỏ       |
| -------------------------- | --------------- |
| Player di chuyển + bắn     | Hiệu ứng nổ     |
| A\* AI tìm đường đến Eagle | Âm thanh        |
| HP + Win/Lose              | Path visualizer |
| **Switch A\* ↔ LNS2**      | Evading state   |
