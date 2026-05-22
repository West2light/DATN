using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static singleton chia sẻ giữa tất cả agent dùng LNS2.
///
/// Port từ C++ default_planner (Team_No_Man's_Sky):
///  • Flow grid          : flow[cell*4+d] = số trajectory đang dùng cạnh cell→d
///                         d: 0=east(+x), 1=south(+y), 2=west(-x), 3=north(-y)
///  • Flow-aware A*      : priority = cumulative(op_flow + vertex_flow) + g + h
///    - op_flow per edge : (flow[from→d]+1) × flow[to→opposite(d)]
///      → phạt đi ngược chiều traffic
///    - vertex_flow      : Σflow[to→*] / 2
///      → phạt đi qua cell đông
///  • Frank-Wolfe        : xóa traj → A* với flow hiện tại → thêm traj mới
///  • Heuristic          : reverse BFS từ goal (lazy, cache per goal)
/// </summary>
public static class LNS2Planner
{
    // ── Grid ──────────────────────────────────────────────────────────────
    private static MapLoader _ml;
    private static int _cols, _rows, _size;

    // flow[cell*4 + d] = số lượng trajectory đang dùng cạnh đó
    private static int[] _flow;

    // _nbrs[cell] = flat-index các neighbor walkable (precomputed)
    private static int[][] _nbrs;

    // ── Heuristics ────────────────────────────────────────────────────────
    // _h[goalFlat] = int[size], _h[goal][source] = BFS dist(source → goal)
    private static readonly Dictionary<int, int[]> _h = new Dictionary<int, int[]>();

    // ── Agent state ───────────────────────────────────────────────────────
    private static int _nextId;
    private static readonly Dictionary<int, List<int>> _trajs   = new Dictionary<int, List<int>>();
    private static readonly Dictionary<int, int>       _goals   = new Dictionary<int, int>();
    private static readonly Dictionary<int, int>       _currPos = new Dictionary<int, int>();

    public static bool IsReady { get; private set; }

    // ── Init ──────────────────────────────────────────────────────────────

    public static void Init(MapLoader ml)
    {
        if (IsReady && _ml == ml) return;
        _ml   = ml;
        _cols = ml.Width;
        _rows = ml.Height;
        _size = _cols * _rows;
        _flow = new int[_size * 4];
        BuildNbrs();
        _h.Clear();
        _trajs.Clear();
        _goals.Clear();
        _currPos.Clear();
        _nextId  = 0;
        IsReady  = true;
    }

    private static void BuildNbrs()
    {
        _nbrs = new int[_size][];
        for (int flat = 0; flat < _size; flat++)
        {
            int r = flat / _cols, c = flat % _cols;
            if (!_ml.IsWalkable(new Vector2Int(c, r)))
            {
                _nbrs[flat] = Array.Empty<int>();
                continue;
            }
            var list = new List<int>(4);
            if (c + 1 < _cols && _ml.IsWalkable(new Vector2Int(c + 1, r))) list.Add(flat + 1);
            if (r + 1 < _rows && _ml.IsWalkable(new Vector2Int(c, r + 1))) list.Add(flat + _cols);
            if (c - 1 >= 0    && _ml.IsWalkable(new Vector2Int(c - 1, r))) list.Add(flat - 1);
            if (r - 1 >= 0    && _ml.IsWalkable(new Vector2Int(c, r - 1))) list.Add(flat - _cols);
            _nbrs[flat] = list.ToArray();
        }
    }

    // ── Registration ──────────────────────────────────────────────────────

    public static int Register() => _nextId++;

    public static void Unregister(int id)
    {
        if (_trajs.TryGetValue(id, out var t)) { RemoveFlow(t); _trajs.Remove(id); }
        _goals.Remove(id);
        _currPos.Remove(id);
    }

    public static void SetCurrentPos(int id, int flat) => _currPos[id] = flat;

    // ── Public API ────────────────────────────────────────────────────────

    public static List<int> GetTraj(int id) =>
        _trajs.TryGetValue(id, out var t) ? t : null;

    public static int ToFlat(Vector2Int c) => c.y * _cols + c.x;

    public static Vector2Int FromFlat(int f) => new Vector2Int(f % _cols, f / _cols);

    /// <summary>
    /// Frank-Wolfe:
    ///  1. Replan agent id (start → goal)
    ///  2. Dùng time budget còn lại replan các agent khác (round-robin)
    /// </summary>
    public static void FrankWolfe(int id, int start, int goal, float budgetMs)
    {
        if (!IsReady) return;
        float t0 = Time.realtimeSinceStartup;

        ReplanOne(id, start, goal);

        var ids = new List<int>(_currPos.Keys);
        if (ids.Count <= 1) return;

        int count = 0, maxCount = ids.Count * 3;
        while ((Time.realtimeSinceStartup - t0) * 1000f < budgetMs && count < maxCount)
        {
            int other = ids[count % ids.Count];
            count++;
            if (other == id) continue;
            if (!_goals.TryGetValue(other, out int g))   continue;
            if (!_currPos.TryGetValue(other, out int pos)) continue;
            ReplanOne(other, pos, g);
        }
    }

    // ── Replan one agent ──────────────────────────────────────────────────

    private static void ReplanOne(int id, int start, int goal)
    {
        if (_trajs.TryGetValue(id, out var old)) RemoveFlow(old);

        var traj = AStarFlow(start, goal);
        if (traj != null && traj.Count > 0)
        {
            _trajs[id] = traj;
            _goals[id] = goal;
            AddFlow(traj);
        }
        else
        {
            _trajs[id] = new List<int> { start };
        }
    }

    // ── Flow management ───────────────────────────────────────────────────

    private static void RemoveFlow(List<int> traj)
    {
        for (int j = 1; j < traj.Count; j++)
        {
            int d = GetDir(traj[j - 1], traj[j]);
            if (d >= 0)
                _flow[traj[j - 1] * 4 + d] = Mathf.Max(0, _flow[traj[j - 1] * 4 + d] - 1);
        }
    }

    private static void AddFlow(List<int> traj)
    {
        for (int j = 1; j < traj.Count; j++)
        {
            int d = GetDir(traj[j - 1], traj[j]);
            if (d >= 0)
                _flow[traj[j - 1] * 4 + d]++;
        }
    }

    // d: 0=east, 1=south, 2=west, 3=north  (khớp C++ get_d)
    private static int GetDir(int from, int to)
    {
        int diff = to - from;
        if (diff ==      1) return 0;
        if (diff ==  _cols) return 1;
        if (diff ==     -1) return 2;
        if (diff == -_cols) return 3;
        return -1;
    }

    // ── Flow-aware A* ─────────────────────────────────────────────────────
    //
    // priority = cumOpFlow + cumVertexFlow + g + h(goal)
    //
    // op_flow per edge   = (flow[from→d]+1) × flow[to→(d+2)%4]
    // vertex_flow at to  = (1 + Σflow[to→*] - 1) / 2  = Σflow[to→*] / 2
    //
    // Lazy-deletion heap: push duplicates, skip stale entries khi pop.

    private struct ANode
    {
        public int G, OpFlow, VertexFlow, Parent;
        public int Pri(int hVal) => G + hVal + OpFlow + VertexFlow;
    }

    private static List<int> AStarFlow(int start, int goal)
    {
        if (start == goal) return new List<int> { start };

        int[] h = GetOrBuildH(goal);

        var nodes  = new Dictionary<int, ANode>(_size);
        var heap   = new MinHeap();
        var closed = new HashSet<int>();

        nodes[start] = new ANode { G = 0, OpFlow = 0, VertexFlow = 0, Parent = -1 };
        heap.Push(h[start], start);

        while (heap.Count > 0)
        {
            int heapPri = heap.PeekPri();
            int curr    = heap.Pop();

            if (closed.Contains(curr)) continue;
            var cn = nodes[curr];
            if (cn.Pri(h[curr]) < heapPri) continue; // stale

            closed.Add(curr);
            if (curr == goal) return ReconstructPath(nodes, goal);

            int[] nbrs = _nbrs[curr];
            for (int ni = 0; ni < nbrs.Length; ni++)
            {
                int next = nbrs[ni];
                if (closed.Contains(next)) continue;

                int d    = GetDir(curr, next);
                int newG = cn.G + 1;

                // op_flow: (flow[curr→d]+1) × flow[next→opposite(d)]
                int opEdge = d >= 0 ? (_flow[curr * 4 + d] + 1) * _flow[next * 4 + (d + 2) % 4] : 0;

                // vertex_flow: Σflow[next→*] / 2
                int vsum = 1;
                for (int di = 0; di < 4; di++) vsum += _flow[next * 4 + di];
                int vEdge = (vsum - 1) / 2;

                var cand    = new ANode { G = newG, OpFlow = cn.OpFlow + opEdge, VertexFlow = cn.VertexFlow + vEdge, Parent = curr };
                int candPri = cand.Pri(h[next]);

                bool better = !nodes.TryGetValue(next, out var ex) || candPri < ex.Pri(h[next]);
                if (better)
                {
                    nodes[next] = cand;
                    heap.Push(candPri, next);
                }
            }
        }
        return null;
    }

    private static List<int> ReconstructPath(Dictionary<int, ANode> nodes, int goal)
    {
        var path = new List<int>();
        int curr = goal;
        while (curr >= 0) { path.Add(curr); curr = nodes[curr].Parent; }
        path.Reverse();
        return path;
    }

    // ── Heuristic: reverse BFS từ goal (lazy, cache) ──────────────────────

    private static int[] GetOrBuildH(int goal)
    {
        if (_h.TryGetValue(goal, out var cached)) return cached;

        int[] dist = new int[_size];
        for (int i = 0; i < _size; i++) dist[i] = int.MaxValue / 2;
        dist[goal] = 0;
        var q = new Queue<int>();
        q.Enqueue(goal);
        while (q.Count > 0)
        {
            int curr = q.Dequeue();
            int[] nbrs = _nbrs[curr];
            for (int ni = 0; ni < nbrs.Length; ni++)
            {
                int nb = nbrs[ni];
                if (dist[nb] > dist[curr] + 1)
                {
                    dist[nb] = dist[curr] + 1;
                    q.Enqueue(nb);
                }
            }
        }
        _h[goal] = dist;
        return dist;
    }

    // ── Binary min-heap (priority, flatId) ────────────────────────────────

    private sealed class MinHeap
    {
        private readonly List<int> _pri  = new List<int>();
        private readonly List<int> _id   = new List<int>();
        public int Count => _pri.Count;

        public int PeekPri() => _pri[0];

        public void Push(int pri, int id)
        {
            _pri.Add(pri);
            _id.Add(id);
            SiftUp(_pri.Count - 1);
        }

        public int Pop()
        {
            int top = _id[0];
            int last = _pri.Count - 1;
            _pri[0] = _pri[last]; _id[0] = _id[last];
            _pri.RemoveAt(last);  _id.RemoveAt(last);
            if (_pri.Count > 0) SiftDown(0);
            return top;
        }

        private void SiftUp(int i)
        {
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (_pri[p] <= _pri[i]) break;
                Swap(i, p);
                i = p;
            }
        }

        private void SiftDown(int i)
        {
            int n = _pri.Count;
            while (true)
            {
                int l = 2*i+1, r = 2*i+2, s = i;
                if (l < n && _pri[l] < _pri[s]) s = l;
                if (r < n && _pri[r] < _pri[s]) s = r;
                if (s == i) break;
                Swap(i, s);
                i = s;
            }
        }

        private void Swap(int a, int b)
        {
            int tp = _pri[a]; _pri[a] = _pri[b]; _pri[b] = tp;
            int ti = _id[a];  _id[a]  = _id[b];  _id[b]  = ti;
        }
    }
}
