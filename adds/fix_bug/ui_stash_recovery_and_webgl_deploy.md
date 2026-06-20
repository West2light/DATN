# Fix: UI changes bị kẹt trong stash + WebGL deploy

## Chẩn đoán

### 1. Tại sao Unity hiển thị UI cũ?

**Local = origin** — cả hai đều ở commit `eca85ce`. Không phải lỗi sync.

Vấn đề thực sự: **stash@{0} chứa UI changes chưa được apply**.

```
stash@{0}: WIP on networking-gcp: be80c4e feat: add web multiplayer deployment lane
```

Stash này được tạo khi branch đang ở commit `be80c4e` (orphaned — không có trong
history của nhánh hiện tại `eca85ce`). Changes chưa được pop/apply nên Unity Editor
vẫn đang chạy code của `eca85ce`.

### 2. Stash chứa gì?

Files liên quan đến networking/UI:
- `Assets/Scripts/Multiplayer/LanClientView.cs` — **196 insertions/deletions** (fixes ownSlot
  logic cho host-mode, wrap world-state parsing trong try-catch)
- `Assets/Scripts/MapLoader.cs` — try-catch cho LoadAndBuild
- `Assets/Scripts/MapTankTestBootstrap.cs` — thay đổi nhỏ
- `Packages/manifest.json` + `packages-lock.json` — package version bump
- `ProjectSettings/ProjectSettings.asset` + `ProjectVersion.txt` — có thể đổi Unity version
- `infra/gcp/scripts/deploy-web.sh` + `startup.sh` — infra scripts

Diff chính trong `LanClientView.cs` so với `eca85ce`:
```diff
- int ownSlot = -1;  // safe: chỉ set khi ownerClientId match
+ int ownSlot = Mathf.Min(1, playerCount - 1); // safe default slot 1
+
+ // Fix host-mode: slot 0 có thể bị race condition nếu LocalClientId chưa assign
+ if (!LanSessionManager.IsDedicatedServer && ownSlot == 0 && playerCount > 1)
+     ownSlot = Mathf.Min(1, playerCount - 1);
```

### 3. Tại sao không build được WebGL lên production?

Chưa rõ lỗi cụ thể — cần check Console trong Unity Editor khi chạy
`Tools/Tank MAPF/Build WebGL Client`. Các nguyên nhân thường gặp:
- Compile error (do package version mismatch trong working tree)
- Scene `MapF_TankTest_PIBT.unity` có dependency bị thiếu
- Working tree có `Packages/manifest.json` modified (Unity đang dùng version khác)

---

## Kế hoạch fix (theo thứ tự)

### Bước 1 — Xác nhận lại UI changes trong stash

```bash
# Xem toàn bộ diff của LanClientView.cs giữa stash WIP vs current HEAD
git diff eca85ce 08e5bfd -- Assets/Scripts/Multiplayer/LanClientView.cs
```

Kiểm tra thêm: có scene/prefab file nào bị thay đổi trực tiếp trong Unity Editor
mà chưa save không? (File > Save Project, Ctrl+S trong scene view).

### Bước 2 — Apply stash có chọn lọc

**Không dùng `git stash pop`** vì stash base (`be80c4e`) khác với current HEAD
(`eca85ce`) → có thể conflict ở Packages và ProjectSettings.

Thay vào đó, apply từng file có chủ đích:

```bash
# Chỉ apply LanClientView.cs từ stash (an toàn nhất)
git checkout 08e5bfd -- Assets/Scripts/Multiplayer/LanClientView.cs
```

Nếu muốn apply MapLoader và MapTankTestBootstrap (thường ổn):
```bash
git checkout 08e5bfd -- Assets/Scripts/MapLoader.cs
git checkout 08e5bfd -- Assets/Scripts/MapTankTestBootstrap.cs
```

**KHÔNG apply** các file sau (có thể phá build hoặc gây conflict):
- `Packages/manifest.json` — đang modified ở working tree, cần kiểm tra riêng
- `ProjectSettings/ProjectVersion.txt` — có thể đổi Unity version
- `infra/gcp/scripts/` — apply riêng nếu cần

### Bước 3 — Kiểm tra Packages conflict

Working tree hiện có `Packages/manifest.json` modified (2.d.animation: 13.0.4 → 14.0.4).
Stash cũng có thay đổi riêng trong manifest. Cần quyết định dùng version nào:

```bash
# Xem diff của manifest trong stash vs current HEAD
git diff eca85ce 08e5bfd -- Packages/manifest.json
```

Nếu stash thêm package mới (ví dụ Unity MCP, animation package) mà current branch
đã có rồi → giữ version hiện tại, không apply manifest từ stash.

### Bước 4 — Verify trong Unity Editor

1. Sau khi apply LanClientView.cs từ stash, mở Unity Editor
2. Chờ Unity recompile (không có lỗi đỏ trong Console)
3. Mở scene `MapF_TankTest.unity` → Play để test lobby internet flow
4. Xác nhận ownSlot logic hoạt động đúng (không còn 2 client control cùng ghost)

### Bước 5 — Commit changes

```bash
git add Assets/Scripts/Multiplayer/LanClientView.cs
# Thêm các file khác nếu apply ở bước 2
git commit -m "Recover LanClientView ownSlot fix and world-state try-catch from stash"
```

### Bước 6 — Fix WebGL build

Trong Unity Editor:
1. Kiểm tra Console — không có compile error
2. Vào `Tools/Tank MAPF/Build WebGL Client`
3. Nếu lỗi do scene `MapF_TankTest_PIBT.unity` thiếu dependency: bỏ scene này
   khỏi `BuildWebClient.BuildScenes[]` hoặc fix dependency trước
4. Build output: `Builds/WebGL/`

### Bước 7 — Deploy lên production

Sau khi build thành công (`Builds/WebGL/` có đủ `WebGL.wasm`, `WebGL.data`,
`WebGL.framework.js`, `WebGL.loader.js`):

```bash
# Deploy thủ công lên GCP VM
# (xem infra/gcp/scripts/deploy-web.sh để biết flow)
bash infra/gcp/scripts/deploy-web.sh
```

Hoặc nếu CI/CD tự động: push commit lên `networking-gcp` → GitHub Actions sẽ
build và deploy.

Verify sau deploy: truy cập `http://35.240.203.91` và kiểm tra WebGL load.

---

## Rủi ro cần lưu ý

| Rủi ro | Mức độ | Cách xử lý |
|--------|--------|------------|
| Stash apply conflict tại LanClientView.cs | Thấp | File khác nhau ~7 dòng, merge thủ công |
| ProjectSettings version mismatch | Trung bình | Không apply ProjectVersion.txt từ stash |
| WebGL build fail do scene PIBT | Trung bình | Tạm bỏ scene PIBT khỏi BuildScenes[] |
| LanClientView fix làm regression | Thấp | Test kỹ host-mode vs dedicated-server mode |

---

## Tóm tắt nguyên nhân gốc

Workflow sai: user tạo stash để lưu changes tạm thời → chuyển sang làm việc khác
→ branch được cập nhật (rebase hoặc new commits) → stash base (`be80c4e`) không còn
trong branch history → stash bị "treo", không thể pop an toàn → Unity vẫn chạy
committed code không có changes của stash.

Fix: apply có chọn lọc từ stash, commit, rồi build + deploy.
