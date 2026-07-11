# Plan: Khôi phục CI/CD (Unity Build + Auto Deploy) và hoàn tất migration HTTP → HTTPS/domain

> Ngày tạo: 2026-06-20 · Nhánh build/deploy chuẩn: `networking-gcp`
> Phạm vi: sửa CI Unity Build (Linux Dedicated + WebGL), xử lý PR #4 conflict, và đưa thay đổi HTTP→HTTPS/domain ra production.

---

## 1. Chẩn đoán (root cause)

### 1.1 "Build Unity CI ở nhánh dev bị gãy" — thực tế
- Workflow `Unity Build` (`.github/workflows/unity-build.yml`) **chỉ trigger khi push vào `networking-gcp`** (paths: `Assets/**`, `Packages/**`, `ProjectSettings/**`, chính file workflow). **Nhánh `dev` không hề chạy Unity Build** — `dev` thậm chí không có thư mục `.github/workflows`. Vì vậy cảm giác "build gãy ở dev" thực ra là build gãy ở `networking-gcp`.
- Run gần nhất trên `networking-gcp` (commit `9f35c9b` "WIP backup", run id `27770848800`): **Linux Dedicated FAIL (exit 1)**, **WebGL FAIL (exit 125)**, **Auto-deploy bị SKIP** (vì `needs: build`). Run trước đó ("Phase B+C", `eca85ce`) vẫn **success**.

### 1.2 Nguyên nhân Build Linux Dedicated FAIL (exit 1)
- CI dùng `unityVersion: 6000.3.10f1` — **giống editor local**.
- Merge từ nhánh `dev` đã **nâng `com.unity.2d.animation` lên `14.0.4`**, kéo `com.unity.2d.common` lên **`13.0.2`**.
- `2d.common 13.0.2` gọi internal engine API GPU-skinning không tồn tại trong Unity 6000.3.10f1:
  - `InternalEngineBridge.cs(51)` → `CS1501` `IsGPUSkinningEnabled` sai số tham số
  - `InternalEngineBridge.cs(66)` → `CS0117` `SpriteRendererDataAccessExtensions.SetBatchBoneTransformIndexAndLocalAABBArray` không có
- Registry xác nhận version 2D tương thích editor này là dòng **13.x** (2d.common 12.0.2), không phải 14.x.
- → Đây chính là lỗi đã sửa tạm thời ở working tree local (hạ `2d.animation` về `13.0.4`). **Chưa commit vào `networking-gcp`** nên CI vẫn đỏ.

### 1.3 Nguyên nhân Build WebGL FAIL (exit 125)
- Log: `docker: failed to register layer: write /opt/unity/Editor/...: no space left on device`.
- Image GameCI WebGL của Unity 6000.3 rất nặng (>30GB); runner `ubuntu-latest` không đủ đĩa khi pull image → docker exit 125. Đây là **lỗi hạ tầng runner**, độc lập với lỗi 2D ở trên.

### 1.4 Vì sao "HTTP→HTTPS/domain không hoạt động sau khi merge vào networking-gcp"
- Code https/domain **đã có mặt** trên `networking-gcp` (các commit kéo về: `WebClipboard`, `serve_webgl.py`, `infra/gcp/scripts/deploy-release.sh`, các plan trong `adds/domain`, `adds/fix`).
- Nhưng **build FAIL → job `auto-deploy` (needs: build) bị SKIP → không có artifact mới được deploy** → thay đổi https/domain không bao giờ ra tới VM. ⇒ Gỡ kẹt build là điều kiện tiên quyết.
- Ngoài ra, ngay cả khi build xanh, phần deploy vẫn **hardcode HTTP + IP**:
  - `unity-build.yml` (job auto-deploy) & `deploy-gcp.yml`: `web_url=http://${server_ip}`, `registry_base_url=http://${server_ip}:8080`; smoke test cũng gọi `http://...`. Cần đổi sang `https://<domain>` để đồng bộ với migration.

### 1.5 PR #4 đang conflict
- `gh pr view 4`: **base = `test-release`, head = `dev`, state = OPEN, mergeable = CONFLICTING (DIRTY)**.
- Merge-base: `170d8ec`. Các file đụng cả 2 phía (nguồn conflict): `Packages/manifest.json`, `Packages/packages-lock.json`, `ProjectSettings/ProjectSettings.asset`, `Assets/Scripts/MenuViewBootstrap.cs`, `Assets/Scripts/Multiplayer/{LanLobbyController,LanSessionManager,LanNetworkBridge}.cs`, `Assets/Scripts/MapTankTestBootstrap.cs`, các `Assets/Scripts/Backtest/*.cs`, và `.gitignore` (`services/invite-registry/data/sessions.json`).

---

## 2. Mục tiêu
1. `Unity Build` trên `networking-gcp` xanh trở lại (cả Linux Dedicated lẫn WebGL).
2. `auto-deploy` chạy và đẩy artifact (server + WebGL) lên VM thành công.
3. Production phục vụ qua **HTTPS + domain** (WebGL tải qua https, client kết nối server qua **WSS**).
4. Giải quyết PR #4 (dev → test-release) hết conflict.

---

## 3. Giai đoạn 0 — Gỡ kẹt build (ƯU TIÊN CAO NHẤT)

### 0.1 Sửa Linux Dedicated: pin package 2D tương thích 6000.3.10f1
Trên `networking-gcp`, sửa `Packages/manifest.json`:
- `com.unity.2d.animation`: `14.0.4` → **`13.0.4`** (kéo `2d.common` về `12.0.2`).
- Kiểm tra `com.unity.2d.psdimporter` (đang `12.0.1`) và `com.unity.2d.spriteshape` (đang `13.0.0`) — đã ổn, giữ nguyên.
- Mở Unity 6000.3.10f1 để regenerate `Packages/packages-lock.json` (hoặc `manage_packages resolve_packages`), xác nhận `read_console` 0 error.

**Lưu ý đồng bộ:** working tree local hiện đã có sẵn bản sửa này (đang ở nhánh `dev`, uncommitted). Cần đảm bảo bản sửa được commit vào **`networking-gcp`** (nhánh chạy CI), và cân nhắc commit cả vào `dev`/`test-release` để tránh tái diễn qua merge.

### 0.2 Sửa WebGL: giải phóng đĩa runner trước khi build
Thêm bước dọn đĩa trước `game-ci/unity-builder@v4` trong `unity-build.yml` (chỉ cần cho WebGL, nhưng bật cho mọi target cũng an toàn):

```yaml
      - name: Free disk space (WebGL image is large)
        if: ${{ matrix.target_platform == 'WebGL' }}
        uses: jlumbroso/free-disk-space@main
        with:
          tool-cache: true
          android: true
          dotnet: true
          haskell: true
          large-packages: true
          docker-images: false   # cần docker để pull image Unity
          swap-storage: false
```
- Đặt step này **ngay sau `Checkout`, trước `Build`**.
- Phương án dự phòng nếu vẫn thiếu đĩa: tách WebGL sang workflow/job riêng có `runs-on` đĩa lớn hơn, hoặc dùng GameCI cache image.

### 0.3 Xác minh nhanh
- Push `networking-gcp` → `gh run watch <id>` → cả 2 job xanh, `auto-deploy` được kích hoạt.

---

## 4. Giai đoạn 1 — Xử lý PR #4 (dev → test-release) conflict

> Quyết định trước: **đích nhắm release là nhánh nào?** Hiện workflow build/deploy gắn cứng `networking-gcp`. Nếu `test-release` mới là nhánh release dự kiến, cần bê workflow sang `test-release` (xem Giai đoạn 2.4).

Quy trình resolve (local):
```bash
git fetch origin
git switch dev
git merge origin/test-release          # hoặc rebase tùy quy ước nhóm
# Resolve thủ công các file:
#   - Packages/manifest.json, packages-lock.json  -> chọn bản pin 2D 13.0.4/12.0.2 (Giai đoạn 0.1)
#   - ProjectSettings/ProjectSettings.asset        -> giữ cấu hình mới nhất, kiểm tra trong Editor
#   - Assets/Scripts/Multiplayer/*.cs, MenuViewBootstrap.cs, MapTankTestBootstrap.cs, Backtest/*.cs
#   - .gitignore (services/invite-registry/data/sessions.json) -> gộp cả 2
git add -A && git commit
# Mở Unity, đảm bảo 0 compile error TRƯỚC khi push
git push origin dev
```
- Sau khi push, kiểm tra lại `gh pr view 4` → `MERGEABLE`.
- Ưu tiên thống nhất **một** phiên bản package 2D xuyên suốt dev/test-release/networking-gcp để conflict manifest không lặp lại.

---

## 5. Giai đoạn 2 — Hoàn tất HTTP → HTTPS + domain trong deploy

> Tham chiếu plan đã có: `adds/domain/https_domain_migration_plan.md`, `adds/fix/webgl_wss_connection_fix_plan.md`. Chỉ thực hiện SAU khi build xanh (Giai đoạn 0).

### 2.1 Hạ tầng VM (terraform/startup/nginx)
- Đảm bảo bản ghi DNS A của domain trỏ về static IP (`tank-mapf-ip`).
- Cấu hình TLS termination trên VM (nginx + Let's Encrypt/certbot), reverse proxy:
  - `https://<domain>/` → WebGL static (port 80 nội bộ)
  - `wss://<domain>/...` → dedicated server (Netcode transport)
  - registry: `https://<domain>/registry` (hoặc giữ `:8080` sau TLS).
- Mở firewall 443 trong `infra/gcp` (terraform).

### 2.2 Cập nhật source client (đã làm ở máy khác — verify lại)
- WebGL client kết nối **WSS** tới `<domain>` thay vì `ws://IP` (tránh mixed-content khi trang chạy https). Kiểm tra `NetworkEndpointConfig` / `InternetSessionClient` / `LanClientView`.
- Build manifest / endpoint config nhúng domain thay vì IP.

### 2.3 Cập nhật workflow deploy (đổi http→https/domain)
Trong `unity-build.yml` (job `auto-deploy`) **và** `deploy-gcp.yml`, ở step *Resolve deployment targets* và *Smoke test*:
- `web_url`: `http://${server_ip}` → `https://${DOMAIN}`
- `registry_base_url`: `http://${server_ip}:8080` → `https://${DOMAIN}/registry` (hoặc cổng tương ứng).
- Thêm GitHub `vars`: `APP_DOMAIN` (và secret nếu cần) để workflow tham chiếu thay vì hardcode IP.
- Smoke test đổi sang gọi `https://${DOMAIN}/...`, `https://${DOMAIN}/Build/WebGL.wasm`, build-manifest, v.v.

### 2.4 (Nếu đổi nhánh release) di chuyển trigger
- Nếu release chuyển sang `test-release`: copy `.github/workflows/*` sang `test-release`, đổi `on.push.branches` và điều kiện `auto-deploy` (`github.ref == 'refs/heads/test-release'`).

---

## 6. Giai đoạn 3 — Kiểm thử & nghiệm thu
1. `gh run watch` cho run mới trên `networking-gcp`: Linux + WebGL **success**, auto-deploy **success**.
2. Smoke test tự động trong workflow xanh (registry healthz, web index, .wasm/.data, build-manifest chứa đúng SHA).
3. Thủ công: mở `https://<domain>` bằng trình duyệt, vào lobby, kết nối server qua WSS, chơi thử 2 client.
4. Kiểm tra không còn cảnh báo mixed-content trong console trình duyệt.
5. Đóng PR #4 sau khi merge sạch.

---

## 7. Thứ tự thực thi đề xuất
1. **Giai đoạn 0.1 + 0.2** (gỡ kẹt build) — làm ngay, ít rủi ro, mở khóa mọi thứ phía sau.
2. Push & xác minh build xanh + auto-deploy chạy (Giai đoạn 0.3).
3. **Giai đoạn 2** (https/domain deploy) — sau khi pipeline đã thông.
4. **Giai đoạn 1** (PR #4) — song song được, nhưng nên chốt chiến lược nhánh release trước.

## 8. Rủi ro & lưu ý
- **Đồng bộ Unity Editor version toàn nhóm**: nếu có máy dùng Unity mới hơn (nơi 2D 14.x đúng), việc pin 13.x sẽ lại xung đột. Cần chốt 1 phiên bản editor chung = 6000.3.10f1.
- Force-push/rewrite history trên nhánh chia sẻ phải dùng `--force-with-lease` và báo nhóm.
- Mỗi lần push `networking-gcp` tốn ~20–25 phút CI; gộp các sửa đổi để tránh chạy thừa.
- Đổi http→https chưa hoàn chỉnh dễ gây **mixed-content** (trang https tải ws://) → client WebGL không kết nối được; phải đổi đồng bộ cả client lẫn proxy.
