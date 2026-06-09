# Bản V2 - Flow UI, Shopping và Kế hoạch Update Tank

Ngày lập: 2026-05-25

## 1. Mục tiêu bản V2

Bản V2 này chuyển đề xuất trước đó thành một hướng triển khai rõ hơn cho các phần:

- Flow UI từ màn Start đến chọn outfit, chọn map, chọn mode và Ready.
- Shopping/economy để người chơi có mục tiêu sau mỗi lần thắng.
- Update tank để sau này có thể đổi màu, đổi thân xe, mua BigTank và mở rộng chỉ số mà không phá cân bằng MAPF.

Phạm vi bản V2 là **thiết kế để triển khai sau**, chưa phải code implementation.

## 2. Đánh giá trạng thái hiện tại

Project hiện tại đã có nền tảng tốt để làm flow UI mới:

- `MenuViewBootstrap` đang dựng lại UI menu lúc runtime.
- `START` hiện load trực tiếp vào `MapF_TankTest`.
- `MapF_TankTest` là scene A* baseline.
- `MapF_TankTest_LNS2` là scene LNS2.
- `Tank.prefab` có các child quan trọng như `TankBase` và `TankTurret`.
- Sprite tank có sẵn nhiều màu và biến thể thân xe như `tank_blue`, `tank_green`, `tank_red`, `tank_sand`, `tank_dark`, `tank_bigRed`, `tank_huge`.
- `MapGameOverController` đã có màn thua, nhưng chưa có màn thắng.

Các phần còn thiếu:

- Chưa có màn Outfit.
- Chưa có màn Map Select.
- Chưa có màn Shop.
- Chưa có Win Summary.
- Chưa có Gold/Credit/progression.
- Chưa có data lưu outfit đã mua, outfit đang dùng, map đã clear.
- Chưa có đủ 5 map và chưa có mode `LNS2 + Halpern`.

Kết luận: nên làm flow UI và reward trước, shop sau. Không nên bắt đầu bằng BigTank có stat mạnh ngay.

## 3. Flow UI đề xuất

Flow tổng:

```text
Menu
  -> SinglePlay
      -> Outfit Select
          -> Map Select
              -> Mode Select
                  -> Ready Summary
                      -> Gameplay
                          -> Win Summary / Game Over
  -> Host
      -> Room Preview / Coming Soon
  -> Shop
  -> Settings
  -> Exit
```

## 4. Màn Menu

Menu nên có các lựa chọn chính:

| Nút | Trạng thái đề xuất | Ghi chú |
|---|---|---|
| SinglePlay | Làm ngay | Vào flow chọn outfit |
| Host | Placeholder | Tạo room id ngẫu nhiên, chưa cần nối BE |
| Shop | Làm sau khi có Gold | Có thể hiện disabled ở Phase 1 |
| Settings | Optional | Âm lượng, fullscreen, reset progress |
| Exit | Làm ngay | Thoát game |

### Host

Host hiện chưa nên làm thật vì project chưa có backend/multiplayer layer. Tuy nhiên có thể dựng UI mock:

```text
HOST ROOM
Room ID: ROOM-4827
Status: Backend chưa kết nối
[Copy ID] [Back]
```

Mục tiêu là giữ đúng hướng thiết kế mà không tạo nợ kỹ thuật mạng quá sớm.

## 5. Màn Outfit Select

Màn Outfit gồm 2 panel:

| Panel | Nội dung |
|---|---|
| Trái | Preview tank đang chọn |
| Phải | Danh sách màu, thân xe, turret/barrel, trạng thái sở hữu |

Flow:

```text
Chọn màu
  -> Preview đổi sprite TankBase/TankBody
Chọn barrel
  -> Preview đổi sprite TankTurret
Chọn body type
  -> Preview đổi Normal / Big / Huge
Click Equip
  -> Lưu selectedOutfitId vào session
Click Next
  -> Sang Map Select
```

### Outfit ban đầu nên có

| Outfit | Loại | Trạng thái |
|---|---|---|
| Blue Normal | Default | Owned |
| Green Normal | Cosmetic | Shop |
| Red Normal | Cosmetic | Shop |
| Sand Normal | Cosmetic | Shop |
| Dark Normal | Cosmetic premium | Shop |
| Big Red | BigTank | Locked |
| Huge Tank | Endgame | Locked |

Giai đoạn đầu nên để outfit chỉ đổi hình, chưa đổi chỉ số. Sau khi Win Summary và metrics ổn mới thêm stat.

## 6. Màn Map Select

Mục tiêu cuối là 5 map, mỗi map có 3 mode:

- A*
- LNS2
- LNS2 + Halpern

Tổng cộng:

```text
5 map x 3 mode = 15 lượt clear chính
```

### Trạng thái hiện tại nên hiển thị

| Nội dung | Trạng thái |
|---|---|
| Map 1 - A* | Enabled |
| Map 1 - LNS2 | Enabled sau khi scene LNS2 ổn định |
| Map 1 - LNS2 + Halpern | Locked / Coming Soon |
| Map 2-5 | Locked |

Không nên fake rằng đã có đủ 5 map. UI có thể hiện đủ slot, nhưng phải ghi rõ locked.

### UI mỗi map card

Mỗi map card nên có:

- Tên map.
- Difficulty.
- Số enemy.
- Reward first clear.
- Trạng thái clear theo từng mode.
- Nút chọn mode.

Ví dụ:

```text
MAP 01 - Training Grid
Difficulty: 1
Modes:
[A* Cleared/Ready] [LNS2 Ready] [Halpern Locked]
Reward: 80 - 120 Gold
```

## 7. Ready Summary

Trước khi vào game nên có màn tóm tắt:

```text
READY
Outfit: Blue Normal
Map: Map 01 - Training Grid
Mode: A*
Reward: 80 Gold + performance bonus
Win condition: Tiêu diệt toàn bộ enemy, Eagle còn sống
[Back] [Ready]
```

Màn này giúp player hiểu mình đang chơi map/mode nào, đồng thời chuẩn bị cho reward economy.

## 8. Gameplay Flow

Khi bấm Ready:

```text
Ready
  -> Ghi lựa chọn vào GameSessionConfig
  -> Load scene tương ứng
  -> MapTankTestBootstrap spawn player
  -> PlayerTankOutfitApplier áp outfit lên Tank.prefab instance
  -> MapScenarioBootstrap / MapScenarioBootstrapLNS2 spawn enemy
  -> Gameplay
```

Không nên để button UI load scene hard-code trực tiếp nữa. Nên có một session object trung gian.

## 9. Win Summary

Win Summary cần được làm trước Shop.

Điều kiện win:

- Tất cả enemy đã chết.
- Eagle còn sống.
- Player chưa chết.

Thông tin hiển thị:

| Thông tin | Mục đích |
|---|---|
| Map vừa clear | Xác nhận progression |
| Mode vừa chơi | Phục vụ so sánh A* / LNS2 / Halpern |
| Thời gian clear | Performance |
| Eagle HP còn lại | Performance |
| Player HP còn lại | Performance |
| Enemy defeated | Feedback |
| Gold earned | Economy |
| First clear bonus | Khuyến khích clear đủ 15 lượt |

Các nút:

- Next Map
- Replay
- Shop
- Menu

## 10. Shopping Flow

Shop nên xuất hiện ở Menu và Win Summary.

Flow:

```text
Menu / Win Summary
  -> Shop
      -> Tank Colors
      -> Barrels
      -> Body Types
      -> BigTank
      -> Confirm Purchase
      -> Equip
```

### Shop tab đề xuất

| Tab | Nội dung | Phase |
|---|---|---|
| Colors | Màu thân tank | Phase 1 shop |
| Barrels | Nòng súng | Phase 1 shop |
| Body | Normal / Big / Huge | Phase 2 shop |
| Upgrades | HP/speed tradeoff | Phase 3, sau metrics |

Không nên có damage upgrade ở bản đầu vì sẽ làm sai lệch so sánh AI mode.

## 11. Currency

Nên dùng 1 currency chính:

```text
Gold
```

Credit chưa nên dùng ngay. Credit chỉ hợp lý khi có backend, account hoặc multiplayer economy.

Đề xuất:

| Currency | Dùng cho | Trạng thái |
|---|---|---|
| Gold | Mua cosmetic, BigTank | Làm được ngay |
| Credit | Online/Host/premium placeholder | Để sau |

## 12. Công thức reward

Reward khi thắng:

```text
firstClearGold = round(mapBaseReward * modeMultiplier)
performanceBonus = round(firstClearGold * performanceRate)
totalGold = firstClearGold + performanceBonus
repeatGold = max(20, round(firstClearGold * 0.25))
```

Mode multiplier:

| Mode | Multiplier |
|---|---:|
| A* | 1.00 |
| LNS2 | 1.25 |
| LNS2 + Halpern | 1.50 |

Map base reward:

| Map | Base Gold |
|---|---:|
| Map 1 | 80 |
| Map 2 | 100 |
| Map 3 | 120 |
| Map 4 | 140 |
| Map 5 | 160 |

Tổng first clear:

```text
(80 + 100 + 120 + 140 + 160) * (1.00 + 1.25 + 1.50)
= 600 * 3.75
= 2250 Gold
```

Performance bonus tối đa 15%:

| Điều kiện | Bonus |
|---|---:|
| Eagle HP >= 75% | +5% |
| Player HP >= 50% | +5% |
| Clear dưới target time | +5% |

Tổng Gold kỳ vọng nếu clear tốt 15 lượt:

```text
2250 - 2587 Gold
```

## 13. Giá shop đề xuất

| Item | Giá | Ghi chú |
|---|---:|---|
| Màu Green / Red | 150 Gold | Mua sớm |
| Màu Sand | 220 Gold | Cosmetic trung bình |
| Màu Dark | 300 Gold | Premium cosmetic |
| Barrel variant | 200 Gold | Đổi ngoại hình turret |
| BigTank body | 1500 Gold | Unlock nửa sau campaign |
| HugeTank body | 2100 Gold | Endgame reward |

Với tổng 2250-2587 Gold:

- Player có thể mua vài cosmetic nhỏ khi chơi.
- Nếu tiết kiệm, player mua được BigTank sau khoảng 10-12 first clear.
- HugeTank nên là phần thưởng gần cuối game.

## 14. Unlock progression

Nên mở khóa theo từng lớp để người chơi hiểu sự khác nhau giữa mode:

| Điều kiện | Unlock |
|---|---|
| Clear Map 1 A* | Map 1 LNS2, Map 2 A* |
| Clear Map N A* | Map N LNS2 |
| Clear Map N LNS2 | Map N Halpern |
| Clear Map N bất kỳ mode | Map N+1 A* |
| Clear 5 lượt A* | Shop BigTank visible |
| Clear 3 lượt LNS2 | BigTank purchasable |
| Clear 15 lượt | HugeTank purchasable |

Cách này ép người chơi đi qua baseline A* trước, sau đó mới thấy LNS2 và Halpern khác gì.

## 15. Update Tank sau này

### Mục tiêu

Update tank không chỉ là đổi sprite. Cần chuẩn bị để sau này có thể:

- Đổi body sprite.
- Đổi turret/barrel sprite.
- Đổi scale.
- Đổi stat có kiểm soát.
- Lưu outfit đang dùng.
- Áp outfit vào player khi spawn.

### Cấu trúc đề xuất

```text
TankOutfitDefinition
  id
  displayName
  bodySpritePath
  turretSpritePath
  bodyType
  price
  unlockCondition
  statProfile

TankStatProfile
  hpMultiplier
  speedMultiplier
  rotationMultiplier
  damageMultiplier
  colliderScaleMultiplier
```

Giai đoạn đầu:

```text
hpMultiplier = 1.0
speedMultiplier = 1.0
rotationMultiplier = 1.0
damageMultiplier = 1.0
colliderScaleMultiplier = 1.0
```

Tức là đổi ngoại hình trước, chưa đổi gameplay.

### Khi cho BigTank có stat

Nếu BigTank có chỉ số riêng, nên dùng tradeoff:

| Tank type | HP | Speed | Rotation | Hitbox | Ghi chú |
|---|---:|---:|---:|---:|---|
| Normal | 100% | 100% | 100% | 100% | Cân bằng |
| BigTank | 125% | 90% | 90% | 115% | Trâu hơn nhưng chậm hơn |
| HugeTank | 150% | 80% | 80% | 130% | Endgame, khó né hơn |

Không nên tăng damage trong bản đầu.

Lý do:

- Damage cao làm win nhanh hơn, ảnh hưởng kết quả so sánh mode AI.
- HP/speed tradeoff dễ giải thích hơn.
- Hitbox lớn giúp BigTank không phải nâng cấp thuần lợi thế.

## 16. Data cần thêm để triển khai

### GameSessionConfig

Lưu lựa chọn tạm thời giữa UI và gameplay:

```text
playMode
roomId
selectedMapId
selectedMode
selectedOutfitId
```

### PlayerProgress

Lưu progression dài hạn:

```text
gold
ownedOutfitIds
equippedOutfitId
clearedMapModeIds
bestClearTimes
bestEagleHpRates
```

Không nên nhét vào `SaveSystem` hiện tại vì `SaveSystem` đang phục vụ save health/scene.

### MapCatalog

```text
mapId
displayName
mapFileName
sceneName
baseReward
difficulty
enabledModes
```

### ShopCatalog

```text
itemId
itemType
displayName
price
outfitId
unlockCondition
```

## 17. Roadmap triển khai sau

### Phase 1 - UI Flow nền

- Menu có `SinglePlay`, `Host`, `Shop`, `Exit`.
- `Host` là placeholder.
- `SinglePlay` mở Outfit Select.
- Outfit Select có preview tank.
- Map Select hiện 5 map slot, nhưng khóa map chưa có.
- Ready Summary load scene hiện có.

### Phase 2 - Win Summary và Reward

- Thêm Win Summary.
- Tính Gold theo map/mode.
- Hiện first clear bonus.
- Chưa cần Shop mua bán thật.

### Phase 3 - Progression Save

- Thêm PlayerProgress.
- Lưu Gold.
- Lưu cleared map/mode.
- Lưu equipped outfit.

### Phase 4 - Shop

- Mua màu tank.
- Mua barrel.
- Equip outfit đã mua.
- BigTank locked theo progression.

### Phase 5 - BigTank và Tank Update

- Áp body type BigTank/HugeTank.
- Thêm stat tradeoff nhẹ.
- Kiểm tra lại cân bằng gameplay.
- Không dùng damage upgrade nếu chưa có metrics.

### Phase 6 - Đủ 5 map x 3 mode

- Thêm 4 map mới.
- Hoàn thiện `LNS2 + Halpern`.
- Mở đủ 15 lượt clear.
- Dùng reward/economy để dẫn người chơi phá đảo.

## 18. Quyết định đề xuất cho bản update sau

Nên làm trước:

- Flow UI đầy đủ từ Menu đến Ready.
- Outfit preview.
- Map/mode selector dạng khóa/mở.
- Win Summary.
- Gold reward.

Làm sau:

- Shop mua bán thật.
- BigTank stat.
- Credit.
- Host nối backend.

Không nên làm:

- Bán damage upgrade sớm.
- Cho BigTank quá mạnh.
- Làm đủ shop trước khi có Win Summary.
- Hiển thị 5 map như đã hoàn chỉnh khi project mới có 1 map thật.

Kết luận: **Bản V2 nên xem shop và BigTank là hệ thống progression sau gameplay core. Flow UI và Win Summary phải đi trước. Tank update nên bắt đầu bằng cosmetic, sau đó mới thêm stat tradeoff nhẹ để không phá cân bằng A* / LNS2 / LNS2 + Halpern.**
