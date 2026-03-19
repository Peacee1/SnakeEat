using UnityEngine;

/// <summary>
/// GameManager — orchestrator with level-progression system.
///
/// LEVEL SYSTEM:
///   • Level 1  → board 5×5,  snake starts at center
///   • Level up → board size doubles each level (5→10→20→40→80…)
///   • Trigger  → when snake fills ≥ 30% of the current board
///   • On level up: board resizes, snake resets to center, score carries over
/// </summary>
public class GameManager : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private GridManager      gridManager;
    [SerializeField] private SnakeController  snakeController;
    [SerializeField] private FoodSpawner      foodSpawner;
    [SerializeField] private UIManager        uiManager;
    [SerializeField] private InputReader      inputReader;
    [SerializeField] private AutoPlayer       autoPlayer;
    [SerializeField] private SpeedBoostManager speedBoostManager;

    [Header("Game Settings")]
    [SerializeField] private float      tickInterval   = 0.01f;
    [SerializeField] private Vector2Int startDirection = new(1, 0);

    private const int   Level1BoardSize  = 5;     // Level 1 board: 5×5
    private const int   MaxLevel         = 5;     // Level 5 = 80×80, maximum
    private const float FillThreshold    = 0.30f; // 30% fill triggers level up (levels 1-4)
    private const float WinFillThreshold = 0.60f; // 60% fill triggers win at max level
    private const float Level1Speed      = 3f;    // ticks/s at level 1; doubles each level
    private int         _currentLevel   = 1;
    private int         _currentBoardSize;
    private bool        _levelingUp;              // prevent double-trigger

    // ── Runtime state ─────────────────────────────────────────────────────────
    private Vector2Int _currentDir;
    private float      _tickTimer;
    private int        _score;
    private bool       _isGameOver;
    private bool       _isWin;
    private int        _totalTicks;
    private float      _restartTimer;
    private const float RestartDelay = 2f;
    private const float WinDelay     = 3f;
    private const int   WinScore     = 9999; // effectively no score-based win

    // SFX
    private AudioSource _sfx;
    private AudioClip   _turnClip;
    private AudioClip   _eatClip;
    private float       _lastTurnSfxTime = -1f;
    private const float TurnSfxCooldown  = 0.08f;

    // Background music
    private AudioSource  _bgSource;
    private AudioClip[]  _bgClips;
    private int          _lastBgIndex = -1;

    // Camera reference for auto-zoom
    private Camera _cam;

    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        _cam = Camera.main;

        var raw = Resources.Load<AudioClip>(
            "SFX/freesound_community-mech-keyboard-02-102918");
        _turnClip = raw != null ? TrimClip(raw, 0.05f) : null;
        var rawEat = Resources.Load<AudioClip>("SFX/ribhavagrawal-point-smooth-beep-230573");
        _eatClip  = rawEat;
        _sfx = gameObject.AddComponent<AudioSource>();
        _sfx.playOnAwake = false;
        _sfx.volume      = 0.6f;

        var bgNames = new[] { "SFX/SoundBackground1", "SFX/SoundBackground2" };
        var loaded  = new System.Collections.Generic.List<AudioClip>();
        foreach (var n in bgNames)
        {
            var c = Resources.Load<AudioClip>(n);
            if (c != null) loaded.Add(c);
            else Debug.LogWarning($"[GameManager] BG track not found: {n}");
        }
        if (loaded.Count > 0)
        {
            _bgClips  = loaded.ToArray();
            _bgSource = gameObject.AddComponent<AudioSource>();
            _bgSource.loop        = false;
            _bgSource.volume      = 0.25f;
            _bgSource.playOnAwake = false;
            PlayRandomBgTrack();
        }

        StartGame();
    }

    private void PlayRandomBgTrack()
    {
        if (_bgClips == null || _bgClips.Length == 0) return;
        int next = _lastBgIndex;
        if (_bgClips.Length > 1)
            while (next == _lastBgIndex) next = UnityEngine.Random.Range(0, _bgClips.Length);
        else
            next = 0;
        _lastBgIndex   = next;
        _bgSource.clip = _bgClips[next];
        _bgSource.Play();
    }

    private void Update()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.05f);

        if (_bgSource != null && !_bgSource.isPlaying)
            PlayRandomBgTrack();

        if (_isGameOver || _isWin)
        {
            _restartTimer -= dt;
            if (_isWin)
                uiManager.ShowWin(_score, Mathf.Max(0f, _restartTimer));
            else
                uiManager.ShowGameOver(_score, Mathf.Max(0f, _restartTimer));
            if (_restartTimer <= 0f || inputReader.IsRestartPressed())
                StartGame();
            return;
        }

        _tickTimer += dt;

        int ticksThisFrame = 0;
        while (_tickTimer >= tickInterval && ticksThisFrame < 50)
        {
            _tickTimer -= tickInterval;
            ticksThisFrame++;
            Tick();
            if (_isGameOver || _levelingUp) break;
        }
    }

    // ── Game setup ────────────────────────────────────────────────────────────

    private void StartGame()
    {
        _isGameOver   = false;
        _isWin        = false;
        _score        = 0;
        _currentDir   = startDirection;
        _tickTimer    = 0f;
        _totalTicks   = 0;
        _levelingUp   = false;
        _currentLevel = 1;

        SetupLevel(_currentLevel, carryScore: false);
    }

    /// <summary>
    /// Configures the board, snake, and food for the given level.
    /// Board size = Level1BoardSize * 2^(level-1).
    /// </summary>
    private void SetupLevel(int level, bool carryScore)
    {
        _currentLevel     = level;
        _currentBoardSize = Level1BoardSize * (1 << (level - 1)); // 5 * 2^(level-1)

        // Speed: 3 ticks/s at level 1, doubles each level → 3·2^(level-1)
        float levelSpeed = Level1Speed * (1 << (level - 1));  // 3, 6, 12, 24, 48

        if (speedBoostManager != null)
        {
            // Let SpeedBoostManager handle it: scales boost proportionally so
            // total speed = (oldTotal) × 2, not just base × 2.
            speedBoostManager.ScaleSpeedForLevelUp(levelSpeed);
        }
        else
        {
            // Fallback if no SpeedBoostManager in scene.
            tickInterval = 1f / levelSpeed;
        }
        if (!carryScore) _score = 0;

        // Resize grid
        gridManager.Resize(_currentBoardSize, _currentBoardSize);

        // Snake starts at center of new board
        var center = new Vector2Int(_currentBoardSize / 2, _currentBoardSize / 2);
        inputReader.ResetDirection(startDirection);
        _currentDir = startDirection;
        _levelingUp = false;

        snakeController.Initialize(gridManager, center);
        foodSpawner.Initialize(gridManager, snakeController);
        foodSpawner.SpawnFood();

        if (autoPlayer != null)
            autoPlayer.Initialise(gridManager, snakeController, foodSpawner, inputReader);

        uiManager.HideGameOver();
        uiManager.UpdateScore(_score);
        uiManager.UpdateLevel(_currentLevel, _currentBoardSize);
        uiManager.UpdateSpeed(tickInterval > 0f ? 1f / tickInterval : 0f);

        // Auto-fit camera to board (orthographic)
        FitCamera();

        Debug.Log($"[Game] Level {level} | Board {_currentBoardSize}×{_currentBoardSize} | Center={center}");
    }

    /// <summary>Scales the orthographic camera so the entire board is visible.</summary>
    private void FitCamera()
    {
        if (_cam == null || !_cam.orthographic) return;

        float boardHalfH = _currentBoardSize * gridManager.CellSize * 0.5f;
        float boardHalfW = _currentBoardSize * gridManager.CellSize * 0.5f;

        float aspectRatio   = (float)Screen.width / Screen.height;
        float requiredOrthoH = boardHalfH + 1.5f;           // 1.5 padding
        float requiredOrthoW = boardHalfW / aspectRatio + 1.5f;

        _cam.orthographicSize = Mathf.Max(requiredOrthoH, requiredOrthoW);
        _cam.transform.position = new Vector3(0f, 0f, _cam.transform.position.z);
    }

    // ── Principal tick ────────────────────────────────────────────────────────

    private void Tick()
    {
        _totalTicks++;
        uiManager.UpdateSpeed(tickInterval > 0f ? 1f / tickInterval : 0f);

        autoPlayer?.TickAI(_currentDir);

        var newDir = inputReader.ConsumeDirection(_currentDir);
        if (newDir != _currentDir)
        {
            uiManager?.FlashKey(newDir);
            if (_turnClip != null && Time.time - _lastTurnSfxTime > TurnSfxCooldown)
            {
                _sfx.PlayOneShot(_turnClip);
                _lastTurnSfxTime = Time.time;
            }
        }
        _currentDir = newDir;

        var headBefore = snakeController.HeadPosition;
        var food       = foodSpawner.FoodPosition;

        var result = snakeController.Move(_currentDir);

        if (result != SnakeController.MoveResult.Success)
        {
            TriggerGameOver(result, headBefore, food);
            return;
        }

        if (snakeController.HeadPosition == food)
        {
            snakeController.Grow();
            _score++;
            uiManager.UpdateScore(_score);
            foodSpawner.SpawnFood();
            if (_eatClip != null) _sfx.PlayOneShot(_eatClip);

            // ── Level-up check ─────────────────────────────────────────────
            CheckLevelUp();
        }
    }

    // ── Level-up ──────────────────────────────────────────────────────────────

    private void CheckLevelUp()
    {
        int L     = snakeController.OccupiedCells.Count;
        int total = _currentBoardSize * _currentBoardSize;
        float fill = (float)L / total;

        // Use different threshold depending on whether this is the final level
        float threshold = (_currentLevel >= MaxLevel) ? WinFillThreshold : FillThreshold;

        if (fill >= threshold && !_levelingUp)
        {
            _levelingUp = true;

            // At max level → Win!
            if (_currentLevel >= MaxLevel)
            {
                Debug.Log($"[Game] 🏆 MAX LEVEL {MaxLevel} cleared! Fill={fill:P0} Score={_score}");
                TriggerWin();
                return;
            }

            int nextLevel = _currentLevel + 1;
            int nextSize  = Level1BoardSize * (1 << (nextLevel - 1));

            Debug.Log($"[Game] 🆙 LEVEL UP {_currentLevel} → {nextLevel} | " +
                      $"Fill={fill:P0} | NextBoard={nextSize}×{nextSize}");

            uiManager.ShowLevelUp(nextLevel, nextSize);
            Invoke(nameof(DoLevelUp), 1.2f);
        }
    }

    private void DoLevelUp()
    {
        SetupLevel(_currentLevel + 1, carryScore: true);
    }

    // ── Win / Game Over ───────────────────────────────────────────────────────

    private void TriggerWin()
    {
        _isWin        = true;
        _restartTimer = WinDelay;
        speedBoostManager?.ResetSpeed();
        uiManager.ShowWin(_score, WinDelay);
        Debug.Log($"[Game] 🏆 WIN! Score={_score}");
    }

    private void TriggerGameOver(SnakeController.MoveResult reason, Vector2Int headPos, Vector2Int food)
    {
        _isGameOver   = true;
        _restartTimer = RestartDelay;
        speedBoostManager?.ResetSpeed();
        uiManager.ShowGameOver(_score, RestartDelay);

        var cells = snakeController.OccupiedCells;
        Vector2Int attempted = headPos + _currentDir;
        string cause = reason switch
        {
            SnakeController.MoveResult.WallCollision =>
                $"💥 HIT WALL  → tried ({attempted.x},{attempted.y}) out of bounds",
            SnakeController.MoveResult.SelfCollision =>
                $"🐍 HIT SELF  → tried ({attempted.x},{attempted.y}) occupied",
            _ => "Unknown"
        };

        string hitSeg = "";
        if (reason == SnakeController.MoveResult.SelfCollision)
            for (int i = 0; i < cells.Count; i++)
                if (cells[i] == attempted)
                {
                    string tag = i == 0 ? "HEAD" : i == cells.Count - 1 ? "TAIL" : $"seg[{i}]";
                    hitSeg = $" ({tag})"; break;
                }

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < cells.Count; i++)
        {
            string tag = i == 0 ? "HEAD" : i == cells.Count - 1 ? "TAIL" : $"[{i}]";
            sb.Append($"  {tag,6}: ({cells[i].x,2},{cells[i].y,2})\n");
        }

        Debug.LogWarning(
            $"[Game] ☠ GAME OVER\n" +
            $"  Cause  : {cause}{hitSeg}\n" +
            $"  Score  : {_score}\n" +
            $"  Level  : {_currentLevel}\n" +
            $"  Board  : {_currentBoardSize}×{_currentBoardSize}\n" +
            $"  Length : {cells.Count}\n" +
            $"  Ticks  : {_totalTicks}\n" +
            $"  Dir    : {DirName(_currentDir)}\n" +
            $"  Food   : ({food.x},{food.y})\n" +
            $"── Body ({cells.Count} segs) ──────────────\n" +
            sb
        );
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void SetTickInterval(float interval)
    {
        tickInterval = Mathf.Max(0.005f, interval);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AudioClip TrimClip(AudioClip src, float skipSec)
    {
        int skipSamples = Mathf.FloorToInt(skipSec * src.frequency);
        int remaining   = src.samples - skipSamples;
        if (remaining <= 0) return src;

        var data = new float[src.samples * src.channels];
        src.GetData(data, 0);

        var trimmed = new float[remaining * src.channels];
        System.Array.Copy(data, skipSamples * src.channels, trimmed, 0, trimmed.Length);

        var clip = AudioClip.Create(src.name + "_trimmed", remaining, src.channels, src.frequency, false);
        clip.SetData(trimmed, 0);
        return clip;
    }

    private static string DirName(Vector2Int d)
    {
        if (d == Vector2Int.up)    return "UP(W)";
        if (d == Vector2Int.down)  return "DOWN(S)";
        if (d == Vector2Int.left)  return "LEFT(A)";
        if (d == Vector2Int.right) return "RIGHT(D)";
        return $"({d.x},{d.y})";
    }
}
