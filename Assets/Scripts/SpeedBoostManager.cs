using UnityEngine;

/// <summary>
/// Listens to TikTokConnector gift/like events and applies speed boosts.
///
/// Gift logic (unified — same diamond value = same effect):
///   diamonds × repeatCount × speedPerDiamond → permanent ticks/s added.
///   Default: 1 Rose (1 💎) = +1 ticks/s.
///
/// Like milestone:
///   Every likeMilestone new likes → +likeSpeedBoost ticks/s permanent.
/// </summary>
public class SpeedBoostManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameManager     gameManager;
    [SerializeField] private TikTokConnector tiktokConnector;
    [SerializeField] private UIManager       uiManager;

    [Header("Base Speed")]
    [Tooltip("Starting ticks-per-second (no boost)")]
    [SerializeField] private float baseSpeed = 50f;

    [Tooltip("Hard cap on speed (ticks/s). Expensive gifts stack but cannot exceed this.")]
    [SerializeField] private float maxSpeed  = 150f;

    [Header("Gift → Permanent Speed (unified by diamond value)")]
    [Tooltip("Permanent ticks/s added per 💎 diamond received.\n" +
             "1 Rose = 1 💎 → +1 ticks/s by default.\n" +
             "All gifts with the same diamond value give the same boost.")]
    [SerializeField] private float speedPerDiamond = 1f;

    [Header("Like Milestone → Permanent Speed")]
    [Tooltip("How many new likes trigger one boost step")]
    [SerializeField] private int   likeMilestone  = 100;

    [Tooltip("Ticks/s permanently added per milestone")]
    [SerializeField] private float likeSpeedBoost = 1f;

    // ── Runtime ────────────────────────────────────────────────────────────────
    private float _permanentBoost;
    private int   _totalLikes;
    private int   _nextLikeMilestone;

    private float CurrentSpeed => Mathf.Min(baseSpeed + _permanentBoost, maxSpeed);

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
        // Unified formula: same diamond value → same permanent boost.
        // 1 Rose (1 💎) × speedPerDiamond (1.0) = +1 ticks/s.
        int   totalDiamonds = gift.diamonds * gift.repeatCount;
        float added         = totalDiamonds * speedPerDiamond;
        _permanentBoost    += added;

        ApplySpeed(CurrentSpeed);

        // Pick emoji based on gift name for the notification.
        string emoji = gift.giftName.ToLower() switch
        {
            "rose"    => "🌹",
            "galaxy"  => "🌌",
            "lion"    => "🦁",
            "universe"=> "🌐",
            _         => "🎁"
        };

        uiManager?.ShowGiftNotification(gift.username, added, emoji);
        Debug.Log($"[Boost] {emoji} {gift.giftName} x{gift.repeatCount} ({totalDiamonds}💎) " +
                  $"from @{gift.username} → +{added:F1} perm | total perm: {_permanentBoost:F1} | " +
                  $"speed: {CurrentSpeed:F1}/{maxSpeed}");
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
            Debug.Log($"[Boost] ❤️ Like milestone {_nextLikeMilestone - likeMilestone}! " +
                      $"+{likeSpeedBoost:F1} perm | total likes: {_totalLikes}");
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Resets all boosts. Call on game over / win.</summary>
    public void ResetSpeed()
    {
        _permanentBoost    = 0f;
        _totalLikes        = 0;
        _nextLikeMilestone = likeMilestone;
        ApplySpeed(baseSpeed);
        Debug.Log($"[Boost] ♻️ Speed reset → {baseSpeed} ticks/s");
    }

    private void ApplySpeed(float ticksPerSecond)
    {
        if (gameManager != null)
            gameManager.SetTickInterval(1f / Mathf.Max(ticksPerSecond, 0.1f));
        if (uiManager != null)
            uiManager.UpdateSpeed(ticksPerSecond);
    }
}
