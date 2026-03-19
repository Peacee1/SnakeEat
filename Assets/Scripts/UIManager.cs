using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI finalScoreText;
    [SerializeField] private TextMeshProUGUI speedText;
    [SerializeField] private GameObject      gameOverPanel;

    // Created at runtime
    private TextMeshProUGUI _notifText;
    private Tween           _notifTween;

    private void Awake()
    {
        // SpeedText — below scoreText
        if (speedText == null && scoreText != null)
            speedText = CreateLabel("SpeedText", scoreText, -55f, scoreText.fontSize);

        // Gift notification text — below speedText
        if (scoreText != null)
            _notifText = CreateLabel("GiftNotifText", scoreText, -110f, scoreText.fontSize * 0.75f);

        if (_notifText != null)
        {
            _notifText.color = new Color(1f, 0.85f, 0.2f, 0f); // gold, starts invisible
        }

        // Rules — bottom-left corner
        if (scoreText != null)
        {
            var parent = scoreText.transform.parent;

            // Line 1: heart icon + "100 hearts = +1 speed"
            var heartSprite = Resources.Load<Sprite>("Pic/phong-cach-hoat-hinh-trai-tim_78370-7988-removebg-preview");
            if (heartSprite != null)
            {
                var heartIconGO = new GameObject("HeartIcon");
                heartIconGO.transform.SetParent(parent, false);
                var heartImg = heartIconGO.AddComponent<Image>();
                heartImg.sprite         = heartSprite;
                heartImg.preserveAspect = true;
                var rtH = heartIconGO.GetComponent<RectTransform>();
                rtH.anchorMin = rtH.anchorMax = rtH.pivot = new Vector2(0f, 0f);
                rtH.anchoredPosition = new Vector2(30f, 78f);  // +3.5 to center with 45px text
                rtH.sizeDelta        = new Vector2(38f, 38f);
            }

            var line1GO = new GameObject("RuleLine1");
            line1GO.transform.SetParent(parent, false);
            var l1 = line1GO.AddComponent<TextMeshProUGUI>();
            l1.text      = "100 hearts = +1 speed";
            l1.font      = scoreText.font;
            l1.fontSize  = scoreText.fontSize * 0.60f;
            l1.color     = new Color(0.75f, 0.75f, 0.75f, 0.85f);
            l1.fontStyle = scoreText.fontStyle;
            var rt1 = line1GO.GetComponent<RectTransform>();
            rt1.anchorMin = rt1.anchorMax = rt1.pivot = new Vector2(0f, 0f);
            rt1.anchoredPosition = new Vector2(74f, 75f);  // offset right of heart icon
            rt1.sizeDelta        = new Vector2(340f, 45f);

            // Line 2: rose icon + "1 Rose = +1 speed"
            // Rose sprite icon
            var roseSprite = Resources.Load<Sprite>("Pic/eba3a9bb85c33e017f3648eaf88d7189tplv-obj");
            if (roseSprite != null)
            {
                var iconGO = new GameObject("RoseIcon");
                iconGO.transform.SetParent(parent, false);
                var img = iconGO.AddComponent<Image>();
                img.sprite          = roseSprite;
                img.preserveAspect  = true;
                var rtI = iconGO.GetComponent<RectTransform>();
                rtI.anchorMin = rtI.anchorMax = rtI.pivot = new Vector2(0f, 0f);
                rtI.anchoredPosition = new Vector2(30f, 30f);
                rtI.sizeDelta        = new Vector2(38f, 38f);
            }

            var line2GO = new GameObject("RuleLine2");
            line2GO.transform.SetParent(parent, false);
            var l2 = line2GO.AddComponent<TextMeshProUGUI>();
            l2.text      = "1 Rose = +1 speed";
            l2.font      = scoreText.font;
            l2.fontSize  = scoreText.fontSize * 0.60f;
            l2.color     = new Color(0.75f, 0.75f, 0.75f, 0.85f);
            l2.fontStyle = scoreText.fontStyle;
            var rt2 = line2GO.GetComponent<RectTransform>();
            rt2.anchorMin = rt2.anchorMax = rt2.pivot = new Vector2(0f, 0f);
            rt2.anchoredPosition = new Vector2(74f, 30f); // offset right of icon
            rt2.sizeDelta        = new Vector2(340f, 45f);
        }
    }

    private void Start()
    {
        HideGameOver();
        UpdateScore(0);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void UpdateScore(int score)
    {
        if (scoreText != null)
            scoreText.text = $"Score: {score}";
    }

    public void UpdateSpeed(float ticksPerSecond)
    {
        if (speedText != null)
            speedText.text = $"Speed: {ticksPerSecond:F0}";
    }

    public void ShowGiftNotification(string username, float buffAmount, string emoji = "🎁")
    {
        if (_notifText == null) return;

        // Format: 🌹 @username  +1.0 speed!
        _notifText.text = $"{emoji} @{username}  +{buffAmount:F1} speed!";

        // Kill previous fade, restart
        _notifTween?.Kill();

        var c = _notifText.color;
        _notifText.color = new Color(c.r, c.g, c.b, 1f);

        _notifTween = DOTween.Sequence()
            .AppendInterval(2.0f)          // hold for 2s
            .Append(
                _notifText.DOFade(0f, 1.0f).SetEase(Ease.InQuad)
            );
    }

    public void ShowGameOver(int finalScore, float countdown)
    {
        if (gameOverPanel != null) gameOverPanel.SetActive(true);
        if (finalScoreText != null)
            finalScoreText.text = $"Score: {finalScore}\nRestart in {countdown:F1}s...";
    }

    public void ShowWin(int finalScore, float countdown)
    {
        if (gameOverPanel != null) gameOverPanel.SetActive(true);
        if (finalScoreText != null)
            finalScoreText.text = $"🏆 WIN! Score: {finalScore}\nRestart in {countdown:F1}s...";
    }

    public void HideGameOver()
    {
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private TextMeshProUGUI CreateLabel(string goName, TextMeshProUGUI reference, float yOffset, float fontSize)
    {
        var go  = new GameObject(goName);
        go.transform.SetParent(reference.transform.parent, worldPositionStays: false);

        var tmp       = go.AddComponent<TextMeshProUGUI>();
        tmp.font      = reference.font;
        tmp.fontSize  = fontSize;
        tmp.color     = reference.color;
        tmp.alignment = reference.alignment;
        tmp.fontStyle = reference.fontStyle;

        var srcRT = reference.GetComponent<RectTransform>();
        var dstRT = tmp.GetComponent<RectTransform>();
        dstRT.anchorMin        = srcRT.anchorMin;
        dstRT.anchorMax        = srcRT.anchorMax;
        dstRT.pivot            = srcRT.pivot;
        dstRT.sizeDelta        = new Vector2(srcRT.sizeDelta.x * 2f, srcRT.sizeDelta.y);
        dstRT.anchoredPosition = srcRT.anchoredPosition + new Vector2(0, yOffset);

        go.transform.SetSiblingIndex(reference.transform.GetSiblingIndex() + 1);
        return tmp;
    }
}
