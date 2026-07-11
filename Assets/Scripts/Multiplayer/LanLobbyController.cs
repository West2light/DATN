using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// LAN lobby overlay.  Call  LanLobbyController.Show(mapFile, algorithm)  from the menu.
/// </summary>
public class LanLobbyController : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    private static LanLobbyController _instance;

    // ── Config ────────────────────────────────────────────────────────────────
    private const ushort GamePort   = 7777;
    private const int    MaxPlayers = 8;

    // ── Palette ───────────────────────────────────────────────────────────────
    private static readonly Color Bg          = new Color(0f,    0f,    0f,    0.90f);
    private static readonly Color Panel       = new Color(0.08f, 0.10f, 0.13f, 1f);
    private static readonly Color PanelLight  = new Color(0.11f, 0.14f, 0.18f, 1f);
    private static readonly Color Gold        = new Color(1.00f, 0.82f, 0.22f, 1f);
    private static readonly Color Blue        = new Color(0.18f, 0.48f, 0.90f, 1f);
    private static readonly Color Green       = new Color(0.10f, 0.52f, 0.26f, 1f);
    private static readonly Color Slate       = new Color(0.18f, 0.20f, 0.25f, 1f);
    private static readonly Color Danger      = new Color(0.38f, 0.08f, 0.08f, 1f);
    private static readonly Color White       = Color.white;
    private static readonly Color Muted       = new Color(0.50f, 0.55f, 0.62f, 1f);
    private static readonly Color BlueTint    = new Color(0.65f, 0.82f, 1.00f, 1f);
    private static readonly Color Sep         = new Color(1f, 1f, 1f, 0.07f);

    // ── Panel dimensions ──────────────────────────────────────────────────────
    // Header   : 0    → 72   (gold bar + title + map info + separator)
    // Content  : 72   → 292  (status + ip box + player list)
    // Separator: 292
    // Footer   : bottom 188px (3 button rows + cancel)
    // Total height: 480
    private const float PW    = 680f;   // panel width (wider for the 2-column lobby)
    private const float PH    = 480f;   // panel height
    private const float PadX  = 22f;
    private float       FullW => PW - PadX * 2f;

    // ── State ─────────────────────────────────────────────────────────────────
    private enum Screen { Choose, Hosting, Joining, Lobby }

    private string     _mapFile, _algorithm;
    private int        _enemyMultiplier = 3;
    private LanDiscovery _discovery;
    private GameObject _root, _panel;
    private int        _hostRetryCount;

    // Content refs
    private Text       _statusTxt;
    private GameObject _ipBox;
    private Text       _ipVal;
    private Text       _playersTxt;

    // Footer refs (hosted as overlapping pairs at same Y)
    private Button     _btnCancel;     // Always visible; moves up for non-owner lobby.
    private Button     _btnHost;       // Choose screen
    private Button     _btnStart;      // Hosting screen (same Y as _btnHost)
    private Button     _btnJoin;       // Choose screen
    private GameObject _joinRow;       // Joining screen (same Y as _btnJoin): input+connect
    private InputField _ipInput;

    // Waiting-lobby refs (internet client flow)
    private GameObject _lobbyBox;       // content container with slot list + tank picker + code
    private Text       _lobbySlotsTxt;  // multi-line player/slot list
    private Text       _lobbyCodeTxt;   // room code (client) / shareable LAN IP (host)
    private Text       _copyLabel;      // COPY button label: "COPY LINK" (client) / "COPY IP" (host)
    private Button     _btnReadyLobby;  // y2: toggle local Ready
    private Button     _btnStartLobby;  // y1: owner-only START (RequestStartServerRpc)
    private Text       _btnReadyLabel, _btnStartLabel;
    private readonly List<Image> _variantSwatches = new List<Image>();
    private readonly List<GameObject> _variantRings = new List<GameObject>();
    private Image      _lobbyTankPreview;
    private Text       _lobbyTankName;
    private int        _shownVariant = -1;
    private string     _joinRegistryUrl = string.Empty;
    private bool       _localReady;
    private bool       _refreshingLobby;
    private static Sprite[] _variantSpriteCache;

    // Display colors approximating the 5 unlocked tank body variants (blue/red/green/dark/sand)
    private static readonly Color[] VariantColors =
    {
        new Color(0.22f, 0.48f, 0.90f), new Color(0.85f, 0.27f, 0.27f),
        new Color(0.26f, 0.70f, 0.36f), new Color(0.40f, 0.42f, 0.48f),
        new Color(0.82f, 0.71f, 0.42f),
    };

    private readonly List<string> _clients = new List<string>();
    private string _pendingIp;
    private string _shareIp = string.Empty;   // "ip:port" the LAN host shares (shown in the lobby UI)
    private string _autoJoinTarget;
    private bool   _connected;   // true between OnClientConnected and OnClientDisconnected
    private Screen _screen = Screen.Choose;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void JsClearInviteUrl();
#endif

    // ── Entry point ───────────────────────────────────────────────────────────

    /// <summary>
    /// Shut down any live NetworkManager, destroy all bridge GameObjects immediately,
    /// and deactivate LAN session state.  Safe to call from any exit path; re-entrant
    /// calls after the first are no-ops (guarded by LanSessionManager.IsActive).
    /// </summary>
    public static void CleanupSession()
    {
        // Guard: only the first call does real work.  When the server shuts down, the
        // client receives OnNetworkDisconnect AND ReturnToMenuAfterDelay fires — both
        // paths call CleanupSession, so we must not double-Shutdown.
        if (!LanSessionManager.IsActive) return;
        LanSessionManager.Deactivate();  // mark inactive before touching NGO so any
                                          // re-entrant call from NGO callbacks bails out.

        // Stop the 30 Hz sync coroutine before Shutdown so it cannot fire
        // one more tick and call SendNamedMessageToAll on a shut-down NM.
        LanGameCoordinator.Instance?.StopSync();

        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            if (nm.IsListening)
                nm.Shutdown();

            // Use DestroyImmediate for bridge GOs so they are gone BEFORE the deferred
            // Destroy(nm) fires at end-of-frame.  If NM is destroyed first, NGO's
            // internal NM.OnDestroy() may traverse the still-alive bridge NetworkObjects
            // and throw MissingReferenceException.  Destroying bridges synchronously
            // (while NM is still alive) prevents that race entirely.
            foreach (var b in UnityEngine.Object.FindObjectsByType<LanNetworkBridge>(FindObjectsSortMode.None))
                if (b != null) UnityEngine.Object.DestroyImmediate(b.gameObject);

            UnityEngine.Object.Destroy(nm.gameObject);
        }
        else
        {
            // NM already gone — clean up any orphaned bridge GOs that slipped through.
            foreach (var b in UnityEngine.Object.FindObjectsByType<LanNetworkBridge>(FindObjectsSortMode.None))
                if (b != null) UnityEngine.Object.DestroyImmediate(b.gameObject);
        }
    }

    public static void Show(string mapFile, string algorithm, int enemyMultiplier = 3)
    {
        ShowInternal(mapFile, algorithm, string.Empty, enemyMultiplier);
    }

    public static void ShowAndJoin(string mapFile, string algorithm, string joinTarget, int enemyMultiplier = 3)
    {
        ShowInternal(mapFile, algorithm, joinTarget, enemyMultiplier);
    }

    public static void ShowJoinPrompt(string mapFile, string algorithm, string registryUrl)
    {
        if (_instance != null) { Destroy(_instance.gameObject); _instance = null; }
        CleanupSession();

        var go = new GameObject("LanLobbyController");
        DontDestroyOnLoad(go);
        _instance                  = go.AddComponent<LanLobbyController>();
        _instance._mapFile         = mapFile;
        _instance._algorithm       = algorithm;
        _instance._enemyMultiplier = 3;
        _instance._autoJoinTarget  = string.Empty;
        _instance._joinRegistryUrl = registryUrl ?? string.Empty;
        _instance.Build();
        _instance.OpenJoinScreen();
    }

    private void OpenJoinScreen()
    {
        if (!EnsureNetworkManager()) return;
        LanSessionManager.ActivateClient(_mapFile, _algorithm, _enemyMultiplier);
        SwitchTo(Screen.Joining);
#if !UNITY_WEBGL
        // Desktop/LAN: start scanning for a host on the local network immediately, so the
        // "JOIN ROOM" menu button auto-fills the host IP without the user having to detour
        // through HOST GAME + map select first. WebGL has no LAN, so it stays code/link only.
        StartAutoDiscover();
#endif
    }

    private static void ShowInternal(string mapFile, string algorithm, string joinTarget, int enemyMultiplier)
    {
        if (_instance != null) { Destroy(_instance.gameObject); _instance = null; }
        CleanupSession();

        var go = new GameObject("LanLobbyController");
        DontDestroyOnLoad(go);
        _instance                = go.AddComponent<LanLobbyController>();
        _instance._mapFile       = mapFile;
        _instance._algorithm     = algorithm;
        _instance._enemyMultiplier = LanSessionManager.NormalizeEnemyMultiplier(enemyMultiplier);
        _instance._autoJoinTarget = joinTarget ?? string.Empty;
        _instance.Build();
#if UNITY_WEBGL && !UNITY_EDITOR
        if (string.IsNullOrWhiteSpace(_instance._autoJoinTarget))
            _instance.SwitchTo(Screen.Joining);
#else
        _instance.SwitchTo(Screen.Choose);
#endif
        if (!string.IsNullOrWhiteSpace(_instance._autoJoinTarget))
            _instance.BeginAutoJoin();
    }

    // ── Build the whole overlay ───────────────────────────────────────────────

    private void Build()
    {
        int L = LayerMask.NameToLayer("UI");

        // Root canvas
        _root = new GameObject("LanOverlay");
        DontDestroyOnLoad(_root);
        _root.layer = L;
        var cv = _root.AddComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay;
        cv.sortingOrder = 300;
        var sc = _root.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1280f, 720f);
        _root.AddComponent<GraphicRaycaster>();

        // Backdrop
        var dim = Mk(_root, "Dim", L);
        Stretch(dim); dim.AddComponent<Image>().color = Bg;

        // Panel
        _panel = Mk(_root, "Panel", L);
        var pRt = _panel.GetComponent<RectTransform>();
        pRt.anchorMin = pRt.anchorMax = new Vector2(0.5f, 0.5f);
        pRt.pivot = new Vector2(0.5f, 0.5f);
        pRt.anchoredPosition = Vector2.zero;
        pRt.sizeDelta = new Vector2(PW, PH);
        _panel.AddComponent<Image>().color = Panel;

        BuildHeader(L);
        BuildContent(L);
        BuildFooter(L);
        BuildLobby(L);
    }

    // ── Header ────────────────────────────────────────────────────────────────

    private void BuildHeader(int L)
    {
        // Gold accent bar (top edge)
        var bar = Mk(_panel, "Bar", L);
        var bRt = bar.GetComponent<RectTransform>();
        bRt.anchorMin = new Vector2(0f, 1f); bRt.anchorMax = new Vector2(1f, 1f);
        bRt.pivot = Vector2.one; bRt.anchoredPosition = Vector2.zero;
        bRt.sizeDelta = new Vector2(0f, 5f);
        bar.AddComponent<Image>().color = Gold;

        // Title
        string title =
#if UNITY_WEBGL && !UNITY_EDITOR
            "MULTIPLAYER  INTERNET";
#else
            "MULTIPLAYER  LAN";
#endif
        TLbl(_panel, "Title", title,
            22, FontStyle.Bold, Gold, new Vector2(0f, -16f), new Vector2(FullW, 28f));

        // Map · algorithm line
        string map = System.IO.Path.GetFileNameWithoutExtension(_mapFile);
        TLbl(_panel, "Sub", $"{map}  ·  {_algorithm}  ·  enemies ×{_enemyMultiplier} per player  ·  up to {MaxPlayers} players",
            12, FontStyle.Normal, Muted, new Vector2(0f, -48f), new Vector2(FullW, 18f));

        HSep(-72f, L);
    }

    // ── Content ───────────────────────────────────────────────────────────────

    private void BuildContent(int L)
    {
        // Status text
        var sGo = Mk(_panel, "Status", L);
        var sRt = sGo.GetComponent<RectTransform>();
        sRt.anchorMin = new Vector2(0f, 1f); sRt.anchorMax = new Vector2(1f, 1f);
        sRt.pivot = new Vector2(0.5f, 1f);
        sRt.anchoredPosition = new Vector2(0f, -88f);
        sRt.sizeDelta = new Vector2(-PadX * 2f, 56f);
        _statusTxt = sGo.AddComponent<Text>();
        _statusTxt.font = F(); _statusTxt.fontSize = 14;
        _statusTxt.color = White; _statusTxt.alignment = TextAnchor.MiddleCenter;

        // IP box (host-only)
        _ipBox = Mk(_panel, "IpBox", L);
        var ibRt = _ipBox.GetComponent<RectTransform>();
        ibRt.anchorMin = new Vector2(0f, 1f); ibRt.anchorMax = new Vector2(1f, 1f);
        ibRt.pivot = new Vector2(0.5f, 1f);
        ibRt.anchoredPosition = new Vector2(0f, -154f);
        ibRt.sizeDelta = new Vector2(-PadX * 2f, 60f);
        _ipBox.AddComponent<Image>().color = new Color(0.06f, 0.14f, 0.26f, 1f);
        _ipBox.SetActive(false);

        // IP box border accent (left edge)
        var accent = Mk(_ipBox, "Accent", L);
        var aRt = accent.GetComponent<RectTransform>();
        aRt.anchorMin = new Vector2(0f, 0f); aRt.anchorMax = new Vector2(0f, 1f);
        aRt.pivot = new Vector2(0f, 0.5f);
        aRt.anchoredPosition = Vector2.zero; aRt.sizeDelta = new Vector2(4f, 0f);
        accent.AddComponent<Image>().color = Gold;

        TLbl(_ipBox, "IpLbl",
            "Your IP — share it with other players:",
            11, FontStyle.Normal, Muted, new Vector2(0f, -6f), new Vector2(FullW - 16f, 18f));

        var ivGo = Mk(_ipBox, "IpVal", L);
        var ivRt = ivGo.GetComponent<RectTransform>();
        ivRt.anchorMin = new Vector2(0f, 0f); ivRt.anchorMax = new Vector2(1f, 0f);
        ivRt.pivot = new Vector2(0.5f, 0f);
        ivRt.anchoredPosition = new Vector2(0f, 8f); ivRt.sizeDelta = new Vector2(-16f, 26f);
        _ipVal = ivGo.AddComponent<Text>();
        _ipVal.font = F(); _ipVal.fontSize = 20; _ipVal.fontStyle = FontStyle.Bold;
        _ipVal.color = Gold; _ipVal.alignment = TextAnchor.MiddleCenter;

        // Player list (host-only)
        var plGo = Mk(_panel, "Players", L);
        var plRt = plGo.GetComponent<RectTransform>();
        plRt.anchorMin = new Vector2(0f, 1f); plRt.anchorMax = new Vector2(1f, 1f);
        plRt.pivot = new Vector2(0.5f, 1f);
        plRt.anchoredPosition = new Vector2(0f, -224f);
        plRt.sizeDelta = new Vector2(-PadX * 2f, 60f);
        _playersTxt = plGo.AddComponent<Text>();
        _playersTxt.font = F(); _playersTxt.fontSize = 13;
        _playersTxt.color = BlueTint; _playersTxt.alignment = TextAnchor.UpperLeft;
        plGo.SetActive(false);

        HSep(-292f, L);
    }

    // ── Footer ────────────────────────────────────────────────────────────────
    //
    //  Layout from bottom (all anchored to panel bottom-center):
    //
    //  y= 14  h=36   [CANCEL]                   ← always visible
    //  y= 58  h=52   [HOST GAME]  or  [START GAME]   ← toggled
    //  y=118  h=44   [JOIN (nhập IP)]  or  [IP row]  ← toggled

    private void BuildFooter(int L)
    {
        const float BH = 36f;   // cancel height
        const float PH1 = 52f;  // host / start height
        const float PH2 = 44f;  // join height
        const float Gap = 8f;

        float y0 = 14f;                    // cancel bottom
        float y1 = y0 + BH + Gap;         // 58
        float y2 = y1 + PH1 + Gap;        // 118

        // CANCEL — always visible, subtle
        _btnCancel = BtnFull("BtnCancel", "CANCEL", Danger, new Color(1f, 0.55f, 0.55f),
            y0, BH, Close);

        // HOST GAME — Choose screen
        _btnHost = BtnFull("BtnHost", "●  HOST GAME", Blue, White,
            y1, PH1, DoHost);

        // START GAME — Hosting screen (same Y, toggled with _btnHost)
        _btnStart = BtnFull("BtnStart", "▶  START GAME", Green, White,
            y1, PH1, DoStartGame);

        // JOIN — Choose screen
        _btnJoin = BtnFull("BtnJoin", "→  JOIN  (enter host IP)", Slate, White,
            y2, PH2, ShowJoinRow);

        // JOIN ROW — Joining screen (same Y, replaces _btnJoin)
        _joinRow = BuildJoinRow(L, y2, PH2);

        // START GAME (owner, Lobby screen) — same Y as host/start, toggled by screen.
        _btnStartLobby = BtnFull("BtnStartLobby", "▶  START GAME", Green, White,
            y1, PH1, DoLobbyStart);
        _btnStartLabel = _btnStartLobby.GetComponentInChildren<Text>();
        SetVis(_btnStartLobby, false);

        // READY toggle (Lobby screen) — same Y as join.
        _btnReadyLobby = BtnFull("BtnReadyLobby", "✔  READY", Blue, White,
            y2, PH2, DoToggleReady);
        _btnReadyLabel = _btnReadyLobby.GetComponentInChildren<Text>();
        SetVis(_btnReadyLobby, false);
    }

    // ── Waiting lobby (internet client flow) ───────────────────────────────────

    private void BuildLobby(int L)
    {
        _lobbyBox = Mk(_panel, "LobbyBox", L);
        var rt = _lobbyBox.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -100f);
        rt.sizeDelta = new Vector2(-PadX * 2f, 210f);
        _lobbyBox.SetActive(false);

        // Left column: tank preview.
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

        // Right column: slots, swatches, room code, and copy action.
        var slots = Mk(_lobbyBox, "Slots", L);
        var sRt = slots.GetComponent<RectTransform>();
        sRt.anchorMin = new Vector2(0.38f, 1f); sRt.anchorMax = new Vector2(1f, 1f);
        sRt.pivot = new Vector2(0.5f, 1f);
        sRt.anchoredPosition = new Vector2(0f, 0f); sRt.sizeDelta = new Vector2(0f, 96f);
        _lobbySlotsTxt = slots.AddComponent<Text>();
        _lobbySlotsTxt.font = F(); _lobbySlotsTxt.fontSize = 13;
        _lobbySlotsTxt.color = BlueTint; _lobbySlotsTxt.alignment = TextAnchor.UpperLeft;
        _lobbySlotsTxt.text = "Loading player list…";

        var pickLbl = Mk(_lobbyBox, "PickLbl", L);
        var plRt = pickLbl.GetComponent<RectTransform>();
        plRt.anchorMin = new Vector2(0.38f, 1f); plRt.anchorMax = new Vector2(1f, 1f);
        plRt.pivot = new Vector2(0f, 1f);
        plRt.anchoredPosition = new Vector2(0f, -100f); plRt.sizeDelta = new Vector2(0f, 16f);
        var plTxt = pickLbl.AddComponent<Text>();
        plTxt.font = F(); plTxt.fontSize = 11; plTxt.color = Muted;
        plTxt.alignment = TextAnchor.MiddleLeft; plTxt.text = "Choose tank color:";

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
        _copyLabel = copy.GetComponentInChildren<Text>();

        EnsureVariantSpritesLoaded();
        UpdateLobbyPreview(LanSessionManager.LocalVariantIndex);
    }

    private static readonly string[] VariantBodyFiles =
    { "tankBody_blue", "tankBody_red", "tankBody_green", "tankBody_dark", "tankBody_sand" };

    private void UpdateLobbyPreview(int idx)
    {
        idx = Mathf.Clamp(idx, 0, VariantColors.Length - 1);
        if (idx == _shownVariant)
            return;

        _shownVariant = idx;
        if (_lobbyTankPreview != null)
        {
            Sprite spr = _variantSpriteCache != null && idx < _variantSpriteCache.Length
                ? _variantSpriteCache[idx]
                : null;
            _lobbyTankPreview.sprite = spr;
            _lobbyTankPreview.color = spr != null ? Color.white : VariantColors[idx];
        }
        if (_lobbyTankName != null) _lobbyTankName.text = VariantDisplayName(idx);
        for (int i = 0; i < _variantRings.Count; i++)
            if (_variantRings[i] != null) _variantRings[i].SetActive(i == idx);
    }

    private static void EnsureVariantSpritesLoaded()
    {
        if (_variantSpriteCache != null)
            return;

        _variantSpriteCache = new Sprite[VariantBodyFiles.Length];
        for (int i = 0; i < VariantBodyFiles.Length; i++)
            _variantSpriteCache[i] = LoadVariantSprite(i);
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

    private GameObject BuildJoinRow(int L, float yFromBottom, float rowH)
    {
        var row = Mk(_panel, "JoinRow", L);
        var rt  = row.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, yFromBottom);
        rt.sizeDelta = new Vector2(FullW, rowH);
        row.SetActive(false);

        // IP InputField (left ~70%)
        var ifGo = Mk(row, "IpInput", L);
        var ifRt = ifGo.GetComponent<RectTransform>();
        ifRt.anchorMin = new Vector2(0f, 0f); ifRt.anchorMax = new Vector2(0.70f, 1f);
        ifRt.offsetMin = new Vector2(2f, 0f); ifRt.offsetMax = Vector2.zero;
        ifGo.AddComponent<Image>().color = new Color(0.10f, 0.12f, 0.16f, 1f);
        _ipInput = ifGo.AddComponent<InputField>();
        var ifTxt = Mk(ifGo, "T", L);
        var ifTxtRt = ifTxt.GetComponent<RectTransform>();
        ifTxtRt.anchorMin = Vector2.zero; ifTxtRt.anchorMax = Vector2.one;
        ifTxtRt.offsetMin = new Vector2(10f, 0f); ifTxtRt.offsetMax = Vector2.zero;
        var ifTxtC = ifTxt.AddComponent<Text>();
        ifTxtC.font = F(); ifTxtC.fontSize = 14; ifTxtC.color = White;
        ifTxtC.alignment = TextAnchor.MiddleLeft;
        _ipInput.textComponent = ifTxtC;
        _ipInput.text = "";

        // Placeholder
        var phGo = Mk(ifGo, "Ph", L);
        var phRt = phGo.GetComponent<RectTransform>();
        phRt.anchorMin = Vector2.zero; phRt.anchorMax = Vector2.one;
        phRt.offsetMin = new Vector2(10f, 0f); phRt.offsetMax = Vector2.zero;
        var phTxt = phGo.AddComponent<Text>();
        phTxt.font = F(); phTxt.fontSize = 13; phTxt.color = Muted;
        phTxt.alignment = TextAnchor.MiddleLeft;
        phTxt.text = "Enter an IP, IP:port, invite link, or session code";
        phTxt.fontStyle = FontStyle.Italic;
        _ipInput.placeholder = phTxt;

        // KẾT NỐI button (right ~30%)
        var cnGo = Mk(row, "Connect", L);
        var cnRt = cnGo.GetComponent<RectTransform>();
        cnRt.anchorMin = new Vector2(0.71f, 0f); cnRt.anchorMax = new Vector2(1f, 1f);
        cnRt.offsetMin = new Vector2(2f, 0f); cnRt.offsetMax = Vector2.zero;
        cnGo.AddComponent<Image>().color = Blue;
        var cnBtn = cnGo.AddComponent<Button>(); cnBtn.targetGraphic = cnGo.GetComponent<Image>();
        cnBtn.onClick.AddListener(() => DoConnect(_ipInput.text.Trim()));
        LblFill(cnGo, "JOIN", 13, FontStyle.Bold, White, L);

        return row;
    }

    private void PositionJoinRowForJoinScreen()
    {
        if (_joinRow == null)
            return;

        var rt = _joinRow.GetComponent<RectTransform>();
        if (rt == null)
            return;

        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 66f);
        rt.sizeDelta = new Vector2(FullW, 56f);
    }

    // ── Screen transitions ────────────────────────────────────────────────────

    // Turn every screen-specific widget off; each SwitchTo case re-enables its own.
    // CANCEL is intentionally not touched (always visible).
    private void HideAllScreenWidgets()
    {
        PositionCancelButton(compact: false);
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
        _joinRow?.SetActive(false);
        _ipBox?.SetActive(false);
        _playersTxt?.gameObject.SetActive(false);
        _lobbyBox?.SetActive(false);
        CancelInvoke(nameof(RefreshLobby));
    }

    private void SwitchTo(Screen s)
    {
        _screen = s;
        switch (s)
        {
            case Screen.Choose:
                SwitchToChoose();
                break;
            case Screen.Hosting:
                HideAllScreenWidgets();
                // Reuse the Internet lobby UI (tank preview + name + colour picker + slot
                // list) so the host gets the SAME tank-selection experience as joiners.
                // The room-code slot is repurposed to show the host's shareable LAN IP.
                if (_statusTxt != null)
                {
                    var hrt = _statusTxt.GetComponent<RectTransform>();
                    hrt.anchoredPosition = new Vector2(0f, -78f);
                    hrt.sizeDelta = new Vector2(-PadX * 2f, 18f);
                    _statusTxt.fontSize = 12;
                }
                SetStatus("Waiting for players…  ·  choose your tank", Gold);
                if (string.IsNullOrEmpty(_shareIp))
                    _shareIp = $"{GetLocalIP()}:{GamePort}";
                _lobbyBox?.SetActive(true);
                if (_copyLabel != null) _copyLabel.text = "COPY IP";
                SetVis(_btnStart, true);   // host START is immediate (owner-driven, no ready gate)
                SetVis(_btnJoin,  true);
                InvokeRepeating(nameof(RefreshLobby), 0f, 0.3f);
                break;
            case Screen.Joining:
                HideAllScreenWidgets();
                SetStatus("Enter an IP, IP:port, invite link, or session code:", Muted);
                if (_statusTxt != null)
                {
                    var srt = _statusTxt.GetComponent<RectTransform>();
                    srt.anchoredPosition = new Vector2(0f, -132f);
                    srt.sizeDelta = new Vector2(-PadX * 2f, 76f);
                    _statusTxt.fontSize = 15;
                }
                PositionJoinRowForJoinScreen();
                SetStatus("Paste an invite link or enter a room code\ne.g. 43E98F or https://luminx.io.vn/play?session=...", Muted);
                _joinRow.SetActive(true);
                break;
            case Screen.Lobby:
                HideAllScreenWidgets();
                if (_statusTxt != null)
                {
                    var srt = _statusTxt.GetComponent<RectTransform>();
                    srt.anchoredPosition = new Vector2(0f, -78f);
                    srt.sizeDelta = new Vector2(-PadX * 2f, 18f);
                    _statusTxt.fontSize = 12;
                }
                SetStatus("Lobby — choose a tank and mark READY", Green);
                if (_copyLabel != null) _copyLabel.text = "COPY LINK";
                _lobbyBox?.SetActive(true);
                SetVis(_btnReadyLobby, true);
                // START (owner-only) visibility is managed each tick by RefreshLobby.
                InvokeRepeating(nameof(RefreshLobby), 0f, 0.3f);
                break;
        }
    }

    private void SwitchToChoose()
    {
        // Stop auto-discovery if user goes back from Joining screen
        if (_discovery != null) { _discovery.StopListening(); }
        HideAllScreenWidgets();
        SetStatus("Choose your role:", Muted);
#if UNITY_WEBGL && !UNITY_EDITOR
        SetVis(_btnHost,  false);
#else
        SetVis(_btnHost,  true);
#endif
        SetVis(_btnJoin,  true);
    }

    // ── Lobby refresh + actions ────────────────────────────────────────────────

    private void RefreshLobby()
    {
        if (_lobbySlotsTxt == null) return;

        var bridges = new List<LanNetworkBridge>(
            FindObjectsByType<LanNetworkBridge>(FindObjectsSortMode.None));
        bridges.RemoveAll(b => b == null || !b.IsSpawned);
        bridges.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));

        ulong ownerId = LanNetworkBridge.ResolveOwnerClientId();
        var   local   = LanNetworkBridge.Local;
        ulong localId = local != null ? local.OwnerClientId : ulong.MaxValue;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Players  ({bridges.Count}/{MaxPlayers})");
        int slot = 0, readyCount = 0;
        foreach (var b in bridges)
        {
            bool isOwner = b.OwnerClientId == ownerId;
            bool isLocal = b.OwnerClientId == localId;
            bool ready   = b.Ready.Value;
            if (ready) readyCount++;
            string star = isOwner ? "★" : "  ";
            string st   = ready ? "✓ ready" : "… not ready";
            string tag  = isOwner ? " [owner]" : "";
            string you  = isLocal ? " (you)" : "";
            sb.AppendLine($"{star} #{slot}  {VariantName(b.VariantIndex.Value)}   {st}{tag}{you}");
            slot++;
        }
        _lobbySlotsTxt.text = sb.ToString();

        if (_lobbyCodeTxt != null)
        {
            if (_screen == Screen.Hosting)
            {
                if (string.IsNullOrEmpty(_shareIp)) _shareIp = $"{GetLocalIP()}:{GamePort}";
                _lobbyCodeTxt.text = $"Your IP:  {_shareIp}";
            }
            else
            {
                string code = LanSessionManager.SessionCode;
                _lobbyCodeTxt.text = string.IsNullOrEmpty(code) ? "" : $"Room code: {code}";
            }
        }

        // Host (Screen.Hosting): gate the immediate START on a minimum player count and
        // reflect it on the button (bridges.Count includes the host, so 2 = host + 1 client).
        if (_screen == Screen.Hosting && _btnStart != null)
        {
            int minStart =
#if UNITY_EDITOR
                1;
#else
                2;
#endif
            bool canStart = bridges.Count >= minStart;
            _btnStart.interactable = canStart;
            var startLbl = _btnStart.GetComponentInChildren<Text>();
            if (startLbl != null)
                startLbl.text = canStart
                    ? "▶  START GAME"
                    : $"START  (need ≥{minStart} players · {bridges.Count})";
        }

        if (local != null && _btnReadyLabel != null)
        {
            _localReady = local.Ready.Value;
            _btnReadyLabel.text = _localReady ? "✖  CANCEL READY" : "✔  READY";
        }

        if (local != null && local.VariantIndex.Value != _shownVariant)
            UpdateLobbyPreview(local.VariantIndex.Value);

        // Ready/owner-START gating applies only to the Internet lobby (Screen.Lobby).
        // The LAN host (Screen.Hosting) uses the immediate _btnStart instead, so leave
        // its footer untouched here.
        if (_screen == Screen.Lobby)
        {
            // START: only the room owner sees it; enabled when everyone is ready.
            bool isRoomOwner = local != null && ownerId != ulong.MaxValue && localId == ownerId;
            SetVis(_btnStartLobby, isRoomOwner);
            PositionCancelButton(compact: !isRoomOwner);
            if (isRoomOwner && _btnStartLobby != null)
            {
                int minStart =
#if UNITY_EDITOR
                    1;
#else
                    2;
#endif
                bool allReady = bridges.Count >= minStart && readyCount == bridges.Count;
                _btnStartLobby.interactable = allReady;
                if (_btnStartLabel != null)
                    _btnStartLabel.text = allReady
                        ? "▶  START GAME"
                        : $"START  ({readyCount}/{bridges.Count} · need ≥{minStart})";
            }
        }
    }

    private void PickVariant(int idx)
    {
        idx = Mathf.Clamp(idx, 0, VariantColors.Length - 1);
        LanSessionManager.LocalVariantIndex = idx;
        PlayerPrefs.SetInt("MenuTankVariant", idx); PlayerPrefs.Save();
        LanNetworkBridge.Local?.SubmitVariant(idx);
        UpdateLobbyPreview(idx);
    }

    private void DoToggleReady()
    {
        var local = LanNetworkBridge.Local;
        if (local == null) { SetStatus("Joining room… please try again shortly.", Muted); return; }
        local.SubmitReady(!local.Ready.Value);
    }

    private void DoLobbyStart()
    {
        LanNetworkBridge.Local?.RequestStartServerRpc();
    }

    private void CopyInvite()
    {
        // LAN host: copy the shareable IP instead of an internet invite link.
        if (_screen == Screen.Hosting)
        {
            if (string.IsNullOrEmpty(_shareIp)) return;
            WebClipboard.Copy(_shareIp);
            SetStatus("IP copied — share it with players!", Green);
            return;
        }
        string link = BuildInviteLink();
        if (string.IsNullOrEmpty(link)) return;
        WebClipboard.Copy(link);
        SetStatus("Invite link copied!", Green);
    }

    private static string BuildInviteLink()
    {
        string code = LanSessionManager.SessionCode;
        if (string.IsNullOrWhiteSpace(code)) return "";
        string reg = LanSessionManager.RegistryUrl ?? "";
        // Web base = registry host without the registry port (the WebGL site is on port 80).
        string webBase = reg;
        int scheme = reg.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            int hostStart = scheme + 3;
            int portColon = reg.IndexOf(':', hostStart);
            if (portColon >= 0) webBase = reg.Substring(0, portColon);
        }
        if (string.IsNullOrWhiteSpace(webBase)) return code;
        return $"{webBase}/play?session={code}";
    }

    private static string VariantName(int i)
    {
        switch (Mathf.Clamp(i, 0, 4))
        {
            case 0:  return "[blue]";
            case 1:  return "[red]";
            case 2:  return "[green]";
            case 3:  return "[gray]";
            default: return "[sand]";
        }
    }

    private void ShowJoinRow()
    {
        if (!EnsureNetworkManager()) return;
        LanSessionManager.ActivateClient(_mapFile, _algorithm, _enemyMultiplier);
        SwitchTo(Screen.Joining);
        StartAutoDiscover();
    }

    private void BeginAutoJoin()
    {
        if (string.IsNullOrWhiteSpace(_autoJoinTarget))
            return;

        // Don't restart a join that is already connected or in progress — re-entry would
        // shut down the live connection and feed the reconnect loop.
        if (_connected || (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient))
            return;

        if (!EnsureNetworkManager())
            return;

        LanSessionManager.ActivateClient(_mapFile, _algorithm, _enemyMultiplier);
        SwitchTo(Screen.Joining);
        if (_ipInput != null)
            _ipInput.text = _autoJoinTarget;
        DoConnect(_autoJoinTarget);
    }

    private void StartAutoDiscover()
    {
        if (_discovery != null) { _discovery.Stop(); Destroy(_discovery); }
        _discovery = gameObject.AddComponent<LanDiscovery>();
        SetStatus("Searching for a host on the LAN…", Muted);
        _discovery.OnHostFound += ip =>
        {
            CancelInvoke(nameof(OnDiscoverTimeout));
            if (_ipInput != null) _ipInput.text = ip;
            SetStatus($"Host found: {ip} — press JOIN", new Color(0.3f, 0.9f, 0.4f));
            _discovery.StopListening();
        };
        _discovery.StartListening();

        // Don't leave the user staring at "Searching…" forever. After a few seconds,
        // switch to a hint to enter the IP manually — but keep listening, so a host that
        // starts later still auto-fills the field.
        CancelInvoke(nameof(OnDiscoverTimeout));
        Invoke(nameof(OnDiscoverTimeout), 7f);
    }

    private void OnDiscoverTimeout()
    {
        if (_connected || _screen != Screen.Joining) return;
        SetStatus(
            "No host found on the LAN yet.\n" +
            "Enter the host IP shown on the host's screen, then press JOIN.\n" +
            "(This keeps scanning — it will auto-fill when a host appears.)",
            new Color(1f, 0.75f, 0.35f));
    }

    // ── Host flow ─────────────────────────────────────────────────────────────

    private void DoHost()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        SetStatus("WebGL cannot host locally. Create an internet room from the menu.", new Color(1f, 0.6f, 0.3f));
        return;
#else
        if (!EnsureNetworkManager()) return;
        if (NetworkManager.Singleton.IsListening)
        {
            SetStatus("Stopping the previous session…", Muted);
            NetworkManager.Singleton.Shutdown();
            Invoke(nameof(RetryHost), 2f);
            return;
        }

        LanSessionManager.ActivateHost(_mapFile, _algorithm, _enemyMultiplier);
        NetworkManagerFactory.ConfigureConnectionApproval(NetworkManager.Singleton, isServer: true);
        NetworkManager.Singleton.OnClientConnectedCallback  -= OnJoin;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnLeave;
        NetworkManager.Singleton.OnClientConnectedCallback  += OnJoin;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnLeave;

        // Subscribe to transport failure so we can recover from "port already in use".
        NetworkManager.Singleton.OnTransportFailure -= OnHostTransportFailure;
        NetworkManager.Singleton.OnTransportFailure += OnHostTransportFailure;

        // Host listens on 0.0.0.0 (all interfaces) so LAN clients can reach it
        var hostTransport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (hostTransport != null)
        {
            hostTransport.SetConnectionData("0.0.0.0", GamePort);
            hostTransport.UseWebSockets = false;
        }

        // StartHost() returns false if transport fails — don't proceed on failure.
        // OnHostTransportFailure will handle retry; _hostRetryCount must NOT be reset here.
        if (!NetworkManager.Singleton.StartHost()) return;

        var lanIps = GetLanIPv4Candidates();
        string ip = lanIps.Count > 0 ? lanIps[0] : GetLocalIP();
        Debug.Log($"[LAN] Hosting on 0.0.0.0:{GamePort}. Primary LAN IP: {ip}. " +
                  $"All candidates: [{string.Join(", ", lanIps)}]");
        _shareIp = $"{ip}:{GamePort}";
        if (_ipVal != null) _ipVal.text = _shareIp;

        _discovery = gameObject.AddComponent<LanDiscovery>();
        _discovery.StartBroadcasting(GamePort);

        _hostRetryCount = 0;   // reset only on genuine success
        LanSessionManager.PlayerCount = 1;
        SwitchTo(Screen.Hosting);
#endif
    }

    private void OnHostTransportFailure()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnTransportFailure -= OnHostTransportFailure;

        _hostRetryCount++;
        const int MaxRetries = 3;

        if (_hostRetryCount <= MaxRetries)
        {
            SetStatus($"Port {GamePort} is busy — retrying ({_hostRetryCount}/{MaxRetries})…",
                new Color(1f, 0.65f, 0.1f));
            Debug.LogWarning($"[LAN] Transport bind failed (attempt {_hostRetryCount}). Retrying in 3 s.");

            // Destroy the entire NM so the OS closes its socket before we recreate.
            if (NetworkManager.Singleton != null)
                Destroy(NetworkManager.Singleton.gameObject);
            Invoke(nameof(RetryHost), 3f);
        }
        else
        {
            _hostRetryCount = 0;
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.Shutdown();
                Destroy(NetworkManager.Singleton.gameObject);
            }
            SetStatus(
                $"Could not open port {GamePort}.\n" +
                "Another process is already using this port.\n" +
                "Close all running builds or restart Unity, then try again.",
                new Color(1f, 0.3f, 0.3f));
            Debug.LogError($"[LAN] Port {GamePort} blocked after {MaxRetries} retries. Manual fix required.");
        }
    }

    private void OnJoin(ulong id)
    {
        if (id == NetworkManager.Singleton.LocalClientId) return;
        LanSessionManager.PlayerCount = NetworkManager.Singleton.ConnectedClients.Count;
        _clients.Add($"Client {_clients.Count + 2}");
        RefreshPlayers();
        SetStatus($"{LanSessionManager.PlayerCount}/{MaxPlayers} players joined", Green);
    }

    private void OnLeave(ulong id)
    {
        LanSessionManager.PlayerCount = Mathf.Max(1, NetworkManager.Singleton.ConnectedClients.Count);
        RefreshPlayers();
    }

    // ── Join flow ─────────────────────────────────────────────────────────────

    private void RetryHost()
    {
        // DestroyImmediate so EnsureNetworkManager() finds Singleton==null
        // and creates a truly fresh NM — deferred Destroy leaves the old
        // (shut-down) singleton alive until end-of-frame, causing StartHost()
        // to run on a stale NM object.
        if (NetworkManager.Singleton != null)
        {
            if (NetworkManager.Singleton.IsListening) NetworkManager.Singleton.Shutdown();
            DestroyImmediate(NetworkManager.Singleton.gameObject);
        }
        DoHost();
    }
    private void RetryConnect() { if (_pendingIp != null) DoConnect(_pendingIp); }

    private void OnClientConnected(ulong _)
    {
        CancelInvoke(nameof(OnConnectionTimeout));
        // Cancel any pending reconnect and clear the retry token. Otherwise a RetryConnect
        // scheduled by an earlier attempt fires AFTER we are connected, calls Shutdown on the
        // live session, and tears down a perfectly good connection (the recurring
        // "Connected → Disconnected" loop seen in the browser console).
        CancelInvoke(nameof(RetryConnect));
        _pendingIp = null;
        _connected = true;
        SetStatus("Connected! Waiting for the host to start…", Green);
        Debug.Log("[LAN] Connected to host.");
        // When the host loads the game scene, NGO will trigger a scene load on this client.
        // Register so we can close the lobby overlay once the game scene is live.
        SceneManager.sceneLoaded -= OnGameSceneLoaded;
        SceneManager.sceneLoaded += OnGameSceneLoaded;
        // Show the waiting lobby: slot list, tank picker, READY, and (owner-only) START.
        SwitchTo(Screen.Lobby);
    }

    private void OnGameSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Ignore the menu scene and any scene that isn't our LAN game scene
        if (scene.name == "Menu") return;
        SceneManager.sceneLoaded -= OnGameSceneLoaded;
        Debug.Log($"[LAN] Game scene '{scene.name}' loaded on client — closing lobby overlay.");
        // Close the overlay without shutting down the NetworkManager
        CancelInvoke();
        _discovery?.Stop();
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback  -= OnJoin;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnLeave;
            NetworkManager.Singleton.OnClientConnectedCallback  -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
        if (_root != null) Destroy(_root);
        _root = null;
        _instance = null;
        Destroy(gameObject);
    }

    private void OnClientDisconnected(ulong _)
    {
        CancelInvoke(nameof(OnConnectionTimeout));
        _connected = false;
        string reason = NetworkManager.Singleton != null ? NetworkManager.Singleton.DisconnectReason : string.Empty;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            SetStatus($"Connection rejected: {reason}", new Color(1f, 0.4f, 0.4f));
            Debug.LogWarning($"[LAN] Disconnected from host. Reason: {reason}");
            return;
        }
        SetStatus("Connection to the host was lost.", new Color(1f, 0.4f, 0.4f));
        Debug.LogWarning("[LAN] Disconnected from host.");
    }

    private void OnConnectionTimeout()
    {
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsConnectedClient)
        {
            NetworkManager.Singleton.Shutdown();
            SetStatus("Connection timed out.\nCheck the IP address and firewall.", new Color(1f, 0.4f, 0.3f));
            Debug.LogWarning($"[LAN] Connection timeout to {_pendingIp ?? "?"}:{GamePort}");
        }
    }

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

        if (string.IsNullOrWhiteSpace(endpoint.host))
        {
            if (string.IsNullOrWhiteSpace(endpoint.sessionCode))
            {
                SetStatus("Invite link is missing a session code.", new Color(1f, 0.35f, 0.35f));
                return;
            }

            if (string.IsNullOrWhiteSpace(endpoint.registryUrl))
            {
                SetStatus("Registry URL is missing for this invite code.", new Color(1f, 0.35f, 0.35f));
                return;
            }

            SetStatus($"Resolving session {endpoint.sessionCode}...", Muted);
            StartCoroutine(ResolveSessionAndConnect(endpoint));
            return;
        }

        BeginClientConnection(endpoint, ip);
    }

    private IEnumerator ResolveSessionAndConnect(NetworkEndpointConfig endpoint)
    {
        bool finished = false;
        NetworkEndpointConfig resolvedEndpoint = endpoint;
        string resolveError = string.Empty;

        yield return InternetSessionClient.ResolveSession(
            endpoint.registryUrl,
            endpoint.sessionCode,
            cfg =>
            {
                resolvedEndpoint = cfg;
                finished = true;
            },
            error =>
            {
                resolveError = error;
                finished = true;
            });

        if (!finished)
        {
            SetStatus("Registry request did not complete.", new Color(1f, 0.35f, 0.35f));
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(resolveError))
        {
            SetStatus(resolveError, new Color(1f, 0.35f, 0.35f));
            yield break;
        }

        BeginClientConnection(resolvedEndpoint, $"{resolvedEndpoint.host}:{resolvedEndpoint.port}");
    }

    private void BeginClientConnection(NetworkEndpointConfig endpoint, string retryToken)
    {
        // Already connected → never tear down a live session for a duplicate/auto-join call.
        if (_connected || (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient))
            return;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
            _pendingIp = retryToken;
            Invoke(nameof(RetryConnect), 0.5f);
            return;
        }

        if (!EnsureNetworkManager()) return;
        var t = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (t == null) { SetStatus("Transport error!", new Color(1f, 0.3f, 0.3f)); return; }

        endpoint.mapFile = string.IsNullOrWhiteSpace(endpoint.mapFile) ? _mapFile : endpoint.mapFile;
        endpoint.algorithm = string.IsNullOrWhiteSpace(endpoint.algorithm) ? _algorithm : endpoint.algorithm;
        endpoint.maxPlayers = endpoint.maxPlayers <= 0 ? LanSessionManager.MaxPlayers : endpoint.maxPlayers;
        endpoint.enemyMultiplier = LanSessionManager.NormalizeEnemyMultiplier(
            endpoint.enemyMultiplier <= 0 ? _enemyMultiplier : endpoint.enemyMultiplier);

        LanSessionManager.ActivateInternetClient(endpoint);
        t.SetConnectionData(endpoint.host, endpoint.port);
        t.UseEncryption = false;
#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL can only use WebSocket. The retry path re-parses a raw "host:port" string
        // whose transportMode defaults to UDP — forcing WebSocket here prevents the doomed
        // UDP attempts seen alternating in the browser console.
        t.UseWebSockets = true;
        t.UseEncryption = endpoint.secureWebSocket;
        if (t.UseEncryption)
        {
            string secureHost = string.IsNullOrWhiteSpace(endpoint.secureWebSocketHost)
                ? endpoint.host
                : endpoint.secureWebSocketHost;
            t.SetClientSecrets(secureHost, null);
        }
#else
        t.UseWebSockets = endpoint.transportMode == NetworkTransportMode.WebSocket;
#endif
        NetworkManagerFactory.ConfigureConnectionApproval(NetworkManager.Singleton, isServer: false);
        Debug.Log($"[LAN] Connecting to {endpoint.host}:{endpoint.port} via {(endpoint.secureWebSocket ? "wss" : endpoint.transportMode.ToArgumentValue())}");

        NetworkManager.Singleton.OnClientConnectedCallback  -= OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        NetworkManager.Singleton.OnClientConnectedCallback  += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        NetworkManager.Singleton.StartClient();
        SetStatus($"Connecting to {endpoint.host}:{endpoint.port}...", Muted);

        // Timeout: if not connected after 8s, show error
        CancelInvoke(nameof(OnConnectionTimeout));
        Invoke(nameof(OnConnectionTimeout), 8f);
    }

    // ── Start game ────────────────────────────────────────────────────────────

    private void DoStartGame()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        int minStart =
#if UNITY_EDITOR
            1;   // allow solo start in the Editor for quick testing
#else
            2;   // real builds require at least 2 players
#endif
        int players = NetworkManager.Singleton.ConnectedClients.Count;
        if (players < minStart)
        {
            SetStatus($"Need at least {minStart} players to start  ({players}/{minStart}).",
                new Color(1f, 0.75f, 0.35f));
            return;
        }

        _discovery?.Stop();
        LanSessionManager.PlayerCount = players;
        NetworkManager.Singleton.SceneManager.LoadScene(LanSessionManager.GameScene, LoadSceneMode.Single);
        Close();
    }

    // ── NetworkManager ────────────────────────────────────────────────────────

    private bool EnsureNetworkManager()
    {
        NetworkTransportMode defaultTransportMode =
#if UNITY_WEBGL && !UNITY_EDITOR
            NetworkTransportMode.WebSocket;
#else
            NetworkTransportMode.Udp;
#endif
        return NetworkManagerFactory.Ensure("0.0.0.0", GamePort, isServer: false, defaultTransportMode, out _);
    }

    // Virtual/tunnel adapters whose IPv4 other LAN machines cannot reach. Matched against
    // NIC Name + Description (lower-cased). WSL appears as "vEthernet (WSL ...)".
    private static readonly string[] VirtualNicHints =
    {
        "wsl", "hyper-v", "hyperv", "virtual", "vethernet", "vpn",
        "loopback", "vmware", "virtualbox", "docker", "tap", "tunnel"
    };

    /// <summary>
    /// Real LAN IPv4 addresses, best-first: 192.168.* &gt; 10.* &gt; 172.* , with
    /// virtual/WSL/VPN adapters and APIPA (169.254.*) filtered out. May be empty.
    /// </summary>
    private static List<string> GetLanIPv4Candidates()
    {
        var scored = new List<KeyValuePair<int, string>>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    string desc = (nic.Name + " " + nic.Description).ToLowerInvariant();
                    bool isVirtual = false;
                    foreach (var hint in VirtualNicHints)
                        if (desc.Contains(hint)) { isVirtual = true; break; }

                    foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (IPAddress.IsLoopback(ua.Address)) continue;
                        string ip = ua.Address.ToString();
                        if (ip.StartsWith("169.254.")) continue;   // APIPA (no DHCP lease)

                        int score = 0;
                        if      (ip.StartsWith("192.168.")) score += 100;
                        else if (ip.StartsWith("10."))      score += 80;
                        else if (ip.StartsWith("172."))     score += 40;   // often Docker/WSL
                        else                                score += 20;
                        if (isVirtual) score -= 200;                       // push virtual NICs last

                        scored.Add(new KeyValuePair<int, string>(score, ip));
                    }
                }
                catch { /* skip this NIC */ }
            }
        }
        catch (Exception e) { Debug.LogWarning("[LAN] NIC enumerate failed: " + e.Message); }

        scored.Sort((a, b) => b.Key.CompareTo(a.Key));
        var result = new List<string>();
        foreach (var kv in scored)
            if (kv.Value != null && !result.Contains(kv.Value))
                result.Add(kv.Value);
        return result;
    }

    private static string GetLocalIP()
    {
        var candidates = GetLanIPv4Candidates();
        if (candidates.Count > 0) return candidates[0];

        // Fallback: the source IP the OS would use toward a public address. May be a
        // virtual adapter, so it is only used when NIC enumeration yields nothing.
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            s.Connect("8.8.8.8", 65530);
            var addr = ((IPEndPoint)s.LocalEndPoint)?.Address;
            if (addr != null && !IPAddress.IsLoopback(addr)) return addr.ToString();
        }
        catch { /* offline / no route */ }
        return "127.0.0.1";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RefreshPlayers()
    {
        if (_playersTxt == null) return;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Players  ({LanSessionManager.PlayerCount}/{MaxPlayers})");
        sb.AppendLine("  ●  Host  (you)");
        foreach (var n in _clients) sb.AppendLine($"  ●  {n}");
        _playersTxt.text = sb.ToString();
    }

    private void SetStatus(string msg, Color col)
    {
        if (_statusTxt == null) return;
        _statusTxt.text = msg; _statusTxt.color = col;
    }

    private static void SetVis(Button b, bool v) { if (b) b.gameObject.SetActive(v); }

    private void Close()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        ReturnToInternetEntryAfterCancel();
        return;
#else
        if (_screen == Screen.Lobby || !string.IsNullOrWhiteSpace(_autoJoinTarget))
        {
            ReturnToInternetEntryAfterCancel();
            return;
        }

        CancelInvoke();
        SceneManager.sceneLoaded -= OnGameSceneLoaded;
        _discovery?.Stop();
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback  -= OnJoin;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnLeave;
            NetworkManager.Singleton.OnClientConnectedCallback  -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
        if (_root != null) Destroy(_root);
        _root = null;
        _instance = null;
        Destroy(gameObject);
#endif
    }

    private void ReturnToInternetEntryAfterCancel()
    {
        CancelInvoke();
        SceneManager.sceneLoaded -= OnGameSceneLoaded;
        _discovery?.Stop();
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback  -= OnJoin;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnLeave;
            NetworkManager.Singleton.OnClientConnectedCallback  -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        CleanupSession();
        _connected = false;
        _pendingIp = null;
        _autoJoinTarget = string.Empty;
        ClearInviteUrl();

        if (_root != null) Destroy(_root);
        _root = null;
        _instance = null;
        Destroy(gameObject);
        MenuViewBootstrap.ShowInternetEntry();
    }

    private void PositionCancelButton(bool compact)
    {
        if (_btnCancel == null)
            return;

        var rt = _btnCancel.GetComponent<RectTransform>();
        if (rt == null)
            return;

        rt.anchoredPosition = new Vector2(0f, compact ? 74f : 14f);
    }

    private static void ClearInviteUrl()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        try { JsClearInviteUrl(); }
        catch (Exception e) { Debug.LogWarning($"[LAN] Could not clear invite URL: {e.Message}"); }
#endif
    }

    // ── UI builder micro-helpers ──────────────────────────────────────────────

    private static Font F() => UiFontProvider.GetDefaultFont();

    private static GameObject Mk(GameObject p, string name, int L)
    {
        var go = new GameObject(name);
        go.layer = L;
        go.AddComponent<RectTransform>();
        go.transform.SetParent(p.transform, false);
        return go;
    }

    private static void Stretch(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    // Top-anchored label (anchor = (0.5, 1), pivot = (0.5, 1))
    private void TLbl(GameObject p, string name, string txt,
        int sz, FontStyle st, Color col, Vector2 pos, Vector2 sd)
    {
        int L = p.layer;
        var go = Mk(p, name, L);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos; rt.sizeDelta = sd;
        var t = go.AddComponent<Text>();
        t.text = txt; t.font = F(); t.fontSize = sz; t.fontStyle = st;
        t.color = col; t.alignment = TextAnchor.MiddleCenter;
    }

    // 1-pixel horizontal separator, top-anchored
    private void HSep(float yFromTop, int L)
    {
        var go = Mk(_panel, "Sep", L);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, yFromTop); rt.sizeDelta = new Vector2(0f, 1f);
        go.AddComponent<Image>().color = Sep;
    }

    // Full-width button, bottom-anchored
    private Button BtnFull(string name, string label, Color bg, Color txtCol,
        float yFromBottom, float h, UnityEngine.Events.UnityAction onClick)
    {
        int L = _panel.layer;
        var go = Mk(_panel, name, L);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, yFromBottom);
        rt.sizeDelta = new Vector2(FullW, h);
        var img = go.AddComponent<Image>(); img.color = bg;
        var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
        var cb = btn.colors;
        cb.normalColor = Color.white; cb.highlightedColor = new Color(1.12f, 1.12f, 1.12f);
        cb.pressedColor = new Color(0.78f, 0.78f, 0.78f);
        cb.disabledColor = new Color(0.35f, 0.35f, 0.35f, 0.5f);
        btn.colors = cb;
        btn.onClick.AddListener(onClick);
        LblFill(go, label, 15, FontStyle.Bold, txtCol, L);
        return btn;
    }

    // Fill a GO with a centered text label
    private static void LblFill(GameObject p, string txt, int sz,
        FontStyle st, Color col, int L)
    {
        var go = Mk(p, "Lbl", L);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var t = go.AddComponent<Text>();
        t.text = txt; t.font = F(); t.fontSize = sz; t.fontStyle = st;
        t.color = col; t.alignment = TextAnchor.MiddleCenter;
    }
}
