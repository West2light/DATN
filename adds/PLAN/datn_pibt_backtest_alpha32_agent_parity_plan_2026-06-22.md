# PLAN: Dua Enemy Agent PIBT single play Alpha32 vao backtest

Ngay: 2026-06-22
Pham vi chinh: `GridEnemyAgentPIBT.cs`, `GridEnemyAgentPIBT_TCP.cs`, `MapScenarioBootstrapPIBT.cs`, `MapScenarioBootstrapPIBT_TCP.cs`, `BacktestRunner.cs`.

## Muc tieu

Mode single play tren map `Alpha32` dang chay tot voi PIBT local. Backtest, dac biet route `PIBT_TCP`, van cho thay enemy dung/xoay/khong tim Base on dinh. Muc tieu la dua lop runtime enemy dang on dinh cua single play vao backtest, thay vi tiep tuc va tung diem tren `GridEnemyAgentPIBT_TCP` dang don gian hon rat nhieu.

## Hien trang sau khi doc code

- Backtest map `Alpha32` tuong ung file `Assets/MapData/random-32-32-10.map`.
- Backtest hien chay 3 algorithm: `AStar`, `PIBT`, `PIBT_TCP`.
- `PIBT` local trong backtest dung scene `MapF_TankTest_PIBT` va `GridEnemyAgentPIBT`.
- `PIBT_TCP` cung dung scene `MapF_TankTest_PIBT`, nhung `MapTankTestBootstrap` se gan them `MapScenarioBootstrapPIBT_TCP` va spawn `GridEnemyAgentPIBT_TCP`.
- `GridEnemyAgentPIBT_TCP` chi nhan action/cell tu server va steer toi target; no thieu nhieu co che cua `GridEnemyAgentPIBT` dang giup single play Alpha32 on dinh.

## Khac biet quan trong can port sang backtest/TCP

1. `GridEnemyAgentPIBT` co replan loop cuc bo:
   - `replanInterval`;
   - `ReplanPath()`;
   - `currentPath`, `pathIndex`;
   - dynamic friendly blocked cells;
   - goal resolution neu Base cell bi block.

2. `GridEnemyAgentPIBT` co lop xu ly ban va blocker:
   - `TryQueueFriendlyShotBlocker()`;
   - neu dong doi chan duong ban thi queue blocked cell va replan;
   - `CanShootTarget()` bo qua friendly trong line-of-sight dung cach.

3. `GridEnemyAgentPIBT` co path following day du:
   - waypoint reach distance rieng cho di thang va re cua;
   - partial drive cycle;
   - steer arc nho khi chua can goc;
   - dung/boc tach waypoint bi friendly chiem.

4. `GridEnemyAgentPIBT` co stuck recovery:
   - progress tracking;
   - spatial stuck detection;
   - scuff detection bang `Rigidbody2D.IsTouchingLayers`;
   - forced replan;
   - reverse recovery;
   - reset state khi chuyen sang shooting.

5. `GridEnemyAgentPIBT_TCP` hien tai thieu:
   - path buffer nhieu waypoint;
   - request/replan local khi bi ket;
   - dynamic blocked cell;
   - destructible clear path;
   - recovery count co y nghia;
   - friendly-shot blocker.

## Nguyen tac trien khai

Khong thay planner C++ bang planner C# trong `PIBT_TCP`, vi backtest van can so sanh PIBT-C++. Chi port phan runtime execution/recovery cua enemy single play sang route TCP/backtest.

Khong sua `TankMover` chung neu chua co bang chung; tat ca thay doi nen nam trong `GridEnemyAgentPIBT_TCP` va `MapScenarioBootstrapPIBT_TCP`.

Khong tiep tuc patch nho theo trieu chung. Can lam theo parity voi agent single play: cung shooting, cung steering, cung stuck recovery, cung metric.

## Pha 0: Dong bang mau chuan Alpha32

Muc tieu: co baseline ro rang de khong tranh luan bang cam tinh.

Viec lam:
- Chay single play `PIBT` tren `Alpha32`.
- Ghi lai so enemy, vi tri Base, spawn cells, va hanh vi trong 30-60 giay.
- Chup screenshot/video neu can.
- Lay log metric toi thieu: `btReplanCount`, `btRecoveryCount`, `btShotCount`, `btCellsVisited`.

PASS:
- Xac nhan `GridEnemyAgentPIBT` tren Alpha32 co the tim/ban Base on dinh.
- Co mot baseline so lieu de doi chieu voi backtest.

## Pha 1: Them trace parity vao backtest

Muc tieu: biet `PIBT_TCP` lech o dau so voi `PIBT` local.

Viec lam:
- Them log mot dong moi 1-2 giay cho `GridEnemyAgentPIBT_TCP`:
  - current cell;
  - target cell;
  - current action;
  - is shooting;
  - distance to target;
  - velocity;
  - stuck duration;
  - recovery state;
  - last server response/action.
- Mo rong `BacktestAgentRecord` neu can de xuat metric TCP:
  - same-cell seconds;
  - shooting seconds;
  - wait action count;
  - reverse recovery count;
  - parse/protocol error count.

PASS:
- Mot run Alpha32 `PIBT` va `PIBT_TCP` co log cung format de so sanh.

## Pha 2: Dua runtime state cua `GridEnemyAgentPIBT` vao `GridEnemyAgentPIBT_TCP`

Muc tieu: `PIBT_TCP` khong con la agent steer-to-one-cell don gian.

Thay doi de xuat:
- Them recovery enum tuong tu `RecoveryLevel.None`, `ForcedReplan`, `Reverse`.
- Them tracking:
  - `lastProgressPosition`;
  - `lastProgressTime`;
  - `spatialSamples`;
  - `recentVisitedCells`;
  - `scuffStartTime`;
  - `lastScuffRecoveryTime`;
  - `reverseRecoveryEndTime`.
- Them public/API nho de bootstrap co the request plan som:
  - `NeedsPlanNow`;
  - `ConsumeNeedsPlanNow()`;
  - `NotifyServerAction(action, nextCell)`.
- Khi stuck:
  - first recovery: yeu cau plan moi ngay lap tuc;
  - second recovery: reverse trong `reverseRecoveryDuration`;
  - sau reverse: yeu cau plan moi.

PASS:
- Agent TCP bi ket khong dung vo han; no co recovery count tang va tiep tuc yeu cau plan.

## Pha 3: Dua shooting/friendly-blocker logic cua single play vao TCP

Muc tieu: tranh tinh trang 1-3 tank ban Base, cac tank khac dung/xoay vi dong doi chan line-of-sight hoac giu target cu.

Thay doi de xuat:
- Port `TryQueueFriendlyShotBlocker()` va `TryGetFriendlyShotBlocker()` sang `GridEnemyAgentPIBT_TCP`.
- Khi friendly chan duong ban:
  - agent khong chi dung lai;
  - danh dau cell friendly dang chiem la blocked runtime;
  - yeu cau bootstrap gui plan moi voi blocked cell metadata neu server ho tro, hoac local runtime ne cell do khi apply target.
- Khi vao shooting:
  - reset progress tracking;
  - clear movement target dang cu;
  - khong lam team-level planner bi treo.

PASS:
- Cac enemy khong dung sau lung enemy dang ban Base qua lau.
- `shooting seconds` khong lam `same-cell seconds` tang bat thuong o cac agent khac.

## Pha 4: Dong bo physics/collision theo route dang tot

Muc tieu: enemy khong de len nhau nhung cung khong bi phan vat ly qua manh.

Viec lam:
- Giu `PlayerBlocker` hoat dong trong `PIBT_TCP`, khong ignore collision cua blocker voi blocker.
- Doi chieu `MapScenarioBootstrapPIBT` va `MapScenarioBootstrapPIBT_TCP`:
  - enemy scale;
  - movement data;
  - layer mask;
  - obstacleContactMask;
  - scuffTimeout;
  - collider source tu prefab.
- Neu single play Alpha32 on dinh voi friendly collision ignored toan bo, thi backtest TCP khong nen bat full collision cho moi collider; chi giu blocker/deconflict toi thieu.

PASS:
- Khong thay tank chong len nhau tai cac cum gan Base/ria map.
- Khong co tank bi physics day bat ra ngoai hanh lang.

## Pha 5: Sua bootstrap TCP de phuc vu runtime parity

Muc tieu: bootstrap khong chi gui plan theo timer, ma phan ung voi runtime agent.

Thay doi de xuat:
- `Update()` cua `MapScenarioBootstrapPIBT_TCP` gui plan khi:
  - den `tcpTickInterval`;
  - bat ky agent `NeedsPlanNow`;
  - sau recovery/reverse;
  - sau parse/protocol recovery thanh cong.
- `BuildStepData()` can doc orientation tu runtime thuc te sau moi recovery, khong chi tu `_agentOrientations` noi bo.
- Khi server tra `W`, `CR`, `CCR`:
  - khong ep agent nhan target current cell nhu movement target;
  - chi cap nhat orientation/action state;
  - agent co quyen request plan moi neu wait qua nguong.

PASS:
- Server tick khong bi doi 0.5s neu agent vua recover can plan moi.
- `W/CR/CCR` khong tao trang thai dung/xoay vo han.

## Pha 6: Backtest Alpha32 truoc, roi moi mo rong

Thu tu test:
1. Chay `Alpha32` voi `PIBT` local, 1 rep, dynamic obstacles off.
2. Chay `Alpha32` voi `PIBT_TCP`, 1 rep, dynamic obstacles off.
3. So sanh agent CSV:
   - `cellsVisited`;
   - `recoveryCount`;
   - `shotCount`;
   - `duration`;
   - `eagleHpAtEnd`;
   - `outcome`.
4. Chi khi Alpha32 dat PASS moi chay full map list.

PASS:
- `PIBT_TCP` tren Alpha32 khong con pattern chi vai enemy hoat dong, cac enemy con lai dung/xoay.
- `eagleHpAtEnd` giam on dinh hoac run ket thuc bang `EagleDestroyed`.
- `same-cell/recovery` co the xuat hien nhung khong lap vo han.

## Thu tu implement de xuat

1. Them trace parity va metric TCP nho, khong doi behavior.
2. Port progress/scuff/reverse recovery tu `GridEnemyAgentPIBT` sang `GridEnemyAgentPIBT_TCP`.
3. Them `NeedsPlanNow` va sua `MapScenarioBootstrapPIBT_TCP` de gui plan som.
4. Port friendly-shot blocker va dynamic occupied-cell handling.
5. Rasoat physics/collider sau khi runtime recovery da co du lieu.
6. Chay Alpha32-only backtest va luu output vao `BacktestResults/` hoac `adds/output/`.

## Rủi ro

- Neu C++ server khong ho tro blocked cell/friendly metadata, TCP runtime chi co the request plan moi va tu tranh local mot phan; de dong bo hoan toan can mo rong protocol.
- Neu server response van malformed, moi runtime fix deu khong co tac dung. Can giu check `plan_result/actions` va log raw response.
- Neu bat collision qua rong, tank co the day nhau khoi corridor. Chi giu collision toi thieu cho blocker.

## Ket luan ky thuat

Huong dung la coi `GridEnemyAgentPIBT` single play Alpha32 la implementation chuan cua enemy runtime. `PIBT_TCP` can dung C++ server cho planner/action, nhung execution/recovery/shooting/physics trong Unity phai dat parity voi `GridEnemyAgentPIBT`; neu khong, backtest se tiep tuc fail o tang runtime chu khong phai thuat toan PIBT.
