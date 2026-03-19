using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// SnakeController — Movement, growth, collision, body pulse, and WiFi head effect.
/// </summary>
public class SnakeController : MonoBehaviour, ISnakeState
{
    // ── Move result ───────────────────────────────────────────────────────────
    public enum MoveResult { Success, WallCollision, SelfCollision }

    [Header("Prefab Reference")]
    [SerializeField] private GameObject segmentPrefab;

    public IReadOnlyList<Vector2Int> OccupiedCells => _segmentPositions;

    private readonly List<Vector2Int> _segmentPositions = new();
    private readonly List<GameObject> _segmentObjects   = new();

    private GridManager _grid;
    private bool        _pendingGrowth;

    [Header("Body Pulse")]
    [SerializeField] private float pulseAmplitude = 0.12f;  // ±scale (±12%)
    [SerializeField] private float pulsePeriod    = 0.9f;   // seconds per full cycle

    [Header("Gradient Colors")]
    [SerializeField] private Color headColor = new Color(0.20f, 1.00f, 0.45f, 1f);  // bright green
    [SerializeField] private Color tailColor = new Color(0.04f, 0.22f, 0.08f, 1f);  // very dark green

    // Exposed so GameManager can include them in game-over debug.
    public int        Length        => _segmentPositions.Count;
    public Vector2Int HeadPosition  => _segmentPositions[0];
    public Vector2Int CurrentDirection { get; private set; } = Vector2Int.right;

    // ── WiFi arc effect ───────────────────────────────────────────────────────
    private GameObject     _wifiRoot;           // container rotated to face direction
    private LineRenderer[] _wifiArcs;
    private Sequence       _wifiSeq;

    private const int   ArcCount   = 5;
    private const float ArcAngle   = 110f;   // degrees (wifi-shaped arc)
    private const int   ArcPoints  = 28;
    private const float ArcSpacing = 0.70f;  // world units between arcs
    private const float ArcWidth   = 0.07f;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Update()
    {
        // Body alternating pulse + gradient color
        float t   = Time.time;
        int   cnt = _segmentObjects.Count;
        for (int i = 0; i < cnt; i++)
        {
            if (_segmentObjects[i] == null) continue;

            // Pulse
            float phase = (i % 2 == 0) ? 0f : Mathf.PI;
            float s = 1f + pulseAmplitude * Mathf.Sin(t * Mathf.PI * 2f / pulsePeriod + phase);
            _segmentObjects[i].transform.localScale = Vector3.one * s;

            // Gradient: t=0 → head (bright), t=1 → tail (dark)
            float gt  = cnt > 1 ? (float)i / (cnt - 1) : 0f;
            var   col = Color.Lerp(headColor, tailColor, gt);
            var   sr  = _segmentObjects[i].GetComponent<SpriteRenderer>();
            if (sr != null) sr.color = col;
        }

        // WiFi arcs follow head and rotate with direction
        if (_wifiRoot != null && _segmentObjects.Count > 0 && _segmentObjects[0] != null)
        {
            _wifiRoot.transform.position = _segmentObjects[0].transform.position;
            _wifiRoot.transform.rotation = Quaternion.Euler(0f, 0f, DirToAngle(CurrentDirection));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    public void Initialize(GridManager grid, Vector2Int startPos)
    {
        _grid = grid;
        foreach (var go in _segmentObjects) Destroy(go);
        _segmentPositions.Clear();
        _segmentObjects.Clear();
        _pendingGrowth = false;
        CurrentDirection = Vector2Int.right;
        SpawnSegment(startPos);

        InitWifiEffect();
    }

    /// <summary>
    /// Moves the snake one step.
    /// Returns a MoveResult so callers can distinguish wall vs self-collision.
    /// </summary>
    public MoveResult Move(Vector2Int direction)
    {
        CurrentDirection = direction;

        Vector2Int currentHead = _segmentPositions[0];
        Vector2Int newHead     = currentHead + direction;

        // ── Wall collision ────────────────────────────────────────────────────
        if (!_grid.IsInBounds(newHead))
            return MoveResult.WallCollision;

        // ── Self collision ────────────────────────────────────────────────────
        int checkCount = _pendingGrowth
            ? _segmentPositions.Count
            : _segmentPositions.Count - 1;

        for (int i = 0; i < checkCount; i++)
            if (_segmentPositions[i] == newHead)
                return MoveResult.SelfCollision;

        // ── Move ──────────────────────────────────────────────────────────────
        _segmentPositions.Insert(0, newHead);
        var headObj = Instantiate(segmentPrefab,
            _grid.GridToWorld(newHead), Quaternion.identity, transform);
        _segmentObjects.Insert(0, headObj);

        if (_pendingGrowth)
        {
            _pendingGrowth = false;
        }
        else
        {
            int last = _segmentObjects.Count - 1;
            Destroy(_segmentObjects[last]);
            _segmentObjects.RemoveAt(last);
            _segmentPositions.RemoveAt(last);
        }

        return MoveResult.Success;
    }

    public void Grow() => _pendingGrowth = true;

    // ─────────────────────────────────────────────────────────────────────────

    private void SpawnSegment(Vector2Int gridPos)
    {
        _segmentPositions.Add(gridPos);
        _segmentObjects.Add(
            Instantiate(segmentPrefab, _grid.GridToWorld(gridPos),
                Quaternion.identity, transform)
        );
    }

    // ── WiFi Arc Effect ───────────────────────────────────────────────────────

    private void InitWifiEffect()
    {
        // Clean up previous
        _wifiSeq?.Kill();
        if (_wifiRoot != null) Destroy(_wifiRoot);

        _wifiRoot = new GameObject("SnakeWifi");
        _wifiArcs = new LineRenderer[ArcCount];

        var mat = new Material(Shader.Find("Sprites/Default"));

        for (int i = 0; i < ArcCount; i++)
        {
            float radius = (i + 1) * ArcSpacing;

            var go = new GameObject($"Arc{i}");
            go.transform.SetParent(_wifiRoot.transform, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial  = mat;
            lr.startWidth      = ArcWidth;
            lr.endWidth        = ArcWidth;
            lr.positionCount   = ArcPoints;
            lr.useWorldSpace   = false;
            lr.sortingOrder    = 7;
            lr.startColor      = lr.endColor = new Color(0.15f, 1f, 0.85f, 0f);

            // Build arc points: from -half to +half degrees, pointing UP (+Y)
            float halfA = ArcAngle * 0.5f * Mathf.Deg2Rad;
            for (int p = 0; p < ArcPoints; p++)
            {
                float a = Mathf.Lerp(-halfA, halfA, (float)p / (ArcPoints - 1));
                lr.SetPosition(p, new Vector3(Mathf.Sin(a) * radius, Mathf.Cos(a) * radius, 0f));
            }

            _wifiArcs[i] = lr;
        }

        StartWifiAnimation();
    }

    private void StartWifiAnimation()
    {
        _wifiSeq = DOTween.Sequence().SetLoops(-1);

        // Each arc lights up in sequence (inner → middle → outer), then all fade
        float stagger  = 0.18f;
        float fadeIn   = 0.08f;
        float hold     = 0.12f;
        float fadeOut  = 0.30f;
        float cycleGap = 0.35f;

        for (int i = 0; i < ArcCount; i++)
        {
            var lr = _wifiArcs[i];
            float t0 = i * stagger;

            // Fade IN
            _wifiSeq.Insert(t0,
                DOTween.To(() => GetAlpha(lr), a => SetAlpha(lr, a), 0.90f, fadeIn));
            // Fade OUT
            _wifiSeq.Insert(t0 + fadeIn + hold,
                DOTween.To(() => GetAlpha(lr), a => SetAlpha(lr, a), 0f, fadeOut));
        }

        float cycleLen = (ArcCount - 1) * stagger + fadeIn + hold + fadeOut + cycleGap;
        _wifiSeq.AppendInterval(Mathf.Max(0f, cycleLen - _wifiSeq.Duration()));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static float GetAlpha(LineRenderer lr) => lr.startColor.a;
    private static void  SetAlpha(LineRenderer lr, float a)
    {
        var c = new Color(0.15f, 1f, 0.85f, a);
        lr.startColor = lr.endColor = c;
    }

    /// <summary>Converts grid direction to Z rotation in degrees (arc points UP by default).</summary>
    private static float DirToAngle(Vector2Int dir)
    {
        if (dir == Vector2Int.up)    return   0f;
        if (dir == Vector2Int.right) return -90f;
        if (dir == Vector2Int.down)  return 180f;
        if (dir == Vector2Int.left)  return  90f;
        return 0f;
    }

    private void OnDestroy()
    {
        _wifiSeq?.Kill();
        if (_wifiRoot != null) Destroy(_wifiRoot);
    }
}
