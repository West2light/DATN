# Plan v2 (cookbook): Fix workflow Multiplayer Internet — hướng dẫn implement từng bước

Ngày lập: 2026-06-16
Repo: `F:\DATN`  ·  Nhánh: `networking-gcp`
Bản gốc (lý do & thiết kế): `adds/multiplayer_internet_production_workflow_fix_plan.md`

> **Mục đích bản v2:** Viết chi tiết tới mức **một model năng lực thấp (dưới Sonnet medium) chỉ cần làm theo cơ học** — mỗi bước có file, đoạn code CŨ cần tìm, đoạn code MỚI thay vào (hoặc nội dung file mới đầy đủ), và cách kiểm tra. KHÔNG cần tự suy luận thiết kế.

---

## CÁCH DÙNG TÀI LIỆU NÀY (đọc trước khi bắt đầu)

**Quy tắc cho model implement:**

1. Làm **tuần tự M1 → M6**. Không nhảy cóc. Mỗi milestone là 1 commit.
2. Với mỗi bước "SỬA": **đọc file trước (`Read`)**, tìm đúng đoạn `=== CŨ ===`, thay bằng `=== MỚI ===` bằng `Edit`. Đoạn CŨ phải khớp **từng ký tự, kể cả khoảng trắng**.
3. Với mỗi bước "TẠO FILE": dùng `Write` với nội dung cho sẵn (Write tự tạo thư mục nếu chưa có).
4. **Sau mỗi milestone**, kiểm tra biên dịch: gọi `mcp__UnityMCP__refresh_unity` rồi `mcp__UnityMCP__read_console` (filter errors). Phải **0 lỗi compile** mới sang milestone sau. Nếu có lỗi → sửa theo thông báo, không bỏ qua.
5. **KHÔNG sửa** các file: `UiFontProvider.cs`, `BuildWebClient.cs`, `NotoSans-Regular.ttf`, hay bất kỳ CanvasScaler nào. Không đổi font/scale.
6. Code/identifier viết bằng tiếng Anh; chuỗi hiển thị giữ tiếng Việt như mẫu.
7. Unity tự sinh file `.meta` khi refresh — KHÔNG tự tạo `.meta` thủ công.

**File sẽ động tới:**
- `Assets/Scripts/MenuViewBootstrap.cs`  (M1)
- `Assets/Scripts/Multiplayer/LanLobbyController.cs`  (M2, M3, M4)
- `Assets/Plugins/WebGL/WebClipboard.jslib`  (M3 — MỚI)
- `Assets/Scripts/Multiplayer/WebClipboard.cs`  (M3 — MỚI)
- `Assets/Scripts/Multiplayer/LanClientView.cs`  (M5)

**Bối cảnh đã xong (KHÔNG đụng):** lobby networked, RPC Ready/Variant/RequestStart, server owner-start, spawn theo số client, link bridge — đã có. Bản này chỉ nối lại menu + picker + clipboard + 1 hardening.

---

## M1 — Menu: đổi nhãn, tách HOST/JOIN, bỏ bước chọn tank khỏi Internet

**File:** `Assets/Scripts/MenuViewBootstrap.cs`
**Kết quả:** Menu hiện "MULTIPLAYER INTERNET" → bấm ra màn chọn HOST / JOIN; HOST → màn map/mode (tiêu đề INTERNET) → tạo room; KHÔNG còn đi qua màn chọn tank.

### Bước 1.1 — Thêm `InternetEntry` vào enum Screen

=== CŨ ===
```csharp
    private enum Screen { Main, Outfit, MapSelect, LanMapSelect }
```
=== MỚI ===
```csharp
    private enum Screen { Main, Outfit, MapSelect, LanMapSelect, InternetEntry }
```

### Bước 1.2 — Thêm biến giữ GameObject màn mới

=== CŨ ===
```csharp
    private GameObject _screenMain, _screenOutfit, _screenMap, _screenLan;
```
=== MỚI ===
```csharp
    private GameObject _screenMain, _screenOutfit, _screenMap, _screenLan, _screenInternet;
```

### Bước 1.3 — Gọi build màn mới trong `Apply()`

=== CŨ ===
```csharp
        BuildScreenMain();
        BuildScreenOutfit();
        BuildScreenMapSelect();
        BuildScreenLanMapSelect();
        ShowScreen(Screen.Main);
```
=== MỚI ===
```csharp
        BuildScreenMain();
        BuildScreenOutfit();
        BuildScreenMapSelect();
        BuildScreenLanMapSelect();
        BuildScreenInternetEntry();
        ShowScreen(Screen.Main);
```

### Bước 1.4 — Đổi nút menu chính: nhãn + điều hướng

=== CŨ ===
```csharp
        // MULTIPLAYER LAN — đi qua màn chọn tank giống Single Play
        Button btnHost = MakeButton(card.transform, "BtnHost",
            "MULTIPLAYER  LAN", new Vector2(0f, -58f), new Vector2(290f, 60f),
            new Color(0.12f, 0.32f, 0.58f, 1f));
        SetTextColor(btnHost.transform, new Color(0.75f, 0.90f, 1f));
        btnHost.onClick.AddListener(() => { _isLanMode = true; ShowScreen(Screen.Outfit); });
```
=== MỚI ===
```csharp
        // MULTIPLAYER INTERNET — vào màn chọn HOST / JOIN (KHÔNG qua màn chọn tank)
        Button btnHost = MakeButton(card.transform, "BtnHost",
            "MULTIPLAYER  INTERNET", new Vector2(0f, -58f), new Vector2(290f, 60f),
            new Color(0.12f, 0.32f, 0.58f, 1f));
        SetTextColor(btnHost.transform, new Color(0.75f, 0.90f, 1f));
        btnHost.onClick.AddListener(() => { _isLanMode = true; ShowScreen(Screen.InternetEntry); });
```

### Bước 1.5 — NEXT của màn Outfit chỉ còn dùng cho Single Play

=== CŨ ===
```csharp
        btnNext.onClick.AddListener(() => ShowScreen(_isLanMode ? Screen.LanMapSelect : Screen.MapSelect));
```
=== MỚI ===
```csharp
        btnNext.onClick.AddListener(() => ShowScreen(Screen.MapSelect));
```

### Bước 1.6 — Đổi tiêu đề màn map/mode "LAN" → "INTERNET"

=== CŨ ===
```csharp
        MakeText(_screenLan.transform, "Title", "LAN  —  SELECT MAP & MODE",
```
=== MỚI ===
```csharp
        MakeText(_screenLan.transform, "Title", "INTERNET  —  SELECT MAP & MODE",
```

### Bước 1.7 — Nút BACK của màn map/mode về INTERNET ENTRY

> ⚠️ Có 2 chỗ giống nhau. Chỉ sửa chỗ có `_screenLan.transform` (KHÔNG phải `_screenMap.transform`).

=== CŨ ===
```csharp
        Button btnBack = MakeBackButton(_screenLan.transform, "BtnBack",
            new Vector2(-540f, -320f), new Vector2(130f, 46f),
            new Color(0.28f, 0.38f, 0.48f, 1f));
        SetTextColor(btnBack.transform, TextLight);
        btnBack.onClick.AddListener(() => ShowScreen(Screen.Outfit));
```
=== MỚI ===
```csharp
        Button btnBack = MakeBackButton(_screenLan.transform, "BtnBack",
            new Vector2(-540f, -320f), new Vector2(130f, 46f),
            new Color(0.28f, 0.38f, 0.48f, 1f));
        SetTextColor(btnBack.transform, TextLight);
        btnBack.onClick.AddListener(() => ShowScreen(Screen.InternetEntry));
```

### Bước 1.8 — Cập nhật `ShowScreen` để bật/tắt màn mới

=== CŨ ===
```csharp
    private void ShowScreen(Screen screen)
    {
        if (_screenMain  != null) _screenMain.SetActive(screen == Screen.Main);
        if (_screenOutfit != null) _screenOutfit.SetActive(screen == Screen.Outfit);
        if (_screenMap   != null) _screenMap.SetActive(screen == Screen.MapSelect);
        if (_screenLan   != null) _screenLan.SetActive(screen == Screen.LanMapSelect);
    }
```
=== MỚI ===
```csharp
    private void ShowScreen(Screen screen)
    {
        if (_screenMain  != null) _screenMain.SetActive(screen == Screen.Main);
        if (_screenOutfit != null) _screenOutfit.SetActive(screen == Screen.Outfit);
        if (_screenMap   != null) _screenMap.SetActive(screen == Screen.MapSelect);
        if (_screenLan   != null) _screenLan.SetActive(screen == Screen.LanMapSelect);
        if (_screenInternet != null) _screenInternet.SetActive(screen == Screen.InternetEntry);
    }
```

### Bước 1.9 — Thêm 2 hàm mới (màn HOST/JOIN + mở Join prompt)

> Dán **ngay phía trên** dòng `private void ShowScreen(Screen screen)` (tức trước phần "Screen transition").

=== THÊM MỚI ===
```csharp
    // ═══════════════════════════════════════════════════════════════════════
    // SCREEN: Internet Entry (HOST / JOIN)
    // ═══════════════════════════════════════════════════════════════════════

    private void BuildScreenInternetEntry()
    {
        _screenInternet = MakePanel(_canvas.transform, "ScreenInternetEntry",
            Vector2.zero, new Vector2(1280f, 720f), BgDark);

        MakeText(_screenInternet.transform, "Title", "MULTIPLAYER INTERNET",
            34, FontStyle.Bold, TextLight,
            new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(720f, 52f));

        MakeText(_screenInternet.transform, "Hint",
            "Tạo phòng mới, hoặc tham gia bằng mã phòng / link mời",
            15, FontStyle.Italic, TextMuted,
            new Vector2(0.5f, 1f), new Vector2(0f, -108f), new Vector2(760f, 24f));

        // HOST GAME → chọn map/mode → tạo room → lobby
        Button btnHostGame = MakeButton(_screenInternet.transform, "BtnHostGame",
            "●  HOST GAME  —  Tạo phòng", new Vector2(0f, 40f), new Vector2(440f, 72f),
            new Color(0.18f, 0.48f, 0.90f, 1f));
        SetTextColor(btnHostGame.transform, TextLight);
        btnHostGame.onClick.AddListener(() => ShowScreen(Screen.LanMapSelect));

        // JOIN ROOM → nhập mã / link → lobby
        Button btnJoinRoom = MakeButton(_screenInternet.transform, "BtnJoinRoom",
            "→  JOIN ROOM  —  Nhập mã / link", new Vector2(0f, -52f), new Vector2(440f, 72f),
            new Color(0.20f, 0.55f, 0.32f, 1f));
        SetTextColor(btnJoinRoom.transform, TextLight);
        btnJoinRoom.onClick.AddListener(OpenJoinPrompt);

        // BACK → main menu
        Button btnBack = MakeBackButton(_screenInternet.transform, "BtnBack",
            new Vector2(-500f, -300f), new Vector2(130f, 46f),
            new Color(0.28f, 0.38f, 0.48f, 1f));
        SetTextColor(btnBack.transform, TextLight);
        btnBack.onClick.AddListener(() => ShowScreen(Screen.Main));
    }

    private void OpenJoinPrompt()
    {
        string defaultMap = Maps.Length > 0 ? Maps[0].mapFile : "Assets/MapData/random-32-32-10.map";
        LanLobbyController.ShowJoinPrompt(defaultMap, "AStar", GetRuntimeRegistryBaseUrl());
    }
```

### Kiểm tra M1
- `refresh_unity` + `read_console` → 0 lỗi. (Tạm thời `LanLobbyController.ShowJoinPrompt` chưa tồn tại → **sẽ báo lỗi compile**. Cách xử lý: làm M2 ngay sau M1 rồi mới compile-check, HOẶC tạm comment dòng gọi `ShowJoinPrompt`. **Khuyến nghị: làm M1 và M2 liền nhau rồi mới check compile.**)
- Sau khi có M2: vào Menu, bấm MULTIPLAYER INTERNET → thấy 2 nút HOST/JOIN; bấm HOST → màn "INTERNET — SELECT MAP & MODE".
- **Commit:** `M1: menu internet entry (host/join split), drop pre-create tank step`

---

## M2 — Lobby: thêm entry JOIN (nhập mã phòng) + seed registry URL

**File:** `Assets/Scripts/Multiplayer/LanLobbyController.cs`
**Kết quả:** Nút JOIN ROOM mở overlay ở màn nhập mã; gõ mã phòng (vd `AEE017`) → resolve qua registry → vào lobby.

### Bước 2.1 — Thêm field `_joinRegistryUrl`

Tìm khối field "Waiting-lobby refs" và thêm 1 dòng. 

=== CŨ ===
```csharp
    private readonly List<Image> _variantSwatches = new List<Image>();
    private bool       _localReady;
    private bool       _refreshingLobby;
```
=== MỚI ===
```csharp
    private readonly List<Image> _variantSwatches = new List<Image>();
    private readonly List<GameObject> _variantRings = new List<GameObject>();
    private Image      _lobbyTankPreview;
    private Text       _lobbyTankName;
    private int        _shownVariant = -1;
    private string     _joinRegistryUrl = string.Empty;
    private bool       _localReady;
    private bool       _refreshingLobby;
```
> (Các field preview/ring khai báo luôn ở đây để M4 dùng — không cần sửa lại.)

### Bước 2.2 — Thêm static entry `ShowJoinPrompt`

Tìm `public static void ShowAndJoin(...)` và dán **ngay sau** nó (trước `private static void ShowInternal`).

=== THÊM MỚI ===
```csharp
    // Mở overlay thẳng vào màn nhập mã phòng / link (cho user JOIN từ menu).
    // registryUrl seed fallback registry để mã phòng trống (vd "AEE017") resolve được.
    public static void ShowJoinPrompt(string mapFile, string algorithm, string registryUrl)
    {
        if (_instance != null) { Destroy(_instance.gameObject); _instance = null; }
        CleanupSession();

        var go = new GameObject("LanLobbyController");
        DontDestroyOnLoad(go);
        _instance                  = go.AddComponent<LanLobbyController>();
        _instance._mapFile         = mapFile;
        _instance._algorithm       = algorithm;
        _instance._autoJoinTarget  = string.Empty;
        _instance._joinRegistryUrl = registryUrl ?? string.Empty;
        _instance.Build();
        _instance.OpenJoinScreen();
    }

    private void OpenJoinScreen()
    {
        if (!EnsureNetworkManager()) return;
        LanSessionManager.ActivateClient(_mapFile, _algorithm);
        SwitchTo(Screen.Joining);
    }
```

### Bước 2.3 — Trong `DoConnect`, seed registry từ `_joinRegistryUrl`

Tìm đầu hàm `DoConnect`.

=== CŨ ===
```csharp
    private void DoConnect(string ip)
    {
        if (!InternetJoinParser.TryParse(ip, LanSessionManager.RegistryUrl, out NetworkEndpointConfig endpoint, out string parseError))
        {
            SetStatus(parseError, new Color(1f, 0.35f, 0.35f));
            return;
        }
```
=== MỚI ===
```csharp
    private void DoConnect(string ip)
    {
        string fallbackRegistry = !string.IsNullOrWhiteSpace(LanSessionManager.RegistryUrl)
            ? LanSessionManager.RegistryUrl
            : _joinRegistryUrl;
        if (!InternetJoinParser.TryParse(ip, fallbackRegistry, out NetworkEndpointConfig endpoint, out string parseError))
        {
            SetStatus(parseError, new Color(1f, 0.35f, 0.35f));
            return;
        }
```

### Kiểm tra M2
- `refresh_unity` + `read_console` → 0 lỗi (giờ `ShowJoinPrompt` đã tồn tại, lỗi M1 hết).
- Test (cần production hoặc 2 build): tab 1 HOST tạo room, copy mã ở header lobby; tab 2 → MULTIPLAYER INTERNET → JOIN ROOM → gõ mã → vào cùng lobby.
- **Commit:** `M2: join-by-code entry from menu + seed registry url`

---

## M3 — COPY LINK chạy được trên WebGL/HTTP (jslib + fallback)

**Files mới:** `Assets/Plugins/WebGL/WebClipboard.jslib`, `Assets/Scripts/Multiplayer/WebClipboard.cs`
**Sửa:** `LanLobbyController.CopyInvite`
**Lý do:** `GUIUtility.systemCopyBuffer` không chạy trên WebGL; site là HTTP nên `navigator.clipboard` bị chặn → **bắt buộc fallback `execCommand('copy')`**.

### Bước 3.1 — TẠO FILE `Assets/Plugins/WebGL/WebClipboard.jslib`

```javascript
mergeInto(LibraryManager.library, {
  JsCopyToClipboard: function (strPtr) {
    var str = UTF8ToString(strPtr);
    try {
      if (window.isSecureContext && navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(str);
        return;
      }
    } catch (e) { /* rơi xuống execCommand */ }
    try {
      var ta = document.createElement('textarea');
      ta.value = str;
      ta.style.position = 'fixed';
      ta.style.top = '0';
      ta.style.left = '0';
      ta.style.opacity = '0';
      document.body.appendChild(ta);
      ta.focus();
      ta.select();
      document.execCommand('copy');
      document.body.removeChild(ta);
    } catch (e) {
      console.warn('[WebClipboard] copy failed', e);
    }
  }
});
```

### Bước 3.2 — TẠO FILE `Assets/Scripts/Multiplayer/WebClipboard.cs`

```csharp
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

/// <summary>
/// Copy text vào clipboard hệ thống. Trên WebGL gọi jslib (navigator.clipboard với
/// fallback execCommand cho HTTP). Ngoài WebGL dùng GUIUtility.systemCopyBuffer.
/// </summary>
public static class WebClipboard
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void JsCopyToClipboard(string str);
#endif

    public static void Copy(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
#if UNITY_WEBGL && !UNITY_EDITOR
        JsCopyToClipboard(text);
#else
        GUIUtility.systemCopyBuffer = text;
#endif
    }
}
```

### Bước 3.3 — Dùng `WebClipboard.Copy` trong `CopyInvite`

File `LanLobbyController.cs`.

=== CŨ ===
```csharp
        string link = BuildInviteLink();
        if (string.IsNullOrEmpty(link)) return;
        GUIUtility.systemCopyBuffer = link;
        SetStatus("Đã copy link mời!", Green);
```
=== MỚI ===
```csharp
        string link = BuildInviteLink();
        if (string.IsNullOrEmpty(link)) return;
        WebClipboard.Copy(link);
        SetStatus("Đã copy link mời!", Green);
```

### Kiểm tra M3
- `refresh_unity` + `read_console` → 0 lỗi.
- Test (production, vì jslib chỉ chạy ở build WebGL thật): vào lobby → bấm COPY LINK → dán ra trình duyệt/notepad → ra `http://35.240.203.91/play?session=<MÃ>`.
- **Commit:** `M3: webgl clipboard copy (navigator.clipboard + execCommand fallback)`

---

## M4 — Lobby 2 cột: cột PREVIEW (tank đổi màu) + slot/swatch/code, START≥2

**File:** `Assets/Scripts/Multiplayer/LanLobbyController.cs`
**Kết quả:** Lobby bố trí giống màn SELECT YOUR TANK: trái có preview tank **đổi màu theo swatch**, phải có slot list + swatch + mã phòng + COPY; START chỉ bật khi ≥2 đã Ready; hết cảnh chữ đè (Image #5).

### Bước 4.1 — Mở rộng panel cho đủ 2 cột

=== CŨ ===
```csharp
    private const float PW    = 520f;   // panel width
    private const float PH    = 480f;   // panel height
```
=== MỚI ===
```csharp
    private const float PW    = 680f;   // panel width (rộng hơn cho lobby 2 cột)
    private const float PH    = 480f;   // panel height
```

### Bước 4.2 — Tiêu đề header: WebGL hiện "INTERNET"

=== CŨ ===
```csharp
        // Title
        TLbl(_panel, "Title", "MULTIPLAYER  LAN",
            22, FontStyle.Bold, Gold, new Vector2(0f, -16f), new Vector2(FullW, 28f));
```
=== MỚI ===
```csharp
        // Title
        string title =
#if UNITY_WEBGL && !UNITY_EDITOR
            "MULTIPLAYER  INTERNET";
#else
            "MULTIPLAYER  LAN";
#endif
        TLbl(_panel, "Title", title,
            22, FontStyle.Bold, Gold, new Vector2(0f, -16f), new Vector2(FullW, 28f));
```

### Bước 4.3 — Thay TOÀN BỘ hàm `BuildLobby` bằng layout 2 cột

> Tìm hàm `private void BuildLobby(int L)` và thay **cả thân hàm** (từ dòng `private void BuildLobby(int L)` tới dấu `}` đóng hàm — ngay trước `private GameObject BuildJoinRow(...)`).

=== MỚI (toàn bộ hàm) ===
```csharp
    private void BuildLobby(int L)
    {
        _lobbyBox = Mk(_panel, "LobbyBox", L);
        var rt = _lobbyBox.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -100f);
        rt.sizeDelta = new Vector2(-PadX * 2f, 210f);
        _lobbyBox.SetActive(false);

        // ── CỘT TRÁI: preview tank (tái dùng look SELECT YOUR TANK) ──────────
        var prevLbl = Mk(_lobbyBox, "PrevLbl", L);
        var prRt = prevLbl.GetComponent<RectTransform>();
        prRt.anchorMin = new Vector2(0f, 1f); prRt.anchorMax = new Vector2(0.34f, 1f);
        prRt.pivot = new Vector2(0.5f, 1f);
        prRt.anchoredPosition = new Vector2(0f, 0f); prRt.sizeDelta = new Vector2(0f, 16f);
        var prTxt = prevLbl.AddComponent<Text>();
        prTxt.font = F(); prTxt.fontSize = 11; prTxt.color = Muted;
        prTxt.alignment = TextAnchor.MiddleCenter; prTxt.text = "PREVIEW";

        var holder = Mk(_lobbyBox, "Holder", L);
        var hRt = holder.GetComponent<RectTransform>();
        hRt.anchorMin = new Vector2(0.03f, 1f); hRt.anchorMax = new Vector2(0.31f, 1f);
        hRt.pivot = new Vector2(0.5f, 1f);
        hRt.anchoredPosition = new Vector2(0f, -20f); hRt.sizeDelta = new Vector2(0f, 120f);
        holder.AddComponent<Image>().color = new Color(0.06f, 0.12f, 0.18f, 1f);

        var imgGo = Mk(holder, "TankImg", L);
        var imRt = imgGo.GetComponent<RectTransform>();
        imRt.anchorMin = new Vector2(0.12f, 0.12f); imRt.anchorMax = new Vector2(0.88f, 0.88f);
        imRt.offsetMin = imRt.offsetMax = Vector2.zero;
        _lobbyTankPreview = imgGo.AddComponent<Image>();
        _lobbyTankPreview.preserveAspect = true;

        var nameGo = Mk(_lobbyBox, "TankName", L);
        var nmRt = nameGo.GetComponent<RectTransform>();
        nmRt.anchorMin = new Vector2(0f, 1f); nmRt.anchorMax = new Vector2(0.34f, 1f);
        nmRt.pivot = new Vector2(0.5f, 1f);
        nmRt.anchoredPosition = new Vector2(0f, -146f); nmRt.sizeDelta = new Vector2(0f, 22f);
        _lobbyTankName = nameGo.AddComponent<Text>();
        _lobbyTankName.font = F(); _lobbyTankName.fontSize = 16; _lobbyTankName.fontStyle = FontStyle.Bold;
        _lobbyTankName.color = Gold; _lobbyTankName.alignment = TextAnchor.MiddleCenter;
        _lobbyTankName.text = "Blue";

        // ── CỘT PHẢI: slot list + swatch + code/copy ─────────────────────────
        var slots = Mk(_lobbyBox, "Slots", L);
        var sRt = slots.GetComponent<RectTransform>();
        sRt.anchorMin = new Vector2(0.38f, 1f); sRt.anchorMax = new Vector2(1f, 1f);
        sRt.pivot = new Vector2(0.5f, 1f);
        sRt.anchoredPosition = new Vector2(0f, 0f); sRt.sizeDelta = new Vector2(0f, 96f);
        _lobbySlotsTxt = slots.AddComponent<Text>();
        _lobbySlotsTxt.font = F(); _lobbySlotsTxt.fontSize = 13;
        _lobbySlotsTxt.color = BlueTint; _lobbySlotsTxt.alignment = TextAnchor.UpperLeft;
        _lobbySlotsTxt.text = "Đang tải danh sách người chơi…";

        var pickLbl = Mk(_lobbyBox, "PickLbl", L);
        var plRt = pickLbl.GetComponent<RectTransform>();
        plRt.anchorMin = new Vector2(0.38f, 1f); plRt.anchorMax = new Vector2(1f, 1f);
        plRt.pivot = new Vector2(0f, 1f);
        plRt.anchoredPosition = new Vector2(0f, -100f); plRt.sizeDelta = new Vector2(0f, 16f);
        var plTxt = pickLbl.AddComponent<Text>();
        plTxt.font = F(); plTxt.fontSize = 11; plTxt.color = Muted;
        plTxt.alignment = TextAnchor.MiddleLeft; plTxt.text = "Chọn màu tank:";

        var picker = Mk(_lobbyBox, "Picker", L);
        var pkRt = picker.GetComponent<RectTransform>();
        pkRt.anchorMin = new Vector2(0.38f, 1f); pkRt.anchorMax = new Vector2(1f, 1f);
        pkRt.pivot = new Vector2(0.5f, 1f);
        pkRt.anchoredPosition = new Vector2(0f, -120f); pkRt.sizeDelta = new Vector2(0f, 32f);
        _variantRings.Clear();
        _variantSwatches.Clear();
        for (int i = 0; i < VariantColors.Length; i++)
        {
            int idx = i;
            float x0 = i * 0.2f, x1 = i * 0.2f + 0.17f;

            var ring = Mk(picker, $"Ring{i}", L);
            var rgRt = ring.GetComponent<RectTransform>();
            rgRt.anchorMin = new Vector2(x0, 0f); rgRt.anchorMax = new Vector2(x1, 1f);
            rgRt.offsetMin = new Vector2(-3f, -3f); rgRt.offsetMax = new Vector2(3f, 3f);
            ring.AddComponent<Image>().color = new Color(1f, 0.82f, 0.15f, 0.95f);
            ring.SetActive(false);
            _variantRings.Add(ring);

            var sw = Mk(picker, $"Sw{i}", L);
            var swRt = sw.GetComponent<RectTransform>();
            swRt.anchorMin = new Vector2(x0, 0f); swRt.anchorMax = new Vector2(x1, 1f);
            swRt.offsetMin = swRt.offsetMax = Vector2.zero;
            var img = sw.AddComponent<Image>(); img.color = VariantColors[i];
            var btn = sw.AddComponent<Button>(); btn.targetGraphic = img;
            btn.onClick.AddListener(() => PickVariant(idx));
            _variantSwatches.Add(img);
        }

        var code = Mk(_lobbyBox, "Code", L);
        var cRt = code.GetComponent<RectTransform>();
        cRt.anchorMin = new Vector2(0.38f, 1f); cRt.anchorMax = new Vector2(0.72f, 1f);
        cRt.pivot = new Vector2(0f, 1f);
        cRt.anchoredPosition = new Vector2(0f, -160f); cRt.sizeDelta = new Vector2(0f, 26f);
        _lobbyCodeTxt = code.AddComponent<Text>();
        _lobbyCodeTxt.font = F(); _lobbyCodeTxt.fontSize = 13; _lobbyCodeTxt.fontStyle = FontStyle.Bold;
        _lobbyCodeTxt.color = Gold; _lobbyCodeTxt.alignment = TextAnchor.MiddleLeft;

        var copy = Mk(_lobbyBox, "Copy", L);
        var cpRt = copy.GetComponent<RectTransform>();
        cpRt.anchorMin = new Vector2(0.74f, 1f); cpRt.anchorMax = new Vector2(1f, 1f);
        cpRt.pivot = new Vector2(1f, 1f);
        cpRt.anchoredPosition = new Vector2(0f, -158f); cpRt.sizeDelta = new Vector2(0f, 28f);
        copy.AddComponent<Image>().color = Slate;
        var copyBtn = copy.AddComponent<Button>(); copyBtn.targetGraphic = copy.GetComponent<Image>();
        copyBtn.onClick.AddListener(CopyInvite);
        LblFill(copy, "COPY LINK", 11, FontStyle.Bold, White, L);

        UpdateLobbyPreview(LanSessionManager.LocalVariantIndex);
    }

    // ── Helpers cho preview tank trong lobby ───────────────────────────────────
    private static readonly string[] VariantBodyFiles =
    { "tankBody_blue", "tankBody_red", "tankBody_green", "tankBody_dark", "tankBody_sand" };

    private void UpdateLobbyPreview(int idx)
    {
        idx = Mathf.Clamp(idx, 0, VariantColors.Length - 1);
        _shownVariant = idx;
        if (_lobbyTankPreview != null)
        {
            Sprite spr = LoadVariantSprite(idx);
            _lobbyTankPreview.sprite = spr;
            _lobbyTankPreview.color  = spr != null ? Color.white : VariantColors[idx];
        }
        if (_lobbyTankName != null) _lobbyTankName.text = VariantDisplayName(idx);
        for (int i = 0; i < _variantRings.Count; i++)
            if (_variantRings[i] != null) _variantRings[i].SetActive(i == idx);
    }

    private static string VariantDisplayName(int i)
    {
        switch (Mathf.Clamp(i, 0, 4))
        {
            case 0:  return "Blue";
            case 1:  return "Red";
            case 2:  return "Green";
            case 3:  return "Dark";
            default: return "Sand";
        }
    }

    private static Sprite LoadVariantSprite(int idx)
    {
        idx = Mathf.Clamp(idx, 0, VariantBodyFiles.Length - 1);
        string file = VariantBodyFiles[idx];
#if UNITY_EDITOR
        string path = "Assets/Sprites/Kenny Topdown Tanks Redux/PNG/Retina/" + file + ".png";
        var spr = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (spr != null) return spr;
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (tex != null)
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        return null;
#else
        Sprite spr = Resources.Load<Sprite>("TankSprites/" + file);
        if (spr != null) return spr;
        Texture2D rtex = Resources.Load<Texture2D>("TankSprites/" + file);
        if (rtex == null) return null;
        return Sprite.Create(rtex, new Rect(0, 0, rtex.width, rtex.height), new Vector2(0.5f, 0.5f), 128f);
#endif
    }
```

### Bước 4.4 — `PickVariant` cập nhật preview ngay khi bấm

=== CŨ ===
```csharp
    private void PickVariant(int idx)
    {
        idx = Mathf.Clamp(idx, 0, VariantColors.Length - 1);
        LanSessionManager.LocalVariantIndex = idx;
        PlayerPrefs.SetInt("MenuTankVariant", idx); PlayerPrefs.Save();
        LanNetworkBridge.Local?.SubmitVariant(idx);
    }
```
=== MỚI ===
```csharp
    private void PickVariant(int idx)
    {
        idx = Mathf.Clamp(idx, 0, VariantColors.Length - 1);
        LanSessionManager.LocalVariantIndex = idx;
        PlayerPrefs.SetInt("MenuTankVariant", idx); PlayerPrefs.Save();
        LanNetworkBridge.Local?.SubmitVariant(idx);
        UpdateLobbyPreview(idx);
    }
```

### Bước 4.5 — `RefreshLobby`: đồng bộ preview + START≥2

Trong hàm `RefreshLobby`, tìm khối cập nhật nhãn READY và thêm đồng bộ preview.

=== CŨ ===
```csharp
        if (local != null && _btnReadyLabel != null)
        {
            _localReady = local.Ready.Value;
            _btnReadyLabel.text = _localReady ? "✖  HỦY SẴN SÀNG" : "✔  SẴN SÀNG";
        }
```
=== MỚI ===
```csharp
        if (local != null && _btnReadyLabel != null)
        {
            _localReady = local.Ready.Value;
            _btnReadyLabel.text = _localReady ? "✖  HỦY SẴN SÀNG" : "✔  SẴN SÀNG";
        }

        // Đồng bộ preview theo variant server xác nhận của bridge mình.
        if (local != null && local.VariantIndex.Value != _shownVariant)
            UpdateLobbyPreview(local.VariantIndex.Value);
```

Tiếp theo, vẫn trong `RefreshLobby`, sửa điều kiện START.

=== CŨ ===
```csharp
        if (isRoomOwner && _btnStartLobby != null)
        {
            bool allReady = bridges.Count >= 1 && readyCount == bridges.Count;
            _btnStartLobby.interactable = allReady;
            if (_btnStartLabel != null)
                _btnStartLabel.text = allReady
                    ? "▶  START GAME"
                    : $"START  ({readyCount}/{bridges.Count} sẵn sàng)";
        }
```
=== MỚI ===
```csharp
        if (isRoomOwner && _btnStartLobby != null)
        {
            int minStart =
#if UNITY_EDITOR
                1;   // cho phép test solo trong Editor
#else
                2;   // production: cần ≥2 người (theo mục 2.0)
#endif
            bool allReady = bridges.Count >= minStart && readyCount == bridges.Count;
            _btnStartLobby.interactable = allReady;
            if (_btnStartLabel != null)
                _btnStartLabel.text = allReady
                    ? "▶  START GAME"
                    : $"START  ({readyCount}/{bridges.Count} · cần ≥{minStart})";
        }
```

### Bước 4.6 — Sửa overlap chữ: status thành 1 dòng mảnh khi ở Lobby

Trong `HideAllScreenWidgets`, thêm reset status về mặc định (đầu hàm).

=== CŨ ===
```csharp
    private void HideAllScreenWidgets()
    {
        SetVis(_btnHost, false);  SetVis(_btnStart, false);  SetVis(_btnStartLobby, false);
        SetVis(_btnJoin, false);  SetVis(_btnReadyLobby, false);
```
=== MỚI ===
```csharp
    private void HideAllScreenWidgets()
    {
        if (_statusTxt != null)
        {
            _statusTxt.gameObject.SetActive(true);
            var srt = _statusTxt.GetComponent<RectTransform>();
            srt.anchoredPosition = new Vector2(0f, -88f);
            srt.sizeDelta = new Vector2(-PadX * 2f, 56f);
            _statusTxt.fontSize = 14;
        }
        SetVis(_btnHost, false);  SetVis(_btnStart, false);  SetVis(_btnStartLobby, false);
        SetVis(_btnJoin, false);  SetVis(_btnReadyLobby, false);
```

Trong `SwitchTo`, sửa nhánh `case Screen.Lobby`.

=== CŨ ===
```csharp
            case Screen.Lobby:
                HideAllScreenWidgets();
                SetStatus("Phòng chờ — chọn tank, bấm SẴN SÀNG.", Green);
                _lobbyBox?.SetActive(true);
                SetVis(_btnReadyLobby, true);
                // START (owner-only) visibility is managed each tick by RefreshLobby.
                InvokeRepeating(nameof(RefreshLobby), 0f, 0.3f);
                break;
```
=== MỚI ===
```csharp
            case Screen.Lobby:
                HideAllScreenWidgets();
                // Status thành 1 dòng mảnh ở trên cùng để không đè lobby 2 cột.
                if (_statusTxt != null)
                {
                    var srt = _statusTxt.GetComponent<RectTransform>();
                    srt.anchoredPosition = new Vector2(0f, -78f);
                    srt.sizeDelta = new Vector2(-PadX * 2f, 18f);
                    _statusTxt.fontSize = 12;
                }
                SetStatus("Phòng chờ — chọn tank & SẴN SÀNG", Green);
                _lobbyBox?.SetActive(true);
                SetVis(_btnReadyLobby, true);
                // START (owner-only) visibility is managed each tick by RefreshLobby.
                InvokeRepeating(nameof(RefreshLobby), 0f, 0.3f);
                break;
```

### Kiểm tra M4
- `refresh_unity` + `read_console` → 0 lỗi.
- Editor: vào lobby (host) → thấy 2 cột; bấm từng swatch → **preview tank đổi sprite + đổi tên + ring vàng nhảy theo**; chữ status 1 dòng trên cùng, không đè slot list.
- START label hiện "cần ≥2" khi <2 ready (Editor cho solo nên minStart=1).
- **Commit:** `M4: 2-column lobby with live tank preview + START>=2 + fix status overlap`

---

## M5 — Hardening per-player tank: ownSlot strict (chống "2 người 1 tank")

**File:** `Assets/Scripts/Multiplayer/LanClientView.cs`
**Kết quả:** Không còn đoán slot khi LocalClientId chưa gán; chờ init resend → mỗi client đúng slot riêng (quan trọng khi ≥3 người).

### Bước 5.1 — Thay default + bỏ fallback ép slot

Trong `OnReceiveInitWorld`.

=== CŨ ===
```csharp
        int ownSlot = Mathf.Min(1, playerCount - 1); // safe default (slot 1 for 2-player)
        int[] variantIndices = new int[playerCount];
        for (int i = 0; i < playerCount; i++)
        {
            reader.ReadValueSafe(out ulong ownerClientId);
            reader.ReadValueSafe(out variantIndices[i]);
            if (ownerClientId == myClientId) ownSlot = i;
        }

        // In host mode, slot 0 belongs to the host. If LocalClientId was not yet assigned
        // when the init message arrived, the loop above can incorrectly resolve a remote
        // client to slot 0. Dedicated server mode has no local host player, so slot 0 is valid.
        if (!LanSessionManager.IsDedicatedServer && ownSlot == 0 && playerCount > 1)
        {
            ownSlot = Mathf.Min(1, playerCount - 1);
            Debug.LogWarning($"[LanClientView] ownSlot resolved to 0 (LocalClientId race?) — forced to {ownSlot}");
        }
        Debug.Log($"[LanClientView] OnReceiveInitWorld pc={playerCount} ec={enemyCount} ownSlot={ownSlot}");
```
=== MỚI ===
```csharp
        int ownSlot = -1; // strict: chỉ set khi có ownerClientId trùng LocalClientId của mình
        int[] variantIndices = new int[playerCount];
        for (int i = 0; i < playerCount; i++)
        {
            reader.ReadValueSafe(out ulong ownerClientId);
            reader.ReadValueSafe(out variantIndices[i]);
            if (ownerClientId == myClientId) ownSlot = i;
        }

        Debug.Log($"[LanClientView] OnReceiveInitWorld pc={playerCount} ec={enemyCount} ownSlot={ownSlot} myId={myClientId}");

        // LocalClientId race: clientId của mình chưa có trong map ownership. KHÔNG đoán slot
        // (đoán làm 2 client cùng rơi vào slot 0/1). Bỏ qua, chờ init resend kế tiếp — server
        // tự gửi lại init mỗi khi số bridge link tăng hoặc variant đổi (LanGameCoordinator).
        if (ownSlot < 0)
        {
            Debug.LogWarning("[LanClientView] ownSlot chưa resolve (LocalClientId race) — chờ init resend.");
            return;
        }
```

### Kiểm tra M5
- `refresh_unity` + `read_console` → 0 lỗi.
- Test 2 và 3 người: mỗi người 1 tank riêng, camera bám tank mình, không ai trùng. Xem log: mỗi client `ownSlot` khác nhau, không có cảnh báo lặp.
- **Commit:** `M5: strict ownSlot resolution (wait for resend, no slot guessing)`

---

## M6 — Build, deploy, verify production

> Không sửa code. Theo trình tự dưới.

### Bước 6.1 — Build WebGL + deploy
- Theo quy trình hiện có (CI / `BuildWebClient.cs` / serve). KHÔNG sửa các file build/font.

### Bước 6.2 — Test 2 tab (đúng sơ đồ mục 2)
```text
Tab A (owner): Menu → MULTIPLAYER INTERNET → HOST GAME → Alpha-32 → A*
   → vào Waiting Lobby; KHÔNG bị hỏi chọn tank ở bước trước
   → header lobby ghi "MULTIPLAYER INTERNET"
   → bấm các swatch: preview tank đổi màu; chọn 1 màu
   → bấm COPY LINK → dán kiểm tra link
Tab B (ẩn danh): Menu → MULTIPLAYER INTERNET → JOIN ROOM → gõ mã phòng (hoặc dán link)
   → vào ĐÚNG lobby của A (thấy 2 slot)
A & B chọn 2 màu KHÁC nhau → READY
A bấm START (chỉ bật khi cả 2 ready)
   → cả hai vào game: 2 tank riêng, camera đúng, body đúng màu, enemy = 12 (=6×2)
```
### Bước 6.3 — Scale + regression
- Mở thêm 1–2 tab (3–4 người): mỗi người 1 tank, enemy = 6×n, không trùng slot.
- Lặp với Chantry + PIBT. Kiểm font/UI & readiness gate vẫn nguyên.

### Checklist "Done" cuối
- [ ] Menu hiện **MULTIPLAYER INTERNET**, có HOST/JOIN; JOIN có ô nhập mã.
- [ ] Luồng Internet **không còn bước chọn tank trước map/mode**; chọn tank **1 lần** trong lobby.
- [ ] Lobby 2 cột, **preview tank đổi màu theo swatch**, không đè chữ.
- [ ] **COPY LINK chạy trên production HTTP**.
- [ ] START chỉ owner, bật khi **≥2 ready**.
- [ ] 2–8 người: **mỗi người 1 tank riêng**, đúng màu, enemy = 6×n; không còn "2 người 1 tank".
- [ ] Font/UI WebGL + readiness gate giữ nguyên.

---

## PHỤ LỤC — Bảng tổng hợp & thứ tự an toàn

| MS | File chính | Loại | Compile-check sau |
|----|-----------|------|-------------------|
| M1 | MenuViewBootstrap.cs | Edit | (làm chung với M2) |
| M2 | LanLobbyController.cs | Edit | ✅ phải 0 lỗi |
| M3 | WebClipboard.jslib, WebClipboard.cs (mới), LanLobbyController.cs | Write+Edit | ✅ |
| M4 | LanLobbyController.cs | Edit (thay cả hàm BuildLobby) | ✅ |
| M5 | LanClientView.cs | Edit | ✅ |
| M6 | — | Build/Test | — |

> **Lưu ý quan trọng:** M1 gọi `LanLobbyController.ShowJoinPrompt` (tạo ở M2). Nên **làm M1 và M2 liền nhau** rồi mới compile-check, tránh báo lỗi "method not found" giữa chừng.

> Nếu bất kỳ bước `Edit` nào báo "không tìm thấy đoạn CŨ": mở file bằng `Read`, copy đúng đoạn hiện tại (kể cả khoảng trắng), rồi Edit lại. KHÔNG đoán.
