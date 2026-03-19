using UnityEngine;

/// <summary>
/// GridManager defines the play area.
/// Supports runtime resize via Resize(w, h) for the level-progression system.
/// </summary>
public class GridManager : MonoBehaviour
{
    [Header("Grid Settings")]
    [SerializeField] private int   width    = 5;
    [SerializeField] private int   height   = 5;
    [SerializeField] private float cellSize = 1f;

    [Header("Visuals")]
    [SerializeField] private Color borderColor     = new Color(0.3f, 0.9f, 0.4f, 1f);
    [SerializeField] private float borderThickness  = 0.08f;
    [SerializeField] private Color backgroundColor = new Color(0.06f, 0.09f, 0.06f, 1f);
    [SerializeField] private bool  showBackground   = true;

    // ── Public properties ─────────────────────────────────────────────────────
    public int   Width    => width;
    public int   Height   => height;
    public float CellSize => cellSize;

    public Vector3 Origin => new Vector3(
        -(width  * cellSize) / 2f + cellSize / 2f,
        -(height * cellSize) / 2f + cellSize / 2f,
        0f
    );

    private Vector3 BottomLeft  => new Vector3(-(width  * cellSize) / 2f, -(height * cellSize) / 2f, 0f);
    private Vector3 BottomRight => new Vector3( (width  * cellSize) / 2f, -(height * cellSize) / 2f, 0f);
    private Vector3 TopRight    => new Vector3( (width  * cellSize) / 2f,  (height * cellSize) / 2f, 0f);
    private Vector3 TopLeft     => new Vector3(-(width  * cellSize) / 2f,  (height * cellSize) / 2f, 0f);

    // Runtime visual objects
    private GameObject   _bgGO;
    private LineRenderer _borderLR;

    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (showBackground) CreateBackground();
        CreateBorder();
    }

    // ── Runtime resize ────────────────────────────────────────────────────────

    /// <summary>
    /// Resizes the board at runtime. Rebuilds the background and border visuals.
    /// Call this before re-initialising the snake and food spawner.
    /// </summary>
    public void Resize(int newWidth, int newHeight)
    {
        width  = newWidth;
        height = newHeight;
        RebuildVisuals();
    }

    private void RebuildVisuals()
    {
        // Background
        if (_bgGO != null) Destroy(_bgGO);
        if (showBackground) CreateBackground();

        // Border
        if (_borderLR != null) Destroy(_borderLR.gameObject);
        CreateBorder();
    }

    // ── Convert helpers ───────────────────────────────────────────────────────

    public Vector3 GridToWorld(Vector2Int gridPos)
    {
        return Origin + new Vector3(gridPos.x * cellSize, gridPos.y * cellSize, 0f);
    }

    public bool IsInBounds(Vector2Int gridPos)
    {
        return gridPos.x >= 0 && gridPos.x < width &&
               gridPos.y >= 0 && gridPos.y < height;
    }

    public Vector2Int GetRandomCell()
    {
        return new Vector2Int(Random.Range(0, width), Random.Range(0, height));
    }

    // ── Visual builders ───────────────────────────────────────────────────────

    private void CreateBackground()
    {
        _bgGO = new GameObject("BoardBackground");
        _bgGO.transform.SetParent(transform, false);
        _bgGO.transform.localPosition = new Vector3(0f, 0f, 0.1f);

        var sr = _bgGO.AddComponent<SpriteRenderer>();
        sr.sprite = CreateWhiteSprite();
        sr.color  = backgroundColor;
        sr.sortingOrder = -10;

        _bgGO.transform.localScale = new Vector3(width * cellSize, height * cellSize, 1f);
    }

    private void CreateBorder()
    {
        var go = new GameObject("BoardBorder");
        go.transform.SetParent(transform, false);

        _borderLR = go.AddComponent<LineRenderer>();
        _borderLR.material         = new Material(Shader.Find("Sprites/Default"));
        _borderLR.startColor       = borderColor;
        _borderLR.endColor         = borderColor;
        _borderLR.startWidth       = borderThickness;
        _borderLR.endWidth         = borderThickness;
        _borderLR.positionCount    = 5;
        _borderLR.useWorldSpace    = true;
        _borderLR.loop             = false;
        _borderLR.sortingOrder     = 10;

        float z = -0.05f;
        _borderLR.SetPositions(new Vector3[]
        {
            new(BottomLeft.x,  BottomLeft.y,  z),
            new(BottomRight.x, BottomRight.y, z),
            new(TopRight.x,    TopRight.y,    z),
            new(TopLeft.x,     TopLeft.y,     z),
            new(BottomLeft.x,  BottomLeft.y,  z),
        });
    }

    private static Sprite CreateWhiteSprite()
    {
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
    }

    // ─────────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = borderColor;
        Gizmos.DrawLine(BottomLeft,  BottomRight);
        Gizmos.DrawLine(BottomRight, TopRight);
        Gizmos.DrawLine(TopRight,    TopLeft);
        Gizmos.DrawLine(TopLeft,     BottomLeft);

        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.12f);
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                Gizmos.DrawWireCube(GridToWorld(new Vector2Int(x, y)),
                    Vector3.one * cellSize * 0.97f);
    }
#endif
}
