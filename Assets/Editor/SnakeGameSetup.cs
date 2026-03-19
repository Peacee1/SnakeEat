using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Editor-only scene bootstrapper.
/// Run via:  Tools → 🐍 Snake → Setup Scene  (or F5)
///
/// Sets up EVERYTHING automatically:
///   1. Validates prefabs at Assets/Resources/Prefab/
///   2. Clears previous Snake setup from the scene
///   3. Creates full GameObject hierarchy
///   4. Attaches all scripts (GameManager, SnakeController, FoodSpawner,
///      UIManager, InputReader, AutoPlayer, GridManager,
///      TikTokConnector, SpeedBoostManager)
///   5. Wires ALL Inspector references
///   6. Creates Canvas + TextMeshPro UI
///   7. Configures Main Camera for 50×50 board
///   8. Supports Undo (Ctrl+Z to undo entire setup)
/// </summary>
public static class SnakeGameSetup
{
    // ── Prefab paths ──────────────────────────────────────────────────────────
    private const string SegmentPrefabPath = "Assets/Resources/Prefab/SnakeSegment.prefab";
    private const string FoodPrefabPath    = "Assets/Resources/Prefab/Food.prefab";

    // Root GameObject names used for cleanup on re-run.
    private static readonly string[] RootNames =
        { "[Systems]", "[Gameplay]", "[UI]", "[TikTok]", "EventSystem" };

    // ─────────────────────────────────────────────────────────────────────────

    [MenuItem("Tools/🐍 Snake/Setup Scene _F5")]
    public static void SetupScene()
    {
        // ── 0. Validate prefabs ───────────────────────────────────────────────
        var segmentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SegmentPrefabPath);
        var foodPrefab    = AssetDatabase.LoadAssetAtPath<GameObject>(FoodPrefabPath);

        if (segmentPrefab == null || foodPrefab == null)
        {
            EditorUtility.DisplayDialog(
                "❌ Prefabs Not Found",
                $"Make sure both prefabs exist:\n\n• {SegmentPrefabPath}\n• {FoodPrefabPath}\n\nCreate them first, then re-run Setup.",
                "OK");
            return;
        }

        // ── 1. Undo group ─────────────────────────────────────────────────────
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Snake — Setup Scene");

        // ── 2. Clear previous setup ───────────────────────────────────────────
        ClearPreviousSetup();

        // ── 3. Build hierarchy ────────────────────────────────────────────────

        // [Systems]
        var systemsGO      = CreateGO("[Systems]");
        var gameManagerGO  = CreateGO("GameManager",  systemsGO.transform);
        var gridManagerGO  = CreateGO("GridManager",  systemsGO.transform);
        var uiManagerGO    = CreateGO("UIManager",    systemsGO.transform);

        // [Gameplay]
        var gameplayGO     = CreateGO("[Gameplay]");
        var snakeGO        = CreateGO("Snake",        gameplayGO.transform);
        var foodSpawnerGO  = CreateGO("FoodSpawner",  gameplayGO.transform);

        // [TikTok]
        var tiktokRootGO       = CreateGO("[TikTok]");
        var tiktokConnectorGO  = CreateGO("TikTokConnector",  tiktokRootGO.transform);
        var speedBoostGO       = CreateGO("SpeedBoostManager", tiktokRootGO.transform);

        // ── 4. Attach scripts ─────────────────────────────────────────────────
        var gridManager    = gridManagerGO.AddComponent<GridManager>();
        var snakeCtrl      = snakeGO.AddComponent<SnakeController>();
        var foodSpawner    = foodSpawnerGO.AddComponent<FoodSpawner>();
        var uiManager      = uiManagerGO.AddComponent<UIManager>();
        var gameManager    = gameManagerGO.AddComponent<GameManager>();
        var inputReader    = gameManagerGO.AddComponent<InputReader>();
        var autoPlayer     = gameManagerGO.AddComponent<AutoPlayer>();

        var tiktokConnector = tiktokConnectorGO.AddComponent<TikTokConnector>();
        var speedBoost      = speedBoostGO.AddComponent<SpeedBoostManager>();

        // ── 5. Build UI ───────────────────────────────────────────────────────
        var (canvas, scoreText, gameOverPanel, gameOverText) = BuildUI();

        // ── 6. Wire all [SerializeField] references ───────────────────────────

        // GridManager — 50×50 board
        SetField(gridManager, "width",  50);
        SetField(gridManager, "height", 50);

        // SnakeController
        SetField(snakeCtrl, "segmentPrefab", segmentPrefab);

        // FoodSpawner
        SetField(foodSpawner, "foodPrefab", foodPrefab);

        // UIManager
        SetField(uiManager, "scoreText",     scoreText);
        SetField(uiManager, "finalScoreText", gameOverText);
        SetField(uiManager, "gameOverPanel",  gameOverPanel);

        // GameManager
        SetField(gameManager, "gridManager",       gridManager);
        SetField(gameManager, "snakeController",   snakeCtrl);
        SetField(gameManager, "foodSpawner",       foodSpawner);
        SetField(gameManager, "uiManager",         uiManager);
        SetField(gameManager, "inputReader",       inputReader);
        SetField(gameManager, "autoPlayer",        autoPlayer);
        SetField(gameManager, "speedBoostManager", speedBoost);
        SetField(gameManager, "startPosition",     new Vector2Int(25, 25));
        SetField(gameManager, "startDirection",    new Vector2Int(1, 0));
        SetField(gameManager, "tickInterval",      1f / 50f);  // base 50 ticks/s

        // TikTokConnector
        SetField(tiktokConnector, "serverUrl",       "ws://localhost:8765");
        SetField(tiktokConnector, "reconnectDelay",  3f);

        // SpeedBoostManager
        SetField(speedBoost, "gameManager",      gameManager);
        SetField(speedBoost, "tiktokConnector",  tiktokConnector);
        SetField(speedBoost, "uiManager",        uiManager);
        SetField(speedBoost, "baseSpeed",        50f);
        SetField(speedBoost, "maxSpeed",         1000f);
        SetField(speedBoost, "speedPerDiamond",  0.05f);
        SetField(speedBoost, "boostDuration",    5f);
        SetField(speedBoost, "roseGiftName",     "Rose");
        SetField(speedBoost, "roseSpeedBoost",   1f);
        SetField(speedBoost, "likeMilestone",    50);
        SetField(speedBoost, "likeSpeedBoost",   1f);

        // ── 7. Camera ─────────────────────────────────────────────────────────
        SetupCamera();

        // ── 8. Done ───────────────────────────────────────────────────────────
        Selection.activeGameObject = gameManagerGO;
        EditorGUIUtility.PingObject(gameManagerGO);

        Debug.Log(
            "✅ <b>Snake scene setup complete!</b>\n" +
            "• 50×50 board, startPos=(25,25), tickInterval=0.01\n" +
            "• TikTokConnector → ws://localhost:8765\n" +
            "• SpeedBoostManager wired (Rose gift + 10k likes → permanent +1 ticks/s)\n" +
            "Press <b>Play ▶</b> to start."
        );

        EditorUtility.DisplayDialog(
            "✅ Setup Complete!",
            "Scene fully configured:\n\n" +
            "• 50×50 board, AI auto-play ON\n" +
            "• TikTokConnector: ws://localhost:8765\n" +
            "• Rose gift → +1 ticks/s permanent\n" +
            "• Every 10k likes → +1 ticks/s permanent\n\n" +
            "Start node server BEFORE pressing Play:\n" +
            "node server.js <username> <sessionid>",
            "Let's go! 🐍");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  UI Builder
    // ─────────────────────────────────────────────────────────────────────────

    private static (GameObject canvas,
                    TextMeshProUGUI scoreText,
                    GameObject gameOverPanel,
                    TextMeshProUGUI gameOverText)
        BuildUI()
    {
        // Canvas
        var canvasGO = CreateGO("[UI]");
        var canvas   = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // EventSystem
        var eventSystemGO = CreateGO("EventSystem");
        eventSystemGO.AddComponent<EventSystem>();
        eventSystemGO.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();

        // Score Text
        var scoreGO   = CreateGO("ScoreText", canvasGO.transform);
        var scoreText = scoreGO.AddComponent<TextMeshProUGUI>();
        scoreText.text      = "Score: 0";
        scoreText.fontSize  = 48;
        scoreText.color     = Color.white;
        scoreText.fontStyle = FontStyles.Bold;

        var scoreRT = scoreGO.GetComponent<RectTransform>();
        scoreRT.anchorMin        = new Vector2(0, 1);
        scoreRT.anchorMax        = new Vector2(0, 1);
        scoreRT.pivot            = new Vector2(0, 1);
        scoreRT.anchoredPosition = new Vector2(30, -30);
        scoreRT.sizeDelta        = new Vector2(400, 65);

        // Game Over Panel
        var panelGO  = CreateGO("GameOverPanel", canvasGO.transform);
        var panelImg = panelGO.AddComponent<Image>();
        panelImg.color = new Color(0f, 0f, 0f, 0.85f);

        var panelRT = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin        = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax        = new Vector2(0.5f, 0.5f);
        panelRT.pivot            = new Vector2(0.5f, 0.5f);
        panelRT.anchoredPosition = Vector2.zero;
        panelRT.sizeDelta        = new Vector2(580, 300);

        // Game Over Text
        var govTextGO = CreateGO("GameOverText", panelGO.transform);
        var gameOverText   = govTextGO.AddComponent<TextMeshProUGUI>();
        gameOverText.text      = "GAME OVER\nScore: 0\nRestart in 2.0s...";
        gameOverText.fontSize  = 42;
        gameOverText.color     = Color.white;
        gameOverText.alignment = TextAlignmentOptions.Center;
        gameOverText.fontStyle = FontStyles.Bold;

        var govRT = govTextGO.GetComponent<RectTransform>();
        govRT.anchorMin = Vector2.zero;
        govRT.anchorMax = Vector2.one;
        govRT.offsetMin = new Vector2(20, 20);
        govRT.offsetMax = new Vector2(-20, -20);

        panelGO.SetActive(false);

        return (canvasGO, scoreText, panelGO, gameOverText);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Camera — adjusted for 50×50 grid (centered at world origin)
    // ─────────────────────────────────────────────────────────────────────────

    private static void SetupCamera()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("⚠️ Main Camera not found. Create one tagged 'MainCamera'.");
            return;
        }

        Undo.RecordObject(cam, "Setup Camera");
        Undo.RecordObject(cam.transform, "Setup Camera Transform");

        // 50×50 grid centered at origin → spans ±25 units.
        // orthographicSize = 26 gives small padding.
        cam.orthographic       = true;
        cam.orthographicSize   = 26f;
        cam.transform.position = new Vector3(0f, 0f, -10f);
        cam.backgroundColor    = new Color(0.05f, 0.05f, 0.08f);
        cam.clearFlags         = CameraClearFlags.SolidColor;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static GameObject CreateGO(string name, Transform parent = null)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        if (parent != null)
            go.transform.SetParent(parent, false);
        return go;
    }

    /// <summary>
    /// Assigns a value to a [SerializeField] field via SerializedObject.
    /// Supports: UnityEngine.Object, Vector2Int, string, float, int, bool.
    /// </summary>
    private static void SetField(Object target, string fieldName, object value)
    {
        var so   = new SerializedObject(target);
        var prop = so.FindProperty(fieldName);

        if (prop == null)
        {
            Debug.LogWarning($"⚠️ Field '{fieldName}' not found on {target.GetType().Name}");
            return;
        }

        switch (value)
        {
            case Object unityObj:
                prop.objectReferenceValue = unityObj;
                break;
            case Vector2Int v:
                prop.vector2IntValue = v;
                break;
            case string s:
                prop.stringValue = s;
                break;
            case float f:
                prop.floatValue = f;
                break;
            case int i:
                prop.intValue = i;
                break;
            case bool b:
                prop.boolValue = b;
                break;
            default:
                Debug.LogWarning($"⚠️ Unsupported type '{value.GetType()}' for field '{fieldName}'");
                break;
        }

        so.ApplyModifiedProperties();
    }

    private static void ClearPreviousSetup()
    {
        foreach (var name in RootNames)
        {
            var existing = GameObject.Find(name);
            if (existing != null)
                Undo.DestroyObjectImmediate(existing);
        }
    }
}
