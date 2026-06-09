# Danh gia UI/UX Start Flow va Shop Economy

Ngay lap: 2026-05-25

## 1. Ket luan nhanh

Voi project hien tai, **nen cap nhat UI/UX Start Flow theo huong Menu -> Outfit -> Map Select -> Ready -> Gameplay -> Win Summary**, nhung **chua nen day shop/bigtank thanh tinh nang gameplay power lon ngay**.

Shop va bigtank la hop ly neu duoc xem la:

- Mot tinh nang meta-progression nhe, giup nguoi choi co muc tieu sau moi man.
- Mot cach khuyen khich nguoi choi clear 15 combination: 5 map x 3 mode.
- Mot noi de demo persistence, unlock, reward economy cho DATN.

Shop se khong hop ly neu:

- Bigtank cho qua nhieu suc manh lam pha can bang A* vs LNS2 vs LNS2+Halpern.
- Economy lam nguoi choi phai grind lap lai qua nhieu trong khi tong noi dung chi co 15 lan choi chinh.
- UI shop duoc lam truoc khi chua co map/mode/progression data ro rang.

Khuyen nghi: **Phase 1 lam outfit + map select + win screen + reward summary. Phase 2 moi bat shop. Phase 3 moi cho bigtank co chi so rieng.**

## 2. Trang thai project hien tai

### Da co

- Scene menu hien tai la `Assets/Scenes/Menu.unity`, co `StartBtn` va `ContinueBtn`.
- `MenuViewBootstrap` dang dung runtime UI de chinh menu, nut `START` dang load thang `MapF_TankTest`.
- Scene A* chinh: `Assets/Scenes/MapF_TankTest.unity`.
- Scene LNS2 chinh: `Assets/Scenes/MapF_TankTest_LNS2.unity`.
- `MapTankTestBootstrap` spawn player tu `Assets/Prefabs/Tank.prefab`, gan input, camera, HP HUD.
- `MapScenarioBootstrap` co flow kill het enemy thi load scene tiep theo `MapF_TankTest_LNS2`.
- `MapScenarioBootstrapLNS2` co enemy count va death tracking, nhung hien chua co win screen/load-next-map rieng.
- `MapGameOverController` da co overlay `GAME OVER` va quay lai `Menu`.
- Sprite tank co san nhieu mau va dang:
  - Base: `tank_blue`, `tank_green`, `tank_red`, `tank_sand`, `tank_dark`.
  - Big/Large: `tank_bigRed`, `tank_darkLarge`, `tank_huge`.
  - Body sprite: `tankBody_blue`, `tankBody_green`, `tankBody_red`, `tankBody_sand`, `tankBody_dark`, `tankBody_bigRed`, `tankBody_darkLarge`, `tankBody_huge`.
  - Barrel sprite theo mau: `tankBlue_barrel*`, `tankGreen_barrel*`, `tankRed_barrel*`, `tankSand_barrel*`, `tankDark_barrel*`.

### Chua co

- Chua co man outfit selector.
- Chua co man map selector.
- Chua co concept 5 map that. Hien `Assets/MapData` moi co `random-32-32-10.map`.
- Chua co scene/mode `LNS2+Halpern`.
- Chua co shop/economy/gold/credit.
- Chua co win summary screen dung nghia. Hien A* scene kill het enemy thi load scene ke tiep, con LNS2 scene chi recount enemy.
- Chua co data model cho selected outfit, selected map, selected mode, unlocked items, reward history.
- `SaveSystem` hien chi luu player health, scene index va save present. Chua phu hop de luu shop/progression lau dai.

## 3. Danh gia luong UI/UX de xuat

### 0. Menu: SinglePlay va Host

Hop ly, nhung nen tach thanh 2 muc:

- `SinglePlay`: lam ngay. Chay offline, khong can BE.
- `Host`: de trang thai `Coming Soon` hoac `Disabled` cho den khi co BE/codebase multiplayer day du.

Neu Host click ngay bay gio, nen tao UI mock nhu:

- Hien random room id dang `ROOM-4832`.
- Trang thai `Backend not connected`.
- Nut `Copy ID` co the de placeholder.

Khong nen noi that BE luc nay vi project hien tai chua co networking/backend layer.

### 1. Start -> Outfit

Hop ly va nen lam som. Day la buoc co gia tri UX cao, rui ro thap.

De xuat outfit screen:

- Ben trai: preview tank theo sprite dang chon.
- Ben phai: danh sach mau/body type.
- Nut `Back`, `Next`.
- Hien ro trang thai item: `Owned`, `Locked`, `Equipped`.

Voi project hien tai, nen bat dau bang cosmetic outfit truoc:

- Mau: Blue, Green, Red, Sand, Dark.
- Dang than: Normal, Big.
- Barrel: 1, 2, 3.

### 2. Outfit co 2 panel

Rat hop ly voi sprite hien co. Can luu y:

- Preview nen dung sprite renderer/runtime UI image, khong can instantiate prefab physics.
- Khi chon mau, phai doi dong bo base/body va barrel.
- `Tank.prefab` hien co child `TankBase` va `TankTurret`, nen ve sau co the ap selected sprite vao cac `SpriteRenderer` nay khi spawn player.

### 3. Map select: toi da 5 map x 3 mode

Dung voi DATN, vi no bien bai toan A* vs LNS2 vs LNS2+Halpern thanh playable progression.

Nhung hien tai project chua san sang day du vi:

- Moi co 1 file `.map`.
- Moi co 2 mode can ban: A* va LNS2.
- LNS2+Halpern chua co implementation ro rang.

Khuyen nghi UI van thiet ke du 5 map x 3 mode, nhung:

- Map 1 + A*: enabled.
- Map 1 + LNS2: enabled neu scene LNS2 compile/play ok.
- Halpern: locked/coming soon.
- Map 2-5: locked den khi co file map va scenario config.

### 4. Ready

Nen co summary truoc khi load:

- Outfit dang chon.
- Map dang chon.
- Mode dang chon.
- Reward first-clear du kien.
- Dieu kien win: diet het enemy va Eagle con song.

Ready khong nen load scene truc tiep bang ten hard-code trong button. Nen di qua `GameSessionConfig` hoac mot object DontDestroyOnLoad de truyen:

- selectedMapId
- selectedMode
- selectedOutfitId
- isHost
- roomId

### 5. Win screen

Can lam. Hien project co game-over nhung thieu win screen ro rang.

Win screen nen hien:

- `VICTORY`
- Map / Mode vua clear.
- Thoi gian clear.
- Enemy defeated.
- Eagle HP con lai.
- Gold earned.
- First clear bonus neu co.
- Nut `Next Map`, `Replay`, `Menu`.

Day la diem rat quan trong vi shop/economy chi co y nghia khi win co reward feedback.

## 4. Shop va bigtank co hop ly khong?

**Co, nhung nen lam theo huong cosmetic truoc, power sau.**

Ly do hop ly:

- 15 lan choi la du de tao progression nhe.
- Tank game co sprite tank/body/barrel san, nen shop outfit khong doi asset moi qua lon.
- DATN can demo UI/UX, data persistence, progression va replay value.

Rui ro:

- Neu bigtank tang HP/damage qua manh, ket qua so sanh A* vs LNS2 bi nhieu.
- Neu reward phu thuoc power cua tank, metric gameplay/AI se kem cong bang.
- Neu shop qua phuc tap, no an scope cua phan MAPF chinh.

Khuyen nghi:

- Phase dau: bigtank la cosmetic/size change nhe, khong doi chi so hoac doi rat it.
- Khi da co metrics, moi them stat tradeoff:
  - BigTank: +25% HP, -10% speed, hitbox lon hon.
  - Normal: can bang.
  - Light: +10% speed, -15% HP.

Khong nen ban damage upgrade trong ban DATN dau tien, vi damage upgrade lam win rate kho so sanh.

## 5. De xuat currency

Nen dung **1 currency chinh: Gold**.

`Credit` chi nen de sau, khi co BE/account/multiplayer. Neu them ca Gold va Credit ngay luc nay se lam thua he thong.

De xuat:

- Gold: earn trong SinglePlay khi win.
- Credit: de placeholder cho Host/online hoac premium currency, chua can implement.

## 6. Can bang reward cho 15 lan choi

Tong content muc tieu: 5 map x 3 mode = 15 first-clear.

### Cong thuc reward

```text
first_clear_gold = round(map_base_reward * mode_multiplier)
performance_bonus = round(first_clear_gold * performance_rate)
total_gold = first_clear_gold + performance_bonus
repeat_clear_gold = max(20, round(first_clear_gold * 0.25))
```

Mode multiplier:

| Mode | Multiplier | Ly do |
|---|---:|---|
| A* | 1.00 | Baseline, de nhat |
| LNS2 | 1.25 | Kho hon, AI phoi hop tot hon |
| LNS2+Halpern | 1.50 | Endgame/challenge mode |

Map base reward:

| Map | Base Gold |
|---|---:|
| Map 1 | 80 |
| Map 2 | 100 |
| Map 3 | 120 |
| Map 4 | 140 |
| Map 5 | 160 |

Tong first-clear khong tinh performance:

```text
(80 + 100 + 120 + 140 + 160) * (1.00 + 1.25 + 1.50)
= 600 * 3.75
= 2250 gold
```

Performance bonus nen toi da 15%:

| Dieu kien | Bonus |
|---|---:|
| Eagle HP >= 75% | +5% |
| Player HP >= 50% | +5% |
| Clear under target time | +5% |

Tong tien neu clear tot 15 man: khoang `2250 -> 2587 gold`.

### Gia shop de can bang

Muc tieu: player mua duoc cosmetic som, nhung bigtank khong mua qua som.

| Item | Gia de xuat | Thoi diem ky vong |
|---|---:|---|
| Mau tank thuong | 150 gold | Sau 2 man dau |
| Barrel variant | 200 gold | Sau 2-3 man |
| Skin dark/sand premium | 300 gold | Sau 3-4 man |
| BigTank body | 1500 gold | Sau khoang 10-12 first-clear |
| HugeTank body | 2100 gold | Gan pha dao 15 first-clear |

Voi tong 2250-2587 gold, player co the:

- Mua vai cosmetic nho trong qua trinh choi.
- Hoac tiet kiem de mua BigTank o nua sau campaign.
- Sau khi pha dao gan het moi du HugeTank.

### Repeat reward

Repeat reward nen thap de tranh grind:

```text
repeat_clear_gold = 25% first_clear_gold
```

Vi du:

- Map 1 A*: first clear 80, repeat 20.
- Map 5 Halpern: first clear 240, repeat 60.

Muc nay du de replay khong vo nghia, nhung khong bien shop thanh grind game.

## 7. Unlock progression de tranh vo can bang

Nen unlock theo hang ngang:

| Dieu kien | Unlock |
|---|---|
| Clear Map 1 A* | Map 1 LNS2, Map 2 A* |
| Clear Map N A* | Map N LNS2 |
| Clear Map N LNS2 | Map N Halpern |
| Clear Map N any mode | Map N+1 A* |
| Clear 5 A* mode | Shop BigTank visible |
| Clear 3 LNS2 mode | BigTank purchasable |

Cach nay giu player di qua baseline A* truoc, sau do moi thay duoc gia tri LNS2/Halpern.

## 8. Kien truc nen co truoc khi implement

Can them cac module sau, nhung nen lam nho:

### GameSessionConfig

Static/DontDestroyOnLoad object luu lua chon tam thoi:

```text
playMode: SinglePlay | Host
roomId: string
selectedMapId: int
selectedAlgorithmMode: AStar | LNS2 | LNS2Halpern
selectedOutfitId: string
```

### PlayerProgress

Luu vao PlayerPrefs/JSON:

```text
gold
ownedOutfits
equippedOutfit
clearedMapModes
bestClearTime
bestEagleHp
```

Khong nen dung `SaveSystem` hien tai de nhoi them tat ca, vi `SaveSystem` dang thiet ke cho health/scene save.

### MapCatalog

Data catalog cho 5 map:

```text
mapId
displayName
mapFileName
sceneName
difficulty
baseReward
availableModes
```

### OutfitCatalog

Data catalog cho shop/outfit:

```text
outfitId
displayName
bodySpritePath
turretSpritePath
price
isBigTank
statModifier
```

## 9. Milestone de lam hop ly

### M1 - UI flow khong shop

- Menu: SinglePlay, Host disabled/placeholder.
- Start -> Outfit.
- Outfit -> Map Select.
- Ready -> load current A*/LNS2 scene.
- Luu selected outfit vao runtime config.

### M2 - Win screen va reward mock

- Them win screen khi enemy count = 0.
- Tinh reward theo map/mode.
- Hien reward, chua can luu Gold that.

### M3 - Progression save

- Them PlayerProgress rieng.
- Luu gold, unlocked map/mode, owned outfit.
- Load lai menu van thay gold/outfit.

### M4 - Shop

- Shop mua mau/barrel/body.
- BigTank visible nhung lock theo progression.
- Chua tang stat qua manh.

### M5 - 5 map x 3 mode

- Them 4 map file moi.
- Tao mode selector dung data.
- Them LNS2+Halpern khi algorithm san sang.

## 10. Quyet dinh de xuat

Nen lam:

- Cap nhat UI/UX Start Flow ngay, vi no lam game co flow demo ro.
- Them outfit selector dua tren sprite co san.
- Them map/mode selector dang data-driven, nhung lock nhung thu chua co.
- Them win screen truoc shop.
- Them Gold economy don gian.

Chua nen lam ngay:

- Host that su noi BE.
- Credit currency rieng.
- Bigtank tang damage/HP qua lon.
- Shop phuc tap truoc khi co win reward va PlayerProgress.

Ket luan: **Shop va bigtank hop ly cho project nay, nhung chi nen la Phase 4 sau khi da co Start Flow, Win Screen, reward summary va save progression. Economy nen tong first-clear khoang 2250 gold, BigTank gia khoang 1500 gold, HugeTank khoang 2100 gold.**
