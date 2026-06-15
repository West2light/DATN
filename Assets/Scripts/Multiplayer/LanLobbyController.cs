using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// LAN lobby overlay.
///
/// Host path:  LanLobbyController.ShowAsHost(mapFile, algo)
///             → creates room immediately (relay or LAN)
///             → transitions to WaitingLobby
///
/// Client path: LanLobbyController.ShowAsJoin()
///             → shows Joining screen (room code / IP input)
///             → transitions to WaitingLobby after connect
/// </summary>
public class LanLobbyController : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    private static LanLobbyController _instance;

    // ── Config ────────────────────────────────────────────────────────────────
    private const ushort GamePort   = 7777;
    private const int    MaxPlayers = 4;

    // ── Palette ───────────────────────────────────────────────────────────────
    private static readonly Color Bg         = new Color(0f,    0f,    0f,    0.90f);
    private static readonly Color Panel      = new Color(0.08f, 0.10f, 0.13f, 1f);
    private static readonly Color Gold       = new Color(1.00f, 0.82f, 0.22f, 1f);
    private static readonly Color Blue       = new Color(0.18f, 0.48f, 0.90f, 1f);
    private static readonly Color Green      = new Color(0.10f, 0.52f, 0.26f, 1f);
    private static readonly Color Slate      = new Color(0.18f, 0.20f, 0.25f, 1f);
    private static readonly Color Danger     = new Color(0.38f, 0.08f, 0.08f, 1f);
    private static readonly Color White      = Color.white;
    private static readonly Color Muted      = new Color(0.50f, 0.55f, 0.62f, 1f);
    private static readonly Color BlueTint   = new Color(0.65f, 0.82f, 1.00f, 1f);
    private static readonly Color Sep        = new Color(1f, 1f, 1f, 0.07f);
    private static readonly Color ReadyGreen = new Color(0.12f, 0.60f, 0.30f, 1f);

    // Tank body colors — must match MenuViewBootstrap.Variants order
    private static readonly Color[] VariantColors =
    {
        new Color(0.22f, 0.50f, 0.90f, 1f), // 0 Blue
        new Color(0.88f, 0.22f, 0.22f, 1f), // 1 Red
        new Color(0.25f, 0.70f, 0.30f, 1f), // 2 Green
        new Color(0.28f, 0.30f, 0.35f, 1f), // 3 Dark
        new Color(0.82f, 0.72f, 0.38f, 1f), // 4 Sand
    };
    private static readonly string[] VariantLabels = { "Blue", "Red", "Green", "Dark", "Sand" };

    // ── Panel dimensions ──────────────────────────────────────────────────────
    private const float PW   = 520f;
    private const float PH   = 480f;
    private const float PadX = 22f;
    private float       FullW => PW - PadX * 2f;

    // ── Screens ───────────────────────────────────────────────────────────────
    private enum Screen { HostCreating, Joining, WaitingLobby }

    // ── Session state ─────────────────────────────────────────────────────────
    private string       _mapFile, _algorithm;
    private bool         _isHost;
    private LanDiscovery _discovery;
    private GameObject   _root, _panel;
    private int          _hostRetryCount;
    private string       _roomCode;
    private string       _pendingIp;

    // ── HostCreating screen refs ──────────────────────────────────────────────
    private GameObject _hostCreatingContainer;
    private Text       _hostCreatingStatus;

    // ── Joining screen refs ───────────────────────────────────────────────────
    private GameObject _joiningContainer;
    private Text       _joiningStatus;
    private InputField _joinInput;

    // ── WaitingLobby screen refs ──────────────────────────────────────────────
    private GameObject _lobbyContainer;
    private Text       _lobbyRoomCodeVal;
    private Button[]   _variantBtns;
    private Image[]    _variantBtnImgs;
    private Text[]     _slotTexts;

    // ── Footer refs ───────────────────────────────────────────────────────────
    private Button _btnStart;
    private Button _btnReady;
    private bool   _localReady;

    // ── Entry points ──────────────────────────────────────────────────────────

    /// <summary>Called from the LAN Map Select screen when the host picks a map.</summary>
    public static void ShowAsHost(string mapFile, string algorithm)
    {
        if (_instance != null) { Destroy(_instance.gameObject); _instance = null; }
        CleanupSession();

        var go = new GameObject("LanLobbyController");
        DontDestroyOnLoad(go);
        _instance            = go.AddComponent<LanLobbyController>();
        _instance._mapFile   = mapFile;
        _instance._algorithm = algorithm;
        _instance._isHost    = true;
        _instance.Build();
        _instance.DoHost();
    }

    /// <summary>Called from the main menu JOIN button.</summary>
    public static void ShowAsJoin()
    {
        if (_instance != null) { Destroy(_instance.gameObject); _instance = null; }
        CleanupSession();

        var go = new GameObject("LanLobbyController");
        DontDestroyOnLoad(go);
        _instance            = go.AddComponent<LanLobbyController>();
        _instance._mapFile   = "";
        _instance._algorithm = "AStar";
        _instance._isHost    = false;
        _instance.Build();
        _instance.SwitchTo(Screen.Joining);
    }

    /// <summary>
    /// Shuts down any live NetworkManager session and destroys bridge GameObjects.
    /// Safe to call from any exit path; re-entrant calls are no-ops.
    /// </summary>
    public static void CleanupSession()
    {
        if (!LanSessionManager.IsActive) return;
        LanSessionManager.Deactivate();

        LanGameCoordinator.Instance?.StopSync();

        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            if (nm.IsListening) nm.Shutdown();
            foreach (var b in UnityEngine.Object.FindObjectsByType<LanNetworkBridge>(FindObjectsSortMode.None))
                if (b != null) UnityEngine.Object.DestroyImmediate(b.gameObject);
            UnityEngine.Object.Destroy(nm.gameObject);
        }
        else
        {
            foreach (var b in UnityEngine.Object.FindObjectsByType<LanNetworkBridge>(FindObjectsSortMode.None))
                if (b != null) UnityEngine.Object.DestroyImmediate(b.gameObject);
        }
    }

    // ── Build ─────────────────────────────────────────────────────────────────

    private void Build()
    {
        int L = LayerMask.NameToLayer("UI");

        _root = new GameObject("LanOverlay");
        DontDestroyOnLoad(_root);
        _root.layer = L;
        var cv = _root.AddComponent<Canvas>();
        cv.renderMode   = RenderMode.ScreenSpaceOverlay;
        cv.sortingOrder = 300;
        var sc = _root.AddComponent<CanvasScaler>();
        sc.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1280f, 720f);
        _root.AddComponent<GraphicRaycaster>();

        var dim = Mk(_root, "Dim", L);
        Stretch(dim); dim.AddComponent<Image>().color = Bg;

        _panel = Mk(_root, "Panel", L);
        var pRt = _panel.GetComponent<RectTransform>();
        pRt.anchorMin = pRt.anchorMax = new Vector2(0.5f, 0.5f);
        pRt.pivot     = new Vector2(0.5f, 0.5f);
        pRt.anchoredPosition = Vector2.zero;
        pRt.sizeDelta        = new Vector2(PW, PH);
        _panel.AddComponent<Image>().color = Panel;

        BuildHeader(L);
        BuildHostCreatingContent(L);
        BuildJoiningContent(L);
        BuildWaitingLobbyContent(L);
        BuildFooter(L);
    }

    // ── Header (always visible) ───────────────────────────────────────────────

    private void BuildHeader(int L)
    {
        // Gold accent bar
        var bar = Mk(_panel, "Bar", L);
        var bRt = bar.GetComponent<RectTransform>();
        bRt.anchorMin = new Vector2(0f, 1f); bRt.anchorMax = new Vector2(1f, 1f);
        bRt.pivot = Vector2.one; bRt.anchoredPosition = Vector2.zero;
        bRt.sizeDelta = new Vector2(0f, 5f);
        bar.AddComponent<Image>().color = Gold;

        TLbl(_panel, "Title", "MULTIPLAYER  LAN",
            22, FontStyle.Bold, Gold, new Vector2(0f, -16f), new Vector2(FullW, 28f));

        // Sub-line: map info for host, join prompt for client
        string sub = _isHost
            ? $"{System.IO.Path.GetFileNameWithoutExtension(_mapFile)}  ·  {_algorithm}  ·  tối đa {MaxPlayers} người"
            : "Nhập Room Code hoặc IP để tham gia phòng";
        TLbl(_panel, "Sub", sub, 12, FontStyle.Normal, Muted,
            new Vector2(0f, -48f), new Vector2(FullW, 18f));

        HSep(-72f, L);
    }

    // ── HostCreating screen (shown while async room setup runs) ───────────────

    private void BuildHostCreatingContent(int L)
    {
        _hostCreatingContainer = MkFillPanel("HostCreating", L);
        _hostCreatingContainer.SetActive(false);

        // Big centred status message
        var sGo = Mk(_hostCreatingContainer, "Status", L);
        var sRt = sGo.GetComponent<RectTransform>();
        sRt.anchorMin = new Vector2(0f, 0.5f); sRt.anchorMax = new Vector2(1f, 0.5f);
        sRt.pivot = new Vector2(0.5f, 0.5f);
        sRt.anchoredPosition = new Vector2(0f, 20f);
        sRt.sizeDelta = new Vector2(-PadX * 2f, 80f);
        _hostCreatingStatus = sGo.AddComponent<Text>();
        _hostCreatingStatus.font = F(); _hostCreatingStatus.fontSize = 16;
        _hostCreatingStatus.color = Muted; _hostCreatingStatus.alignment = TextAnchor.MiddleCenter;
        _hostCreatingStatus.horizontalOverflow = HorizontalWrapMode.Wrap;
        _hostCreatingStatus.text = "Đang tạo phòng…";
    }

    // ── Joining screen (Room Code / IP input) ─────────────────────────────────
    //
    //  y from content top (-72):
    //   -88  status text  (h=36)
    //  -138  input box    (h=52)  — prominent, full-width
    //  -210  auto-discover hint  (h=20)
    //  -240  connect button (h=52)

    private void BuildJoiningContent(int L)
    {
        _joiningContainer = MkFillPanel("Joining", L);
        _joiningContainer.SetActive(false);

        // Status
        var stGo = Mk(_joiningContainer, "Status", L);
        var stRt = stGo.GetComponent<RectTransform>();
        stRt.anchorMin = new Vector2(0f, 1f); stRt.anchorMax = new Vector2(1f, 1f);
        stRt.pivot = new Vector2(0.5f, 1f);
        stRt.anchoredPosition = new Vector2(0f, -88f);
        stRt.sizeDelta = new Vector2(-PadX * 2f, 36f);
        _joiningStatus = stGo.AddComponent<Text>();
        _joiningStatus.font = F(); _joiningStatus.fontSize = 13;
        _joiningStatus.color = Muted; _joiningStatus.alignment = TextAnchor.MiddleCenter;

        // Input box
        var ifBox = Mk(_joiningContainer, "InputBox", L);
        var ibRt  = ifBox.GetComponent<RectTransform>();
        ibRt.anchorMin = new Vector2(0f, 1f); ibRt.anchorMax = new Vector2(1f, 1f);
        ibRt.pivot = new Vector2(0.5f, 1f);
        ibRt.anchoredPosition = new Vector2(0f, -134f);
        ibRt.sizeDelta = new Vector2(-PadX * 2f, 52f);
        ifBox.AddComponent<Image>().color = new Color(0.10f, 0.13f, 0.17f, 1f);

        // Input field inside box
        var ifGo = Mk(ifBox, "Input", L);
        var ifRt = ifGo.GetComponent<RectTransform>();
        ifRt.anchorMin = Vector2.zero; ifRt.anchorMax = Vector2.one;
        ifRt.offsetMin = new Vector2(14f, 0f); ifRt.offsetMax = new Vector2(-14f, 0f);
        _joinInput = ifGo.AddComponent<InputField>();

        var ifTxtGo = Mk(ifGo, "T", L);
        var ifTxtRt = ifTxtGo.GetComponent<RectTransform>();
        ifTxtRt.anchorMin = Vector2.zero; ifTxtRt.anchorMax = Vector2.one;
        ifTxtRt.offsetMin = ifTxtRt.offsetMax = Vector2.zero;
        var ifTxtC = ifTxtGo.AddComponent<Text>();
        ifTxtC.font = F(); ifTxtC.fontSize = 18; ifTxtC.color = White;
        ifTxtC.alignment = TextAnchor.MiddleLeft;
        _joinInput.textComponent = ifTxtC;

        var phGo = Mk(ifGo, "Ph", L);
        var phRt = phGo.GetComponent<RectTransform>();
        phRt.anchorMin = Vector2.zero; phRt.anchorMax = Vector2.one;
        phRt.offsetMin = phRt.offsetMax = Vector2.zero;
        var phTxt = phGo.AddComponent<Text>();
        phTxt.font = F(); phTxt.fontSize = 14; phTxt.color = Muted;
        phTxt.alignment = TextAnchor.MiddleLeft;
        phTxt.text = "Room Code hoặc IP  (vd: AB3X7K / 192.168.1.5)";
        phTxt.fontStyle = FontStyle.Italic;
        _joinInput.placeholder = phTxt;

        // Auto-discover hint
        var discGo = Mk(_joiningContainer, "Hint", L);
        var dRt    = discGo.GetComponent<RectTransform>();
        dRt.anchorMin = new Vector2(0f, 1f); dRt.anchorMax = new Vector2(1f, 1f);
        dRt.pivot = new Vector2(0.5f, 1f);
        dRt.anchoredPosition = new Vector2(0f, -198f);
        dRt.sizeDelta = new Vector2(-PadX * 2f, 22f);
        var dTxt = discGo.AddComponent<Text>();
        dTxt.font = F(); dTxt.fontSize = 11; dTxt.color = Muted;
        dTxt.alignment = TextAnchor.MiddleCenter;
        dTxt.text = "Đang tự động tìm host trong mạng LAN…";

        // Connect button
        var cnGo = Mk(_joiningContainer, "BtnConnect", L);
        var cnRt = cnGo.GetComponent<RectTransform>();
        cnRt.anchorMin = new Vector2(0f, 1f); cnRt.anchorMax = new Vector2(1f, 1f);
        cnRt.pivot = new Vector2(0.5f, 1f);
        cnRt.anchoredPosition = new Vector2(0f, -232f);
        cnRt.sizeDelta = new Vector2(-PadX * 2f, 52f);
        cnGo.AddComponent<Image>().color = Blue;
        var cnBtn = cnGo.AddComponent<Button>(); cnBtn.targetGraphic = cnGo.GetComponent<Image>();
        cnBtn.onClick.AddListener(() => DoConnect(_joinInput.text.Trim()));
        LblFill(cnGo, "→  KẾT NỐI", 16, FontStyle.Bold, White, L);

        HSep(-72f - 300f, L);   // divider just above footer
    }

    // ── WaitingLobby screen ───────────────────────────────────────────────────
    //
    //  y from panel top:
    //   -82   Room Code box   (h=50)
    //  -142   Variant label   (h=16)
    //  -162   Variant buttons (h=38)
    //  -210   Sep
    //  -220   Players header  (h=16)
    //  -238 .. -297  4 slot rows (14px + 2px gap each)

    private void BuildWaitingLobbyContent(int L)
    {
        _lobbyContainer = MkFillPanel("Lobby", L);
        _lobbyContainer.SetActive(false);

        // Room Code box
        var rcBox = Mk(_lobbyContainer, "RcBox", L);
        var rcRt  = rcBox.GetComponent<RectTransform>();
        rcRt.anchorMin = new Vector2(0f, 1f); rcRt.anchorMax = new Vector2(1f, 1f);
        rcRt.pivot = new Vector2(0.5f, 1f);
        rcRt.anchoredPosition = new Vector2(0f, -82f);
        rcRt.sizeDelta = new Vector2(-PadX * 2f, 50f);
        rcBox.AddComponent<Image>().color = new Color(0.06f, 0.14f, 0.26f, 1f);

        var rcAccent = Mk(rcBox, "Accent", L);
        var raRt = rcAccent.GetComponent<RectTransform>();
        raRt.anchorMin = new Vector2(0f, 0f); raRt.anchorMax = new Vector2(0f, 1f);
        raRt.pivot = new Vector2(0f, 0.5f);
        raRt.anchoredPosition = Vector2.zero; raRt.sizeDelta = new Vector2(4f, 0f);
        rcAccent.AddComponent<Image>().color = Gold;

        var rcLbl = Mk(rcBox, "Lbl", L);
        var rlRt  = rcLbl.GetComponent<RectTransform>();
        rlRt.anchorMin = new Vector2(0f, 1f); rlRt.anchorMax = new Vector2(1f, 1f);
        rlRt.pivot = new Vector2(0.5f, 1f);
        rlRt.anchoredPosition = new Vector2(0f, -4f); rlRt.sizeDelta = new Vector2(-12f, 14f);
        var rlTxt = rcLbl.AddComponent<Text>();
        rlTxt.font = F(); rlTxt.fontSize = 11; rlTxt.color = Muted;
        rlTxt.alignment = TextAnchor.MiddleCenter;
        rlTxt.text = "Room Code  —  chia sẻ để người khác join:";

        var rcVal = Mk(rcBox, "Val", L);
        var rvRt  = rcVal.GetComponent<RectTransform>();
        rvRt.anchorMin = new Vector2(0f, 0f); rvRt.anchorMax = new Vector2(1f, 0f);
        rvRt.pivot = new Vector2(0.5f, 0f);
        rvRt.anchoredPosition = new Vector2(0f, 4f); rvRt.sizeDelta = new Vector2(-12f, 28f);
        _lobbyRoomCodeVal = rcVal.AddComponent<Text>();
        _lobbyRoomCodeVal.font = F(); _lobbyRoomCodeVal.fontSize = 22;
        _lobbyRoomCodeVal.fontStyle = FontStyle.Bold;
        _lobbyRoomCodeVal.color = Gold; _lobbyRoomCodeVal.alignment = TextAnchor.MiddleCenter;
        _lobbyRoomCodeVal.verticalOverflow = VerticalWrapMode.Overflow;

        // Variant label
        var bodyLbl = Mk(_lobbyContainer, "BodyLbl", L);
        var blRt    = bodyLbl.GetComponent<RectTransform>();
        blRt.anchorMin = new Vector2(0f, 1f); blRt.anchorMax = new Vector2(1f, 1f);
        blRt.pivot = new Vector2(0.5f, 1f);
        blRt.anchoredPosition = new Vector2(0f, -140f); blRt.sizeDelta = new Vector2(-PadX * 2f, 16f);
        var blTxt = bodyLbl.AddComponent<Text>();
        blTxt.font = F(); blTxt.fontSize = 12; blTxt.color = Muted;
        blTxt.alignment = TextAnchor.MiddleLeft;
        blTxt.text = "Chọn màu xe tăng:";

        // Variant swatches
        int   varCount = VariantColors.Length;
        float btnW     = (FullW - (varCount - 1) * 6f) / varCount;
        _variantBtns    = new Button[varCount];
        _variantBtnImgs = new Image[varCount];
        for (int i = 0; i < varCount; i++)
        {
            int idx = i;
            var vGo = Mk(_lobbyContainer, $"V{i}", L);
            var vRt = vGo.GetComponent<RectTransform>();
            vRt.anchorMin = new Vector2(0f, 1f); vRt.anchorMax = new Vector2(0f, 1f);
            vRt.pivot = new Vector2(0f, 1f);
            vRt.anchoredPosition = new Vector2(PadX + i * (btnW + 6f), -160f);
            vRt.sizeDelta = new Vector2(btnW, 38f);
            var vImg = vGo.AddComponent<Image>(); vImg.color = VariantColors[i];
            _variantBtnImgs[i] = vImg;
            var vBtn = vGo.AddComponent<Button>(); vBtn.targetGraphic = vImg;
            vBtn.onClick.AddListener(() => OnPickVariant(idx));
            _variantBtns[i] = vBtn;
            var nGo = Mk(vGo, "N", L);
            var nRt = nGo.GetComponent<RectTransform>();
            nRt.anchorMin = Vector2.zero; nRt.anchorMax = Vector2.one;
            nRt.offsetMin = nRt.offsetMax = Vector2.zero;
            var nTxt = nGo.AddComponent<Text>();
            nTxt.font = F(); nTxt.fontSize = 11; nTxt.fontStyle = FontStyle.Bold;
            nTxt.color = White; nTxt.alignment = TextAnchor.MiddleCenter;
            nTxt.text = VariantLabels[i];
        }

        // Sep
        {
            var go = Mk(_lobbyContainer, "Sep", L);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -208f); rt.sizeDelta = new Vector2(0f, 1f);
            go.AddComponent<Image>().color = Sep;
        }

        // Players header
        var plHdr = Mk(_lobbyContainer, "PlHdr", L);
        var phRt  = plHdr.GetComponent<RectTransform>();
        phRt.anchorMin = new Vector2(0f, 1f); phRt.anchorMax = new Vector2(1f, 1f);
        phRt.pivot = new Vector2(0.5f, 1f);
        phRt.anchoredPosition = new Vector2(0f, -218f); phRt.sizeDelta = new Vector2(-PadX * 2f, 16f);
        var phTxt = plHdr.AddComponent<Text>();
        phTxt.font = F(); phTxt.fontSize = 12; phTxt.color = Muted;
        phTxt.alignment = TextAnchor.MiddleLeft;
        phTxt.text = $"Người chơi  (0/{MaxPlayers})";

        // Slot rows
        _slotTexts = new Text[MaxPlayers];
        for (int i = 0; i < MaxPlayers; i++)
        {
            var slGo = Mk(_lobbyContainer, $"Slot{i}", L);
            var slRt = slGo.GetComponent<RectTransform>();
            slRt.anchorMin = new Vector2(0f, 1f); slRt.anchorMax = new Vector2(1f, 1f);
            slRt.pivot = new Vector2(0.5f, 1f);
            slRt.anchoredPosition = new Vector2(0f, -238f - i * 15f);
            slRt.sizeDelta = new Vector2(-PadX * 2f, 14f);
            _slotTexts[i] = slGo.AddComponent<Text>();
            _slotTexts[i].font = F(); _slotTexts[i].fontSize = 12;
            _slotTexts[i].color = Muted; _slotTexts[i].alignment = TextAnchor.MiddleLeft;
            _slotTexts[i].text = $"  Slot {i + 1}  —";
        }
    }

    // ── Footer ────────────────────────────────────────────────────────────────
    //
    //  y= 14  h=36   [CANCEL]             always
    //  y= 58  h=44   [START GAME]         WaitingLobby host only
    //  y=110  h=44   [READY]              WaitingLobby

    private void BuildFooter(int L)
    {
        BtnFull("BtnCancel", "CANCEL", Danger, new Color(1f, 0.55f, 0.55f), 14f, 36f, Close);

        _btnStart = BtnFull("BtnStart", "▶  START GAME", Green, White, 58f, 44f, DoStartGame);
        _btnStart.gameObject.SetActive(false);

        _btnReady = BtnFull("BtnReady", "◉  READY", Slate, White, 110f, 44f, DoToggleReady);
        _btnReady.gameObject.SetActive(false);
    }

    // ── Screen transitions ────────────────────────────────────────────────────

    private void SwitchTo(Screen s)
    {
        _hostCreatingContainer?.SetActive(s == Screen.HostCreating);
        _joiningContainer?.SetActive(s == Screen.Joining);
        _lobbyContainer?.SetActive(s == Screen.WaitingLobby);

        SetVis(_btnReady, s == Screen.WaitingLobby);
        if (s == Screen.WaitingLobby)
        {
            bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
            SetVis(_btnStart, isHost);
            if (isHost) _btnStart.interactable = false;
        }
        else
        {
            SetVis(_btnStart, false);
        }

        if (s == Screen.Joining)
        {
            _joiningStatus.text = "Đang tự động tìm host trong mạng LAN…";
            _joiningStatus.color = Muted;
            LanSessionManager.ActivateClient(_mapFile, _algorithm);
            EnsureNetworkManager();
            StartAutoDiscover();
        }

        if (s == Screen.WaitingLobby)
        {
            if (_lobbyRoomCodeVal != null)
                _lobbyRoomCodeVal.text = string.IsNullOrEmpty(_roomCode) ? "—" : _roomCode;
            HighlightVariant(LanSessionManager.LocalVariantIndex);
            RefreshLobbySlots();
        }
    }

    // ── Host flow ─────────────────────────────────────────────────────────────

    private async void DoHost()
    {
        if (!EnsureNetworkManager()) return;
        if (NetworkManager.Singleton.IsListening)
        {
            SetStatus("Đang dừng session cũ…", Muted);
            NetworkManager.Singleton.Shutdown();
            Invoke(nameof(RetryHost), 2f);
            return;
        }

        SwitchTo(Screen.HostCreating);
        LanSessionManager.ActivateHost(_mapFile, _algorithm);

        NetworkManager.Singleton.OnClientConnectedCallback  -= OnClientJoined;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientLeft;
        NetworkManager.Singleton.OnClientConnectedCallback  += OnClientJoined;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientLeft;
        NetworkManager.Singleton.OnTransportFailure         -= OnHostTransportFailure;
        NetworkManager.Singleton.OnTransportFailure         += OnHostTransportFailure;

        bool   relayOk  = false;
        string roomCode = null;
        SetStatus("Đang kết nối Unity Relay…", Muted);

        try
        {
            bool inited = await RelayManager.InitAsync();
            if (inited)
            {
                SetStatus("Đang tạo phòng…", Muted);
                var (allocation, code) = await RelayManager.CreateRoomAsync(MaxPlayers - 1);
                RelayManager.ApplyHostToTransport(allocation);
                roomCode = code;
                relayOk  = true;
                LanSessionManager.UseRelay = true;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Relay] Failed, falling back to LAN: {e.Message}");
            LanSessionManager.UseRelay = false;
        }

        if (!relayOk)
        {
            var tr = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (tr != null) tr.SetConnectionData("0.0.0.0", GamePort);
            SetStatus("Relay không khả dụng — dùng LAN IP…", Muted);
        }

        if (!NetworkManager.Singleton.StartHost()) return;

        if (relayOk)
        {
            _roomCode = roomCode ?? "ERROR";
            Debug.Log($"[Relay] Hosting with Room Code: {_roomCode}");
        }
        else
        {
            string ip = GetLocalIP();
            _roomCode = $"{ip}:{GamePort}";
            Debug.Log($"[LAN] Hosting on 0.0.0.0:{GamePort}  (local IP: {ip})");
            _discovery = gameObject.AddComponent<LanDiscovery>();
            _discovery.StartBroadcasting(GamePort);
        }

        _hostRetryCount = 0;
        _localReady = false;
        LanSessionManager.PlayerCount = 1;
        SwitchTo(Screen.WaitingLobby);
    }

    private void OnHostTransportFailure()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnTransportFailure -= OnHostTransportFailure;

        _hostRetryCount++;
        const int MaxRetries = 3;
        if (_hostRetryCount <= MaxRetries)
        {
            SetStatus($"Port {GamePort} bận — thử lại ({_hostRetryCount}/{MaxRetries})…",
                new Color(1f, 0.65f, 0.1f));
            if (NetworkManager.Singleton != null) Destroy(NetworkManager.Singleton.gameObject);
            Invoke(nameof(RetryHost), 3f);
        }
        else
        {
            _hostRetryCount = 0;
            if (NetworkManager.Singleton != null) { NetworkManager.Singleton.Shutdown(); Destroy(NetworkManager.Singleton.gameObject); }
            SetStatus($"Không thể mở port {GamePort}. Thoát build đang chạy rồi thử lại.",
                new Color(1f, 0.3f, 0.3f));
        }
    }

    private void OnClientJoined(ulong id)
    {
        if (id == NetworkManager.Singleton.LocalClientId) return;
        LanSessionManager.PlayerCount = NetworkManager.Singleton.ConnectedClients.Count;
        RefreshLobbySlots();
    }

    private void OnClientLeft(ulong id)
    {
        LanSessionManager.PlayerCount = Mathf.Max(1, NetworkManager.Singleton.ConnectedClients.Count);
        RefreshLobbySlots();
    }

    // ── Join flow ─────────────────────────────────────────────────────────────

    private void RetryHost()
    {
        if (NetworkManager.Singleton != null)
        {
            if (NetworkManager.Singleton.IsListening) NetworkManager.Singleton.Shutdown();
            DestroyImmediate(NetworkManager.Singleton.gameObject);
        }
        DoHost();
    }
    private void RetryConnect() { if (_pendingIp != null) DoConnect(_pendingIp); }

    private void StartAutoDiscover()
    {
        if (_discovery != null) { _discovery.Stop(); Destroy(_discovery); }
        _discovery = gameObject.AddComponent<LanDiscovery>();
        _discovery.OnHostFound += ip =>
        {
            if (_joinInput != null && string.IsNullOrEmpty(_joinInput.text))
                _joinInput.text = ip;
            SetStatus($"Tìm thấy host: {ip}  —  bấm KẾT NỐI", new Color(0.3f, 0.9f, 0.4f));
            _discovery.StopListening();
        };
        _discovery.StartListening();
    }

    private void DoConnect(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            SetStatus("Nhập Room Code hoặc IP!", new Color(1f, 0.35f, 0.35f));
            return;
        }
        string trimmed = input.Trim();
        if (RelayManager.IsJoinCode(trimmed.ToUpper()))
            DoConnectViaRelayAsync(trimmed.ToUpper());
        else
            DoConnectViaIP(trimmed);
    }

    private async void DoConnectViaRelayAsync(string code)
    {
        SetStatus($"Đang kết nối Room Code {code}…", Muted);
        try
        {
            bool inited = await RelayManager.InitAsync();
            if (!inited) { SetStatus("Không thể kết nối Unity Relay.", new Color(1f, 0.4f, 0.3f)); return; }

            JoinAllocation joinAlloc = await RelayManager.JoinRoomAsync(code);
            RelayManager.ApplyClientToTransport(joinAlloc);
            LanSessionManager.UseRelay = true;
            _roomCode = code;

            RegisterClientCallbacks();
            NetworkManager.Singleton.StartClient();
            SceneManager.sceneLoaded -= OnGameSceneLoaded;
            SceneManager.sceneLoaded += OnGameSceneLoaded;
            SetStatus("Đã vào phòng! Chờ host bắt đầu…", new Color(0.3f, 0.9f, 0.4f));
            CancelInvoke(nameof(OnConnectionTimeout));
            Invoke(nameof(OnConnectionTimeout), 15f);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Relay] Join failed: {e.Message}");
            SetStatus($"Lỗi Room Code: {e.Message}", new Color(1f, 0.4f, 0.3f));
        }
    }

    private void DoConnectViaIP(string ip)
    {
        int colon = ip.IndexOf(':');
        if (colon >= 0) ip = ip.Substring(0, colon).Trim();

        if (string.IsNullOrWhiteSpace(ip)) { SetStatus("IP không hợp lệ!", new Color(1f, 0.35f, 0.35f)); return; }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
            _pendingIp = ip;
            Invoke(nameof(RetryConnect), 0.5f);
            return;
        }

        var t = NetworkManager.Singleton?.GetComponent<UnityTransport>();
        if (t == null) { SetStatus("Lỗi transport!", new Color(1f, 0.3f, 0.3f)); return; }

        t.SetConnectionData(ip, GamePort);
        LanSessionManager.UseRelay = false;
        _roomCode = $"{ip}:{GamePort}";
        Debug.Log($"[LAN] Connecting to {ip}:{GamePort}");

        RegisterClientCallbacks();
        NetworkManager.Singleton.StartClient();
        SetStatus($"Đang kết nối tới {ip}:{GamePort}…", Muted);
        CancelInvoke(nameof(OnConnectionTimeout));
        Invoke(nameof(OnConnectionTimeout), 8f);
    }

    private void RegisterClientCallbacks()
    {
        NetworkManager.Singleton.OnClientConnectedCallback  -= OnSelfConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnSelfDisconnected;
        NetworkManager.Singleton.OnClientConnectedCallback  += OnSelfConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnSelfDisconnected;
    }

    private void OnSelfConnected(ulong _)
    {
        CancelInvoke(nameof(OnConnectionTimeout));
        Debug.Log("[LAN] Connected to host.");
        _localReady = false;
        SceneManager.sceneLoaded -= OnGameSceneLoaded;
        SceneManager.sceneLoaded += OnGameSceneLoaded;
        SwitchTo(Screen.WaitingLobby);
    }

    private void OnSelfDisconnected(ulong _)
    {
        CancelInvoke(nameof(OnConnectionTimeout));
        SetStatus("Mất kết nối với host.", new Color(1f, 0.4f, 0.4f));
    }

    private void OnConnectionTimeout()
    {
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsConnectedClient)
        {
            NetworkManager.Singleton.Shutdown();
            SetStatus("Hết thời gian — kiểm tra IP và firewall.", new Color(1f, 0.4f, 0.3f));
        }
    }

    private void OnGameSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "Menu") return;
        SceneManager.sceneLoaded -= OnGameSceneLoaded;
        Debug.Log($"[LAN] Game scene '{scene.name}' loaded — closing lobby.");
        CancelInvoke();
        _discovery?.Stop();
        UnregisterAllCallbacks();
        if (_root != null) Destroy(_root);
        _root = null; _instance = null;
        Destroy(gameObject);
    }

    // ── WaitingLobby: variant picker ──────────────────────────────────────────

    private void OnPickVariant(int idx)
    {
        if (idx < 0 || idx >= VariantColors.Length) return;
        LanSessionManager.LocalVariantIndex = idx;
        HighlightVariant(idx);
        var bridge = FindOwnBridge();
        if (bridge != null)
        {
            if (bridge.IsServer) bridge.VariantIndex.Value = idx;
            else                 bridge.SendVariantServerRpc(idx);
        }
    }

    private void HighlightVariant(int idx)
    {
        if (_variantBtnImgs == null) return;
        for (int i = 0; i < _variantBtnImgs.Length; i++)
        {
            if (_variantBtnImgs[i] == null || i >= VariantColors.Length) continue;
            _variantBtnImgs[i].color = (i == idx)
                ? Color.Lerp(VariantColors[i], White, 0.40f)
                : VariantColors[i];
        }
    }

    // ── WaitingLobby: ready toggle ────────────────────────────────────────────

    private void DoToggleReady()
    {
        _localReady = !_localReady;
        if (_btnReady != null)
        {
            var lbl = _btnReady.GetComponentInChildren<Text>();
            if (lbl != null) lbl.text = _localReady ? "✔  READY" : "◉  READY";
            var img = _btnReady.GetComponent<Image>();
            if (img != null) img.color = _localReady ? ReadyGreen : Slate;
        }
        var bridge = FindOwnBridge();
        if (bridge != null)
        {
            if (bridge.IsServer) bridge.IsReady.Value = _localReady;
            else                 bridge.SetReadyServerRpc(_localReady);
        }
    }

    // ── WaitingLobby: polling ─────────────────────────────────────────────────

    private void Update()
    {
        if (_lobbyContainer == null || !_lobbyContainer.activeSelf) return;
        RefreshLobbySlots();
        RefreshStartButton();
    }

    private void RefreshLobbySlots()
    {
        if (_slotTexts == null) return;
        var bridges = new List<LanNetworkBridge>(
            UnityEngine.Object.FindObjectsByType<LanNetworkBridge>(FindObjectsSortMode.None));
        bridges.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));

        var plHdr = _lobbyContainer?.transform.Find("PlHdr");
        if (plHdr != null)
        {
            var t = plHdr.GetComponent<Text>();
            if (t != null) t.text = $"Người chơi  ({bridges.Count}/{MaxPlayers})";
        }

        for (int i = 0; i < _slotTexts.Length; i++)
        {
            if (_slotTexts[i] == null) continue;
            if (i < bridges.Count)
            {
                var b    = bridges[i];
                string who  = (i == 0) ? "Host" : $"Người chơi {i + 1}";
                string you  = b.IsOwner ? " (bạn)" : "";
                string rdy  = b.IsReady.Value ? "✔ READY" : "…";
                _slotTexts[i].text  = $"  ● Slot {i + 1}  {who}{you}   {rdy}";
                _slotTexts[i].color = b.IsReady.Value ? new Color(0.3f, 0.9f, 0.4f) : BlueTint;
            }
            else
            {
                _slotTexts[i].text  = $"  Slot {i + 1}  —";
                _slotTexts[i].color = Muted;
            }
        }
    }

    private void RefreshStartButton()
    {
        if (_btnStart == null || !_btnStart.gameObject.activeSelf) return;
        var bridges = UnityEngine.Object.FindObjectsByType<LanNetworkBridge>(FindObjectsSortMode.None);
        int ready = 0;
        foreach (var b in bridges) if (b != null && b.IsReady.Value) ready++;
        _btnStart.interactable = ready >= 2;
    }

    // ── Start game ────────────────────────────────────────────────────────────

    private void DoStartGame()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        var bridges = UnityEngine.Object.FindObjectsByType<LanNetworkBridge>(FindObjectsSortMode.None);
        int ready = 0;
        foreach (var b in bridges) if (b != null && b.IsReady.Value) ready++;
        if (ready < 2) return;

        _discovery?.Stop();
        LanSessionManager.PlayerCount = NetworkManager.Singleton.ConnectedClients.Count;
        NetworkManager.Singleton.SceneManager.LoadScene(LanSessionManager.GameScene, LoadSceneMode.Single);
        Close();
    }

    // ── NetworkManager ────────────────────────────────────────────────────────

    private bool EnsureNetworkManager()
    {
        if (NetworkManager.Singleton != null) return true;
        var go = new GameObject("NetworkManager");
        DontDestroyOnLoad(go);
        var tr = go.AddComponent<UnityTransport>();
        tr.SetConnectionData("0.0.0.0", GamePort);
        var nm = go.AddComponent<NetworkManager>();
        if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();
        nm.NetworkConfig.NetworkTransport      = tr;
        nm.NetworkConfig.EnableSceneManagement = true;
        var bridge = GetOrCreateBridgePrefab();
        if (bridge != null) nm.NetworkConfig.PlayerPrefab = bridge;
        return true;
    }

    private static GameObject GetOrCreateBridgePrefab()
    {
        var p = Resources.Load<GameObject>("LanBridgePrefab");
        if (p != null) return p;
#if UNITY_EDITOR
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        const string path = "Assets/Resources/LanBridgePrefab.prefab";
        var tmp = new GameObject("LanBridgePrefab");
        tmp.AddComponent<NetworkObject>();
        tmp.AddComponent<LanNetworkBridge>();
        bool ok;
        PrefabUtility.SaveAsPrefabAsset(tmp, path, out ok);
        DestroyImmediate(tmp);
        if (ok) { AssetDatabase.Refresh(); return AssetDatabase.LoadAssetAtPath<GameObject>(path); }
#endif
        return null;
    }

    private static string GetLocalIP()
    {
        try
        {
            using var s = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Dgram, 0);
            s.Connect("8.8.8.8", 65530);
            return ((System.Net.IPEndPoint)s.LocalEndPoint)?.Address.ToString() ?? "127.0.0.1";
        }
        catch
        {
            foreach (var ip in System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName()))
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                    && !System.Net.IPAddress.IsLoopback(ip))
                    return ip.ToString();
            return "127.0.0.1";
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static LanNetworkBridge FindOwnBridge()
    {
        foreach (var b in UnityEngine.Object.FindObjectsByType<LanNetworkBridge>(FindObjectsSortMode.None))
            if (b != null && b.IsOwner) return b;
        return null;
    }

    private void SetStatus(string msg, Color col)
    {
        if (_hostCreatingContainer != null && _hostCreatingContainer.activeSelf && _hostCreatingStatus != null)
        { _hostCreatingStatus.text = msg; _hostCreatingStatus.color = col; return; }
        if (_joiningContainer != null && _joiningContainer.activeSelf && _joiningStatus != null)
        { _joiningStatus.text = msg; _joiningStatus.color = col; }
    }

    private static void SetVis(Button b, bool v) { if (b) b.gameObject.SetActive(v); }

    private void UnregisterAllCallbacks()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        nm.OnClientConnectedCallback  -= OnClientJoined;
        nm.OnClientDisconnectCallback -= OnClientLeft;
        nm.OnClientConnectedCallback  -= OnSelfConnected;
        nm.OnClientDisconnectCallback -= OnSelfDisconnected;
    }

    private void Close()
    {
        CancelInvoke();
        SceneManager.sceneLoaded -= OnGameSceneLoaded;
        _discovery?.Stop();
        UnregisterAllCallbacks();
        if (_root != null) Destroy(_root);
        _root = null; _instance = null;
        Destroy(gameObject);
    }

    // ── UI micro-helpers ──────────────────────────────────────────────────────

    private static Font F() => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

    private static GameObject Mk(GameObject p, string name, int L)
    {
        var go = new GameObject(name);
        go.layer = L;
        go.AddComponent<RectTransform>();
        go.transform.SetParent(p.transform, false);
        return go;
    }

    // Full-panel fill container (anchored 0,0 → 1,1; no offset)
    private GameObject MkFillPanel(string name, int L)
    {
        var go = Mk(_panel, name, L);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return go;
    }

    private static void Stretch(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

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

    private void HSep(float yFromTop, int L)
    {
        var go = Mk(_panel, "Sep", L);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, yFromTop); rt.sizeDelta = new Vector2(0f, 1f);
        go.AddComponent<Image>().color = Sep;
    }

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
