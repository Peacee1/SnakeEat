using UnityEngine;

/// <summary>
/// GridManager defines the play area.
/// It converts between grid coordinates (Vector2Int) and world positions (Vector3).
/// Also draws a visible border and a dark background at runtime.
/// Attach to: GridManager GameObject.
/// </summary>
public class GridManager : MonoBehaviour
{
    [Header("Grid Settings")]
    [SerializeField] private int   width    = 50;
    [SerializeField] private int   height   = 50;
    [SerializeField] private float cellSize = 1f;

    [Header("Visuals")]
    [SerializeField] private Color borderColor     = new Color(0.3f, 0.9f, 0.4f, 1f);  // green border
    [SerializeField] private float borderThickness  = 0.08f;
    [SerializeField] private Color backgroundColor = new Color(0.06f, 0.09f, 0.06f, 1f); // dark board bg
    [SerializeField] private bool  showBackground   = true;

    // ── Public properties ─────────────────────────────────────────────────────
    public int   Width    => width;
    public int   Height   => height;
    public float CellSize => cellSize;

    // Bottom-left CENTER of cell (0,0), grid is centered on world origin.
    public Vector3 Origin => new Vector3(
        -(width  * cellSize) / 2f + cellSize / 2f,
        -(height * cellSize) / 2f + cellSize / 2f,
        0f
    );

    // The four corners of the board (outer edge of border cells).
    private Vector3 BottomLeft  => new Vector3(-(width  * cellSize) / 2f, -(height * cellSize) / 2f, 0f);
    private Vector3 BottomRight => new Vector3( (width  * cellSize) / 2f, -(height * cellSize) / 2f, 0f);
    private Vector3 TopRight    => new Vector3( (width  * cellSize) / 2f,  (height * cellSize) / 2f, 0f);
    private Vector3 TopLeft     => new Vector3(-(width  * cellSize) / 2f,  (height * cellSize) / 2f, 0f);

    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (showBackground) CreateBackground();
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

    /// <summary>
    /// Creates a filled quad behind the board for a dark background.
    /// Uses a plain white sprite tinted with backgroundColor.
    /// </summary>
    private void CreateBackground()
    {
        var go = new GameObject("BoardBackground");
        go.transform.SetParent(transform, false);
        go.transform.position = Vector3.zero;

        // Push it slightly behind everything else.
        go.transform.localPosition = new Vector3(0f, 0f, 0.1f);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = CreateWhiteSprite();
        sr.color  = backgroundColor;
        sr.sortingOrder = -10;

        // Scale to cover the entire grid.
        go.transform.localScale = new Vector3(width * cellSize, height * cellSize, 1f);
    }

    /// <summary>
    /// Creates a LineRenderer that draws the rectangular border of the board.
    /// </summary>
    private void CreateBorder()
    {
        var go = new GameObject("BoardBorder");
        go.transform.SetParent(transform, false);

        var lr = go.AddComponent<LineRenderer>();

        // Use the built-in Sprites/Default shader so no extra material is needed.
        lr.material         = new Material(Shader.Find("Sprites/Default"));
        lr.startColor       = borderColor;
        lr.endColor         = borderColor;
        lr.startWidth       = borderThickness;
        lr.endWidth         = borderThickness;
        lr.positionCount    = 5;          // 4 corners + close the loop
        lr.useWorldSpace    = true;
        lr.loop             = false;
        lr.sortingOrder     = 10;         // in front of everything

        // Slightly offset to sit in front of background
        float z = -0.05f;
        lr.SetPositions(new Vector3[]
        {
            new(BottomLeft.x,  BottomLeft.y,  z),
            new(BottomRight.x, BottomRight.y, z),
            new(TopRight.x,    TopRight.y,    z),
            new(TopLeft.x,     TopLeft.y,     z),
            new(BottomLeft.x,  BottomLeft.y,  z),   // close loop
        });
    }

    /// <summary>
    /// Programmatically creates a 1×1 white sprite so we don't need a texture asset.
    /// </summary>
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
        // Outer border (matches the LineRenderer at runtime)
        Gizmos.color = borderColor;
        Gizmos.DrawLine(BottomLeft,  BottomRight);
        Gizmos.DrawLine(BottomRight, TopRight);
        Gizmos.DrawLine(TopRight,    TopLeft);
        Gizmos.DrawLine(TopLeft,     BottomLeft);

        // Cell grid (faint)
        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.12f);
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                Gizmos.DrawWireCube(GridToWorld(new Vector2Int(x, y)),
                    Vector3.one * cellSize * 0.97f);
    }
#endif
}
