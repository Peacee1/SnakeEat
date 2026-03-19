using UnityEngine;

/// <summary>
/// Listens to TikTokConnector gift/like events and applies speed boosts to the snake.
///
/// Triggers (permanent, stackable):
///   • 1 Rose gift   → +roseSpeedBoost ticks/s
///   • every likeMilestone likes → +likeSpeedBoost ticks/s
///
/// Temporary boost (existing):
///   • Any gift → +diamonds × speedPerDiamond ticks/s for boostDuration seconds
/// </summary>
public class SpeedBoostManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameManager     gameManager;
    [SerializeField] private TikTokConnector tiktokConnector;
    [SerializeField] private UIManager       uiManager;

    [Header("Base Speed")]
    [Tooltip("Starting ticks-per-second (no boost)")]
    [SerializeField] private float baseSpeed     = 50f;

    [Tooltip("Max speed reachable (ticks/s)")]
    [SerializeField] private float maxSpeed      = 200f;

    [Header("Diamond Boost (temporary)")]
    [Tooltip("Speed added per 💎 diamond")]
    [SerializeField] private float speedPerDiamond = 0.05f;

    [Tooltip("How long one gift's boost lasts (seconds)")]
    [SerializeField] private float boostDuration   = 5f;

    [Header("Rose Gift → Permanent Speed Boost")]
    [Tooltip("Gift name to match (case-insensitive)")]
    [SerializeField] private string roseGiftName   = "Rose";

    [Tooltip("Ticks/s permanently added per rose")]
    [SerializeField] private float  roseSpeedBoost = 1f;

    [Header("Like Milestone → Permanent Speed Boost")]
    [Tooltip("How many new likes trigger one boost")]
    [SerializeField] private int   likeMilestone  = 50;

    [Tooltip("Ticks/s permanently added per milestone")]
    [SerializeField] private float likeSpeedBoost = 1f;

    // ── Runtime ────────────────────────────────────────────────────────────────
    private float _permanentBoost;  // cumulative permanent ticks/s
    private float _tempBoost;       // current temporary boost on top
    private float _boostEndTime;
    private int   _totalLikes;
    private int   _nextLikeMilestone;

    private float CurrentSpeed => Mathf.Min(baseSpeed + _permanentBoost + _tempBoost, maxSpeed);

    private void Start()
    {
        _nextLikeMilestone = likeMilestone;
        ApplySpeed(CurrentSpeed);

        if (tiktokConnector == null)
        {
            Debug.LogWarning("[Boost] TikTokConnector not assigned.");
            return;
        }

        tiktokConnector.OnGift         += HandleGift;
        tiktokConnector.OnLike         += HandleLike;
        tiktokConnector.OnConnected    += url => Debug.Log($"[Boost] TikTok connected: {url}");
        tiktokConnector.OnDisconnected += ()  => Debug.LogWarning("[Boost] TikTok disconnected.");
    }

    private void Update()
    {
        // Decay temporary boost
        if (Time.time > _boostEndTime && _tempBoost > 0f)
        {
            _tempBoost = Mathf.MoveTowards(_tempBoost, 0f, 2f * Time.deltaTime);
            ApplySpeed(CurrentSpeed);
        }
    }

    private void OnDestroy()
    {
        if (tiktokConnector != null)
        {
            tiktokConnector.OnGift -= HandleGift;
            tiktokConnector.OnLike -= HandleLike;
        }
    }

    // ── Handlers ───────────────────────────────────────────────────────────────

    private void HandleGift(TikTokConnector.GiftEvent gift)
    {
        // Rose → permanent boost
        if (gift.giftName.Equals(roseGiftName, System.StringComparison.OrdinalIgnoreCase))
        {
            float added = roseSpeedBoost * gift.repeatCount;
            _permanentBoost += added;
            ApplySpeed(CurrentSpeed);
            uiManager?.ShowGiftNotification(gift.username, added, "🌹");
            Debug.Log($"[Boost] 🌹 Rose x{gift.repeatCount} from @{gift.username} → +{added:F1} perm | total perm: {_permanentBoost:F1}");
            return;
        }

        // Any other gift → temporary diamond boost
        int   totalDiamonds = gift.diamonds * gift.repeatCount;
        float tempAdded     = totalDiamonds * speedPerDiamond;
        _tempBoost    = Mathf.Max(_tempBoost + tempAdded, 0f);
        _boostEndTime = Time.time + boostDuration;
        ApplySpeed(CurrentSpeed);
        uiManager?.ShowGiftNotification(gift.username, tempAdded, "🎁");
        Debug.Log($"[Boost] 🎁 {gift.giftName} x{gift.repeatCount} (+{tempAdded:F1} temp for {boostDuration}s)");
    }

    private void HandleLike(TikTokConnector.LikeEvent like)
    {
        _totalLikes += like.count;

        while (_totalLikes >= _nextLikeMilestone)
        {
            _permanentBoost    += likeSpeedBoost;
            _nextLikeMilestone += likeMilestone;
            ApplySpeed(CurrentSpeed);
            uiManager?.ShowGiftNotification(like.username, likeSpeedBoost, "❤️");
            Debug.Log($"[Boost] ❤️ {_nextLikeMilestone - likeMilestone} likes milestone! +{likeSpeedBoost:F1} perm | total likes: {_totalLikes}");
        }
    }

    // ── Apply speed to GameManager ─────────────────────────────────────────────

    /// <summary>Resets all boosts to zero and returns to base speed. Call on game over or win.</summary>
    public void ResetSpeed()
    {
        _permanentBoost    = 0f;
        _tempBoost         = 0f;
        _boostEndTime      = 0f;
        _totalLikes        = 0;
        _nextLikeMilestone = likeMilestone;
        ApplySpeed(baseSpeed);
        Debug.Log($"[Boost] ♻️ Speed reset to base: {baseSpeed}");
    }

    private void ApplySpeed(float ticksPerSecond)
    {
        if (gameManager != null)
            gameManager.SetTickInterval(1f / Mathf.Max(ticksPerSecond, 0.1f));

        if (uiManager != null)
            uiManager.UpdateSpeed(ticksPerSecond);
    }
}
