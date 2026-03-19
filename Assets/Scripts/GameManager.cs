using UnityEngine;

/// <summary>
/// GameManager — orchestrator with detailed per-move and game-over debug logging.
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
    [SerializeField] private float      tickInterval   = 0.01f;  // 100 ticks/s (20× faster than default 0.2f)
    [SerializeField] private Vector2Int startPosition  = new(25, 25);
    [SerializeField] private Vector2Int startDirection = new(1, 0);

    [Header("Debug")]
    // Game-over is always logged. Disable in code if needed.

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
    private const int   WinScore     = 2000;

    // SFX
    private AudioSource _sfx;
    private AudioClip   _turnClip;
    private AudioClip   _eatClip;
    private float       _lastTurnSfxTime = -1f;
    private const float TurnSfxCooldown  = 0.08f;  // max ~12 clicks/s

    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        // Load turn SFX and trim the first 0.2s (silence/noise at start).
        var raw = Resources.Load<AudioClip>(
            "SFX/freesound_community-mech-keyboard-02-102918");
        _turnClip = raw != null ? TrimClip(raw, 0.05f) : null;
        var rawEat = Resources.Load<AudioClip>("SFX/ribhavagrawal-point-smooth-beep-230573");
        _eatClip  = rawEat;
        _sfx = gameObject.AddComponent<AudioSource>();
        _sfx.playOnAwake = false;
        _sfx.volume      = 0.6f;

        // Background music — loops at half volume.
        var bgClip = Resources.Load<AudioClip>("SFX/SoundBackground1");
        if (bgClip != null)
        {
            var bgSource        = gameObject.AddComponent<AudioSource>();
            bgSource.clip        = bgClip;
            bgSource.loop        = true;
            bgSource.volume      = 0.25f;
            bgSource.playOnAwake = false;
            bgSource.Play();
        }
        else Debug.LogWarning("[GameManager] Background music not found at Resources/SFX/SoundBackground1");

        StartGame();
    }

    private void Update()
    {
        // Cap deltaTime so a single heavy frame never drains the timer instantly.
        float dt = Mathf.Min(Time.deltaTime, 0.05f);

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

        // Multiple ticks per frame when tickInterval < deltaTime.
        // Cap at 50 to prevent heavy AI (BFS+FloodFill on large boards) from
        // causing multi-second frame spikes that make the countdown appear broken.
        int ticksThisFrame = 0;
        while (_tickTimer >= tickInterval && ticksThisFrame < 50)
        {
            _tickTimer -= tickInterval;
            ticksThisFrame++;
            Tick();
            if (_isGameOver) break;
        }
    }

    // ── Game loop ─────────────────────────────────────────────────────────────

    private void StartGame()
    {
        _isGameOver   = false;
        _isWin        = false;
        _score        = 0;
        _currentDir   = startDirection;
        _tickTimer    = 0f;
        _totalTicks   = 0;

        inputReader.ResetDirection(startDirection);
        snakeController.Initialize(gridManager, startPosition);
        foodSpawner.Initialize(gridManager, snakeController);
        foodSpawner.SpawnFood();

        if (autoPlayer != null)
            autoPlayer.Initialise(gridManager, snakeController, foodSpawner, inputReader);

        uiManager.HideGameOver();
        uiManager.UpdateScore(_score);
        uiManager.UpdateSpeed(tickInterval > 0f ? 1f / tickInterval : 0f);

        Debug.Log($"[Game] ▶ New game. Grid={gridManager.Width}×{gridManager.Height} " +
                  $"Start={startPosition} Dir={startDirection}");
    }

    /// <summary>Creates a new AudioClip with the first <paramref name="skipSec"/> seconds removed.</summary>
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

    /// <summary>Called by SpeedBoostManager to change snake speed at runtime.</summary>
    public void SetTickInterval(float interval)
    {
        tickInterval = Mathf.Max(0.005f, interval); // cap at ~200 ticks/s
    }

    private void Tick()
    {
        _totalTicks++;
        uiManager.UpdateSpeed(tickInterval > 0f ? 1f / tickInterval : 0f);


        // 1. AI injects direction (no-op if AI off).
        autoPlayer?.TickAI(_currentDir);

        // 2. Consume buffered direction — detect turn.
        var newDir = inputReader.ConsumeDirection(_currentDir);
        if (newDir != _currentDir && _turnClip != null
            && Time.time - _lastTurnSfxTime > TurnSfxCooldown)
        {
            _sfx.PlayOneShot(_turnClip);
            _lastTurnSfxTime = Time.time;
        }
        _currentDir = newDir;

        // 3. Cache pre-move state.
        var headBefore = snakeController.HeadPosition;
        var food       = foodSpawner.FoodPosition;

        // 4. Move.
        var result = snakeController.Move(_currentDir);

        // 5. Handle collisions.
        if (result != SnakeController.MoveResult.Success)
        {
            TriggerGameOver(result, headBefore, food);
            return;
        }

        // 6. Check food.
        if (snakeController.HeadPosition == food)
        {
            snakeController.Grow();
            _score++;
            uiManager.UpdateScore(_score);
            foodSpawner.SpawnFood();
            if (_eatClip != null) _sfx.PlayOneShot(_eatClip);

            if (_score >= WinScore)
            {
                TriggerWin();
                return;
            }
        }
    }

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

        // ── Cause ──────────────────────────────────────────────────────────
        Vector2Int attempted = headPos + _currentDir;
        string cause = reason switch
        {
            SnakeController.MoveResult.WallCollision =>
                $"💥 HIT WALL  → tried ({attempted.x},{attempted.y}) out of bounds",
            SnakeController.MoveResult.SelfCollision =>
                $"🐍 HIT SELF  → tried ({attempted.x},{attempted.y}) occupied by body",
            _ => "Unknown"
        };

        // Which segment was hit?
        string hitSeg = "";
        if (reason == SnakeController.MoveResult.SelfCollision)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i] == attempted)
                {
                    string tag = i == 0 ? "HEAD" : i == cells.Count - 1 ? "TAIL" : $"seg[{i}]";
                    hitSeg = $" ({tag})";  break;
                }
            }
        }

        // ── All body positions ─────────────────────────────────────────────
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
            $"  Length : {cells.Count}\n" +
            $"  Ticks  : {_totalTicks}\n" +
            $"  Dir    : {DirName(_currentDir)}\n" +
            $"  Food   : ({food.x},{food.y})\n" +
            $"── Body ({cells.Count} segs) ──────────────\n" +
            sb
        );
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string DirName(Vector2Int d)
    {
        if (d == Vector2Int.up)    return "UP(W)";
        if (d == Vector2Int.down)  return "DOWN(S)";
        if (d == Vector2Int.left)  return "LEFT(A)";
        if (d == Vector2Int.right) return "RIGHT(D)";
        return $"({d.x},{d.y})";
    }
}
