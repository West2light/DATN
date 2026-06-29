using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Headless backtest: maps × algorithms (A* / PIBT / PIBT-C++) × repetitions.
/// No player. Run ends when Eagle HP = 0, all enemies dead, or timeout (180 s).
/// Results exported to two CSV files in Application.persistentDataPath.
/// Attach to any scene, or call BacktestRunner.Launch() from code.
/// </summary>
public class BacktestRunner : MonoBehaviour
{
    // ── Config ─────────────────────────────────────────────────────────────
    public const int   Reps          = 3;   // default; caller can override via Launch()
    public const float RunTimeoutSec = 180f;
    private const float BacktestTimeScale = 3f; // x3 tua nhanh giống YouTube

    private static readonly string[] MapFiles =
    {
        "Assets/MapData/random-32-32-10.map",
        "Assets/MapData/ht_mansion_n.map",
        "Assets/MapData/ht_chantry.map",
        "Assets/MapData/lt_gallowstemplar_n.map",
        "Assets/MapData/maze-128-128-10.map",
    };
    private static readonly string[] MapLabels =
        { "Alpha32", "Mansion", "Chantry", "Gallows", "Maze128" };

    // Expose map metadata for BacktestConfigUI
    public static int         MapCount                  => MapFiles.Length;
    public static string      GetMapLabel(int i)        => MapLabels[i];
    public static string      GetMapFile(int i)         => MapFiles[i];

    private static List<int>  AllMapIndices()
    {
        var list = new List<int>();
        for (int i = 0; i < MapFiles.Length; i++) list.Add(i);
        return list;
    }

    // ── Types ──────────────────────────────────────────────────────────────
    private struct Job
    {
        public string mapFile, mapLabel, scene, algorithm;
        public int    rep;
    }

    // ── State ──────────────────────────────────────────────────────────────
    private static BacktestRunner _instance;

    private readonly List<Job>                  _jobs    = new List<Job>();
    private readonly List<BacktestRunRecord>    _results = new List<BacktestRunRecord>();
    private int   _jobIndex;
    private float _runElapsed;

    private Damagable              _eagleDamagable;
    private int                    _eagleMaxHp;
    private readonly List<GridEnemyAgent>     _agentsA = new List<GridEnemyAgent>();
    private readonly List<GridEnemyAgentPIBT> _agentsL = new List<GridEnemyAgentPIBT>();
    private readonly List<GridEnemyAgentPIBT_TCP> _agentsT = new List<GridEnemyAgentPIBT_TCP>();

    private Text       _progressText;
    private Text       _statusText;
    private Text       _realtimeText;
    private GameObject _overlayCanvas;

    // Flags set by event subscription (safe after eagle/enemies are destroyed)
    private bool _eagleDestroyed;
    private bool _allEnemiesDead;

    // ── Public entry / cleanup ─────────────────────────────────────────────
    public static void Cleanup()
    {
        Time.timeScale = 1f; // reset về tốc độ bình thường khi cancel/cleanup
        if (_instance == null) return;
        if (_instance._overlayCanvas != null) Destroy(_instance._overlayCanvas);
        Destroy(_instance.gameObject);
        _instance = null;
    }

    public const int DefaultAgentCount = 4; // = số ô spawn cố định cũ

    // selectedMapIndices: indices into MapFiles/MapLabels; null = run all maps
    public static void Launch(List<int> selectedMapIndices = null, int reps = Reps,
                              bool dynamicObstacles = false, int agentCount = DefaultAgentCount)
    {
        if (_instance != null) return;
        var go = new GameObject("BacktestRunner");
        var runner = go.AddComponent<BacktestRunner>();
        runner._selectedMapIndices  = selectedMapIndices;
        runner._reps                = Mathf.Max(1, reps);
        runner._dynamicObstacles    = dynamicObstacles;
        runner._agentCount          = Mathf.Clamp(agentCount, 1, 20);
    }

    private List<int> _selectedMapIndices;
    private int       _reps = Reps;
    private bool      _dynamicObstacles;
    private int       _agentCount = DefaultAgentCount;
    private DynamicObstacleSpawner _obstacleSpawner;

    // ── Unity lifecycle ────────────────────────────────────────────────────
    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        BuildJobs();
        BuildOverlayUI();
        StartCoroutine(RunAll());
    }

    // ── Job list ───────────────────────────────────────────────────────────
    private void BuildJobs()
    {
        string[] scenes = { "MapF_TankTest", "MapF_TankTest_PIBT", "MapF_TankTest_PIBT" };
        string[] algos  = { "AStar", "PIBT", "PIBT_TCP" };

        // If no selection provided, run all maps
        var indices = _selectedMapIndices ?? AllMapIndices();

        for (int mi = 0; mi < indices.Count; mi++)
        {
            int m = indices[mi];
            if (m < 0 || m >= MapFiles.Length) continue;
            for (int a = 0; a < scenes.Length; a++)
            for (int r = 1; r <= _reps; r++)
                _jobs.Add(new Job
                {
                    mapFile   = MapFiles[m],
                    mapLabel  = MapLabels[m],
                    scene     = scenes[a],
                    algorithm = algos[a],
                    rep       = r,
                });
        }
    }

    // ── Main coroutine ─────────────────────────────────────────────────────
    private IEnumerator RunAll()
    {
        Time.timeScale = BacktestTimeScale; // x3 tua nhanh toàn bộ backtest
        Debug.Log($"[BacktestRunner] Time.timeScale set to {BacktestTimeScale}x");

        for (_jobIndex = 0; _jobIndex < _jobs.Count; _jobIndex++)
            yield return RunJob(_jobs[_jobIndex]);

        Time.timeScale = 1f; // reset về tốc độ bình thường khi xong
        Debug.Log("[BacktestRunner] Time.timeScale reset to 1x");

        BacktestMode.Deactivate();

        try { ExportCSV(); }
        catch (Exception e) { Debug.LogError($"[BacktestRunner] ExportCSV failed: {e}"); }

        ShowDoneUI();
        BacktestResultChart.Show(_results);
    }

    private IEnumerator RunJob(Job job)
    {
        SetProgress($"Loading  {job.mapLabel}  [{job.algorithm}]  rep {job.rep}/{_reps}");
        Debug.Log($"[BacktestRunner] Starting job {_jobIndex + 1}/{_jobs.Count}: map={job.mapLabel}, algorithm={job.algorithm}, scene={job.scene}, rep={job.rep}");

        PlayerPrefs.SetString("SelectedMapFile", job.mapFile);
        PlayerPrefs.SetString("SelectedAlgorithm", job.algorithm);
        PlayerPrefs.Save();
        BacktestMode.Activate(job.algorithm, job.mapLabel, _dynamicObstacles, _agentCount);

        SceneManager.LoadScene(job.scene);

        yield return null; // scene unloads
        yield return null; // Awake on new objects
        yield return null; // Start on new objects (Bootstrap spawns scenario here)

        PauseMenuController.Ensure();

        _enemiesAliveCount = 0;
        InjectScene();

        // Khởi động dynamic obstacle spawner nếu mode được bật
        if (_dynamicObstacles) StartDynamicObstacles();

        float startTime = Time.time;
        _runElapsed = 0f;

        while (true)
        {
            _runElapsed = Time.time - startTime;

            string endReason = null;
            if (_eagleDestroyed)                 endReason = "EagleDestroyed";
            else if (_allEnemiesDead)             endReason = "AllEnemiesDead";
            else if (_runElapsed >= RunTimeoutSec) endReason = "Timeout";

            if (endReason != null)
            {
                StopDynamicObstacles();
                try { RecordRun(job, endReason); }
                catch (Exception e) { Debug.LogError($"[BacktestRunner] RecordRun failed: {e}"); }
                Debug.Log($"[BacktestRunner] Run {_jobIndex + 1}/{_jobs.Count} done — {endReason} ({_runElapsed:F1}s)");
                break;
            }

            int completed = _jobIndex;
            int total     = _jobs.Count;
            SetProgress($"[{completed}/{total}] {job.mapLabel} | {job.algorithm} | rep {job.rep} | {_runElapsed:F0}s");
            UpdateRealtimePanel(job);
            yield return null;
        }

        // For PIBT_TCP: wait for any in-flight plan_step thread to finish, then send a
        // clean shutdown before the scene unloads. This avoids the stream race between
        // the background read thread and PIBTTcpClient.OnDestroy().
        if (job.algorithm == "PIBT_TCP")
        {
            var tcpBootstrap = FindFirstObjectByType<MapScenarioBootstrapPIBT_TCP>();
            if (tcpBootstrap != null)
                yield return tcpBootstrap.ShutdownGracefully();
        }

        yield return new WaitForSeconds(0.5f);
    }

    // ── Scene injection ────────────────────────────────────────────────────
    private void InjectScene()
    {
        _agentsA.Clear();
        _agentsL.Clear();
        _agentsT.Clear();
        _eagleDamagable  = null;
        _eagleDestroyed  = false;
        _allEnemiesDead  = false;

        // Find eagle via scenario bootstrap
        var scenario = FindFirstObjectByType<MapScenarioBootstrap>();
        if (scenario != null && scenario.EagleBase != null)
            _eagleDamagable = scenario.EagleBase.GetComponentInChildren<Damagable>();

        var scenarioLns2 = FindFirstObjectByType<MapScenarioBootstrapPIBT>();
        if (_eagleDamagable == null && scenarioLns2 != null && scenarioLns2.EagleBase != null)
            _eagleDamagable = scenarioLns2.EagleBase.GetComponentInChildren<Damagable>();

        var scenarioTcp = FindFirstObjectByType<MapScenarioBootstrapPIBT_TCP>();
        if (_eagleDamagable == null && scenarioTcp != null && scenarioTcp.EagleBase != null)
            _eagleDamagable = scenarioTcp.EagleBase.GetComponentInChildren<Damagable>();

        // Fallback: find by name
        if (_eagleDamagable == null)
        {
            var eagleGo = GameObject.Find("EagleBase");
            if (eagleGo == null) eagleGo = GameObject.Find("Eagle");
            if (eagleGo != null)
                _eagleDamagable = eagleGo.GetComponentInChildren<Damagable>();
        }

        // Subscribe to eagle death — sets flag immediately when HP hits 0,
        // BEFORE Unity destroys the GameObject (so polling is never needed).
        if (_eagleDamagable != null)
        {
            if (_eagleDamagable.OnDead == null)
                _eagleDamagable.OnDead = new UnityEngine.Events.UnityEvent();
            _eagleDamagable.OnDead.AddListener(() => _eagleDestroyed = true);
        }
        else
        {
            Debug.LogWarning("[BacktestRunner] Eagle Damagable not found in scene!");
        }

        _eagleMaxHp = _eagleDamagable != null ? Mathf.Max(0, _eagleDamagable.MaxHealth) : 0;

        _agentsA.AddRange(FindObjectsByType<GridEnemyAgent>(FindObjectsSortMode.None));
        _agentsL.AddRange(FindObjectsByType<GridEnemyAgentPIBT>(FindObjectsSortMode.None));
        _agentsT.AddRange(FindObjectsByType<GridEnemyAgentPIBT_TCP>(FindObjectsSortMode.None));

        // Subscribe to each agent's death to track all-dead condition
        foreach (var a in _agentsA) SubscribeAgentDeath(a.GetComponentInChildren<Damagable>());
        foreach (var a in _agentsL) SubscribeAgentDeath(a.GetComponentInChildren<Damagable>());
        foreach (var a in _agentsT) SubscribeAgentDeath(a.GetComponentInChildren<Damagable>());

        float t = Time.time;
        foreach (var a in _agentsA) a.btSpawnTime = t;
        foreach (var a in _agentsL) a.btSpawnTime = t;
        foreach (var a in _agentsT) a.btSpawnTime = t;

        Debug.Log($"[BacktestRunner] Injected: eagle={_eagleDamagable != null}, agentsA={_agentsA.Count}, agentsL={_agentsL.Count}, agentsT={_agentsT.Count}");

        AttachCameraController();
    }

    private BacktestCameraController _camCtrl;

    private void AttachCameraController()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        // Remove stale controller from previous run
        var old = cam.GetComponent<BacktestCameraController>();
        if (old != null) Destroy(old);

        var mapLoader = FindFirstObjectByType<MapLoader>();
        if (mapLoader == null) return;

        float mapW = mapLoader.BuildWidth  * mapLoader.tileSize;
        float mapH = mapLoader.BuildHeight * mapLoader.tileSize;

        _camCtrl = cam.gameObject.AddComponent<BacktestCameraController>();
        _camCtrl.Init(cam, mapW, mapH);
    }

    private int _enemiesAliveCount;

    private void SubscribeAgentDeath(Damagable d)
    {
        if (d == null) return;
        _enemiesAliveCount++;
        if (d.OnDead == null) d.OnDead = new UnityEngine.Events.UnityEvent();
        d.OnDead.AddListener(() =>
        {
            _enemiesAliveCount--;
            if (_enemiesAliveCount <= 0) _allEnemiesDead = true;
        });
    }

    // ── Dynamic obstacle integration ───────────────────────────────────────
    private void StartDynamicObstacles()
    {
        if (_eagleDamagable == null) return;

        var mapLoader = FindFirstObjectByType<MapLoader>();
        if (mapLoader == null) return;

        var scenario  = FindFirstObjectByType<MapScenarioBootstrap>();
        var navMask   = scenario?.NavMask;

        var go = new GameObject("DynamicObstacleSpawner");
        _obstacleSpawner = go.AddComponent<DynamicObstacleSpawner>();
        _obstacleSpawner.Init(mapLoader, navMask, _eagleDamagable.transform.position);
        _obstacleSpawner.SetAgents(_agentsA, _agentsL, _agentsT);
    }

    private void StopDynamicObstacles()
    {
        if (_obstacleSpawner != null)
        {
            Destroy(_obstacleSpawner.gameObject);
            _obstacleSpawner = null;
        }
    }

    // ── Real-time metrics panel ────────────────────────────────────────────
    private void UpdateRealtimePanel(Job job)
    {
        if (_realtimeText == null) return;

        int eagleHp = _eagleDamagable != null ? Mathf.Max(0, _eagleDamagable.Health) : 0;
        int totalReplans = 0;
        foreach (var a in _agentsA) totalReplans += a.btReplanCount;
        foreach (var a in _agentsL) totalReplans += a.btReplanCount;
        foreach (var a in _agentsT) totalReplans += a.btReplanCount;

        string dynTag = _dynamicObstacles ? "  [DYN]" : "";
        _realtimeText.text =
            $"<b>{job.algorithm}{dynTag}</b>\n" +
            $"Map: {job.mapLabel}\n" +
            $"Agents: {_enemiesAliveCount}\n" +
            $"Eagle HP: {eagleHp}\n" +
            $"Replans: {totalReplans}\n" +
            $"Time: {_runElapsed:F0}s";
    }

    // ── Record results ─────────────────────────────────────────────────────
    private void RecordRun(Job job, string outcome)
    {
        var rec = new BacktestRunRecord
        {
            map       = job.mapLabel,
            algorithm = job.algorithm,
            rep       = job.rep,
            outcome   = outcome,
            duration  = _runElapsed,
            eagleHpAtEnd = _eagleDamagable != null ? Mathf.Max(0, _eagleDamagable.Health) : -1,
            eagleHpMax   = _eagleMaxHp,
            agents       = new List<BacktestAgentRecord>(),
        };

        int alive = 0;
        foreach (var a in _agentsA) CollectAgent(a, ref rec, ref alive, a != null && IsAlive(a.GetComponentInChildren<Damagable>()));
        foreach (var a in _agentsL) CollectAgentLns2(a, ref rec, ref alive, a != null && IsAlive(a.GetComponentInChildren<Damagable>()));
        foreach (var a in _agentsT) CollectAgentTcp(a, ref rec, ref alive, a != null && IsAlive(a.GetComponentInChildren<Damagable>()));

        rec.enemiesAliveAtEnd = alive;
        rec.agentCount        = _agentsA.Count + _agentsL.Count + _agentsT.Count;
        rec.totalReplans      = 0; rec.totalRecoveries = 0; rec.totalShots = 0; rec.totalCells = 0;
        foreach (var ar in rec.agents)
        {
            rec.totalReplans    += ar.replanCount;
            rec.totalRecoveries += ar.recoveryCount;
            rec.totalShots      += ar.shotCount;
            rec.totalCells      += ar.cellsVisited;
        }

        _results.Add(rec);
    }

    private static bool IsAlive(Damagable d) => d != null && d.Health > 0;

    private static void CollectAgent(GridEnemyAgent a, ref BacktestRunRecord rec, ref int alive, bool isAlive)
    {
        if (a == null) return;
        if (isAlive) alive++;
        rec.agents.Add(new BacktestAgentRecord
        {
            agentName         = a.name,
            replanCount       = a.btReplanCount,
            recoveryCount     = a.btRecoveryCount,
            shotCount         = a.btShotCount,
            cellsVisited      = a.btCellsVisited,
            initialPathLength = a.btInitialPathLength,
            deadAtEnd         = !isAlive,
        });
    }

    private static void CollectAgentLns2(GridEnemyAgentPIBT a, ref BacktestRunRecord rec, ref int alive, bool isAlive)
    {
        if (a == null) return;
        if (isAlive) alive++;
        rec.agents.Add(new BacktestAgentRecord
        {
            agentName         = a.name,
            replanCount       = a.btReplanCount,
            recoveryCount     = a.btRecoveryCount,
            shotCount         = a.btShotCount,
            cellsVisited      = a.btCellsVisited,
            initialPathLength = a.btInitialPathLength,
            deadAtEnd         = !isAlive,
        });
    }

    private static void CollectAgentTcp(GridEnemyAgentPIBT_TCP a, ref BacktestRunRecord rec, ref int alive, bool isAlive)
    {
        if (a == null) return;
        if (isAlive) alive++;
        rec.agents.Add(new BacktestAgentRecord
        {
            agentName         = a.name,
            replanCount       = a.btReplanCount,
            recoveryCount     = a.btRecoveryCount,
            shotCount         = a.btShotCount,
            cellsVisited      = a.btCellsVisited,
            initialPathLength = a.btInitialPathLength,
            deadAtEnd         = !isAlive,
        });
    }

    // ── CSV export ─────────────────────────────────────────────────────────
    private void ExportCSV()
    {
        // Save next to the project root so it's easy to find in the Editor.
        // Falls back to persistentDataPath in a real build.
#if UNITY_EDITOR
        string dir = Path.Combine(Application.dataPath, "..", "BacktestResults");
#else
        string dir = Application.persistentDataPath;
#endif
        Directory.CreateDirectory(dir);  // no-op if already exists

        string ts  = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string sum = Path.Combine(dir, $"backtest_summary_{ts}.csv");
        string agt = Path.Combine(dir, $"backtest_agents_{ts}.csv");

        Debug.Log($"[BacktestRunner] Writing {_results.Count} run records to:\n  {Path.GetFullPath(sum)}");

        // ── Summary: one row per run ────────────────────────────────────────
        var sb = new StringBuilder();
        sb.AppendLine("Run,Map,Algorithm,Rep,Outcome,Duration_s,EagleHP,EagleHPMax,EagleHPLostPct,AgentCount,EnemiesAlive,TotalReplans,TotalRecoveries,TotalShots,TotalCells");
        for (int i = 0; i < _results.Count; i++)
        {
            var r = _results[i];
            float hpLostPct = (r.eagleHpMax > 0 && r.eagleHpAtEnd >= 0)
                ? (1f - (float)r.eagleHpAtEnd / r.eagleHpMax) * 100f
                : 0f;
            sb.AppendLine(string.Join(",",
                i + 1, r.map, r.algorithm, r.rep, r.outcome,
                r.duration.ToString("F2"), r.eagleHpAtEnd, r.eagleHpMax,
                hpLostPct.ToString("F1"), r.agentCount,
                r.enemiesAliveAtEnd, r.totalReplans, r.totalRecoveries,
                r.totalShots, r.totalCells));
        }
        File.WriteAllText(sum, sb.ToString(), Encoding.UTF8);
        Debug.Log($"[BacktestRunner] Summary saved: {Path.GetFullPath(sum)}");

        // ── Agents: one row per agent per run ──────────────────────────────
        sb.Clear();
        sb.AppendLine("Run,Map,Algorithm,Rep,Outcome,AgentName,Replans,Recoveries,Shots,CellsVisited,InitialPathLen,DeadAtEnd");
        for (int i = 0; i < _results.Count; i++)
        {
            var r = _results[i];
            foreach (var a in r.agents)
            {
                sb.AppendLine(string.Join(",",
                    i + 1, r.map, r.algorithm, r.rep, r.outcome,
                    a.agentName, a.replanCount, a.recoveryCount,
                    a.shotCount, a.cellsVisited, a.initialPathLength,
                    a.deadAtEnd ? 1 : 0));
            }
        }
        File.WriteAllText(agt, sb.ToString(), Encoding.UTF8);
        Debug.Log($"[BacktestRunner] Agents saved: {Path.GetFullPath(agt)}");

        // ── HTML chart report ──────────────────────────────────────────────
        string html = Path.Combine(dir, $"backtest_chart_{ts}.html");
        try
        {
            string png = Path.Combine(dir, $"backtest_chart_{ts}.png");
            if (TryRunMatplotlibReport(sum, html, png))
            {
                Debug.Log($"[BacktestRunner] Matplotlib chart saved: {Path.GetFullPath(png)}");
                Debug.Log($"[BacktestRunner] Chart HTML saved: {Path.GetFullPath(html)}");
            }
            else
            {
                File.WriteAllText(html, BuildHTML(_results), Encoding.UTF8);
                Debug.LogWarning("[BacktestRunner] Matplotlib report unavailable; wrote built-in HTML fallback.");
                Debug.Log($"[BacktestRunner] Chart HTML saved: {Path.GetFullPath(html)}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[BacktestRunner] ExportHTML failed: {e}");
        }

#if UNITY_EDITOR
        UnityEditor.EditorUtility.RevealInFinder(Path.GetFullPath(sum));
#endif
    }

    private static bool TryRunMatplotlibReport(string summaryCsv, string htmlOut, string pngOut)
    {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        string[] pythonCandidates = { "/opt/homebrew/bin/python3", "/usr/local/bin/python3", "/usr/bin/python3", "python3" };
#else
        string[] pythonCandidates = { "python3", "python" };
#endif
        string script = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "backtest_plot_report.py"));
        if (!File.Exists(script))
        {
            Debug.LogWarning($"[BacktestRunner] Matplotlib script not found: {script}");
            return false;
        }

        foreach (string python in pythonCandidates)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = python,
                    Arguments = $"\"{script}\" \"{summaryCsv}\" \"{htmlOut}\" \"{pngOut}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using (var proc = Process.Start(psi))
                {
                    if (proc == null) continue;
                    bool exited = proc.WaitForExit(30000);
                    if (!exited)
                    {
                        try { proc.Kill(); } catch { }
                        Debug.LogWarning($"[BacktestRunner] Matplotlib via {python} timed out.");
                        continue;
                    }
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();

                    if (proc.ExitCode == 0 && File.Exists(htmlOut) && File.Exists(pngOut))
                    {
                        if (!string.IsNullOrWhiteSpace(stdout))
                            Debug.Log($"[BacktestRunner] Matplotlib: {stdout.Trim()}");
                        return true;
                    }

                    if (!string.IsNullOrWhiteSpace(stderr))
                        Debug.LogWarning($"[BacktestRunner] Matplotlib via {python} failed: {stderr.Trim()}");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BacktestRunner] Matplotlib via {python} unavailable: {e.Message}");
            }
        }

        return false;
    }

    // ── Overlay UI ─────────────────────────────────────────────────────────
    private void BuildOverlayUI()
    {
        var canvasGo = new GameObject("BacktestOverlay");
        _overlayCanvas = canvasGo;
        DontDestroyOnLoad(canvasGo);
        canvasGo.layer = LayerMask.NameToLayer("UI");
        var cv = canvasGo.AddComponent<Canvas>();
        cv.renderMode   = RenderMode.ScreenSpaceOverlay;
        cv.sortingOrder = 200;
        var sc = canvasGo.AddComponent<CanvasScaler>();
        sc.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1280f, 720f);

        // Background bar at bottom
        var bar = new GameObject("Bar");
        bar.layer = canvasGo.layer;
        bar.transform.SetParent(canvasGo.transform, false);
        var barRt = bar.AddComponent<RectTransform>();
        barRt.anchorMin = new Vector2(0f, 0f);
        barRt.anchorMax = new Vector2(1f, 0f);
        barRt.pivot     = new Vector2(0.5f, 0f);
        barRt.anchoredPosition = Vector2.zero;
        barRt.sizeDelta = new Vector2(0f, 36f);
        bar.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.82f);

        // Progress label
        var labelGo = new GameObject("Progress");
        labelGo.layer = canvasGo.layer;
        labelGo.transform.SetParent(bar.transform, false);
        var lRt = labelGo.AddComponent<RectTransform>();
        lRt.anchorMin = Vector2.zero; lRt.anchorMax = Vector2.one;
        lRt.pivot = new Vector2(0.5f, 0.5f);
        lRt.offsetMin = new Vector2(16f, 0f); lRt.offsetMax = new Vector2(-16f, 0f);
        _progressText = labelGo.AddComponent<Text>();
        _progressText.font      = UiFontProvider.GetDefaultFont();
        _progressText.fontSize  = 14;
        _progressText.color     = new Color(1f, 0.85f, 0.2f, 1f);
        _progressText.alignment = TextAnchor.MiddleLeft;
        _progressText.text      = "BACKTEST RUNNING...";

        // Run counter (right side)
        var cntGo = new GameObject("Counter");
        cntGo.layer = canvasGo.layer;
        cntGo.transform.SetParent(bar.transform, false);
        var cRt = cntGo.AddComponent<RectTransform>();
        cRt.anchorMin = new Vector2(1f, 0f); cRt.anchorMax = new Vector2(1f, 1f);
        cRt.pivot = new Vector2(1f, 0.5f);
        cRt.anchoredPosition = new Vector2(-16f, 0f); cRt.sizeDelta = new Vector2(200f, 0f);
        _statusText = cntGo.AddComponent<Text>();
        _statusText.font      = UiFontProvider.GetDefaultFont();
        _statusText.fontSize  = 13;
        _statusText.color     = new Color(0.7f, 0.9f, 0.7f, 1f);
        _statusText.alignment = TextAnchor.MiddleRight;

        // Camera hint (left of counter, inside bar)
        var hintGo = new GameObject("CamHint");
        hintGo.layer = canvasGo.layer;
        hintGo.transform.SetParent(bar.transform, false);
        var hRt = hintGo.AddComponent<RectTransform>();
        hRt.anchorMin = new Vector2(0.5f, 0f); hRt.anchorMax = new Vector2(0.5f, 1f);
        hRt.pivot = new Vector2(0.5f, 0.5f);
        hRt.anchoredPosition = Vector2.zero; hRt.sizeDelta = new Vector2(280f, 0f);
        var hTxt = hintGo.AddComponent<Text>();
        hTxt.font      = UiFontProvider.GetDefaultFont();
        hTxt.fontSize  = 11;
        hTxt.color     = new Color(0.55f, 0.60f, 0.68f, 1f);
        hTxt.alignment = TextAnchor.MiddleCenter;
        hTxt.text      = "Scroll = Zoom  |  RMB/MMB drag = Pan  |  WASD = Pan";

        // ── Real-time metrics panel (top-right) ───────────────────────────
        var panel = new GameObject("RealtimePanel");
        panel.layer = canvasGo.layer;
        panel.transform.SetParent(canvasGo.transform, false);
        var panelRt = panel.AddComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(1f, 1f); panelRt.anchorMax = new Vector2(1f, 1f);
        panelRt.pivot = new Vector2(1f, 1f);
        panelRt.anchoredPosition = new Vector2(-12f, -76f);
        panelRt.sizeDelta = new Vector2(170f, 112f);
        var panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0.05f, 0.06f, 0.08f, 0.88f);

        var rtGo = new GameObject("RealtimeText");
        rtGo.layer = canvasGo.layer;
        rtGo.transform.SetParent(panel.transform, false);
        var rtRt = rtGo.AddComponent<RectTransform>();
        rtRt.anchorMin = Vector2.zero; rtRt.anchorMax = Vector2.one;
        rtRt.offsetMin = new Vector2(8f, 6f); rtRt.offsetMax = new Vector2(-8f, -6f);
        _realtimeText = rtGo.AddComponent<Text>();
        _realtimeText.font           = UiFontProvider.GetDefaultFont();
        _realtimeText.fontSize       = 12;
        _realtimeText.color          = new Color(0.85f, 0.92f, 1f, 1f);
        _realtimeText.alignment      = TextAnchor.UpperLeft;
        _realtimeText.supportRichText = true;
        _realtimeText.text           = "";
    }

    private void SetProgress(string msg)
    {
        if (_progressText != null) _progressText.text = $"[BACKTEST]  {msg}";
        if (_statusText   != null) _statusText.text   = $"{_jobIndex + 1} / {_jobs.Count}";
    }

    private void ShowDoneUI()
    {
#if UNITY_EDITOR
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "BacktestResults"));
#else
        string dir = Application.persistentDataPath;
#endif
        string msg = $"[BACKTEST DONE]  {_results.Count} / {_jobs.Count} runs  —  CSV saved to  BacktestResults/";
        if (_progressText != null) _progressText.text = msg;
        if (_statusText   != null) _statusText.text   = $"DONE {_results.Count}/{_jobs.Count}";
        // Hide real-time panel so it doesn't overlap the result chart
        if (_realtimeText != null) _realtimeText.transform.parent.gameObject.SetActive(false);
        Debug.Log($"[BacktestRunner] All done. {_results.Count} records. Folder: {dir}");
    }

    // ── HTML chart builder ─────────────────────────────────────────────────
    private static string BuildHTML(List<BacktestRunRecord> results)
    {
        string[] algos = { "AStar", "PIBT", "PIBT_TCP" };

        // Collect unique maps preserving insertion order
        var maps = new List<string>();
        foreach (var r in results)
            if (!maps.Contains(r.map)) maps.Add(r.map);

        // Aggregate per (map, algo): sum & count for 5 metrics
        // idx: 0=duration, 1=replans, 2=shots, 3=cells, 4=eagleHp
        const int NM = 5;
        var sums  = new Dictionary<(string, string), float[]>();
        var cnts  = new Dictionary<(string, string), int[]>();
        foreach (var r in results)
        {
            var k = (r.map, r.algorithm);
            if (!sums.ContainsKey(k)) { sums[k] = new float[NM]; cnts[k] = new int[NM]; }
            sums[k][0] += r.duration;      cnts[k][0]++;
            sums[k][1] += r.totalReplans;  cnts[k][1]++;
            sums[k][2] += r.totalShots;    cnts[k][2]++;
            sums[k][3] += r.totalCells;    cnts[k][3]++;
            sums[k][4] += r.eagleHpAtEnd;  cnts[k][4]++;
        }

        float Avg(string map, string algo, int i)
        {
            var k = (map, algo);
            if (!sums.ContainsKey(k) || cnts[k][i] == 0) return 0f;
            return sums[k][i] / cnts[k][i];
        }

        string[] metLabels    = { "Average time (s)", "Total replans", "Total shots", "Cells traveled", "Final Eagle HP" };
        bool[]   lowerBetter  = { true, false, false, false, false };

        string BestAlgo(string map, int mi)
        {
            string best = null;
            float bestVal = 0f;
            foreach (string algo in algos)
            {
                float v = Avg(map, algo, mi);
                if (v <= 0f) continue;
                if (best == null || (lowerBetter[mi] ? v < bestVal : v > bestVal))
                {
                    best = algo;
                    bestVal = v;
                }
            }
            return best;
        }

        var sb = new StringBuilder();

        // ── HTML head ──────────────────────────────────────────────────────
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang='vi'><head><meta charset='UTF-8'>");
        sb.AppendLine("<title>Backtest Report — A* vs PIBT vs PIBT-C++</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("*{box-sizing:border-box;margin:0;padding:0}");
        sb.AppendLine("body{background:#0e1014;color:#d0d8e8;font-family:'Segoe UI',Arial,sans-serif;padding:32px}");
        sb.AppendLine("h1{color:#f5d050;font-size:22px;margin-bottom:4px}");
        sb.AppendLine(".subtitle{color:#6a7280;font-size:13px;margin-bottom:32px}");
        sb.AppendLine(".section{background:#161820;border-radius:10px;padding:24px;margin-bottom:24px}");
        sb.AppendLine(".section h2{font-size:14px;font-weight:600;color:#8a93a8;margin-bottom:20px;text-transform:uppercase;letter-spacing:.05em}");
        sb.AppendLine(".chart-wrap{display:flex;align-items:flex-end;gap:0;height:200px;border-bottom:1px solid #2a2d38;padding-bottom:8px;margin-bottom:8px}");
        sb.AppendLine(".group{display:flex;align-items:flex-end;gap:4px;margin-right:16px;flex-direction:column}");
        sb.AppendLine(".bars{display:flex;align-items:flex-end;gap:4px}");
        sb.AppendLine(".bar{width:28px;border-radius:3px 3px 0 0;position:relative;min-height:2px;transition:opacity .15s}");
        sb.AppendLine(".bar:hover{opacity:.8}");
        sb.AppendLine(".bar-a{background:#4a96ff}");
        sb.AppendLine(".bar-p{background:#ff8c24}");
        sb.AppendLine(".bar-t{background:#66d98c}");
        sb.AppendLine(".bar-val{display:none}");
        sb.AppendLine(".map-lbl{font-size:10px;color:#6a7280;text-align:center;margin-top:6px;width:62px}");
        sb.AppendLine(".legend{display:flex;gap:20px;margin-bottom:12px}");
        sb.AppendLine(".leg{display:flex;align-items:center;gap:6px;font-size:12px;color:#8a93a8}");
        sb.AppendLine(".dot{width:12px;height:12px;border-radius:2px}");
        sb.AppendLine(".win-a{color:#4a96ff;font-weight:700}");
        sb.AppendLine(".win-p{color:#ff8c24;font-weight:700}");
        sb.AppendLine(".win-t{color:#66d98c;font-weight:700}");
        sb.AppendLine("table{width:100%;border-collapse:collapse;font-size:12px}");
        sb.AppendLine("th{background:#1e2128;color:#6a7280;padding:8px 12px;text-align:left;font-weight:600}");
        sb.AppendLine("td{padding:7px 12px;border-bottom:1px solid #1e2128;color:#c0c8d8}");
        sb.AppendLine("tr:last-child td{border-bottom:none}");
        sb.AppendLine("</style></head><body>");

        // ── Title ──────────────────────────────────────────────────────────
        sb.AppendLine($"<h1>Backtest Report — A* vs PIBT vs PIBT-C++</h1>");
        sb.AppendLine($"<p class='subtitle'>Run date: {DateTime.Now:dd/MM/yyyy HH:mm}  •  {results.Count} runs  •  {maps.Count} map(s)</p>");

        // ── One bar-chart section per metric ───────────────────────────────
        for (int mi = 0; mi < NM; mi++)
        {
            // Compute max for scale
            float maxVal = 0f;
            foreach (var m in maps)
                foreach (var algo in algos)
                    maxVal = Mathf.Max(maxVal, Avg(m, algo, mi));
            if (maxVal <= 0f) continue;
            float scale = 180f / maxVal; // px per unit (chart height = 180px)

            sb.AppendLine("<div class='section'>");
            sb.AppendLine($"<h2>{metLabels[mi]}</h2>");
            sb.AppendLine("<div class='legend'>");
            sb.AppendLine("  <span class='leg'><span class='dot' style='background:#4a96ff'></span>A*</span>");
            sb.AppendLine("  <span class='leg'><span class='dot' style='background:#ff8c24'></span>PIBT</span>");
            sb.AppendLine("  <span class='leg'><span class='dot' style='background:#66d98c'></span>PIBT-C++</span>");
            sb.AppendLine("</div>");
            sb.AppendLine("<div class='chart-wrap'>");

            foreach (var map in maps)
            {
                float aVal = Avg(map, "AStar", mi);
                float pVal = Avg(map, "PIBT",  mi);
                float tVal = Avg(map, "PIBT_TCP", mi);
                int   aH   = Mathf.Max(2, Mathf.RoundToInt(aVal * scale));
                int   pH   = Mathf.Max(2, Mathf.RoundToInt(pVal * scale));
                int   tH   = Mathf.Max(2, Mathf.RoundToInt(tVal * scale));
                string aFmt = aVal >= 100 ? aVal.ToString("F0") : aVal.ToString("F1");
                string pFmt = pVal >= 100 ? pVal.ToString("F0") : pVal.ToString("F1");
                string tFmt = tVal >= 100 ? tVal.ToString("F0") : tVal.ToString("F1");

                sb.AppendLine($"<div class='group'>");
                sb.AppendLine($"  <div class='bars'>");
                sb.AppendLine($"    <div class='bar bar-a' style='height:{aH}px' title='A*: {aFmt}'><span class='bar-val'>{aFmt}</span></div>");
                sb.AppendLine($"    <div class='bar bar-p' style='height:{pH}px' title='PIBT: {pFmt}'><span class='bar-val'>{pFmt}</span></div>");
                sb.AppendLine($"    <div class='bar bar-t' style='height:{tH}px' title='PIBT-C++: {tFmt}'><span class='bar-val'>{tFmt}</span></div>");
                sb.AppendLine($"  </div>");
                sb.AppendLine($"  <div class='map-lbl'>{map}</div>");
                sb.AppendLine($"</div>");
            }

            sb.AppendLine("</div></div>"); // chart-wrap / section
        }

        // ── Summary table ──────────────────────────────────────────────────
        sb.AppendLine("<div class='section'><h2>Summary (averages)</h2>");
        sb.AppendLine("<table><tr><th>Map</th><th>Algorithm</th>");
        foreach (var ml in metLabels) sb.AppendLine($"<th>{ml}</th>");
        sb.AppendLine("</tr>");

        foreach (var map in maps)
        {
            foreach (var algo in algos)
            {
                sb.Append($"<tr><td>{map}</td><td>{algo}</td>");
                for (int mi = 0; mi < NM; mi++)
                {
                    float v    = Avg(map, algo,    mi);
                    string bestAlgo = BestAlgo(map, mi);
                    string cls  = algo == bestAlgo ? (algo == "AStar" ? "win-a" : algo == "PIBT" ? "win-p" : "win-t") : "";
                    string fmt  = v >= 100 ? v.ToString("F0") : v.ToString("F1");
                    sb.Append($"<td class='{cls}'>{fmt}</td>");
                }
                sb.AppendLine("</tr>");
            }
        }

        sb.AppendLine("</table></div>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }
}
