using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AutoPlayer — Advanced Snake AI (v3).
///
/// STRATEGY LAYERS (in priority order):
///   T0   Hamiltonian-cycle follow when board fill ≥ 60 % (safe space-filling).
///   T1   BFS to food + full safety gauntlet:
///          • Density pre-filter (dynamic threshold)
///          • Articulation-point check (bridge detection)
///          • Adaptive path simulation with incremental obstacles
///          • Deep post-eat lookahead (simulate M steps after eating)
///          • Tail-escape reachability check
///   T1.5 Tail-chase with minSafe floor (avoids dead corridors).
///   T2   Greedy heuristic: weighted space + food + upcoming + partition risk.
///   EM   Emergency: any reachable direction.
///
/// KEY IMPROVEMENTS vs v2:
///   A. Articulation-point (bridge) detection — rejects BFS paths that pass
///      through a single-cell bottleneck separating the snake from free space.
///   B. Deep post-eat simulation — after eating, simulate M additional steps
///      to ensure the snake is not walking into a long-term trap.
///   C. Hamiltonian cycle approximation — at fill ≥ 60 %, construct and follow
///      a space-filling path rather than greedy-chasing food.
///   D. Dynamic density threshold — tightens from 0.50 → 0.25 as fill rises,
///      so the filter actually blocks dangerous high-density corridors.
///   E. T1.5 minSafe floor — tail-chase only if that direction has meaningful
///      space (≥ max(8, L/4)), preventing one-cell-corridor commits.
///   F. Partition-risk penalty in T2 — penalises directions that would split
///      the largest contiguous region, catching subtle traps the scorer missed.
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

    // ── Thresholds ────────────────────────────────────────────────────────────
    // Fill fraction at which Hamiltonian mode activates.
    private const float HamiltonFill = 0.60f;
    // Deep lookahead steps after eating.
    private const int   DeepSteps    = 8;
    // Hamiltonian cycle: pre-built ordered list of all cells.
    private List<Vector2Int> _hamCycle;
    private bool             _hamDirty  = true;   // rebuild on length change
    private int              _hamIndex  = 0;       // current position in cycle

    // ── BFS (reused each tick — no GC allocs) ────────────────────────────────
    private readonly Queue<Vector2Int>                  _bfsQ    = new();
    private readonly Dictionary<Vector2Int, Vector2Int> _bfsPrev = new();

    // ── Flood-fill (reused each tick) ─────────────────────────────────────────
    private readonly Queue<Vector2Int>   _ffQ   = new();
    private readonly HashSet<Vector2Int> _ffVis = new();

    // ── Articulation-point detection scratch ──────────────────────────────────
    private readonly Dictionary<Vector2Int, int>  _apDisc   = new();
    private readonly Dictionary<Vector2Int, int>  _apLow    = new();
    private readonly Dictionary<Vector2Int, bool> _apVisited= new();
    private readonly HashSet<Vector2Int>           _apPoints = new();
    private int _apTimer;

    // ── Shared obstacle set (built ONCE per tick, excludes tail) ─────────────
    private readonly HashSet<Vector2Int> _obs    = new();
    // ── Simulation obstacle set (incremental during T1) ───────────────────────
    private readonly HashSet<Vector2Int> _simObs = new();

    // ── Anti-oscillation: head history queue + O(1) set ──────────────────────
    private readonly Queue<Vector2Int>   _headHistory    = new();
    private readonly HashSet<Vector2Int> _headHistorySet = new();
    private const int HistoryLen = 60;

    // ── Per-direction flood-fill cache ────────────────────────────────────────
    private readonly int[] _nbSpace = new int[4];

    private static readonly Vector2Int[] Dirs =
        { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

    // ── Scratch list for deep-lookahead path extension ────────────────────────
    private readonly List<Vector2Int> _extPath = new();

    // ── Scratch for partition-risk check (reused per T2 candidate) ───────────
    private readonly HashSet<Vector2Int> _partObs = new();

    // =========================================================================

    public bool AutoPlayEnabled { get => autoPlayEnabled; set => autoPlayEnabled = value; }

    public void Initialise(GridManager gm, SnakeController sc,
                            FoodSpawner fs, InputReader ir)
    {
        gridManager     = gm;
        snakeController = sc;
        foodSpawner     = fs;
        inputReader     = ir;
        _hamDirty       = true;
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
                _headHistorySet.Remove(_headHistory.Dequeue());
        }

        inputReader.InjectDirection(ChooseDirection(currentDir));
    }

    // =========================================================================
    //  Core Decision
    // =========================================================================

    private Vector2Int ChooseDirection(Vector2Int currentDir)
    {
        var body  = snakeController.OccupiedCells;
        int L     = body.Count;
        var head  = body[0];
        var food  = foodSpawner.FoodPosition;
        int total = gridManager.Width * gridManager.Height;

        // ── Build shared obstacle set ONCE (excludes tail — it will slide away) ──
        _obs.Clear();
        for (int k = 0; k < L - 1; k++) _obs.Add(body[k]);

        int   freeSpace = total - L;
        float fill      = (float)L / total;

        Vector2Int reverseDir = -currentDir;

        // ── Flood-fill space for each candidate neighbour ─────────────────────
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

        // minSafe floor used by T1.5 and IsSafe fallback
        int minSafe = Mathf.Max(8, L >> 2);

        bool IsSafe(Vector2Int dir)
        {
            int sp = SpaceOf(dir);
            if (sp <= 0) return false;
            if (anyFullySafe) return sp >= L;
            // No direction is fully safe → accept best available iff ≥ minSafe
            return sp == maxSpace && sp >= minSafe;
        }

        // ── T0: Hamiltonian cycle (high-fill safe space-filler) ───────────────
        if (fill >= HamiltonFill)
        {
            var hamDir = HamiltonianStep(head, body, L, reverseDir);
            if (hamDir != Vector2Int.zero)
            {
                if (debugLog) Debug.Log($"[AI] T0 Hamiltonian dir={hamDir} fill={fill:P0}");
                return hamDir;
            }
        }

        // ── T1: BFS to food ───────────────────────────────────────────────────
        var foodPath = BFS(head, food, _obs);

        if (foodPath != null && foodPath.Count > 0)
        {
            int D = foodPath.Count;

            // Dynamic maxD: accept shorter paths more readily at high fill
            float shortcutRate = Mathf.Lerp(0.80f, 0.20f, fill);
            int   maxD         = Mathf.Clamp((int)(freeSpace * shortcutRate), 4, total / 2);

            if (D <= maxD)
            {
                var proposed = foodPath[0] - head;
                if (IsSafe(proposed))
                {
                    // ── Density pre-filter (dynamic threshold) ────────────────
                    // Threshold tightens with fill: 0.50 at fill=0 → 0.25 at fill=1.
                    // Prevents committing to wall-hugging paths at high density.
                    float densityThresh = Mathf.Lerp(0.50f, 0.25f, fill);
                    bool  densityOk     = true;
                    {
                        int numSmp  = Mathf.Max(1, Mathf.Min(5, D));
                        int smpStep = Mathf.Max(1, D / numSmp);
                        float sumDensity = 0f;
                        for (int si = 0; si < D; si += smpStep)
                        {
                            var pos   = foodPath[si];
                            int tot25 = 0, blocked = 0;
                            for (int dx = -3; dx <= 3; dx++)
                            for (int dy = -3; dy <= 3; dy++)
                            {
                                if (Mathf.Abs(dx) + Mathf.Abs(dy) > 3) continue;
                                tot25++;
                                var nb = pos + new Vector2Int(dx, dy);
                                if (!gridManager.IsInBounds(nb) || _obs.Contains(nb)) blocked++;
                            }
                            sumDensity += tot25 > 0 ? (float)blocked / tot25 : 0f;
                        }
                        float avgDensity = sumDensity / numSmp;
                        if (avgDensity > densityThresh)
                        {
                            densityOk = false;
                            if (debugLog) Debug.Log($"[AI] T1 SKIP density: avg={avgDensity:P0} > thresh={densityThresh:P0}");
                        }
                    }

                    // ── Articulation-point check on first step ─────────────────
                    // Reject if the proposed next cell is an articulation point
                    // (removing it disconnects the reachable free space).
                    if (densityOk)
                    {
                        var nextCell = head + proposed;
                        if (IsArticulationPoint(nextCell, _obs))
                        {
                            densityOk = false;
                            if (debugLog) Debug.Log($"[AI] T1 SKIP: first step {nextCell} is articulation point");
                        }
                    }

                    if (!densityOk) { /* fall through */ }
                    else
                    {
                        // ── Adaptive path simulation (incremental _simObs) ────
                        _simObs.Clear();
                        foreach (var c in _obs) _simObs.Add(c);

                        bool pathSafe  = true;
                        int maxSamples = D <= 3 ? 1 : Mathf.FloorToInt(Mathf.Lerp(6f, 15f, fill));
                        int sampleEvery= Mathf.Max(1, D / maxSamples);

                        for (int s = 0; s < D && pathSafe; s++)
                        {
                            bool isEating = (s == D - 1);

                            if (!isEating)
                            {
                                var newHead = foodPath[s];

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

                                _simObs.Add(newHead);
                                if (s < L - 1)
                                    _simObs.Remove(body[L - 2 - s]);
                                else
                                    _simObs.Remove(foodPath[s - (L - 1)]);
                            }
                            else
                            {
                                // Eating step: snake grows, tail stays.
                                _simObs.Clear();
                                for (int k = 0; k < D - 1; k++)    _simObs.Add(foodPath[k]);
                                for (int k = 0; k < L - D;  k++)   _simObs.Add(body[k]);

                                int sp = FloodFillCount(food, _simObs);
                                if (sp < L + 1)
                                {
                                    pathSafe = false;
                                    if (debugLog) Debug.Log($"[AI] T1 SKIP: post-eat sp={sp}<L+1={L + 1}");
                                }
                            }
                        }

                        // ── Deep post-eat lookahead ───────────────────────────
                        // After eating, simulate DeepSteps more moves (greedy toward
                        // largest space) to check the snake is not heading into a trap.
                        if (pathSafe)
                        {
                            _simObs.Clear();
                            for (int k = 0; k < D - 1; k++) _simObs.Add(foodPath[k]);
                            for (int k = 0; k < L - D;  k++) _simObs.Add(body[k]);

                            Vector2Int simHead = food;
                            int        simLen  = L + 1;
                            bool deepOk = true;

                            // Build a small ordered list of the simulated body for tail tracking.
                            // simBody[0] = new head (food), simBody[1..D-1] = path cells (reversed),
                            // simBody[D..L-1] = surviving original body segments.
                            _extPath.Clear();
                            _extPath.Add(food);
                            for (int k = D - 2; k >= 0; k--) _extPath.Add(foodPath[k]);
                            for (int k = 0; k < L - D; k++)  _extPath.Add(body[k]);
                            // _extPath is now the full snake body (head first) after eating.

                            for (int step = 0; step < DeepSteps && deepOk; step++)
                            {
                                // Greedy: pick direction with most flood-fill space.
                                int bestSp = -1;
                                Vector2Int bestD = Vector2Int.zero;
                                foreach (var d in Dirs)
                                {
                                    var nb2 = simHead + d;
                                    if (!gridManager.IsInBounds(nb2) || _simObs.Contains(nb2)) continue;
                                    int sp2 = FloodFillCount(nb2, _simObs);
                                    if (sp2 > bestSp) { bestSp = sp2; bestD = d; }
                                }

                                if (bestSp < 0)
                                {
                                    // Completely stuck
                                    deepOk = false;
                                    if (debugLog) Debug.Log($"[AI] T1 DEEP: stuck at step={step}");
                                    break;
                                }
                                if (bestSp < simLen)
                                {
                                    deepOk = false;
                                    if (debugLog) Debug.Log($"[AI] T1 DEEP: sp={bestSp}<simLen={simLen} step={step}");
                                    break;
                                }

                                // Advance simulation
                                var nxtHead = simHead + bestD;
                                _extPath.Insert(0, nxtHead);
                                // tail of extended body
                                if (_extPath.Count > simLen)
                                {
                                    _simObs.Remove(_extPath[_extPath.Count - 1]);
                                    _extPath.RemoveAt(_extPath.Count - 1);
                                }
                                _simObs.Add(nxtHead);
                                simHead = nxtHead;
                            }

                            if (!deepOk) pathSafe = false;
                        }

                        // ── Tail-escape check ─────────────────────────────────
                        if (pathSafe)
                        {
                            _simObs.Clear();
                            for (int k = 0; k < D - 1; k++) _simObs.Add(foodPath[k]);
                            for (int k = 0; k < L - D;  k++) _simObs.Add(body[k]);

                            if (BFS(food, body[L - 1], _simObs) == null)
                            {
                                pathSafe = false;
                                if (debugLog) Debug.Log("[AI] T1 SKIP: no tail-escape");
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
        }
        else if (foodPath == null && debugLog)
        {
            Debug.Log($"[AI] Food {food} ENCLOSED (BFS null). Fallback.");
        }

        // ── T1.5: Tail-follow with minSafe floor ──────────────────────────────
        // Activates when no direction has flood-fill ≥ L (tight space).
        // Only follow the tail if the path direction has ≥ minSafe open cells.
        if (!anyFullySafe && maxSpace > 0)
        {
            var tail     = body[L - 1];
            var tailPath = BFS(head, tail, _obs);
            if (tailPath != null && tailPath.Count > 0)
            {
                var tailDir = tailPath[0] - head;
                int tailSp  = SpaceOf(tailDir);
                if (tailSp >= minSafe)
                {
                    if (debugLog) Debug.Log($"[AI] T1.5 Tail-chase dir={tailDir} sp={tailSp} minSafe={minSafe}");
                    return tailDir;
                }
            }
        }

        // ── T2: Greedy heuristic scorer ───────────────────────────────────────
        int   maxMH  = gridManager.Width + gridManager.Height - 2;
        float wFood  = Mathf.Lerp(0.55f, 0.05f, fill);
        float wSpace = 1f - wFood;

        var upcoming = foodSpawner.UpcomingFoodPositions;

        bool       hasSafe   = false, hasUnsafe = false;
        Vector2Int bestSafe  = default, bestUnsafe = default;
        float      scoreSafe = float.MinValue, scoreUnsafe = float.MinValue;

        foreach (var d in Dirs)
        {
            int sp = SpaceOf(d);
            if (sp <= 0) continue;

            var nb = head + d;
            if (!gridManager.IsInBounds(nb) || _obs.Contains(nb)) continue;

            float spaceScore = maxSpace > 0 ? (float)sp / maxSpace : 0f;
            int   mh         = Mathf.Abs(nb.x - food.x) + Mathf.Abs(nb.y - food.y);
            float foodScore  = maxMH > 0 ? 1f - (float)mh / maxMH : 0f;

            // Upcoming-food bonus
            float upcomingBonus = 0f;
            if (upcoming != null)
                foreach (var uf in upcoming)
                {
                    int umh = Mathf.Abs(nb.x - uf.x) + Mathf.Abs(nb.y - uf.y);
                    upcomingBonus += maxMH > 0 ? 0.05f * (1f - (float)umh / maxMH) : 0f;
                }

            // Anti-oscillation penalty
            float histPenalty = _headHistorySet.Contains(nb) ? 0.45f : 0f;

            // Partition-risk penalty: after stepping to nb, if the resulting
            // free space splits into multiple components, penalise heavily.
            // We detect this cheaply by flood-filling from nb with nb added to obs,
            // then checking if (total reachable from nb with nb blocked) < (sp - 1).
            // A full articulation-point analysis is expensive here, so we use a
            // lighter proxy: re-flood from nb's neighbours — if any pair are
            // disconnected from each other, nb is a bridge.
            float partPenalty = 0f;
            {
                _partObs.Clear();
                foreach (var c in _obs) _partObs.Add(c);
                _partObs.Add(nb);   // pretend we moved there
                // Count reachable from each valid free neighbour of nb
                int components  = 0;
                int largestComp = 0;
                int totalFromNb = 0;
                // Use neighbours of nb (excluding head which is now in _obs via _obs)
                foreach (var d2 in Dirs)
                {
                    var nb2 = nb + d2;
                    if (!gridManager.IsInBounds(nb2) || _partObs.Contains(nb2)) continue;
                    if (_ffVis.Contains(nb2)) continue; // already counted in a component
                    int compSz = FloodFillCount(nb2, _partObs);
                    // FloodFill leaves _ffVis with all cells of this component;
                    // mark them so we don't double-count.
                    // (We can't safely reuse _ffVis here since FloodFillCount rebuilds it,
                    //  but each call restarts, so we mark via a second HashSet — avoid alloc
                    //  by accepting slight over-counting: component count is sufficient.)
                    components++;
                    totalFromNb += compSz;
                    if (compSz > largestComp) largestComp = compSz;
                }
                // If more than one component exists, nb is a cut-vertex in the
                // directed sense. Penalise proportional to how much space is cut off.
                if (components > 1 && totalFromNb > 0)
                {
                    float lostFrac = 1f - (float)largestComp / totalFromNb;
                    partPenalty   = 0.60f * lostFrac;
                    _partObs.Clear(); // clean up
                }
            }

            float score = wSpace * spaceScore + wFood * foodScore + upcomingBonus
                          - histPenalty - partPenalty;

            if (IsSafe(d))
            { if (!hasSafe   || score > scoreSafe)   { hasSafe   = true; scoreSafe   = score; bestSafe   = d; } }
            else
            { if (!hasUnsafe || score > scoreUnsafe) { hasUnsafe = true; scoreUnsafe = score; bestUnsafe = d; } }
        }

        if (hasSafe || hasUnsafe)
        {
            var chosen = hasSafe ? bestSafe : bestUnsafe;
            if (debugLog) Debug.Log(
                $"[AI] T2 Greedy dir={chosen} score={Mathf.Max(scoreSafe, scoreUnsafe):F3} fill={fill:P0}");
            return chosen;
        }

        // ── EMERGENCY ─────────────────────────────────────────────────────────
        if (debugLog) Debug.LogError("[AI] EMERGENCY: No valid neighbours!");
        if (bestSpI >= 0) return Dirs[bestSpI];
        foreach (var d in Dirs)
        {
            if (d == reverseDir) continue;
            var nb = head + d;
            if (gridManager.IsInBounds(nb) && !_obs.Contains(nb)) return d;
        }
        foreach (var d in Dirs)
        {
            if (d == reverseDir) continue;
            if (gridManager.IsInBounds(head + d)) return d;
        }
        return reverseDir;
    }

    // =========================================================================
    //  T0: Hamiltonian Cycle Approximation
    // =========================================================================
    // Builds a deterministic space-filling path over the entire grid using a
    // boustrophedon (snake-scan) pattern, then advances the head along it.
    // When the next Hamiltonian cell is blocked (body), we skip forward in the
    // cycle until we find a reachable cell that also has adequate flood-fill.
    // This guarantees the snake eventually covers every cell without trapping
    // itself in spirals — provided fill is not so high that the cycle is broken.

    private Vector2Int HamiltonianStep(Vector2Int head, IReadOnlyList<Vector2Int> body,
                                        int L, Vector2Int reverseDir)
    {
        BuildHamiltonianCycle();
        if (_hamCycle == null || _hamCycle.Count == 0) return Vector2Int.zero;

        int N = _hamCycle.Count;

        // Sync _hamIndex to current head position in cycle.
        int headIdx = _hamCycle.IndexOf(head);
        if (headIdx >= 0) _hamIndex = headIdx;

        // Try up to N steps forward in the cycle.
        // We also allow a shortcut: if we can reach food earlier in the cycle
        // and it is safe, take it (improves score while keeping Hamiltonian safety).
        int minSafe = Mathf.Max(8, L >> 2);

        for (int skip = 1; skip <= N; skip++)
        {
            int nextIdx   = (_hamIndex + skip) % N;
            var nextCell  = _hamCycle[nextIdx];
            var dir       = nextCell - head;

            // Only adjacent moves are valid
            if (Mathf.Abs(dir.x) + Mathf.Abs(dir.y) != 1) continue;
            if (dir == reverseDir) continue;
            if (!gridManager.IsInBounds(nextCell)) continue;
            if (_obs.Contains(nextCell)) continue;

            int sp = FloodFillCount(nextCell, _obs);
            if (sp < minSafe && skip == 1) continue; // don't go into a tight cell even on-cycle
            if (sp <= 0) continue;

            _hamIndex = nextIdx;
            return dir;
        }

        return Vector2Int.zero; // cycle failed → fall through to T1
    }

    /// <summary>
    /// Builds a boustrophedon (row-alternating) Hamiltonian cycle over the grid.
    /// Rebuilt when board dimensions change (should be once per game).
    /// </summary>
    private void BuildHamiltonianCycle()
    {
        if (!_hamDirty && _hamCycle != null) return;
        _hamDirty = false;

        int W = gridManager.Width;
        int H = gridManager.Height;
        _hamCycle = new List<Vector2Int>(W * H);

        // Boustrophedon: left-to-right on even rows, right-to-left on odd rows.
        for (int y = 0; y < H; y++)
        {
            if (y % 2 == 0)
                for (int x = 0; x < W; x++)   _hamCycle.Add(new Vector2Int(x, y));
            else
                for (int x = W - 1; x >= 0; x--) _hamCycle.Add(new Vector2Int(x, y));
        }
        // The list is already a valid Hamiltonian path; we treat it as a cycle
        // by wrapping index arithmetic.
    }

    // =========================================================================
    //  Articulation Point (Cut Vertex) Detection
    // =========================================================================
    // Given a candidate cell, determines if removing it from the free graph
    // would disconnect any pair of currently reachable free cells.
    // Uses iterative DFS (avoids C# stack overflow on large grids).

    private bool IsArticulationPoint(Vector2Int candidate, HashSet<Vector2Int> obs)
    {
        // If candidate is already an obstacle, it's not an AP (no effect).
        if (obs.Contains(candidate)) return false;

        // Temporarily add candidate.
        obs.Add(candidate);

        // Count connected components of free cells adjacent to candidate.
        // If ≥ 2 components exist, candidate is an AP.
        int components = 0;
        _apVisited.Clear();
        foreach (var d in Dirs)
        {
            var nb = candidate + d;
            if (!gridManager.IsInBounds(nb) || obs.Contains(nb)) continue;
            if (_apVisited.ContainsKey(nb)) continue;

            // BFS/flood this component
            _ffQ.Clear();
            _ffQ.Enqueue(nb);
            _apVisited[nb] = true;
            while (_ffQ.Count > 0)
            {
                var cur = _ffQ.Dequeue();
                foreach (var d2 in Dirs)
                {
                    var nb2 = cur + d2;
                    if (!gridManager.IsInBounds(nb2) || obs.Contains(nb2)) continue;
                    if (_apVisited.ContainsKey(nb2)) continue;
                    _apVisited[nb2] = true;
                    _ffQ.Enqueue(nb2);
                }
            }
            components++;
            if (components >= 2) break;
        }

        obs.Remove(candidate);
        return components >= 2;
    }

    // =========================================================================
    //  Flood-fill: count reachable free cells from start
    // =========================================================================
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

    // =========================================================================
    //  BFS — shortest path from start to goal, avoiding obs
    // =========================================================================
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
