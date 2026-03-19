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

    // ── Level UI (created at runtime) ─────────────────────────────────────────
    private TextMeshProUGUI _levelText;       // "LEVEL 1" at center-top
    private TextMeshProUGUI _levelUpFlash;    // "LEVEL UP!" big flash
    private Tween           _levelUpTween;

    // Arrow key UI
    private Image[]  _arrowKeyImages;
    private Sprite[] _arrowNormalSprites;
    private Sprite[] _arrowPressedSprites;

    private void Awake()
    {
        // SpeedText — below scoreText
        if (speedText == null && scoreText != null)
            speedText = CreateLabel("SpeedText", scoreText, -55f, scoreText.fontSize);

        // Gift notification text
        if (scoreText != null)
            _notifText = CreateLabel("GiftNotifText", scoreText, -110f, scoreText.fontSize * 0.75f);

        if (_notifText != null)
            _notifText.color = new Color(1f, 0.85f, 0.2f, 0f);

        // ── Level text — CENTER TOP ────────────────────────────────────────────
        if (scoreText != null)
        {
            var parent = scoreText.transform.parent;

            // "LEVEL 1  (5×5)" — fixed center-top
            var levelGO = new GameObject("LevelText");
            levelGO.transform.SetParent(parent, false);
            _levelText           = levelGO.AddComponent<TextMeshProUGUI>();
            _levelText.font      = scoreText.font;
            _levelText.fontSize  = scoreText.fontSize * 1.10f;
            _levelText.fontStyle = FontStyles.Bold;
            _levelText.color     = new Color(0.35f, 1f, 0.55f, 1f);   // bright green
            _levelText.alignment = TextAlignmentOptions.Center;
            _levelText.text      = "LEVEL 1  (5×5)";

            var rtL = levelGO.GetComponent<RectTransform>();
            rtL.anchorMin        = new Vector2(0f, 1f);   // top-center
            rtL.anchorMax        = new Vector2(1f, 1f);
            rtL.pivot            = new Vector2(0.5f, 1f);
            rtL.anchoredPosition = new Vector2(0f, -18f); // 18 px below top edge
            rtL.sizeDelta        = new Vector2(0f, 70f);

            // "LEVEL UP!" flash text — hidden by default
            var flashGO = new GameObject("LevelUpFlash");
            flashGO.transform.SetParent(parent, false);
            _levelUpFlash           = flashGO.AddComponent<TextMeshProUGUI>();
            _levelUpFlash.font      = scoreText.font;
            _levelUpFlash.fontSize  = scoreText.fontSize * 2.8f;
            _levelUpFlash.fontStyle = FontStyles.Bold;
            _levelUpFlash.color     = new Color(1f, 0.9f, 0.1f, 0f);  // gold, starts invisible
            _levelUpFlash.alignment = TextAlignmentOptions.Center;
            _levelUpFlash.text      = "LEVEL UP!";

            var rtF = flashGO.GetComponent<RectTransform>();
            rtF.anchorMin        = new Vector2(0f, 0f);
            rtF.anchorMax        = new Vector2(1f, 1f);
            rtF.pivot            = new Vector2(0.5f, 0.5f);
            rtF.offsetMin        = Vector2.zero;
            rtF.offsetMax        = Vector2.zero;
        }

        // ── Rules — bottom-left ────────────────────────────────────────────────
        if (scoreText != null)
        {
            var parent = scoreText.transform.parent;

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
                rtH.anchoredPosition = new Vector2(30f, 78f);
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
            rt1.anchoredPosition = new Vector2(74f, 75f);
            rt1.sizeDelta        = new Vector2(340f, 45f);

            var roseSprite = Resources.Load<Sprite>("Pic/eba3a9bb85c33e017f3648eaf88d7189tplv-obj");
            if (roseSprite != null)
            {
                var iconGO = new GameObject("RoseIcon");
                iconGO.transform.SetParent(parent, false);
                var img = iconGO.AddComponent<Image>();
                img.sprite         = roseSprite;
                img.preserveAspect = true;
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
            rt2.anchoredPosition = new Vector2(74f, 30f);
            rt2.sizeDelta        = new Vector2(340f, 45f);
        }

        // ── Arrow keys ─────────────────────────────────────────────────────────
        var arrowSprites = Resources.LoadAll<Sprite>(
            "Pic/Gemini_Generated_Image_ii93hxii93hxii93-removebg-preview");

        if (arrowSprites != null && arrowSprites.Length >= 4 && scoreText != null)
        {
            var parent2   = scoreText.transform.parent;
            const float keySize = 90f;
            const float gap     = 6f;
            float step  = keySize + gap;

            var containerGO = new GameObject("ArrowKeysHint");
            containerGO.transform.SetParent(parent2, false);
            var rtC = containerGO.AddComponent<RectTransform>();
            rtC.anchorMin        = new Vector2(1f, 0.5f);
            rtC.anchorMax        = new Vector2(1f, 0.5f);
            rtC.pivot            = new Vector2(1f, 0.5f);
            rtC.anchoredPosition = new Vector2(-60f, 0f);
            rtC.sizeDelta        = new Vector2(step * 3f, step * 2f);

            var positions = new Vector2[]
            {
                new Vector2( 0f,    step * 0.5f),
                new Vector2(-step, -step * 0.5f),
                new Vector2( 0f,   -step * 0.5f),
                new Vector2( step, -step * 0.5f),
            };

            int[] normalIdx  = { 1, 0, 2, 3 };
            int[] pressedIdx = { 5, 4, 6, 7 };

            _arrowKeyImages      = new Image[4];
            _arrowNormalSprites  = new Sprite[4];
            _arrowPressedSprites = new Sprite[4];

            for (int i = 0; i < 4; i++)
            {
                var keyGO = new GameObject($"ArrowKey_{i}");
                keyGO.transform.SetParent(containerGO.transform, false);
                var img = keyGO.AddComponent<Image>();
                img.preserveAspect = true;
                img.color          = new Color(1f, 1f, 1f, 0.80f);

                _arrowNormalSprites[i]  = normalIdx[i]  < arrowSprites.Length ? arrowSprites[normalIdx[i]]  : null;
                _arrowPressedSprites[i] = pressedIdx[i] < arrowSprites.Length ? arrowSprites[pressedIdx[i]] : null;
                img.sprite = _arrowNormalSprites[i];
                _arrowKeyImages[i] = img;

                var rtK = keyGO.GetComponent<RectTransform>();
                rtK.anchorMin        = new Vector2(0.5f, 0.5f);
                rtK.anchorMax        = new Vector2(0.5f, 0.5f);
                rtK.pivot            = new Vector2(0.5f, 0.5f);
                rtK.anchoredPosition = positions[i];
                rtK.sizeDelta        = new Vector2(keySize, keySize);
            }
        }
    }

    public void FlashKey(Vector2Int direction)
    {
        int idx = direction == Vector2Int.up    ? 0 :
                  direction == Vector2Int.left   ? 1 :
                  direction == Vector2Int.down   ? 2 :
                  direction == Vector2Int.right  ? 3 : -1;

        if (idx < 0 || _arrowKeyImages == null || idx >= _arrowKeyImages.Length) return;
        var img     = _arrowKeyImages[idx];
        var pressed = _arrowPressedSprites[idx];
        var normal  = _arrowNormalSprites[idx];
        if (img == null || pressed == null) return;

        img.sprite = pressed;
        DOVirtual.DelayedCall(0.5f, () => { if (img != null) img.sprite = normal; });
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

    /// <summary>Updates the level label at center-top.</summary>
    public void UpdateLevel(int level, int boardSize)
    {
        if (_levelText != null)
            _levelText.text = $"LEVEL {level}  ({boardSize}×{boardSize})";
    }

    /// <summary>
    /// Plays a gold "LEVEL UP!" flash animation in the centre of the screen,
    /// then updates the level label to show the new level.
    /// </summary>
    public void ShowLevelUp(int newLevel, int newBoardSize)
    {
        if (_levelUpFlash == null) return;

        _levelUpTween?.Kill();

        // Update the small label immediately
        if (_levelText != null)
            _levelText.text = $"LEVEL {newLevel}  ({newBoardSize}×{newBoardSize})";

        string msg = $"LEVEL {newLevel}!\n{newBoardSize}×{newBoardSize} BOARD";
        _levelUpFlash.text  = msg;

        // Scale punch: start small, punch to 1.3×, settle to 1.0×
        _levelUpFlash.transform.localScale = Vector3.one * 0.4f;

        _levelUpTween = DOTween.Sequence()
            // Fade in + scale up
            .Append(_levelUpFlash.DOFade(1f, 0.20f).SetEase(Ease.OutCubic))
            .Join(_levelUpFlash.transform.DOScale(1.3f, 0.20f).SetEase(Ease.OutBack))
            // Settle to normal size
            .Append(_levelUpFlash.transform.DOScale(1.0f, 0.15f).SetEase(Ease.InOutSine))
            // Hold
            .AppendInterval(0.55f)
            // Fade out
            .Append(_levelUpFlash.DOFade(0f, 0.30f).SetEase(Ease.InQuad))
            .OnComplete(() => _levelUpFlash.transform.localScale = Vector3.one);
    }

    public void ShowGiftNotification(string username, float buffAmount, string emoji = "🎁")
    {
        if (_notifText == null) return;

        _notifText.text = $"{emoji} @{username}  +{buffAmount:F1} speed!";
        _notifTween?.Kill();

        var c = _notifText.color;
        _notifText.color = new Color(c.r, c.g, c.b, 1f);

        _notifTween = DOTween.Sequence()
            .AppendInterval(2.0f)
            .Append(_notifText.DOFade(0f, 1.0f).SetEase(Ease.InQuad));
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
            finalScoreText.text = $"🏆 WIN!  Score: {finalScore}\nRestart in {countdown:F1}s...";
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
