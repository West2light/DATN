using System;
using System.Collections;
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
    private const float PW    = 520f;   // panel width
    private const float PH    = 480f;   // panel height
    private const float PadX  = 22f;
    private float       FullW => PW - PadX * 2f;

    // ── State ─────────────────────────────────────────────────────────────────
    private enum Screen { Choose, Hosting, Joining }

    private string     _mapFile, _algorithm;
    private LanDiscovery _discovery;
    private GameObject _root, _panel;
    private int        _hostRetryCount;

    // Content refs
    private Text       _statusTxt;
    private GameObject _ipBox;
    private Text       _ipVal;
    private Text       _playersTxt;

    // Footer refs (hosted as overlapping pairs at same Y)
    private Button     _btnHost;       // Choose screen
    private Button     _btnStart;      // Hosting screen (same Y as _btnHost)
    private Button     _btnJoin;       // Choose screen
    private GameObject _joinRow;       // Joining screen (same Y as _btnJoin): input+connect
    private InputField _ipInput;

    private readonly List<string> _clients = new List<string>();
    private string _pendingIp;
    private string _autoJoinTarget;

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

    public static void Show(string mapFile, string algorithm)
    {
        ShowInternal(mapFile, algorithm, string.Empty);
    }

    public static void ShowAndJoin(string mapFile, string algorithm, string joinTarget)
    {
        ShowInternal(mapFile, algorithm, joinTarget);
    }

    private static void ShowInternal(string mapFile, string algorithm, string joinTarget)
    {
        if (_instance != null) { Destroy(_instance.gameObject); _instance = null; }
        CleanupSession();

        var go = new GameObject("LanLobbyController");
        DontDestroyOnLoad(go);
        _instance                = go.AddComponent<LanLobbyController>();
        _instance._mapFile       = mapFile;
        _instance._algorithm     = algorithm;
        _instance._autoJoinTarget = joinTarget ?? string.Empty;
        _instance.Build();
        _instance.SwitchTo(Screen.Choose);
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
        TLbl(_panel, "Title", "MULTIPLAYER  LAN",
            22, FontStyle.Bold, Gold, new Vector2(0f, -16f), new Vector2(FullW, 28f));

        // Map · algorithm line
        string map = System.IO.Path.GetFileNameWithoutExtension(_mapFile);
        TLbl(_panel, "Sub", $"{map}  ·  {_algorithm}  ·  tối đa {MaxPlayers} người",
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
            "IP của bạn  —  share cho người chơi khác:",
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
        BtnFull("BtnCancel", "CANCEL", Danger, new Color(1f, 0.55f, 0.55f),
            y0, BH, Close);

        // HOST GAME — Choose screen
        _btnHost = BtnFull("BtnHost", "●  HOST GAME", Blue, White,
            y1, PH1, DoHost);

        // START GAME — Hosting screen (same Y, toggled with _btnHost)
        _btnStart = BtnFull("BtnStart", "▶  START GAME", Green, White,
            y1, PH1, DoStartGame);

        // JOIN — Choose screen
        _btnJoin = BtnFull("BtnJoin", "→  JOIN  (nhập IP host)", Slate, White,
            y2, PH2, ShowJoinRow);

        // JOIN ROW — Joining screen (same Y, replaces _btnJoin)
        _joinRow = BuildJoinRow(L, y2, PH2);
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

        // "Back" button (left ~15%)
        var backGo = Mk(row, "Back", L);
        var bkRt   = backGo.GetComponent<RectTransform>();
        bkRt.anchorMin = new Vector2(0f, 0f); bkRt.anchorMax = new Vector2(0.13f, 1f);
        bkRt.offsetMin = bkRt.offsetMax = Vector2.zero;
        backGo.AddComponent<Image>().color = Slate;
        var bkBtn = backGo.AddComponent<Button>(); bkBtn.targetGraphic = backGo.GetComponent<Image>();
        bkBtn.onClick.AddListener(SwitchToChoose);
        LblFill(backGo, "◀", 16, FontStyle.Bold, Muted, L);

        // IP InputField (middle ~55%)
        var ifGo = Mk(row, "IpInput", L);
        var ifRt = ifGo.GetComponent<RectTransform>();
        ifRt.anchorMin = new Vector2(0.14f, 0f); ifRt.anchorMax = new Vector2(0.70f, 1f);
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
        phTxt.text = "Nhập IP, IP:port, invite link, hoặc session code";
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

    // ── Screen transitions ────────────────────────────────────────────────────

    private void SwitchTo(Screen s)
    {
        switch (s)
        {
            case Screen.Choose:
                SwitchToChoose();
                break;
            case Screen.Hosting:
                SetStatus($"Đang chờ người chơi kết nối…", Gold);
                SetVis(_btnHost,   false);  SetVis(_btnStart, true);
                SetVis(_btnJoin,   true);   _joinRow.SetActive(false);
                _ipBox?.SetActive(true);
                _playersTxt?.gameObject.SetActive(true);
                RefreshPlayers();
                break;
            case Screen.Joining:
                SetStatus("Nhập IP, IP:port, invite link, hoặc session code:", Muted);
                SetVis(_btnHost,  false);  SetVis(_btnStart, false);
                SetVis(_btnJoin,  false);  _joinRow.SetActive(true);
                _ipBox?.SetActive(false);
                _playersTxt?.gameObject.SetActive(false);
                break;
        }
    }

    private void SwitchToChoose()
    {
        // Stop auto-discovery if user goes back from Joining screen
        if (_discovery != null) { _discovery.StopListening(); }
        SetStatus("Chọn vai trò của bạn:", Muted);
        SetVis(_btnHost,  true);  SetVis(_btnStart, false);
        SetVis(_btnJoin,  true);  _joinRow.SetActive(false);
        _ipBox?.SetActive(false);
        _playersTxt?.gameObject.SetActive(false);
    }

    private void ShowJoinRow()
    {
        if (!EnsureNetworkManager()) return;
        LanSessionManager.ActivateClient(_mapFile, _algorithm);
        SwitchTo(Screen.Joining);
        StartAutoDiscover();
    }

    private void BeginAutoJoin()
    {
        if (string.IsNullOrWhiteSpace(_autoJoinTarget))
            return;

        if (!EnsureNetworkManager())
            return;

        LanSessionManager.ActivateClient(_mapFile, _algorithm);
        SwitchTo(Screen.Joining);
        if (_ipInput != null)
            _ipInput.text = _autoJoinTarget;
        DoConnect(_autoJoinTarget);
    }

    private void StartAutoDiscover()
    {
        if (_discovery != null) { _discovery.Stop(); Destroy(_discovery); }
        _discovery = gameObject.AddComponent<LanDiscovery>();
        SetStatus("Đang tự động tìm host trong mạng LAN…", Muted);
        _discovery.OnHostFound += ip =>
        {
            if (_ipInput != null) _ipInput.text = ip;
            SetStatus($"Tìm thấy host: {ip}  —  bấm KẾT NỐI", new Color(0.3f, 0.9f, 0.4f));
            _discovery.StopListening();
        };
        _discovery.StartListening();
    }

    // ── Host flow ─────────────────────────────────────────────────────────────

    private void DoHost()
    {
        if (!EnsureNetworkManager()) return;
        if (NetworkManager.Singleton.IsListening)
        {
            SetStatus("Đang dừng session cũ…", Muted);
            NetworkManager.Singleton.Shutdown();
            Invoke(nameof(RetryHost), 2f);
            return;
        }

        LanSessionManager.ActivateHost(_mapFile, _algorithm);
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

        string ip = GetLocalIP();
        Debug.Log($"[LAN] Hosting on 0.0.0.0:{GamePort}  (LAN IP: {ip})");
        if (_ipVal != null) _ipVal.text = $"{ip}:{GamePort}";

        _discovery = gameObject.AddComponent<LanDiscovery>();
        _discovery.StartBroadcasting(GamePort);

        _hostRetryCount = 0;   // reset only on genuine success
        LanSessionManager.PlayerCount = 1;
        SwitchTo(Screen.Hosting);
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
                $"Không thể mở port {GamePort}.\n" +
                "Tiến trình khác đang chiếm port này.\n" +
                "Thoát hết build đang chạy hoặc khởi động lại Unity rồi thử lại.",
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
        SetStatus($"{LanSessionManager.PlayerCount}/{MaxPlayers}  người đã vào", Green);
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
        SetStatus("Đã kết nối!  Chờ host bắt đầu…", Green);
        Debug.Log("[LAN] Connected to host.");
        // When the host loads the game scene, NGO will trigger a scene load on this client.
        // Register so we can close the lobby overlay once the game scene is live.
        SceneManager.sceneLoaded -= OnGameSceneLoaded;
        SceneManager.sceneLoaded += OnGameSceneLoaded;
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
        string reason = NetworkManager.Singleton != null ? NetworkManager.Singleton.DisconnectReason : string.Empty;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            SetStatus($"Connection rejected: {reason}", new Color(1f, 0.4f, 0.4f));
            Debug.LogWarning($"[LAN] Disconnected from host. Reason: {reason}");
            return;
        }
        SetStatus("Mất kết nối với host.", new Color(1f, 0.4f, 0.4f));
        Debug.LogWarning("[LAN] Disconnected from host.");
    }

    private void OnConnectionTimeout()
    {
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsConnectedClient)
        {
            NetworkManager.Singleton.Shutdown();
            SetStatus("Hết thời gian — không thể kết nối.\nKiểm tra IP và firewall.", new Color(1f, 0.4f, 0.3f));
            Debug.LogWarning($"[LAN] Connection timeout to {_pendingIp ?? "?"}:{GamePort}");
        }
    }

    private void DoConnect(string ip)
    {
        if (!InternetJoinParser.TryParse(ip, LanSessionManager.RegistryUrl, out NetworkEndpointConfig endpoint, out string parseError))
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
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
            _pendingIp = retryToken;
            Invoke(nameof(RetryConnect), 0.5f);
            return;
        }

        if (!EnsureNetworkManager()) return;
        var t = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (t == null) { SetStatus("Lỗi transport!", new Color(1f, 0.3f, 0.3f)); return; }

        endpoint.mapFile = string.IsNullOrWhiteSpace(endpoint.mapFile) ? _mapFile : endpoint.mapFile;
        endpoint.algorithm = string.IsNullOrWhiteSpace(endpoint.algorithm) ? _algorithm : endpoint.algorithm;
        endpoint.maxPlayers = endpoint.maxPlayers <= 0 ? LanSessionManager.MaxPlayers : endpoint.maxPlayers;

        LanSessionManager.ActivateInternetClient(endpoint);
        t.SetConnectionData(endpoint.host, endpoint.port);
        t.UseWebSockets = endpoint.transportMode == NetworkTransportMode.WebSocket;
        NetworkManagerFactory.ConfigureConnectionApproval(NetworkManager.Singleton, isServer: false);
        Debug.Log($"[LAN] Connecting to {endpoint.host}:{endpoint.port} via {endpoint.transportMode.ToArgumentValue()}");

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
        _discovery?.Stop();
        LanSessionManager.PlayerCount = NetworkManager.Singleton.ConnectedClients.Count;
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

    private void RefreshPlayers()
    {
        if (_playersTxt == null) return;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Người chơi  ({LanSessionManager.PlayerCount}/{MaxPlayers})");
        sb.AppendLine("  ●  Host  (bạn)");
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
