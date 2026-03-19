using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AutoPlayer — Adaptive Greedy Snake AI.
///
/// STRATEGY:
///   T1  BFS to food (shortest path, step-by-step safety validated).
///           maxD and sample-count scale with fill: aggressive when sparse,
///           conservative when crowded.
///
///   T2  Greedy heuristic scorer.
///           Score each legal direction by fill-adaptive weights:
///             space score = flood-fill reachable / maxSpace  [0,1]
///             food score  = 1 - Manhattan(nb,food) / maxMH  [0,1]
///           Prefer directions where space >= L; unsafe only as last resort.
///
///   EM  Emergency — any in-bounds non-body direction.
/// </summary>
public class AutoPlayer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GridManager     gridManager;
    [SerializeField] private SnakeController snakeController;
    [SerializeField] private FoodSpawner     foodSpawner;
    [SerializeField] private InputReader     inputReader;

    [Header("AI Settings")]
    [SerializeField] private bool autoPlayEnabled = true;
    [SerializeField] private bool debugLog        = false;

    // BFS structures (reused every tick — no GC)
    private readonly Queue<Vector2Int>                  _bfsQ    = new();
    private readonly Dictionary<Vector2Int, Vector2Int> _bfsPrev = new();
    private readonly HashSet<Vector2Int>                _obs     = new();

    // Flood-fill structures (separate from BFS to avoid conflicts)
    private readonly Queue<Vector2Int>   _ffQ   = new();
    private readonly HashSet<Vector2Int> _ffVis = new();
    private readonly HashSet<Vector2Int> _ffObs = new();

    // Temp list for virtual body simulation
    private readonly List<Vector2Int> _virtualBody = new();

    // Body lookup HashSet (rebuilt each tick — O(1) collision checks vs O(n))
    private readonly HashSet<Vector2Int> _bodySet = new();

    // Anti-oscillation: recent head positions (last N ticks)
    private readonly Queue<Vector2Int> _headHistory = new();
    private const int HistoryLen = 60;  // covers one full row traversal on 50×50

    private static readonly Vector2Int[] Dirs =
        { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

    // -------------------------------------------------------------------------

    public bool AutoPlayEnabled { get => autoPlayEnabled; set => autoPlayEnabled = value; }

    public void Initialise(GridManager gm, SnakeController sc,
                           FoodSpawner fs, InputReader ir)
    {
        gridManager     = gm;
        snakeController = sc;
        foodSpawner     = fs;
        inputReader     = ir;
    }

    public void TickAI(Vector2Int currentDir)
    {
        if (!autoPlayEnabled) return;

        // Record head position history for anti-oscillation.
        if (snakeController.OccupiedCells.Count > 0)
        {
            var h = snakeController.OccupiedCells[0];
            _headHistory.Enqueue(h);
            while (_headHistory.Count > HistoryLen) _headHistory.Dequeue();
        }

        inputReader.InjectDirection(ChooseDirection(currentDir));
    }

    // -------------------------------------------------------------------------
    //  Core Decision
    // -------------------------------------------------------------------------

    private Vector2Int ChooseDirection(Vector2Int currentDir)
    {
        var body  = snakeController.OccupiedCells;
        int L     = body.Count;
        var head  = body[0];
        var food  = foodSpawner.FoodPosition;
        int total = gridManager.Width * gridManager.Height;

        // Build body HashSet once per tick (excludes tail — it will move away).
        _bodySet.Clear();
        for (int k = 0; k < L - 1; k++) _bodySet.Add(body[k]);

        // Fill metrics.
        int   freeSpace = total - L;
        float fill      = (float)L / total;

        // The reverse of the current direction is a 180 turn — blocked by InputReader.
        Vector2Int reverseDir = -currentDir;

        // Precompute flood-fill space for each neighbour.
        // Skip the reverse direction (180 turn blocked by InputReader).
        int[] nbSpace  = { -1, -1, -1, -1 };
        int   maxSpace = 0;
        int   bestSpI  = -1;
        for (int i = 0; i < 4; i++)
        {
            if (Dirs[i] == reverseDir) continue;
            var nb = head + Dirs[i];
            if (!gridManager.IsInBounds(nb) || _bodySet.Contains(nb)) continue;
            nbSpace[i] = FloodFillCount(nb, body, skipTail: true);
            if (nbSpace[i] > maxSpace) { maxSpace = nbSpace[i]; bestSpI = i; }
        }

        int SpaceOf(Vector2Int dir)
        { for (int i = 0; i < 4; i++) if (Dirs[i] == dir) return nbSpace[i]; return -1; }

        // "Fully safe" = reachable cells >= snake length (guaranteed survival room).
        // When ANY direction is fully safe, ONLY accept fully-safe ones.
        // When NONE is fully safe (very cramped), fall back to best available.
        bool anyFullySafe = false;
        for (int i = 0; i < 4; i++) if (nbSpace[i] >= L) { anyFullySafe = true; break; }

        bool IsSafe(Vector2Int dir)
        {
            int sp = SpaceOf(dir);
            if (sp <= 0) return false;
            return anyFullySafe ? sp >= L : sp == maxSpace;
        }

        // T1: BFS to food (adaptive step-by-step safety check) ----------------
        var foodPath = BFS(head, food, body, skipTail: true);

        if (foodPath != null && foodPath.Count > 0)
        {
            int D = foodPath.Count;

            // maxD: aggressive at low fill, conservative at high fill.
            // Raised cap to total/2 so BFS is used even for far-away food.
            float shortcutRate = Mathf.Lerp(0.80f, 0.20f, fill);
            int   maxD         = Mathf.Clamp((int)(freeSpace * shortcutRate), 4, total / 2);

            if (D <= maxD)
            {
                var proposed = foodPath[0] - head;
                if (IsSafe(proposed))
                {
                    // ── LOW FILL FAST TRACK ──────────────────────────────────
                    // At < 30% fill the board is wide open. Skip expensive
                    // path simulation and trust BFS directly. This prevents
                    // the simulation from incorrectly rejecting valid paths
                    // near edges and causing the oscillation loop.
                    if (fill < 0.30f)
                    {
                        if (debugLog) Debug.Log($"[AI] T1-FastTrack D={D} fill={fill:P0}");
                        return proposed;
                    }
                    // Adaptive path simulation:
                    // Body state is advanced every step (O(L) Insert+Remove).
                    // FloodFillCount (O(board)) is sampled at most ~3-8 times.
                    // Safety sample count scales with fill:
                    //   low fill  -> 3 checks (space is plentiful)
                    //   high fill -> 8 checks (tight quarters)
                    bool pathSafe    = true;
                    int  maxChecks   = D <= 3 ? 0 : Mathf.FloorToInt(Mathf.Lerp(3f, 8f, fill));
                    int  sampleEvery = maxChecks > 0 ? Mathf.Max(1, D / maxChecks) : D;

                    _virtualBody.Clear();
                    for (int k = 0; k < L; k++) _virtualBody.Add(body[k]);

                    for (int s = 0; s < D && pathSafe; s++)
                    {
                        bool isEating = (s == D - 1);

                        if (!isEating)
                        {
                            // Always advance body (keeps geometry correct for later steps).
                            _virtualBody.Insert(0, foodPath[s]);
                            _virtualBody.RemoveAt(_virtualBody.Count - 1);

                            if (s % sampleEvery == 0 || s == D - 2)
                            {
                                int sp = FloodFillCount(_virtualBody[0], _virtualBody, skipTail: true);
                                if (sp < L)
                                {
                                    pathSafe = false;
                                    if (debugLog) Debug.Log($"[AI] T1 SKIP: choke s={s}/{D} sp={sp}<L={L}");
                                }
                                // Early accept: vast open space — safe without further checks.
                                else if (sp >= freeSpace * 0.8f) break;
                            }
                        }
                        else
                        {
                            // Eating step: _virtualBody = pre-eat state.
                            // food NOT in _virtualBody -> not an obstacle for flood-fill.
                            int sp = FloodFillCount(food, _virtualBody, skipTail: true);
                            if (sp < L + 1)
                            {
                                pathSafe = false;
                                if (debugLog) Debug.Log($"[AI] T1 SKIP: post-eat choke sp={sp} < L+1={L + 1}");
                            }
                        }
                    }

                    if (pathSafe)
                    {
                        if (debugLog) Debug.Log($"[AI] T1 -> D={D} fill={fill:P0}");
                        return proposed;
                    }
                }
            }
        }
        else if (foodPath == null && debugLog)
        {
            Debug.Log($"[AI] Food {food} ENCLOSED (BFS null). Greedy fallback.");
        }

        // T2: Greedy heuristic scorer ------------------------------------------
        // Score every legal direction by fill-adaptive combo of:
        //   space score = flood-fill from that neighbour / maxSpace  [0,1]
        //   food score  = 1 - Manhattan(nb, food) / maxMH           [0,1]
        // Low fill  -> wFood heavy (chase aggressively).
        // High fill -> wSpace heavy (protect survival room).
        // Fully-safe directions (space >= L) always preferred over unsafe.

        int   maxMH  = gridManager.Width + gridManager.Height - 2;
        float wFood  = Mathf.Lerp(0.60f, 0.05f, fill);   // aggressive -> conservative
        float wSpace = 1f - wFood;

        Vector2Int bestSafe   = Vector2Int.zero;
        Vector2Int bestUnsafe = Vector2Int.zero;
        float      bestSafeScore   = float.MinValue;
        float      bestUnsafeScore = float.MinValue;

        foreach (var d in Dirs)
        {
            int sp = SpaceOf(d);
            if (sp <= 0) continue;

            var nb = head + d;
            if (!gridManager.IsInBounds(nb) || _bodySet.Contains(nb)) continue;

            float spaceScore = maxSpace > 0 ? (float)sp / maxSpace : 0f;
            int   mh         = Mathf.Abs(nb.x - food.x) + Mathf.Abs(nb.y - food.y);
            float foodScore  = maxMH   > 0 ? 1f - (float)mh / maxMH : 0f;

            // Anti-oscillation: strongly penalise recently-visited cells.
            float historyPenalty = _headHistory.Contains(nb) ? 0.45f : 0f;

            float score = wSpace * spaceScore + wFood * foodScore - historyPenalty;

            if (IsSafe(d)) { if (score > bestSafeScore)   { bestSafeScore   = score; bestSafe   = d; } }
            else           { if (score > bestUnsafeScore) { bestUnsafeScore = score; bestUnsafe = d; } }
        }

        var chosen = bestSafe != Vector2Int.zero ? bestSafe : bestUnsafe;
        if (chosen != Vector2Int.zero)
        {
            if (debugLog) Debug.Log(
                $"[AI] Greedy dir={chosen} score={Mathf.Max(bestSafeScore,bestUnsafeScore):F3} fill={fill:P0} wF={wFood:F2}");
            return chosen;
        }

        // EMERGENCY: prefer non-body then any in-bounds cell -------------------
        if (debugLog) Debug.LogError("[AI] EMERGENCY: No valid neighbours!");
        if (bestSpI >= 0)
        {
            if (debugLog) Debug.LogError($"[AI] EMERGENCY max-space dir={Dirs[bestSpI]} space={maxSpace}");
            return Dirs[bestSpI];
        }
        foreach (var d in Dirs)
        {
            var nb = head + d;
            if (gridManager.IsInBounds(nb) && !IsInBody(nb, body, includeTail: true)) return d;
        }
        foreach (var d in Dirs)
            if (gridManager.IsInBounds(head + d)) return d;

        return Vector2Int.right;
    }

    // Flood-fill: count reachable cells from start ----------------------------
    private int FloodFillCount(Vector2Int start, IReadOnlyList<Vector2Int> body, bool skipTail)
    {
        _ffObs.Clear();
        int bodyEnd = skipTail ? body.Count - 1 : body.Count;
        for (int k = 0; k < bodyEnd; k++) _ffObs.Add(body[k]);

        if (!gridManager.IsInBounds(start) || _ffObs.Contains(start)) return 0;

        _ffVis.Clear();
        _ffQ.Clear();
        _ffQ.Enqueue(start);
        _ffVis.Add(start);

        while (_ffQ.Count > 0)
        {
            var cur = _ffQ.Dequeue();
            foreach (var d in Dirs)
            {
                var nb = cur + d;
                if (!gridManager.IsInBounds(nb)) continue;
                if (_ffObs.Contains(nb))         continue;
                if (_ffVis.Contains(nb))         continue;
                _ffVis.Add(nb);
                _ffQ.Enqueue(nb);
            }
        }
        return _ffVis.Count;
    }

    // -------------------------------------------------------------------------
    //  BFS
    // -------------------------------------------------------------------------

    private List<Vector2Int> BFS(Vector2Int start, Vector2Int goal,
                                  IReadOnlyList<Vector2Int> body, bool skipTail)
    {
        if (start == goal) return new List<Vector2Int>();

        _obs.Clear();
        int bodyEnd = skipTail ? body.Count - 1 : body.Count;
        for (int k = 0; k < bodyEnd; k++) _obs.Add(body[k]);

        _bfsQ.Clear();
        _bfsPrev.Clear();
        _bfsPrev[start] = start;
        _bfsQ.Enqueue(start);

        while (_bfsQ.Count > 0)
        {
            var cur = _bfsQ.Dequeue();
            if (cur == goal) return Reconstruct(start, goal);

            foreach (var d in Dirs)
            {
                var nb = cur + d;
                if (!gridManager.IsInBounds(nb)) continue;
                if (_obs.Contains(nb))           continue;
                if (_bfsPrev.ContainsKey(nb))    continue;
                _bfsPrev[nb] = cur;
                _bfsQ.Enqueue(nb);
            }
        }
        return null;
    }

    private List<Vector2Int> Reconstruct(Vector2Int start, Vector2Int goal)
    {
        var path = new List<Vector2Int>();
        if (start == goal) return path;

        var cur   = goal;
        int guard = gridManager.Width * gridManager.Height + 2;
        while (cur != start && guard-- > 0)
        {
            path.Add(cur);
            if (!_bfsPrev.TryGetValue(cur, out cur)) break;
        }
        path.Reverse();
        return path;
    }

    // -------------------------------------------------------------------------
    //  Helpers
    // -------------------------------------------------------------------------

    /// <summary>Returns true if pos is occupied by any body segment.
    /// includeTail: whether to count the last segment (often moving away).</summary>
    private static bool IsInBody(Vector2Int pos, IReadOnlyList<Vector2Int> body,
                                  bool includeTail)
    {
        int count = includeTail ? body.Count : body.Count - 1;
        for (int k = 0; k < count; k++)
            if (body[k] == pos) return true;
        return false;
    }
}
