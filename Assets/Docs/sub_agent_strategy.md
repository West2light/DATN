# De xuat sub-agent cho project MAPF Tank

Tai lieu nay mo ta cach chia sub-agent khi tiep tuc fix, code va scale project Unity 2D tank game co MAPF. Muc tieu khong phai tao that nhieu agent, ma la tach ro trach nhiem de tranh agent dung sai MCP tool, sua chong cheo file, hoac review qua chung chung.

## Nguyen tac chung

- Moi sub-agent can co owner ro: code, scene/prefab, UI, validation, docs, hoac review.
- Moi task nen co dau ra cu the: patch file, tool plan, screenshot, checklist loi, hay ket qua test.
- Khong nen de hai sub-agent cung sua mot file/scene/prefab trong cung luc neu khong co owner chinh.
- MCP tool nen duoc chon theo resource-first workflow: doc `editor_state`, `project_info`, scene/object context truoc, roi moi dung tool mutate.
- Sau khi sua C# script: validate/compile, doc console error/warning, roi moi gan component hoac chay Play Mode smoke test.
- Scene `Assets/Scenes/MapF_TankTest.unity` la source-of-truth cho MAPF/AI. Legacy AI va MAPF AI phai duoc giu tach bach.

## Bo sub-agent nen co

### 1. Project Lead / Integrator

Day la agent dieu phoi chinh, thuong la agent dang noi chuyen voi user. Agent nay khong nhat thiet lam het moi viec, nhung chiu trach nhiem ghep ket qua lai thanh mot thay doi co the chay duoc.

Trach nhiem:
- Hieu yeu cau user va chia task thanh cac scope nho.
- Quyet dinh sub-agent nao can goi, sub-agent nao khong can.
- Giu map ownership file/scene de tranh conflict.
- Tich hop patch, doc ket qua test, va dua ra ket luan cuoi.

Dau ra mong doi:
- Plan ngan gon cho task.
- Danh sach file/scene/prefab bi tac dong.
- Ket qua verification cuoi cung.

Nen dung khi:
- Moi task deu nen co vai tro nay.

Khong nen lam:
- Giao het quyen quyet dinh kien truc cho tung sub-agent rieng le.
- De sub-agent tu y doi scope lon hon yeu cau ban dau.

### 2. MCP Tool Router / Unity Operator

Y kien cua ban ve mot sub-agent chuyen chon MCP tool la hop ly, nhung nen gioi han vai tro nay thanh "tool planner/operator", khong phai agent quyet dinh gameplay. Agent nay chon cach dung MCP an toan, nhanh va dung ngu canh Unity.

Trach nhiem:
- Doc resource truoc khi thao tac: `mcpforunity://editor/state`, `project/info`, `scene/gameobject-api`, `custom-tools`, `tool-groups`.
- Chon tool phu hop: `find_gameobjects`, `manage_scene`, `manage_gameobject`, `manage_components`, `manage_asset`, `manage_prefabs`, `manage_camera`, `read_console`, `run_tests`.
- De xuat batch command khi co nhieu thao tac lap lai.
- Chi ro precondition: editor ready, khong compiling, scene dung, instance dung.
- Sau thao tac, yeu cau verification: console, screenshot, hierarchy, play mode smoke.

Dau ra mong doi:
- Tool plan cu the: tool nao, target nao, thu tu nao, vi sao.
- Neu duoc giao execute: log ket qua MCP va canh bao neu Unity dang busy/stale.

Nen dung khi:
- Can thao tac Unity Editor, prefab, scene, component, screenshot, Play Mode, hoac console.
- Co nhieu tool MCP co the dung va can chon cach it rui ro nhat.

Khong nen lam:
- Tu sua C# logic gameplay neu khong duoc giao.
- "Nem" tool plan cho agent khac ma khong kem precondition/verification.

### 3. Gameplay & MAPF Engineer

Day la sub-agent code gameplay va pathfinding chinh. Voi project hien tai, day la agent quan trong nhat khi tien tu A* baseline sang prioritized planning, reservation table, metrics, va LNS2.

Trach nhiem:
- Sua va mo rong logic trong `GridAStarPathfinder`, `GridEnemyAgent`, `MapLoader`, `MapScenarioBootstrap`, `MapTankTestBootstrap`, va `Assets/Scripts/Pathfinding/`.
- Dam bao grid cell, world position, clearance, blocked cells, nav mask, va line-of-sight dung voi convention top-left origin.
- Giu legacy AI va MAPF AI khong dieu khien cung mot tank.
- Thiet ke code de sau nay do duoc metrics: path length, replan count, stuck count, collision/conflict count, CPU time.

Dau ra mong doi:
- Patch C# gon, de doc, co guard cho null/reference/layer mask.
- Ghi ro behavioral change.
- Checklist validation: `validate_script`, compile, console, smoke test scene `MapF_TankTest`.

Nen dung khi:
- Fix enemy stuck, replan, smoothing, reservation, collision avoidance, target selection, shooting logic.
- Implement milestone MAPF moi.

Khong nen lam:
- Sua prefab/scene serialization lon neu khong co Prefab/Scene agent phoi hop.
- Doi sang Unity NavMesh lam navigation chinh, vi project dang lay `.map` benchmark lam source-of-truth.

### 4. Prefab & Scene Assembly Agent

Nen tach agent nay khoi UI/UX. Prefab/scene wiring lien quan den Unity serialization, component reference, layer, collider, ScriptableObject; UI/UX lai lien quan den flow va hien thi. Tach ra se giam conflict.

Trach nhiem:
- Tao/cap nhat prefab, scene object, component, collider, layer, tag, sorting layer.
- Gan serialized reference cho `TankController`, `TankMover`, `Turret`, `Damagable`, data ScriptableObject.
- Kiem tra `MapF_TankTest` boot flow va object hierarchy.
- Dung MCP tool de inspect va sua scene/prefab khi can.

Dau ra mong doi:
- Danh sach object/prefab/scene da thay doi.
- Component diff o muc concept: them component nao, gan field nao, layer nao.
- Screenshot hoac hierarchy check neu co thay doi visual/scene.

Nen dung khi:
- Can tao enemy variant, eagle base visual, obstacle collider, debug object, scene bootstrap object.
- Can sua loi reference trong Inspector.

Khong nen lam:
- Sua thuat toan pathfinding.
- Tao UI flow phuc tap neu chua co UI/UX spec.

### 5. UI/UX & Debug Visualization Agent

Agent nay lo phan user-facing va developer-facing UI: HUD, menu, debug overlay, path visualization, metrics panel. Voi thesis MAPF, debug visualization rat dang dau tu vi no giup chung minh khac biet giua A*, prioritized planning, LNS2.

Trach nhiem:
- Thiet ke HUD/menu/debug overlay de doc duoc trong top-down tank game.
- Tao path lines, waypoint markers, grid overlay, blocked/reserved cell overlay, agent state label.
- Tao metrics panel: FPS, enemy count, average path length, replan count, stuck count, conflict count.
- Dam bao UI khong che gameplay, doc duoc o nhieu resolution.

Dau ra mong doi:
- UI prefab/scene wiring hoac script UI ro rang.
- Screenshot desktop/game view sau khi thay doi.
- Mo ta state nao duoc hien thi va cach tat/bat debug.

Nen dung khi:
- Can thay doi Canvas, UI Toolkit, TMP text, debug visualization, menu chon algorithm.
- Can lam demo de nguoi xem thay "de hon ro ret".

Khong nen lam:
- Tu quyet dinh metrics algorithm neu chua co Gameplay/Metrics agent thong nhat.

### 6. QA / Validation Agent

Agent nay khong chi "chay test", ma phai nghien cuu case nao de lam hong thay doi vua code. Day la agent bat buoc khi code AI/pathfinding vi loi thuong chi lo ra trong Play Mode.

Trach nhiem:
- Tao checklist regression cho `MapF_TankTest`.
- Chay compile/console/Play Mode smoke test.
- Neu co Unity Test Framework test thi chay EditMode/PlayMode test.
- Kiem tra case barrier: enemy sau tuong, nhieu enemy cung target, replan voi blocked cells, eagle/player line-of-sight.
- Ghi lai warning/error runtime va phan biet loi project voi warning benign cua Unity/package.

Dau ra mong doi:
- Ket qua test co timestamp/context.
- Bug report co reproduction step.
- Danh sach rui ro con lai neu chua visual-test duoc.

Nen dung khi:
- Sau moi phase code MAPF.
- Truoc khi ket luan bug da fix.
- Truoc khi demo milestone.

Khong nen lam:
- Sua code lon trong khi dang validate, tru khi duoc giao fix nho va bao lai.

### 7. Code Quality / Architecture & Scale Reviewer

Y kien cua ban ve mot sub-agent review quality va scale la can thiet. Tuy nhien agent nay nen review sau khi da co patch hoac design cu the, khong review chung chung.

Trach nhiem:
- Review bug/risk truoc style.
- Kiem tra coupling giua MAPF AI, legacy AI, `TankController`, scene bootstrap.
- Kiem tra scale: nhieu enemy, replan interval, allocation trong Update, data structure cho reservation/LNS2.
- Kiem tra kha nang do metrics va giai thich trong thesis.
- De xuat refactor chi khi no giam rui ro that.

Dau ra mong doi:
- Findings theo muc do nghiem trong, co file/line neu co patch.
- De xuat test bo sung.
- Khuyen nghi scale: can cache gi, can pool gi, can tach class nao.

Nen dung khi:
- Sau phase code lon.
- Truoc khi merge thay doi algorithm.
- Khi code bat dau co nhieu feature MAPF chen vao mot class.

Khong nen lam:
- Bien review thanh rewrite.
- Bat refactor lon neu milestone dang can verification nhanh.

### 8. Metrics & Experiment Agent

Day la agent nen co rieng cho project DATN, vi muc tieu khong chi la game chay duoc ma con phai so sanh AI navigation tiers.

Trach nhiem:
- Dinh nghia metrics: path length, time-to-target, replan count, stuck duration, collision/conflict count, CPU ms, win/loss condition.
- Tao data logging cho tung run.
- Dam bao A*, prioritized planning, LNS2 co cung scenario input de so sanh cong bang.
- De xuat format export: CSV/JSON, seed, map name, enemy count, algorithm name.

Dau ra mong doi:
- Metrics schema.
- Logger/instrumentation plan hoac patch.
- Checklist de chay experiment lap lai.

Nen dung khi:
- Chuyen tu "fix gameplay" sang "do va so sanh".
- Viet chuong thuc nghiem cua DATN.

Khong nen lam:
- Lam UI truoc khi metrics definition on dinh.

### 9. Documentation / Thesis Traceability Agent

Agent nay giu docs khong lech khoi code. Voi project DATN, day la viec quan trong vi moi phase code nen map duoc vao milestone va noi dung bao cao.

Trach nhiem:
- Cap nhat `Assets/Docs/*.md` sau moi milestone.
- Ghi lai quyet dinh ky thuat: vi sao giu 4-neighbor, vi sao dung nav mask inflate, vi sao smoothing khong cat qua obstacle.
- Tao checklist demo va limitation.
- Lien ket code change voi muc tieu DATN.

Dau ra mong doi:
- Markdown ngan, dung voi code hien tai.
- Khong ghi qua chac nhung dieu chua test visual/experiment.

Nen dung khi:
- Sau moi phase hoan thanh.
- Truoc buoi demo/thuyet trinh.

Khong nen lam:
- Viet docs nhu marketing; phai trung thuc voi rui ro va test gap.

## Flow de xuat khi lam mot task lon

1. Project Lead nhan yeu cau va chia scope.
2. MCP Tool Router kiem tra Unity state, scene, object, tool can dung neu task can Editor.
3. Gameplay/MAPF hoac Prefab/UI agent implement theo ownership.
4. QA agent validate bang compile, console, Play Mode, screenshot neu can.
5. Code Quality reviewer doc patch neu thay doi co blast radius lon.
6. Documentation agent cap nhat note neu phase/milestone thay doi.
7. Project Lead tich hop, tom tat ket qua va rui ro con lai.

## Matrix kich hoat nhanh

| Task | Sub-agent nen dung |
| --- | --- |
| Fix enemy stuck/replan/path smoothing | Gameplay & MAPF, QA, Code Quality neu patch lon |
| Tao/cap nhat prefab enemy/eagle/object scene | MCP Tool Router, Prefab & Scene, QA |
| Lam debug path/grid/metrics overlay | UI/UX & Debug Visualization, Metrics, QA |
| Them prioritized planning/reservation table | Gameplay & MAPF, Metrics, Code Quality, QA, Docs |
| Them LNS2 hoac algorithm so sanh | Gameplay & MAPF, Metrics, Code Quality, QA, Docs |
| Chuan bi demo DATN | UI/UX, Metrics, QA, Docs, Project Lead |
| Loi Inspector/reference/layer/tag | MCP Tool Router, Prefab & Scene, QA |
| Refactor class pathfinding lon | Gameplay & MAPF, Code Quality, QA |

## Handoff format nen bat buoc

Moi sub-agent nen tra ve ket qua theo format ngan:

```md
## Scope
- Files/scene/prefab da cham vao

## Changes
- Thay doi chinh

## Verification
- Lenh/tool da chay
- Console/test/play mode result

## Risks
- Dieu chua verify
- Case can test tiep
```

Voi MCP Tool Router, format nen la:

```md
## Unity Context
- Instance/scene/editor state

## Tool Plan
- Resource/tool can dung theo thu tu
- Target object/path/component

## Preconditions
- Editor ready?
- Scene dung?
- Compile/domain reload dang idle?

## Verification
- Console
- Screenshot/hierarchy
- Play Mode smoke test neu can
```

## Anti-pattern can tranh

- Qua nhieu sub-agent cho mot fix nho mot file.
- Sub-agent review sua code truc tiep ma khong noi ro ownership.
- MCP Tool Router vua chon tool vua sua gameplay logic, lam mat ranh gioi trach nhiem.
- Prefab/Scene agent va Gameplay agent cung sua `MapScenarioBootstrap` hoac scene trong cung luc ma khong co Project Lead dieu phoi.
- Ket luan "da fix" neu chi compile pass nhung chua Play Mode smoke test cho bug runtime.
- Them visualization/metrics ma khong co toggle, lam demo roi mat tap trung vao gameplay.

## De xuat bo core toi thieu

Neu chi muon bat dau gon, nen co 5 vai tro core:

1. Project Lead / Integrator.
2. MCP Tool Router / Unity Operator.
3. Gameplay & MAPF Engineer.
4. Prefab, Scene & UI Builder.
5. QA + Code Quality Reviewer.

Khi project lon hon, tach `Prefab, Scene & UI Builder` thanh hai agent rieng va tach `QA + Code Quality Reviewer` thanh hai agent rieng. Rieng `Metrics & Experiment Agent` nen bat dau som truoc khi vao cac milestone so sanh algorithm, vi neu de muon thi code gameplay se kho do lai mot cach cong bang.

