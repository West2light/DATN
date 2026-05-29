using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Overlay lobby UI for LAN multiplayer.
/// Call LanLobbyController.Show(mapFile, algorithm) from the menu.
/// Auto-creates LanBridgePrefab in Editor if missing.
/// </summary>
public class LanLobbyController : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    private static LanLobbyController _instance;

    // ── Config ────────────────────────────────────────────────────────────────
    private const ushort GamePort   = 7777;
    private const int    MaxPlayers = 8;

    // ── Palette ───────────────────────────────────────────────────────────────
    private static readonly Color BgOverlay   = new Color(0f,    0f,    0f,    0.88f);
    private static readonly Color PanelBg     = new Color(0.07f, 0.09f, 0.12f, 1f);
    private static readonly Color AccentBlue  = new Color(0.22f, 0.52f, 0.92f, 1f);
    private static readonly Color AccentGreen = new Color(0.12f, 0.55f, 0.28f, 1f);
    private static readonly Color AccentGold  = new Color(1f,    0.82f, 0.22f, 1f);
    private static readonly Color BtnSlate    = new Color(0.16f, 0.18f, 0.22f, 1f);
    private static readonly Color BtnDanger   = new Color(0.45f, 0.10f, 0.10f, 1f);
    private static readonly Color TextWhite   = Color.white;
    private static readonly Color TextMuted   = new Color(0.52f, 0.58f, 0.65f, 1f);
    private static readonly Color TextBlue    = new Color(0.65f, 0.82f, 1.00f, 1f);

    // ── Layout ────────────────────────────────────────────────────────────────
    // Panel: 560 × 480
    // Header zone: 0–84 px from top (gold bar + title + map info + sep)
    // Content zone: 84–282 px from top (status + player list)
    // Footer zone: bottom 198 px (3 button rows + cancel)
    private const float PanelW   = 560f;
    private const float PanelH   = 480f;
    private const float PadX     = 24f;

    // ── State ─────────────────────────────────────────────────────────────────
    private enum LobbyScreen { ModeSelect, HostWait, JoinManual }

    private string       _mapFile;
    private string       _algorithm;
    private LanDiscovery _discovery;
    private GameObject   _root;
    private GameObject   _panel;

    // Dynamic text elements
    private Text    _statusText;
    private Text    _playerListText;
    private Image   _statusIcon;

    // Buttons whose visibility depends on screen state
    private Button   _btnHost;
    private Button   _btnJoinManual;
    private Text     _hostIpText;
    private GameObject _ipBoxGo;
    private Button   _btnStart;
    private Button   _btnConnect;
    private GameObject _ipRow;
    private InputField _ipInput;

    private readonly List<string> _playerNames = new List<string>();

    // ── Public entry point ────────────────────────────────────────────────────

    public static void Show(string mapFile, string algorithm)
    {
        if (_instance != null) { Destroy(_instance.gameObject); _instance = null; }

        var go = new GameObject("LanLobbyController");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<LanLobbyController>();
        _instance._mapFile    = mapFile;
        _instance._algorithm  = algorithm;
        _instance.BuildOverlay();
        _instance.ShowScreen(LobbyScreen.ModeSelect);
    }

    // ── Build overlay ─────────────────────────────────────────────────────────

    private void BuildOverlay()
    {
        int layer = LayerMask.NameToLayer("UI");

        // Root canvas
        _root = new GameObject("LanLobbyOverlay");
        DontDestroyOnLoad(_root);
        _root.layer = layer;
        var cv = _root.AddComponent<Canvas>();
        cv.renderMode  = RenderMode.ScreenSpaceOverlay;
        cv.sortingOrder = 300;
        var sc = _root.AddComponent<CanvasScaler>();
        sc.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1280f, 720f);
        _root.AddComponent<GraphicRaycaster>();

        // Full-screen dim
        var dim = NewChild(_root, "Dim", layer);
        Stretch(dim); dim.AddComponent<Image>().color = BgOverlay;

        // Panel
        _panel = NewChild(_root, "Panel", layer);
        Center(_panel.GetComponent<RectTransform>(), new Vector2(PanelW, PanelH));
        _panel.AddComponent<Image>().color = PanelBg;

        BuildHeader(layer);
        BuildContent(layer);
        BuildFooter(layer);
    }

    // ── Header (gold bar + title + map info + separator) ─────────────────────

    private void BuildHeader(int layer)
    {
        // Gold accent bar (5 px, top edge)
        var bar = NewChild(_panel, "GoldBar", layer);
        var bRt = bar.GetComponent<RectTransform>();
        bRt.anchorMin = new Vector2(0f, 1f); bRt.anchorMax = new Vector2(1f, 1f);
        bRt.pivot = Vector2.one; bRt.anchoredPosition = Vector2.zero;
        bRt.sizeDelta = new Vector2(0f, 5f);
        bar.AddComponent<Image>().color = AccentGold;

        // Title
        TopLbl(_panel, "Title", "MULTIPLAYER  LAN", 22, FontStyle.Bold, AccentGold,
            new Vector2(0f, -18f), new Vector2(-PadX * 2f, 30f));

        // Map info subtitle
        string mapShort = System.IO.Path.GetFileNameWithoutExtension(_mapFile);
        TopLbl(_panel, "MapInfo",
            $"{mapShort}  ·  {_algorithm}  ·  tối đa {MaxPlayers} người",
            12, FontStyle.Normal, TextMuted,
            new Vector2(0f, -52f), new Vector2(-PadX * 2f, 18f));

        // Separator at y=-76 from top
        HSep(_panel, layer, -76f);
    }

    // ── Content (status text + player list) ──────────────────────────────────

    private void BuildContent(int layer)
    {
        // Status text (multi-line, anchor top)
        var statusGo = NewChild(_panel, "Status", layer);
        var sRt = statusGo.GetComponent<RectTransform>();
        sRt.anchorMin = new Vector2(0f, 1f); sRt.anchorMax = new Vector2(1f, 1f);
        sRt.pivot = new Vector2(0.5f, 1f);
        sRt.anchoredPosition = new Vector2(0f, -92f);
        sRt.sizeDelta = new Vector2(-PadX * 2f, 72f);
        _statusText = statusGo.AddComponent<Text>();
        _statusText.font = Fnt(); _statusText.fontSize = 14;
        _statusText.color = TextWhite; _statusText.alignment = TextAnchor.UpperCenter;

        // Player list (shown only in HostWait)
        var plGo = NewChild(_panel, "PlayerList", layer);
        var pRt = plGo.GetComponent<RectTransform>();
        pRt.anchorMin = new Vector2(0f, 1f); pRt.anchorMax = new Vector2(1f, 1f);
        pRt.pivot = new Vector2(0.5f, 1f);
        pRt.anchoredPosition = new Vector2(0f, -172f);
        pRt.sizeDelta = new Vector2(-PadX * 2f, 104f);
        _playerListText = plGo.AddComponent<Text>();
        _playerListText.font = Fnt(); _playerListText.fontSize = 13;
        _playerListText.color = TextBlue; _playerListText.alignment = TextAnchor.UpperLeft;

        // IP display box (shown when hosting, hidden otherwise)
        int lyr = LayerMask.NameToLayer("UI");
        var ipBox = NewChild(_panel, "IpBox", lyr);
        var ibRt  = ipBox.GetComponent<RectTransform>();
        ibRt.anchorMin = new Vector2(0f, 1f); ibRt.anchorMax = new Vector2(1f, 1f);
        ibRt.pivot = new Vector2(0.5f, 1f);
        ibRt.anchoredPosition = new Vector2(0f, -204f);
        ibRt.sizeDelta = new Vector2(-PadX * 2f, 52f);
        ipBox.AddComponent<Image>().color = new Color(0.10f, 0.16f, 0.24f, 1f);
        ipBox.SetActive(false);

        // Label "IP của bạn:"
        var ibLbl = NewChild(ipBox, "Lbl", lyr);
        var ibLblRt = ibLbl.GetComponent<RectTransform>();
        ibLblRt.anchorMin = new Vector2(0f, 1f); ibLblRt.anchorMax = new Vector2(1f, 1f);
        ibLblRt.pivot = new Vector2(0.5f, 1f);
        ibLblRt.anchoredPosition = new Vector2(0f, -4f); ibLblRt.sizeDelta = new Vector2(-16f, 18f);
        var ibLblTxt = ibLbl.AddComponent<Text>();
        ibLblTxt.text = "Địa chỉ IP của bạn  (share cho người chơi khác):";
        ibLblTxt.font = Fnt(); ibLblTxt.fontSize = 11; ibLblTxt.color = TextMuted;
        ibLblTxt.alignment = TextAnchor.MiddleCenter;

        // IP value (large, gold)
        var ibVal = NewChild(ipBox, "IpVal", lyr);
        var ibValRt = ibVal.GetComponent<RectTransform>();
        ibValRt.anchorMin = new Vector2(0f, 0f); ibValRt.anchorMax = new Vector2(1f, 0f);
        ibValRt.pivot = new Vector2(0.5f, 0f);
        ibValRt.anchoredPosition = new Vector2(0f, 4f); ibValRt.sizeDelta = new Vector2(-16f, 26f);
        _hostIpText = ibVal.AddComponent<Text>();
        _hostIpText.font = Fnt(); _hostIpText.fontSize = 20; _hostIpText.fontStyle = FontStyle.Bold;
        _hostIpText.color = AccentGold; _hostIpText.alignment = TextAnchor.MiddleCenter;

        // Store reference to toggle visibility
        _ipBoxGo = ipBox;

        // Separator above footer at y=-282
        HSep(_panel, LayerMask.NameToLayer("UI"), -282f);
    }

    // ── Footer (button rows, anchored from BOTTOM of panel) ──────────────────
    //
    //  y=14  ┌──────────────────────────────────── CANCEL ────────────────────┐ h=38
    //  y=60  ┌────────────────────── HOST GAME ───────────────────────────────┐ h=48
    //  y=116 ┌── JOIN  NHẬP IP ──┐   ┌──────── ENTER IP ──────── KẾT NỐI ──┐  h=38
    //  y=162 ┌────────────────────── START GAME ──────────────────────────────┐ h=44

    private void BuildFooter(int layer)
    {
        const float BtnH1 = 48f, BtnH2 = 38f, BtnH3 = 44f;
        const float BtnW  = 244f;  // each half-width button (join row only)
        const float Gap   = 8f;
        float fullW = PanelW - PadX * 2f;

        // ── Row 0: Cancel (always visible, full width) ────────────────────────
        FootBtn("BtnCancel", "CANCEL", BtnDanger, TextMuted,
            new Vector2(0f, 14f), new Vector2(fullW, BtnH2), Close);

        // ── Row 1: HOST GAME (full width) ─────────────────────────────────────
        float row1Y = 14f + BtnH2 + Gap; // 60
        _btnHost = FootBtn("BtnHost", "HOST GAME", AccentBlue, TextWhite,
            new Vector2(0f, row1Y), new Vector2(fullW, BtnH1), StartHost);

        // ── Row 2: JOIN (nhập IP)  |  IP input + CONNECT  ────────────────────
        float row2Y = row1Y + BtnH1 + Gap; // 116
        _btnJoinManual = FootBtn("BtnJoinManual", "JOIN  (nhập IP)", BtnSlate, TextWhite,
            new Vector2(-(BtnW / 2f + Gap / 2f), row2Y), new Vector2(BtnW, BtnH2),
            ShowJoinManualRow);

        // IP input + Connect button (hidden until JoinManual)
        _ipRow = NewChild(_panel, "IpRow", layer);
        var ipRowRt = _ipRow.GetComponent<RectTransform>();
        ipRowRt.anchorMin = ipRowRt.anchorMax = new Vector2(0.5f, 0f);
        ipRowRt.pivot = new Vector2(0.5f, 0f);
        ipRowRt.anchoredPosition = new Vector2(BtnW / 2f + Gap / 2f, row2Y);
        ipRowRt.sizeDelta = new Vector2(BtnW, BtnH2);
        _ipRow.SetActive(false);

        // Input field (left part of ipRow)
        var inputGo = NewChild(_ipRow, "IpField", layer);
        var iRt = inputGo.GetComponent<RectTransform>();
        iRt.anchorMin = new Vector2(0f, 0f); iRt.anchorMax = new Vector2(0.6f, 1f);
        iRt.offsetMin = Vector2.zero; iRt.offsetMax = Vector2.zero;
        inputGo.AddComponent<Image>().color = new Color(0.10f, 0.12f, 0.16f, 1f);
        _ipInput = inputGo.AddComponent<InputField>();
        var iLbl = NewChild(inputGo, "IpLbl", layer);
        var iLblRt = iLbl.GetComponent<RectTransform>();
        iLblRt.anchorMin = Vector2.zero; iLblRt.anchorMax = Vector2.one;
        iLblRt.offsetMin = new Vector2(8f, 0f); iLblRt.offsetMax = Vector2.zero;
        var iText = iLbl.AddComponent<Text>();
        iText.font = Fnt(); iText.fontSize = 13; iText.color = TextWhite;
        iText.alignment = TextAnchor.MiddleLeft;
        _ipInput.textComponent = iText;
        _ipInput.text = "";

        // Connect button (right part of ipRow)
        var connectGo = NewChild(_ipRow, "BtnConnect", layer);
        var cRt = connectGo.GetComponent<RectTransform>();
        cRt.anchorMin = new Vector2(0.62f, 0f); cRt.anchorMax = new Vector2(1f, 1f);
        cRt.offsetMin = Vector2.zero; cRt.offsetMax = Vector2.zero;
        connectGo.AddComponent<Image>().color = AccentBlue;
        _btnConnect = connectGo.AddComponent<Button>();
        _btnConnect.targetGraphic = connectGo.GetComponent<Image>();
        _btnConnect.onClick.AddListener(() => ConnectToHost(_ipInput.text.Trim()));
        var cLbl = NewChild(connectGo, "Lbl", layer);
        var cLblRt = cLbl.GetComponent<RectTransform>();
        cLblRt.anchorMin = Vector2.zero; cLblRt.anchorMax = Vector2.one;
        cLblRt.offsetMin = cLblRt.offsetMax = Vector2.zero;
        var cText = cLbl.AddComponent<Text>();
        cText.text = "KẾT NỐI"; cText.font = Fnt(); cText.fontSize = 12;
        cText.fontStyle = FontStyle.Bold; cText.color = TextWhite;
        cText.alignment = TextAnchor.MiddleCenter;

        // ── Row 3: START GAME (full width, host only) ─────────────────────────
        float row3Y = row2Y + BtnH2 + Gap; // 162
        _btnStart = FootBtn("BtnStart", "▶  START GAME", AccentGreen, TextWhite,
            new Vector2(0f, row3Y), new Vector2(PanelW - PadX * 2f, BtnH3), StartGame);
        _btnStart.gameObject.SetActive(false);
    }

    // ── Screen state machine ──────────────────────────────────────────────────

    private void ShowScreen(LobbyScreen screen)
    {
        switch (screen)
        {
            case LobbyScreen.ModeSelect:
                SetStatus("Chọn vai trò của bạn:", TextMuted);
                SetVisible(_btnHost,       true);
                SetVisible(_btnJoinManual, true);
                SetVisible(_btnStart,      false);
                SetVisible(_playerListText?.gameObject, false);
                _ipRow?.SetActive(false);
                _ipBoxGo?.SetActive(false);
                break;

            case LobbyScreen.HostWait:
                string localIp = GetLocalIP();
                if (_hostIpText != null) _hostIpText.text = $"{localIp}:{GamePort}";
                SetStatus($"Đang chờ người chơi…", AccentGold);
                SetVisible(_btnHost,       false);
                SetVisible(_btnJoinManual, false);
                SetVisible(_btnStart,      true);
                SetVisible(_playerListText?.gameObject, true);
                _ipRow?.SetActive(false);
                _ipBoxGo?.SetActive(true);
                RefreshPlayerList();
                break;

            case LobbyScreen.JoinManual:
                SetStatus("Nhập địa chỉ IP của máy host:", TextMuted);
                SetVisible(_btnHost,       false);
                SetVisible(_btnJoinManual, false);
                SetVisible(_btnStart,      false);
                SetVisible(_playerListText?.gameObject, false);
                _ipRow?.SetActive(true);
                _ipBoxGo?.SetActive(false);
                break;
        }
    }

    // ── Host flow ─────────────────────────────────────────────────────────────

    private void StartHost()
    {
        if (!EnsureNetworkManager()) return;

        LanSessionManager.ActivateHost(_mapFile, _algorithm);
        NetworkManager.Singleton.OnClientConnectedCallback  += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        NetworkManager.Singleton.StartHost();

        _discovery = gameObject.AddComponent<LanDiscovery>();
        _discovery.StartBroadcasting(GamePort);

        LanSessionManager.PlayerCount = 1;
        ShowScreen(LobbyScreen.HostWait);
    }

    private void OnClientConnected(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId) return;
        LanSessionManager.PlayerCount = NetworkManager.Singleton.ConnectedClients.Count;
        _playerNames.Add($"Player {_playerNames.Count + 2}");
        RefreshPlayerList();
        SetStatus($"{LanSessionManager.PlayerCount}/{MaxPlayers} người đã tham gia", AccentGreen);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        LanSessionManager.PlayerCount = Mathf.Max(1, NetworkManager.Singleton.ConnectedClients.Count);
        RefreshPlayerList();
    }

    // ── Join flow ─────────────────────────────────────────────────────────────

    private void ShowJoinManualRow()
    {
        if (!EnsureNetworkManager()) return;
        LanSessionManager.ActivateClient(_mapFile, _algorithm);
        ShowScreen(LobbyScreen.JoinManual);
    }

    private void ConnectToHost(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            SetStatus("IP không hợp lệ!", new Color(1f, 0.3f, 0.3f));
            return;
        }

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null) { SetStatus("Lỗi transport!", new Color(1f, 0.3f, 0.3f)); return; }
        transport.SetConnectionData(ip, GamePort);

        NetworkManager.Singleton.OnClientConnectedCallback  += _ => OnJoinedServer();
        NetworkManager.Singleton.OnClientDisconnectCallback += _ =>
            SetStatus("Mất kết nối với host.", new Color(1f, 0.4f, 0.4f));

        NetworkManager.Singleton.StartClient();
        SetStatus($"Đang kết nối tới {ip}:{GamePort}…", TextMuted);
    }

    private void OnJoinedServer()
    {
        SetStatus("Đã kết nối!\nChờ host bắt đầu game…", AccentGreen);
    }

    // ── Start game (host only) ────────────────────────────────────────────────

    private void StartGame()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        _discovery?.StopBroadcasting();
        LanSessionManager.PlayerCount = NetworkManager.Singleton.ConnectedClients.Count;

        NetworkManager.Singleton.SceneManager.LoadScene(
            LanSessionManager.GameScene, LoadSceneMode.Single);

        Close();
    }

    // ── NetworkManager setup ──────────────────────────────────────────────────

    private bool EnsureNetworkManager()
    {
        if (NetworkManager.Singleton != null) return true;

        // Transport must be added BEFORE NetworkManager so NGO's OnEnable finds it
        var nmGo = new GameObject("NetworkManager");

        var transport = nmGo.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", GamePort);

        var nm = nmGo.AddComponent<NetworkManager>();
        // NGO 2.x: NetworkConfig is a plain public field initialised to null when
        // AddComponent is called at runtime (not from a serialised prefab/Inspector).
        // NGO's own OnValidate acknowledges this: "May occur when the component is added".
        if (nm.NetworkConfig == null)
            nm.NetworkConfig = new NetworkConfig();

        nm.NetworkConfig.NetworkTransport      = transport;
        nm.NetworkConfig.EnableSceneManagement = true;

        var bridge = GetOrCreateBridgePrefab();
        if (bridge != null)
            nm.NetworkConfig.PlayerPrefab = bridge;

        // NGO's OnEnable calls DontDestroyOnLoad internally; don't call it again here
        return true;
    }

    /// <summary>Returns the LanBridgePrefab from Resources, auto-creating it in the Editor if absent.</summary>
    private static GameObject GetOrCreateBridgePrefab()
    {
        var prefab = Resources.Load<GameObject>("LanBridgePrefab");
        if (prefab != null) return prefab;

#if UNITY_EDITOR
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");

        const string path = "Assets/Resources/LanBridgePrefab.prefab";
        var temp = new GameObject("LanBridgePrefab");
        temp.AddComponent<NetworkObject>();
        temp.AddComponent<LanNetworkBridge>();

        bool ok;
        PrefabUtility.SaveAsPrefabAsset(temp, path, out ok);
        DestroyImmediate(temp);

        if (ok)
        {
            AssetDatabase.Refresh();
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        Debug.LogError("[LanLobbyController] Failed to auto-create LanBridgePrefab.");
#else
        Debug.LogError("[LanLobbyController] LanBridgePrefab missing from Resources/.");
#endif
        return null;
    }

    // ── UI state helpers ──────────────────────────────────────────────────────

    private void RefreshPlayerList()
    {
        if (_playerListText == null) return;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Người chơi ({LanSessionManager.PlayerCount}/{MaxPlayers}):");
        sb.AppendLine("  ● Bạn (host)");
        foreach (string n in _playerNames)
            sb.AppendLine($"  ● {n}");
        _playerListText.text = sb.ToString();
    }

    private void SetStatus(string msg, Color col)
    {
        if (_statusText == null) return;
        _statusText.text  = msg;
        _statusText.color = col;
    }

    private static void SetVisible(Button btn, bool v)
    {
        if (btn != null) btn.gameObject.SetActive(v);
    }

    private static void SetVisible(GameObject go, bool v)
    {
        if (go != null) go.SetActive(v);
    }

    private static string GetLocalIP()
    {
        try
        {
            // Prefer the address that can reach the internet (LAN gateway)
            using var socket = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            var ep = socket.LocalEndPoint as System.Net.IPEndPoint;
            return ep?.Address.ToString() ?? "127.0.0.1";
        }
        catch
        {
            // Fallback: enumerate host addresses
            foreach (var ip in System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName()))
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                    && !System.Net.IPAddress.IsLoopback(ip))
                    return ip.ToString();
            return "127.0.0.1";
        }
    }

    private void Close()
    {
        _discovery?.Stop();
        if (_root != null) Destroy(_root);
        Destroy(gameObject);
        _instance = null;
    }

    // ── UI builder helpers ────────────────────────────────────────────────────

    private static Font Fnt() => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

    private static GameObject NewChild(GameObject parent, string name, int layer)
    {
        var go = new GameObject(name);
        go.layer = layer;
        go.AddComponent<RectTransform>();
        go.transform.SetParent(parent.transform, false);
        return go;
    }

    private static void Stretch(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static void Center(RectTransform rt, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
    }

    private static void HSep(GameObject panel, int layer, float yFromTop)
    {
        int l = panel.layer;
        var sep = NewChild(panel, "Sep", l);
        var rt  = sep.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, yFromTop); rt.sizeDelta = new Vector2(0f, 1f);
        sep.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);
    }

    // Top-anchored label helper
    private void TopLbl(GameObject parent, string name, string text,
        int size, FontStyle style, Color color, Vector2 pos, Vector2 sizeDelta)
    {
        int layer = parent.layer;
        var go = NewChild(parent, name, layer);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos; rt.sizeDelta = sizeDelta;
        var t = go.AddComponent<Text>();
        t.text = text; t.font = Fnt(); t.fontSize = size; t.fontStyle = style;
        t.color = color; t.alignment = TextAnchor.MiddleCenter;
    }

    // Bottom-anchored button helper — pos.y = distance from panel BOTTOM (must be >= 0)
    private Button FootBtn(string name, string label, Color bg, Color textColor,
        Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        int layer = _panel.layer;
        var go = NewChild(_panel, name, layer);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot     = new Vector2(0.5f, 0f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;

        var img = go.AddComponent<Image>(); img.color = bg;
        var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
        var cb  = btn.colors;
        cb.normalColor      = Color.white;
        cb.highlightedColor = new Color(1.12f, 1.12f, 1.12f);
        cb.pressedColor     = new Color(0.80f, 0.80f, 0.80f);
        cb.disabledColor    = new Color(0.35f, 0.35f, 0.35f, 0.6f);
        btn.colors = cb;
        btn.onClick.AddListener(onClick);

        var lbl = NewChild(go, "Lbl", layer);
        var lRt = lbl.GetComponent<RectTransform>();
        lRt.anchorMin = Vector2.zero; lRt.anchorMax = Vector2.one;
        lRt.offsetMin = lRt.offsetMax = Vector2.zero;
        var t = lbl.AddComponent<Text>();
        t.text = label; t.font = Fnt(); t.fontSize = 14; t.fontStyle = FontStyle.Bold;
        t.color = textColor; t.alignment = TextAnchor.MiddleCenter;
        return btn;
    }
}
