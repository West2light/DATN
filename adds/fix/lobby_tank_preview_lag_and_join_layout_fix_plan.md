# Fix: Lobby — preview tank "nháy/khựng" khi đổi màu + màn JOIN hở khoảng giữa

Ngày lập: 2026-06-16
Repo: `F:\DATN`  ·  Nhánh: `networking-gcp`
File chính: `Assets/Scripts/Multiplayer/LanLobbyController.cs`
Bối cảnh: lobby 2 cột (preview + slot/swatch) đã chạy; WSS đã kết nối được. Đây là 2 lỗi UI tinh chỉnh.

---

## 0. Triệu chứng

1. **Preview tank giật/nháy** (Ảnh #1): click swatch đổi màu → khung hình **khựng một nhịp** và preview **nháy** lên rồi mới hiện màu mới.
2. **Màn JOIN hở khoảng** (Ảnh #2): màn nhập room có **khoảng trống ở giữa CANCEL và hàng JOIN**, do tái dùng layout của HOST (vùng giữa vốn là status/danh sách người chơi).

---

## 1. Nguyên nhân (đã truy trong code)

### 1.1 Preview giật — `Resources.Load` đồng bộ mỗi lần click

`PickVariant(idx)` → `UpdateLobbyPreview(idx)` → `LoadVariantSprite(idx)`:
```csharp
// LanLobbyController.cs:526-545  (nhánh WebGL)
Sprite spr = Resources.Load<Sprite>("TankSprites/" + file);   // ĐỒNG BỘ, blocking
if (spr != null) return spr;
Texture2D rtex = Resources.Load<Texture2D>("TankSprites/" + file);
return Sprite.Create(rtex, ...);                               // cấp phát sprite mới (GC)
```
- `Resources.Load` chạy **đồng bộ ngay trong frame click** → lần đầu mỗi sprite phải resolve asset + decode texture + upload GPU → **khựng 1 nhịp**.
- Khoảnh khắc gán `_lobbyTankPreview.sprite = spr` trong lúc tải → **nháy** (sprite chưa sẵn → trống → hiện).
- Nhánh `Sprite.Create` (nếu rơi vào) còn **cấp phát Sprite mới mỗi click** → rác GC.

> `RefreshLobby` (line 728-729) **đã có guard** `if (local.VariantIndex.Value != _shownVariant)` nên KHÔNG reload mỗi 0.3s — tốt, không phải nguồn churn. Nguồn giật là per-click `Resources.Load`.

### 1.2 Màn JOIN hở khoảng — tái dùng footer 3 hàng của HOST

`BuildFooter` (line 335-373) bố trí 3 mốc tính từ đáy panel:
```
y0 = 14   → CANCEL (h=36)
y1 = 58   → HOST/START (h=52)   ← màn JOIN KHÔNG dùng → để TRỐNG
y2 = 118  → JOIN row / READY (h=44)
```
`SwitchTo(Screen.Joining)` (line 648-652) chỉ bật `_joinRow` (ở y2) + CANCEL (y0). **Slot y1 (~52px) ở giữa bị bỏ trống** → đúng "khoảng hở giữa CANCEL và JOIN". Ngoài ra vùng content phía trên (status -88) gần như rỗng → panel 680×480 trông trống trải cho 1 ô input.

---

## 2. Fix #1 — Preload + cache sprite, đổi màu tức thì

**Mục tiêu:** đổi màu chỉ là gán sprite đã nạp sẵn (không `Resources.Load`, không decode, không cấp phát).

`LanLobbyController.cs`:
1. Thêm cache:
   ```csharp
   private static Sprite[] _variantSpriteCache;   // index 0-4, nạp 1 lần
   ```
2. Hàm preload (gọi 1 lần khi build lobby):
   ```csharp
   private static void EnsureVariantSpritesLoaded()
   {
       if (_variantSpriteCache != null) return;
       _variantSpriteCache = new Sprite[VariantBodyFiles.Length];
       for (int i = 0; i < VariantBodyFiles.Length; i++)
           _variantSpriteCache[i] = LoadVariantSprite(i);   // nạp 1 lần duy nhất
   }
   ```
3. `UpdateLobbyPreview` lấy từ cache thay vì load:
   ```csharp
   private void UpdateLobbyPreview(int idx)
   {
       idx = Mathf.Clamp(idx, 0, VariantColors.Length - 1);
       if (idx == _shownVariant) return;          // guard: bỏ qua nếu không đổi
       _shownVariant = idx;
       if (_lobbyTankPreview != null)
       {
           Sprite spr = (_variantSpriteCache != null && idx < _variantSpriteCache.Length)
               ? _variantSpriteCache[idx] : null;
           _lobbyTankPreview.sprite = spr;
           _lobbyTankPreview.color  = spr != null ? Color.white : VariantColors[idx];
       }
       if (_lobbyTankName != null) _lobbyTankName.text = VariantDisplayName(idx);
       for (int i = 0; i < _variantRings.Count; i++)
           if (_variantRings[i] != null) _variantRings[i].SetActive(i == idx);
   }
   ```
4. Gọi `EnsureVariantSpritesLoaded()` trong `BuildLobby` (trước `UpdateLobbyPreview(...)` ở line ~493) — nạp toàn bộ 5 sprite **một lần khi mở lobby**, nên click sau đó tức thì, không giật.
   - (Tùy chọn, mượt tuyệt đối) gọi preload sớm hơn ở `Build()`/khi mở overlay, hoặc warm ở menu, để cú "nạp 1 lần" không rơi đúng lúc người dùng đang nhìn picker.

> Lưu ý: giữ guard `idx == _shownVariant` để click lại đúng màu hiện tại không gán lại sprite (tránh nháy thừa).

---

## 3. Fix #2 — Màn JOIN bố cục riêng, bỏ khoảng hở

**Mục tiêu:** màn JOIN là form gọn, căn giữa, không phụ thuộc slot y1/y2 của HOST.

Trong `SwitchTo(Screen.Joining)` (line 648), thay vì để `_joinRow` ở y2 và bỏ trống y1:

1. **Đưa hàng nhập (`_joinRow`) lên giữa vùng content** + **prompt rõ ràng phía trên**:
   - Reposition `_joinRow` RectTransform về **giữa panel** (vd anchor (0.5,0.5), `anchoredPosition ≈ (0, -10)`, giữ chiều cao ~46), thay cho vị trí footer y2.
   - Dùng `_statusTxt` (hoặc 1 label riêng) làm **prompt căn giữa** ngay trên ô nhập: ví dụ
     `"Dán link mời hoặc nhập mã phòng"` + dòng phụ nhỏ `"vd: 43E98F  ·  https://luminx.io.vn/play?session=…"`.
2. **Footer chỉ còn CANCEL** (y0=14) trên màn JOIN → **không còn slot y1 trống** → hết khoảng hở giữa CANCEL và JOIN.
3. (Tùy chọn, gọn hơn) **ẩn dòng map/algo subline** trên màn JOIN (line ~259): người JOIN chưa biết map/mode (lấy từ registry sau khi resolve), hiển thị `"random-32-32-10 · AStar"` mặc định dễ gây hiểu nhầm. Chỉ hiện subline ở `Screen.Lobby`.
4. Khi rời màn JOIN (`HideAllScreenWidgets`/sang Lobby), **trả `_joinRow` về vị trí/anchor cũ** nếu các màn khác còn dùng (hiện chỉ Joining dùng `_joinRow`, nên có thể set vị trí 1 lần — nhưng để an toàn, set lại trong mỗi `SwitchTo(Joining)`).

> Cách tối giản (nếu không muốn căn giữa): chỉ cần **dời `_joinRow` từ y2 xuống y1** (sát trên CANCEL) trên màn Joining → khử ngay "khoảng hở giữa cancel và join". Khuyến nghị làm bản căn giữa + prompt cho gọn gàng đúng kiểu hộp thoại nhập mã.

Bố cục JOIN sau fix:
```
┌ MULTIPLAYER INTERNET ───────────────┐
│            (ẩn subline map/algo)      │
│                                       │
│      Dán link mời hoặc nhập mã phòng  │   ← prompt căn giữa
│   ┌──┬───────────────────────┬─────┐  │
│   │◀ │ 43E98F / https://...   │JOIN │  │   ← input row căn giữa
│   └──┴───────────────────────┴─────┘  │
│                                       │
├───────────────────────────────────────┤
│              CANCEL                    │   ← chỉ còn CANCEL ở đáy
└───────────────────────────────────────┘
```

---

## 4. Verify

- **Preview:** mở lobby → click lần lượt 5 màu → preview đổi **tức thì**, không khựng, không nháy (kể cả màu lần đầu). Lần mở lobby đầu có thể có 1 nhịp nạp nhẹ (chấp nhận được; tùy chọn warm sớm để hết hẳn).
- **JOIN:** Menu → MULTIPLAYER INTERNET → JOIN ROOM → màn nhập **không còn khoảng trống** giữa CANCEL và ô nhập; prompt rõ ràng; dán link/mã → JOIN vào đúng lobby.
- Regression: HOST (auto-join) + Lobby (slot/swatch/preview/READY/START) vẫn đúng; CANCEL vẫn hoạt động.

---

## 5. Milestones

| MS | Nội dung | File | Done khi |
|----|----------|------|----------|
| **G1** | Preload + cache 5 sprite; `UpdateLobbyPreview` lookup cache + guard | `LanLobbyController.cs` | Đổi màu tức thì, không giật/nháy |
| **G2** | Màn JOIN: input căn giữa + prompt, footer chỉ CANCEL, (tùy chọn) ẩn subline | `LanLobbyController.cs` | Hết khoảng hở; form JOIN gọn |
| **G3** | Rebuild WebGL + redeploy | `BuildWebClient` / `deploy-web` | Bản mới lên production |
| **G4** | Verify (§4) | — | Preview mượt + JOIN gọn trên prod |

Phụ thuộc: G1 + G2 (độc lập nhau) → G3 → G4.

---

## 6. Rủi ro & lưu ý

- **Không đụng** font/glyph/scale WebGL (`UiFontProvider`, `CanvasScaler`, `BuildWebClient`). Chỉ sửa logic nạp sprite + layout JOIN.
- Sprite tank đã có sẵn ở `Assets/Resources/TankSprites/tankBody_*.png` (WebGL runtime) — cache chỉ giữ tham chiếu, không tốn thêm asset.
- Cache là `static` → sống qua các lần mở lobby; an toàn vì sprite bất biến. Không cần giải phóng (Resources sprite do Unity quản lý).
- `_joinRow` hiện chỉ dùng ở `Screen.Joining`; nếu sau này dùng lại ở màn khác, nhớ reset vị trí. Hiện set lại trong `SwitchTo(Joining)` là đủ.
- Thuần client UI → chỉ cần rebuild WebGL + redeploy, không đụng server/registry/nginx.
```
