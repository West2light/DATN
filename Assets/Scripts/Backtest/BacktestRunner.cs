using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Headless backtest: 5 maps × 2 algorithms (A* / LNS2) × 3 repetitions = 30 runs.
/// No player. Run ends when Eagle HP = 0, all enemies dead, or timeout (120 s).
/// Results exported to two CSV files in Application.persistentDataPath.
/// Attach to any scene, or call BacktestRunner.Launch() from code.
/// </summary>
public class BacktestRunner : MonoBehaviour
{
    // ── Config ─────────────────────────────────────────────────────────────
    public const int   Reps          = 3;   // default; caller can override via Launch()
    public const float RunTimeoutSec = 120f;

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

    private struct AgentRecord
    {
        public string agentName;
        public int    replanCount, recoveryCount, shotCount, cellsVisited, initialPathLength;
        public bool   deadAtEnd;
    }

    private struct RunRecord
    {
        public string map, algorithm, outcome;
        public int    rep, eagleHpAtEnd, enemiesAliveAtEnd, agentCount;
        public float  duration;
        public int    totalReplans, totalRecoveries, totalShots, totalCells;
        public List<AgentRecord> agents;
    }

    // ── State ──────────────────────────────────────────────────────────────
    private static BacktestRunner _instance;

    private readonly List<Job>       _jobs    = new List<Job>();
    private readonly List<RunRecord> _results = new List<RunRecord>();
    private int   _jobIndex;
    private float _runElapsed;

    private Damagable              _eagleDamagable;
    private readonly List<GridEnemyAgent>     _agentsA = new List<GridEnemyAgent>();
    private readonly List<GridEnemyAgentLNS2> _agentsL = new List<GridEnemyAgentLNS2>();

    private Text _progressText;
    private Text _statusText;

    // Flags set by event subscription (safe after eagle/enemies are destroyed)
    private bool _eagleDestroyed;
    private bool _allEnemiesDead;

    // ── Public entry point ─────────────────────────────────────────────────
    // selectedMapIndices: indices into MapFiles/MapLabels; null = run all maps
    public static void Launch(List<int> selectedMapIndices = null, int reps = Reps)
    {
        if (_instance != null) return;
        var go = new GameObject("BacktestRunner");
        var runner = go.AddComponent<BacktestRunner>();
        runner._selectedMapIndices = selectedMapIndices;
        runner._reps = Mathf.Max(1, reps);
    }

    private List<int> _selectedMapIndices;
    private int       _reps = Reps;

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
        string[] scenes = { "MapF_TankTest", "MapF_TankTest_LNS2" };
        string[] algos  = { "AStar", "LNS2" };

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
        for (_jobIndex = 0; _jobIndex < _jobs.Count; _jobIndex++)
            yield return RunJob(_jobs[_jobIndex]);

        BacktestMode.Deactivate();

        try { ExportCSV(); }
        catch (Exception e) { Debug.LogError($"[BacktestRunner] ExportCSV failed: {e}"); }

        ShowDoneUI();
    }

    private IEnumerator RunJob(Job job)
    {
        SetProgress($"Loading  {job.mapLabel}  [{job.algorithm}]  rep {job.rep}/{_reps}");

        PlayerPrefs.SetString("SelectedMapFile", job.mapFile);
        PlayerPrefs.Save();
        BacktestMode.Activate(job.algorithm, job.mapLabel);

        SceneManager.LoadScene(job.scene);

        yield return null; // scene unloads
        yield return null; // Awake on new objects
        yield return null; // Start on new objects (Bootstrap spawns scenario here)

        _enemiesAliveCount = 0;
        InjectScene();

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
                try { RecordRun(job, endReason); }
                catch (Exception e) { Debug.LogError($"[BacktestRunner] RecordRun failed: {e}"); }
                Debug.Log($"[BacktestRunner] Run {_jobIndex + 1}/{_jobs.Count} done — {endReason} ({_runElapsed:F1}s)");
                break;
            }

            int completed = _jobIndex;
            int total     = _jobs.Count;
            SetProgress($"[{completed}/{total}] {job.mapLabel} | {job.algorithm} | rep {job.rep} | {_runElapsed:F0}s");
            yield return null;
        }

        yield return new WaitForSeconds(0.5f);
    }

    // ── Scene injection ────────────────────────────────────────────────────
    private void InjectScene()
    {
        _agentsA.Clear();
        _agentsL.Clear();
        _eagleDamagable  = null;
        _eagleDestroyed  = false;
        _allEnemiesDead  = false;

        // Find eagle via scenario bootstrap
        var scenario = FindFirstObjectByType<MapScenarioBootstrap>();
        if (scenario != null && scenario.EagleBase != null)
            _eagleDamagable = scenario.EagleBase.GetComponentInChildren<Damagable>();

        var scenarioLns2 = FindFirstObjectByType<MapScenarioBootstrapLNS2>();
        if (_eagleDamagable == null && scenarioLns2 != null && scenarioLns2.EagleBase != null)
            _eagleDamagable = scenarioLns2.EagleBase.GetComponentInChildren<Damagable>();

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

        _agentsA.AddRange(FindObjectsByType<GridEnemyAgent>(FindObjectsSortMode.None));
        _agentsL.AddRange(FindObjectsByType<GridEnemyAgentLNS2>(FindObjectsSortMode.None));

        // Subscribe to each agent's death to track all-dead condition
        foreach (var a in _agentsA) SubscribeAgentDeath(a.GetComponentInChildren<Damagable>());
        foreach (var a in _agentsL) SubscribeAgentDeath(a.GetComponentInChildren<Damagable>());

        float t = Time.time;
        foreach (var a in _agentsA) a.btSpawnTime = t;
        foreach (var a in _agentsL) a.btSpawnTime = t;

        Debug.Log($"[BacktestRunner] Injected: eagle={_eagleDamagable != null}, agentsA={_agentsA.Count}, agentsL={_agentsL.Count}");
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

    // ── Record results ─────────────────────────────────────────────────────
    private void RecordRun(Job job, string outcome)
    {
        var rec = new RunRecord
        {
            map       = job.mapLabel,
            algorithm = job.algorithm,
            rep       = job.rep,
            outcome   = outcome,
            duration  = _runElapsed,
            eagleHpAtEnd = _eagleDamagable != null ? Mathf.Max(0, _eagleDamagable.Health) : -1,
            agents       = new List<AgentRecord>(),
        };

        int alive = 0;
        foreach (var a in _agentsA) CollectAgent(a, ref rec, ref alive, a != null && IsAlive(a.GetComponentInChildren<Damagable>()));
        foreach (var a in _agentsL) CollectAgentLns2(a, ref rec, ref alive, a != null && IsAlive(a.GetComponentInChildren<Damagable>()));

        rec.enemiesAliveAtEnd = alive;
        rec.agentCount        = _agentsA.Count + _agentsL.Count;
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

    private static void CollectAgent(GridEnemyAgent a, ref RunRecord rec, ref int alive, bool isAlive)
    {
        if (a == null) return;
        if (isAlive) alive++;
        rec.agents.Add(new AgentRecord
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

    private static void CollectAgentLns2(GridEnemyAgentLNS2 a, ref RunRecord rec, ref int alive, bool isAlive)
    {
        if (a == null) return;
        if (isAlive) alive++;
        rec.agents.Add(new AgentRecord
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
        sb.AppendLine("Run,Map,Algorithm,Rep,Outcome,Duration_s,EagleHP,AgentCount,EnemiesAlive,TotalReplans,TotalRecoveries,TotalShots,TotalCells");
        for (int i = 0; i < _results.Count; i++)
        {
            var r = _results[i];
            sb.AppendLine(string.Join(",",
                i + 1, r.map, r.algorithm, r.rep, r.outcome,
                r.duration.ToString("F2"), r.eagleHpAtEnd, r.agentCount,
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

#if UNITY_EDITOR
        // Reveal in Finder/Explorer so the user can find the files immediately
        UnityEditor.EditorUtility.RevealInFinder(Path.GetFullPath(sum));
#endif
    }

    // ── Overlay UI ─────────────────────────────────────────────────────────
    private void BuildOverlayUI()
    {
        var canvasGo = new GameObject("BacktestOverlay");
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
        _progressText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
        _statusText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _statusText.fontSize  = 13;
        _statusText.color     = new Color(0.7f, 0.9f, 0.7f, 1f);
        _statusText.alignment = TextAnchor.MiddleRight;
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
        Debug.Log($"[BacktestRunner] All done. {_results.Count} records. Folder: {dir}");
    }
}
