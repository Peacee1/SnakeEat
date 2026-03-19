using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AutoPlayer — Adaptive Greedy Snake AI (Optimised).
///
/// STRATEGY:
///   T1  BFS to food (shortest path, step-by-step safety validated).
///   T2  Greedy heuristic: fill-adaptive space + food weighted scorer.
///   EM  Emergency: any reachable direction.
///
/// OPTIMISATIONS vs previous version:
///   1. Single shared _obs HashSet (built once/tick, excludes tail).
///      Eliminates duplicate O(L) rebuilds inside BFS and FloodFill.
///   2. Incremental _simObs in T1 simulation: O(1)/step instead of
///      O(L) FloodFillCount-internal rebuild per sampled step.
///   3. _headHistorySet — O(1) anti-oscillation lookup (was O(N) Queue).
///   4. BFS + FloodFill accept pre-built HashSet, no internal rebuild.
///   5. Fixed eating-step: tail does NOT move on eat → skipTail was wrong.
///   6. Fixed T2 sentinel: bool flags replace Vector2Int.zero guard,
///      eliminating silent bug when best direction points to cell (0,0).
///   7. maxChecks minimum 1 → clean sampleEvery (no divide-by-zero path).
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

    // BFS (reused each tick — no GC allocs)
    private readonly Queue<Vector2Int>                  _bfsQ    = new();
    private readonly Dictionary<Vector2Int, Vector2Int> _bfsPrev = new();

    // Flood-fill (reused each tick)
    private readonly Queue<Vector2Int>   _ffQ   = new();
    private readonly HashSet<Vector2Int> _ffVis = new();

    // Shared obstacle set — built ONCE per tick (body[0..L-2], excludes tail).
    // Passed directly into BFS and all FloodFill calls; never rebuilt inside them.
    private readonly HashSet<Vector2Int> _obs = new();

    // Simulation obstacle set — updated incrementally during T1 path simulation.
    private readonly HashSet<Vector2Int> _simObs = new();

    // Anti-oscillation: head history queue + O(1) lookup set.
    private readonly Queue<Vector2Int>   _headHistory    = new();
    private readonly HashSet<Vector2Int> _headHistorySet = new();
    private const int HistoryLen = 60;

    // Per-direction space cache (index matches Dirs[])
    private readonly int[] _nbSpace = new int[4];

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

        var body = snakeController.OccupiedCells;

        // Track head history for anti-oscillation (O(1) set ops).
        if (body.Count > 0)
        {
            var h = body[0];
            _headHistory.Enqueue(h);
            _headHistorySet.Add(h);
            if (_headHistory.Count > HistoryLen)
                // Note: if same cell appears twice in history, Remove may clear
                // it one tick early — that's conservative (extra penalty), never wrong.
                _headHistorySet.Remove(_headHistory.Dequeue());
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

        // ── Build shared obstacle set ONCE (excludes tail — it will move away) ──
        _obs.Clear();
        for (int k = 0; k < L - 1; k++) _obs.Add(body[k]);

        int   freeSpace = total - L;
        float fill      = (float)L / total;

        Vector2Int reverseDir = -currentDir;

        // ── Flood-fill space for each candidate neighbour ──────────────────────
        int maxSpace = 0;
        int bestSpI  = -1;
        for (int i = 0; i < 4; i++)
        {
            _nbSpace[i] = -1;
            if (Dirs[i] == reverseDir) continue;
            var nb = head + Dirs[i];
            if (!gridManager.IsInBounds(nb) || _obs.Contains(nb)) continue;
            _nbSpace[i] = FloodFillCount(nb, _obs);
            if (_nbSpace[i] > maxSpace) { maxSpace = _nbSpace[i]; bestSpI = i; }
        }

        int SpaceOf(Vector2Int dir)
        { for (int i = 0; i < 4; i++) if (Dirs[i] == dir) return _nbSpace[i]; return -1; }

        bool anyFullySafe = false;
        for (int i = 0; i < 4; i++) if (_nbSpace[i] >= L) { anyFullySafe = true; break; }

        bool IsSafe(Vector2Int dir)
        {
            int sp = SpaceOf(dir);
            if (sp <= 0) return false;
            if (anyFullySafe) return sp >= L;
            // When no direction is fully safe, still require meaningful space
            // to prevent committing to narrow corridors or 1-cell pockets.
            // minSafe = L/4 (or 8 minimum) — corridors shorter than this are traps.
            int minSafe = Mathf.Max(8, L >> 2);
            return sp == maxSpace && sp >= minSafe;
        }

        // ── T1: BFS to food ────────────────────────────────────────────────────
        var foodPath = BFS(head, food, _obs);

        if (foodPath != null && foodPath.Count > 0)
        {
            int D = foodPath.Count;

            float shortcutRate = Mathf.Lerp(0.80f, 0.20f, fill);
            int   maxD         = Mathf.Clamp((int)(freeSpace * shortcutRate), 4, total / 2);

            if (D <= maxD)
            {
                var proposed = foodPath[0] - head;
                if (IsSafe(proposed))
                {
                    // ── Path density pre-filter ──────────────────────────────
                    // Sample up to 5 points along the BFS path. At each point,
                    // count obstacles (body OR out-of-bounds wall) within
                    // Manhattan distance 3 (diamond, 25 cells total).
                    // Threshold 50 %: catches wall-hugging serpentine fill              
                    //   e.g. path along x=0, column x=1 body:
                    //        9 OOB + 5 body = 14/25 = 56 % > 50 % → REJECT.
                    //   path through open interior at low fill: ~10–20 % → PASS.
                    // Cost: 5 × 25 = 125 lookups — still negligible.
                    bool densityOk  = true;
                    {
                        int numSmp  = Mathf.Max(1, Mathf.Min(5, D));
                        int smpStep = Mathf.Max(1, D / numSmp);
                        float sumDensity = 0f;
                        for (int si = 0; si < D; si += smpStep)
                        {
                            var pos = foodPath[si];
                            int total25 = 0, blocked = 0;
                            for (int dx = -3; dx <= 3; dx++)
                            for (int dy = -3; dy <= 3; dy++)
                            {
                                if (Mathf.Abs(dx) + Mathf.Abs(dy) > 3) continue;
                                total25++;
                                var nb = pos + new Vector2Int(dx, dy);
                                if (!gridManager.IsInBounds(nb) || _obs.Contains(nb)) blocked++;
                            }
                            sumDensity += total25 > 0 ? (float)blocked / total25 : 0f;
                        }
                        float avgDensity = sumDensity / numSmp;
                        if (avgDensity > 0.50f)
                        {
                            densityOk = false;
                            if (debugLog) Debug.Log($"[AI] T1 SKIP: path too dense avg={avgDensity:P0}");
                        }
                    }

                    if (!densityOk) { /* fall through to T1.5 / T2 */ }
                    else
                    {
                    // ── Adaptive path simulation (incremental _simObs) ────────
                    _simObs.Clear();
                    foreach (var c in _obs) _simObs.Add(c);


                    bool pathSafe   = true;
                    // Adaptive sampling: 5–12 flood-fill checks across the path.
                    // sampleEvery=1 (check every step) was O(D × board) per tick
                    // → fps drops. Now that FloodFill is correct, 5–12 samples
                    // still catches corridor closures at reasonable granularity.
                    // Low fill → 5 samples (space is plentiful, fewer checks needed).
                    // High fill → 12 samples (tight quarters, denser checking).
                    int maxSamples  = D <= 3 ? 1 : Mathf.FloorToInt(Mathf.Lerp(5f, 12f, fill));
                    int sampleEvery = Mathf.Max(1, D / maxSamples);

                    for (int s = 0; s < D && pathSafe; s++)
                    {
                        bool isEating = (s == D - 1);

                        if (!isEating)
                        {
                            var newHead = foodPath[s];

                            // ── FloodFill BEFORE updating _simObs ──────────────────
                            // newHead is a free cell at this point (not yet in obs).
                            // Adding it FIRST then flood-filling from it yields 0
                            // because FloodFillCount returns 0 if start ∈ obs.
                            if (s % sampleEvery == 0 || s == D - 2)
                            {
                                int sp = FloodFillCount(newHead, _simObs);
                                if (sp < L)
                                {
                                    pathSafe = false;
                                    if (debugLog) Debug.Log($"[AI] T1 SKIP: choke s={s}/{D} sp={sp}<L={L}");
                                }
                                else if (sp >= freeSpace * 0.8f) break; // early accept
                            }

                            // Now commit newHead to obstacles and release the freed tail slot.
                            _simObs.Add(newHead);
                            if (s < L - 1)
                                _simObs.Remove(body[L - 2 - s]);
                            else
                                _simObs.Remove(foodPath[s - (L - 1)]);
                        }
                        else
                        {
                            // Eating step: snake grows — tail does NOT move.
                            // Build post-eat obstacle set:
                            //   path cells foodPath[0..D-2] (NOT food=foodPath[D-1],
                            //     which is the new head — must NOT be its own obstacle)
                            //   body[0..L-D-1] (body segments not yet replaced by path)
                            _simObs.Clear();
                            for (int k = 0; k < D - 1; k++) _simObs.Add(foodPath[k]);
                            for (int k = 0; k < L - D; k++) _simObs.Add(body[k]);

                            int sp = FloodFillCount(food, _simObs);
                            if (sp < L + 1)
                            {
                                pathSafe = false;
                                if (debugLog) Debug.Log($"[AI] T1 SKIP: post-eat choke sp={sp}<L+1={L + 1}");
                            }
                        }
                    }

                    if (pathSafe)
                    {
                        // ── Tail-escape check ─────────────────────────────────
                        // Always rebuild post-eat _simObs (simulation may have
                        // early-accepted, leaving _simObs in intermediate state).
                        // food = foodPath[D-1] excluded from obs — it's the new head.
                        _simObs.Clear();
                        for (int k = 0; k < D - 1; k++) _simObs.Add(foodPath[k]);
                        for (int k = 0; k < L - D;  k++) _simObs.Add(body[k]);

                        if (BFS(food, body[L - 1], _simObs) == null)
                        {
                            pathSafe = false;
                            if (debugLog) Debug.Log("[AI] T1 SKIP: no tail-escape from post-eat position");
                        }
                    }

                    if (pathSafe)
                    {
                        if (debugLog) Debug.Log($"[AI] T1 -> D={D} fill={fill:P0}");
                        return proposed;
                    }
                    } // end else (densityOk)
                }
            }
        }
        else if (foodPath == null && debugLog)
        {
            Debug.Log($"[AI] Food {food} ENCLOSED (BFS null). Greedy/tail fallback.");
        }

        // ── T1.5: Tail-follow when cramped ────────────────────────────────────────
        // Activates when no direction has flood-fill >= L (snake is in a tight spot).
        // The tail always moves away each tick, so BFS-ing toward it naturally
        // unwinds the snake toward open space — avoiding spiral self-traps.
        // _obs excludes the tail, so BFS can route through where the tail will vacate.
        if (!anyFullySafe && maxSpace > 0)
        {
            var tail     = body[L - 1];
            var tailPath = BFS(head, tail, _obs);
            if (tailPath != null && tailPath.Count > 0)
            {
                var tailDir = tailPath[0] - head;
                if (SpaceOf(tailDir) > 0)
                {
                    if (debugLog) Debug.Log($"[AI] T1.5 Tail-chase dir={tailDir} maxSp={maxSpace} L={L}");
                    return tailDir;
                }
            }
        }

        // ── T2: Greedy heuristic scorer ────────────────────────────────────────
        int   maxMH  = gridManager.Width + gridManager.Height - 2;
        float wFood  = Mathf.Lerp(0.60f, 0.05f, fill);
        float wSpace = 1f - wFood;

        var upcoming = foodSpawner.UpcomingFoodPositions;

        // Use bool flags instead of Vector2Int.zero sentinel —
        // fixes silent bug when best direction points to grid cell (0, 0).
        bool       hasSafe   = false, hasUnsafe = false;
        Vector2Int bestSafe  = default, bestUnsafe = default;
        float      scoreSafe = float.MinValue, scoreUnsafe = float.MinValue;

        foreach (var d in Dirs)
        {
            int sp = SpaceOf(d);
            if (sp <= 0) continue;

            var nb = head + d;
            if (!gridManager.IsInBounds(nb) || _obs.Contains(nb)) continue;

            float spaceScore     = maxSpace > 0 ? (float)sp / maxSpace : 0f;
            int   mh             = Mathf.Abs(nb.x - food.x) + Mathf.Abs(nb.y - food.y);
            float foodScore      = maxMH   > 0 ? 1f - (float)mh / maxMH : 0f;
            // Upcoming-food direction bonus (5 % weight) — soft bias only,
            // so it never overrides the primary food or space incentives.
            float upcomingBonus  = 0f;
            if (upcoming != null)
                foreach (var uf in upcoming)
                {
                    int umh = Mathf.Abs(nb.x - uf.x) + Mathf.Abs(nb.y - uf.y);
                    upcomingBonus += maxMH > 0 ? 0.05f * (1f - (float)umh / maxMH) : 0f;
                }
            float histPenalty    = _headHistorySet.Contains(nb) ? 0.45f : 0f;
            float score          = wSpace * spaceScore + wFood * foodScore + upcomingBonus - histPenalty;

            if (IsSafe(d))
            { if (!hasSafe   || score > scoreSafe)   { hasSafe   = true; scoreSafe   = score; bestSafe   = d; } }
            else
            { if (!hasUnsafe || score > scoreUnsafe) { hasUnsafe = true; scoreUnsafe = score; bestUnsafe = d; } }
        }

        if (hasSafe || hasUnsafe)
        {
            var chosen = hasSafe ? bestSafe : bestUnsafe;
            if (debugLog) Debug.Log(
                $"[AI] Greedy dir={chosen} score={Mathf.Max(scoreSafe, scoreUnsafe):F3} fill={fill:P0} wF={wFood:F2}");
            return chosen;
        }

        // ── EMERGENCY ──────────────────────────────────────────────────────────
        if (debugLog) Debug.LogError("[AI] EMERGENCY: No valid neighbours!");
        if (bestSpI >= 0)
        {
            if (debugLog) Debug.LogError($"[AI] EMERGENCY max-space dir={Dirs[bestSpI]} space={maxSpace}");
            return Dirs[bestSpI];
        }
        foreach (var d in Dirs)
        {
            if (d == reverseDir) continue;
            var nb = head + d;
            if (gridManager.IsInBounds(nb) && !_obs.Contains(nb)) return d;
        }
        // Last resort: any in-bounds non-reverse direction (even if body).
        // Skipping reverseDir because InputReader will block it anyway,
        // causing the snake to continue in currentDir into a wall.
        foreach (var d in Dirs)
        {
            if (d == reverseDir) continue;
            if (gridManager.IsInBounds(head + d)) return d;
        }
        // Truly cornered with no forward options — reverse is the only cell.
        return reverseDir;
    }

    // ── Flood-fill: count reachable cells from start ───────────────────────────
    // Accepts a pre-built obstacle set — zero internal allocs.
    private int FloodFillCount(Vector2Int start, HashSet<Vector2Int> obs)
    {
        if (!gridManager.IsInBounds(start) || obs.Contains(start)) return 0;

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
                if (obs.Contains(nb))            continue;
                if (_ffVis.Contains(nb))         continue;
                _ffVis.Add(nb);
                _ffQ.Enqueue(nb);
            }
        }
        return _ffVis.Count;
    }

    // ── BFS ───────────────────────────────────────────────────────────────────
    // Accepts a pre-built obstacle set — zero internal allocs.
    private List<Vector2Int> BFS(Vector2Int start, Vector2Int goal,
                                  HashSet<Vector2Int> obs)
    {
        if (start == goal) return new List<Vector2Int>();

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
                if (obs.Contains(nb))            continue;
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
}
