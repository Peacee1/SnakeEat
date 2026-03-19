using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// FoodSpawner manages placing and removing food on the grid.
/// It depends on ISnakeState (not SnakeController directly), following SOLID-D.
///
/// Attach to: FoodSpawner GameObject.
/// </summary>
public class FoodSpawner : MonoBehaviour
{
    [Header("Prefab Reference")]
    [SerializeField] private GameObject foodPrefab;

    private GridManager _grid;
    private ISnakeState _snakeState;
    private GameObject  _currentFood;
    private Tween       _rippleLoop;

    // Pre-generate N future food positions so AutoPlayer can plan ahead.
    private const int FutureCount = 2;
    private readonly Queue<Vector2Int> _futureQueue = new();
    private readonly List<Vector2Int>  _upcomingList = new();

    /// <summary>Upcoming food positions (next FutureCount spawns after the current one).</summary>
    public IReadOnlyList<Vector2Int> UpcomingFoodPositions => _upcomingList;

    public void Initialize(GridManager grid, ISnakeState snakeState)
    {
        _grid       = grid;
        _snakeState = snakeState;

        // Pre-fill the future queue so UpcomingFoodPositions is ready from turn 1.
        _futureQueue.Clear();
        for (int i = 0; i < FutureCount; i++) _futureQueue.Enqueue(GenerateFoodCell());
        RefreshUpcomingList();
    }

    /// <summary>Destroys existing food (if any) and spawns new food at a valid position.</summary>
    public void SpawnFood()
    {
        _rippleLoop?.Kill();
        if (_currentFood != null) Destroy(_currentFood);

        // Pop the next pre-generated position from the queue, then replenish.
        Vector2Int pos = _futureQueue.Count > 0 ? _futureQueue.Dequeue() : GenerateFoodCell();
        FoodPosition = pos;
        while (_futureQueue.Count < FutureCount) _futureQueue.Enqueue(GenerateFoodCell());
        RefreshUpcomingList();
        _currentFood = Instantiate(foodPrefab, _grid.GridToWorld(pos),
            Quaternion.identity, transform);

        // Pulse animation: scale 1 → 1.25 → 1, loop forever.
        _currentFood.transform.localScale = Vector3.one;
        _currentFood.transform
            .DOScale(Vector3.one * 1.25f, 0.5f)
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo);

        // Rinple loop: emit a ring every 0.9s.
        EmitRipple();
        _rippleLoop = DOVirtual.DelayedCall(0.9f, EmitRipple, false)
            .SetLoops(-1, LoopType.Restart);
    }

    /// <summary>Returns the grid position of the current food item.</summary>
    public Vector2Int FoodPosition { get; private set; }

    // ── Ripple ────────────────────────────────────────────────────────────────

    private void EmitRipple()
    {
        if (_currentFood == null) return;

        var go = new GameObject("FoodRipple");
        go.transform.position   = _currentFood.transform.position;
        go.transform.localScale = Vector3.one * 0.05f;

        var sr         = go.AddComponent<SpriteRenderer>();
        sr.sprite      = _circleSprite != null ? _circleSprite : (_circleSprite = CreateCircleSprite());
        sr.color       = new Color(1f, 0.85f, 0.1f, 0.75f);   // warm yellow
        sr.sortingOrder = 5;

        // Expand and fade out, then destroy.
        go.transform.DOScale(Vector3.one * 3.5f, 0.7f).SetEase(Ease.OutCubic);
        sr.DOFade(0f, 0.7f)
          .SetEase(Ease.InQuad)
          .OnComplete(() => { if (go != null) Destroy(go); });
    }

    // Cache the generated circle sprite so it's only created once.
    private static Sprite _circleSprite;

    private static Sprite CreateCircleSprite(int radius = 32)
    {
        int size = radius * 2;
        var tex  = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear };

        var pixels = new Color[size * size];
        var center = new Vector2(radius - 0.5f, radius - 0.5f);

        float outer = radius;
        float inner = radius - 3f;   // ring thickness = 3px

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), center);
            float a = d > inner && d <= outer ? 1f : 0f;
            pixels[y * size + x] = new Color(1f, 1f, 1f, a);
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f), size);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RefreshUpcomingList()
    {
        _upcomingList.Clear();
        foreach (var p in _futureQueue) _upcomingList.Add(p);
    }

    /// <summary>Generates a random free cell WITHOUT setting FoodPosition (no side-effect).</summary>
    private Vector2Int GenerateFoodCell()
    {
        Vector2Int candidate;
        int maxAttempts = _grid != null ? _grid.Width * _grid.Height : 1;
        int attempts    = 0;
        do
        {
            candidate = _grid.GetRandomCell();
            attempts++;
            if (attempts > maxAttempts)
            {
                Debug.LogWarning("FoodSpawner: Could not find a free cell!");
                break;
            }
        }
        while (IsOccupiedBySnake(candidate));

        return candidate;
    }

    private bool IsOccupiedBySnake(Vector2Int pos)
    {
        var occupied = _snakeState.OccupiedCells;
        for (int i = 0; i < occupied.Count; i++)
            if (occupied[i] == pos) return true;
        return false;
    }
}
