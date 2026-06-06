using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Spawn và despawn thùng sắt tối màu liên tục ở các ô walkable ngoài vùng
/// an toàn quanh Eagle Base.  Dùng để stress-test khả năng dynamic replanning
/// của A* và PIBT trong chế độ backtest.
///
/// Crates KHÔNG có layer Hittable → không bị đạn phá huỷ.
/// Crates CÓ BoxCollider2D (layer Walls) → vật lý block tank.
/// MapLoader.MarkCellBlocked + NavMask.SetCellAgentWalkable → cập nhật pathfinding.
/// </summary>
public class DynamicObstacleSpawner : MonoBehaviour
{
    // Tốc độ spawn cố định (giây / crate)
    public float spawnInterval  = 2.5f;
    // Thời gian mỗi crate tồn tại trước khi biến mất
    public float crateLifetime  = 4f;
    // Số crate tối đa đồng thời
    public int   maxActiveCrates = 8;
    // Bán kính an toàn quanh Eagle (world units) — không spawn trong vùng này
    public float safeRadius     = 6f;

    private MapLoader    _mapLoader;
    private GridNavMask  _navMask;   // có thể null (PIBT không dùng NavMask)
    private Vector3      _eaglePos;
    private Sprite       _crateSprite;

    private List<GridEnemyAgent>     _agentsA;
    private List<GridEnemyAgentPIBT> _agentsL;

    private readonly List<CrateEntry> _active = new List<CrateEntry>();
    private float _nextSpawn;

    private struct CrateEntry
    {
        public GameObject go;
        public Vector2Int cell;
    }

    private static readonly Color DarkCrateColor = new Color(0.25f, 0.12f, 0.08f, 1f);

    public void Init(MapLoader mapLoader, GridNavMask navMask, Vector3 eagleWorldPos)
    {
        _mapLoader = mapLoader;
        _navMask   = navMask;
        _eaglePos  = eagleWorldPos;
        _crateSprite = LoadCrateSprite();
        _nextSpawn = Time.time + spawnInterval;
    }

    public void SetAgents(List<GridEnemyAgent> agentsA, List<GridEnemyAgentPIBT> agentsL)
    {
        _agentsA = agentsA;
        _agentsL = agentsL;
    }

    private void Update()
    {
        if (_mapLoader == null) return;
        if (Time.time >= _nextSpawn)
        {
            TrySpawn();
            _nextSpawn = Time.time + spawnInterval;
        }
    }

    private void TrySpawn()
    {
        if (_active.Count >= maxActiveCrates) return;

        if (!TryPickCell(out Vector2Int cell)) return;

        // Block trong grid + NavMask trước khi tạo GO
        _mapLoader.MarkCellBlocked(cell);
        _navMask?.SetCellAgentWalkable(cell, false);

        GameObject go = BuildCrateGO(cell);
        _active.Add(new CrateEntry { go = go, cell = cell });
        StartCoroutine(RemoveAfter(go, cell, crateLifetime));
    }

    private IEnumerator RemoveAfter(GameObject go, Vector2Int cell, float delay)
    {
        yield return new WaitForSeconds(delay);
        DespawnCrate(go, cell);
    }

    private void DespawnCrate(GameObject go, Vector2Int cell)
    {
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            if (_active[i].go == go) { _active.RemoveAt(i); break; }
        }

        _mapLoader?.UnmarkCellBlocked(cell);
        _navMask?.SetCellAgentWalkable(cell, true);

        if (go != null) Destroy(go);
    }

    private void OnDestroy()
    {
        StopAllCoroutines();
        foreach (var e in _active)
        {
            _mapLoader?.UnmarkCellBlocked(e.cell);
            _navMask?.SetCellAgentWalkable(e.cell, true);
            if (e.go != null) Destroy(e.go);
        }
        _active.Clear();
    }

    // ── Cell selection ──────────────────────────────────────────────────────

    private bool TryPickCell(out Vector2Int result)
    {
        // 1st priority: a cell that lies on an active agent's planned path
        if (TryPickCellOnAgentPath(out result)) return true;
        // Fallback: random walkable cell anywhere on the map
        return TryPickCellRandom(out result);
    }

    /// <summary>
    /// Gộp toàn bộ cells từ path hiện tại của tất cả agents, xáo trộn, rồi
    /// chọn cell đầu tiên hợp lệ (không gần Eagle, chưa bị chiếm, walkable).
    /// Bỏ qua ~25% đầu path (gần vị trí tank hiện tại) để không block ngay
    /// sát mặt tank, và ~15% cuối path (gần Eagle).
    /// </summary>
    private bool TryPickCellOnAgentPath(out Vector2Int result)
    {
        var candidates = new List<Vector2Int>();

        void AddPath(IReadOnlyList<Vector2Int> path)
        {
            if (path == null || path.Count < 2) return;
            // Take cells 1-6 ahead (skip index 0 = agent's current cell).
            // Spawning close forces an immediate replan rather than a future one.
            int limit = Mathf.Min(7, path.Count);
            for (int i = 1; i < limit; i++)
                candidates.Add(path[i]);
        }

        if (_agentsA != null) foreach (var a in _agentsA) if (a != null) AddPath(a.CurrentPath);
        if (_agentsL != null) foreach (var a in _agentsL) if (a != null) AddPath(a.CurrentPath);

        if (candidates.Count == 0) { result = default; return false; }

        // Shuffle so we don't always pick the same agent's path
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        foreach (var cell in candidates)
        {
            if (!IsValidSpawnCell(cell)) continue;
            result = cell;
            return true;
        }

        result = default;
        return false;
    }

    private bool TryPickCellRandom(out Vector2Int result)
    {
        int x0 = _mapLoader.BuildStartX, y0 = _mapLoader.BuildStartY;
        int w  = _mapLoader.BuildWidth,  h  = _mapLoader.BuildHeight;

        for (int attempt = 0; attempt < 60; attempt++)
        {
            var cell = new Vector2Int(Random.Range(x0 + 1, x0 + w - 1),
                                     Random.Range(y0 + 1, y0 + h - 1));
            if (!IsValidSpawnCell(cell)) continue;
            result = cell;
            return true;
        }
        result = default;
        return false;
    }

    private bool IsValidSpawnCell(Vector2Int cell)
    {
        if (!_mapLoader.IsWalkable(cell)) return false;
        if (Vector3.Distance(_mapLoader.CellToWorld(cell), _eaglePos) < safeRadius) return false;
        foreach (var e in _active) if (e.cell == cell) return false;
        return true;
    }

    // ── GameObject construction ─────────────────────────────────────────────

    private GameObject BuildCrateGO(Vector2Int cell)
    {
        var go = new GameObject($"DynCrate_{cell.x}_{cell.y}");
        go.transform.position   = _mapLoader.CellToWorld(cell);
        go.transform.localScale = Vector3.one * _mapLoader.tileSize;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite       = _crateSprite;
        sr.color        = DarkCrateColor;
        sr.sortingOrder = 3;

        // Block vật lý (không phá huỷ được, không phải Hittable)
        int wallLayer = LayerMask.NameToLayer("Walls");
        if (wallLayer < 0) wallLayer = LayerMask.NameToLayer("ObstaclesMovement");
        if (wallLayer >= 0) go.layer = wallLayer;

        go.AddComponent<BoxCollider2D>().size = Vector2.one;

        return go;
    }

    private static Sprite LoadCrateSprite()
    {
#if UNITY_EDITOR
        var s = AssetDatabase.LoadAssetAtPath<Sprite>(
            "Assets/Sprites/Kenny Topdown Tanks Redux/PNG/Retina/crateMetal.png");
        if (s != null) return s;
#endif
        // Fallback: solid colored quad
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
    }
}
