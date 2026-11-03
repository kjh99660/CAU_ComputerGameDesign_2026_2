using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class BattleView : UIScreenView
{
    [SerializeField] private Button pauseButton;
    [SerializeField] private TMP_Text scoreValue;
    [SerializeField] private TMP_Text timerValue;
    [SerializeField] private Image specialGaugeFill;
    [Header("Combat feedback - provisional")]
    [SerializeField, Min(0.05f)] private float scoreFeedbackDuration = 0.3f;
    [SerializeField, Min(0.05f)] private float hitFlashDuration = 0.35f;
    [SerializeField, Range(0f, 0.5f)] private float hitFlashOpacity = 0.18f;
    private Image _hitFlash;
    private float _scoreFeedbackRemaining;
    private float _hitFlashRemaining;
    private Color _scoreBaseColor;
    private Vector3 _scoreBaseScale;
    private bool _scoreStyleCached;
    private TMP_Text _gaugePercent;
    private Sprite _gaugeRingSprite;
    private Texture2D _gaugeRingTexture;
    private int _shownPercent = -1;

    public event Action PauseClicked;
    private float _lastRemainingSeconds = 0f;
    private int _lastRenderedSecond = -1;
    private int _lastScore = 0;
    public override void AutoBind()
    {
        base.AutoBind();
        if (pauseButton == null) pauseButton = FindNamed<Button>("PauseButton");
        if (scoreValue == null) scoreValue = FindNamed<TMP_Text>("ScoreValue");
        if (timerValue == null) timerValue = FindNamed<TMP_Text>("TimerValue");
        if (specialGaugeFill == null) specialGaugeFill = FindNamed<Image>("SpecialGaugeFill");
    }

    private void OnEnable()
    {
        AutoBind();
        EnsureSpecialGauge();
        EnsureHitFlash();
        if (pauseButton != null) pauseButton.onClick.AddListener(HandlePauseClicked);
    }

    private void OnDisable()
    {
        if (pauseButton != null) pauseButton.onClick.RemoveListener(HandlePauseClicked);
    }

    public void Render(BattleUiModel model)
    {
        RenderScore(model.Score);
        RenderTimer(model.RemainingSeconds);
        RenderGauge(model.GaugeNormalized);
    }

    public void SetPauseInteractable(bool interactable) => SetButtonInteractable(pauseButton, interactable);

    //TODO : 메니저 클래스에서 가져오기
    public override void ResetView()
    {
        ClearFeedback();
        Render(new BattleUiModel(0, 100f, 0f));
    }

    public void PlayScoreFeedback(int delta)
    {
        if (scoreValue == null || delta == 0) return;
        CacheScoreStyle();
        _scoreFeedbackRemaining = Mathf.Max(0.05f, scoreFeedbackDuration);
        scoreValue.color = delta > 0 ? new Color(0.25f, 1f, 1f) : new Color(1f, 0.35f, 0.35f);
        scoreValue.transform.localScale = _scoreBaseScale * 1.18f;
    }

    public void PlayPlayerHitFeedback()
    {
        EnsureHitFlash();
        _hitFlashRemaining = Mathf.Max(0.05f, hitFlashDuration);
        _hitFlash.color = new Color(1f, 0.08f, 0.08f, hitFlashOpacity);
    }

    public void UpdateFeedback(float deltaTime)
    {
        if (_scoreFeedbackRemaining > 0f)
        {
            _scoreFeedbackRemaining = Mathf.Max(0f, _scoreFeedbackRemaining - deltaTime);
            float t = _scoreFeedbackRemaining / Mathf.Max(0.05f, scoreFeedbackDuration);
            if (scoreValue != null) scoreValue.transform.localScale = _scoreBaseScale * (1f + 0.18f * t);
            if (_scoreFeedbackRemaining <= 0f && scoreValue != null) scoreValue.color = _scoreBaseColor;
        }
        if (_hitFlashRemaining > 0f && _hitFlash != null)
        {
            _hitFlashRemaining = Mathf.Max(0f, _hitFlashRemaining - deltaTime);
            _hitFlash.color = new Color(1f, 0.08f, 0.08f, hitFlashOpacity * _hitFlashRemaining / Mathf.Max(0.05f, hitFlashDuration));
        }
    }

    private void CacheScoreStyle()
    {
        if (_scoreStyleCached || scoreValue == null) return;
        _scoreBaseScale = scoreValue.transform.localScale;
        _scoreBaseColor = scoreValue.color;
        _scoreStyleCached = true;
    }

    private void EnsureHitFlash()
    {
        if (_hitFlash != null) return;
        var go = new GameObject("PlayerHitFlash", typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)go.transform;
        rect.SetParent(transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        _hitFlash = go.GetComponent<Image>();
        _hitFlash.color = Color.clear;
        _hitFlash.raycastTarget = false;
    }

    private void ClearFeedback()
    {
        _scoreFeedbackRemaining = _hitFlashRemaining = 0f;
        if (_hitFlash != null) _hitFlash.color = Color.clear;
        if (_scoreStyleCached && scoreValue != null)
        {
            scoreValue.color = _scoreBaseColor;
            scoreValue.transform.localScale = _scoreBaseScale;
        }
    }

    public void RenderScore(int score)
    {
        _lastScore = score;
        SetText(scoreValue, score.ToString("D6"));
    }

    public void RenderGauge(float value)
    {
        float normalized = Mathf.Clamp01(value);
        bool full = normalized >= 1f;
        if (specialGaugeFill != null)
        {
            specialGaugeFill.fillAmount = normalized;
            specialGaugeFill.color = full ? new Color(1f, 0.78f, 0.24f) : new Color(0.12f, 0.92f, 1f);
        }
        int percent = Mathf.FloorToInt(normalized * 100f);
        if (_gaugePercent != null && percent != _shownPercent)
        {
            _shownPercent = percent;
            _gaugePercent.SetText("{0:0}%", percent);
            _gaugePercent.color = full ? new Color(1f, 0.85f, 0.4f) : Color.white;
        }
    }

    // A neutral circular meter makes the unfilled portion and percentage readable.
    private void EnsureSpecialGauge()
    {
        if (specialGaugeFill != null) return;
        const int size = 128;
        _gaugeRingTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float radius = new Vector2(x + 0.5f - size * 0.5f, y + 0.5f - size * 0.5f).magnitude;
            byte alpha = (byte)(255f * Mathf.Clamp01(Mathf.Min(radius - 49f, 62f - radius)));
            pixels[y * size + x] = new Color32(255, 255, 255, alpha);
        }
        _gaugeRingTexture.SetPixels32(pixels);
        _gaugeRingTexture.Apply(false, true);
        _gaugeRingSprite = Sprite.Create(_gaugeRingTexture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        var frameObject = new GameObject("SpecialGaugeFrame", typeof(RectTransform), typeof(Image));
        var frameRect = (RectTransform)frameObject.transform;
        frameRect.SetParent(transform, false);
        frameRect.anchorMin = frameRect.anchorMax = new Vector2(0.5f, 0f);
        frameRect.pivot = new Vector2(0.5f, 0f);
        frameRect.anchoredPosition = new Vector2(0f, 35f);
        frameRect.sizeDelta = new Vector2(170f, 170f);
        Image frame = frameObject.GetComponent<Image>();
        frame.sprite = _gaugeRingSprite;
        frame.color = new Color(0.06f, 0.19f, 0.25f, 0.95f);
        frame.raycastTarget = false;
        var fillObject = new GameObject("SpecialGaugeFill", typeof(RectTransform), typeof(Image));
        var fillRect = (RectTransform)fillObject.transform;
        fillRect.SetParent(frameRect, false);
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        specialGaugeFill = fillObject.GetComponent<Image>();
        specialGaugeFill.sprite = _gaugeRingSprite;
        specialGaugeFill.type = Image.Type.Filled;
        specialGaugeFill.fillMethod = Image.FillMethod.Radial360;
        specialGaugeFill.fillOrigin = (int)Image.Origin360.Top;
        specialGaugeFill.color = new Color(0.12f, 0.92f, 1f);
        specialGaugeFill.raycastTarget = false;
        specialGaugeFill.fillAmount = 0f;
        var textObject = new GameObject("SpecialGaugePercent", typeof(RectTransform), typeof(TextMeshProUGUI));
        var textRect = (RectTransform)textObject.transform;
        textRect.SetParent(frameRect, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(20f, 20f);
        textRect.offsetMax = new Vector2(-20f, -20f);
        _gaugePercent = textObject.GetComponent<TextMeshProUGUI>();
        if (scoreValue != null) _gaugePercent.font = scoreValue.font;
        _gaugePercent.alignment = TextAlignmentOptions.Center;
        _gaugePercent.fontStyle = FontStyles.Bold;
        _gaugePercent.fontSize = 40f;
        _gaugePercent.enableAutoSizing = true;
        _gaugePercent.fontSizeMin = 24f;
        _gaugePercent.fontSizeMax = 40f;
        _gaugePercent.outlineColor = new Color32(0, 0, 0, 255);
        _gaugePercent.outlineWidth = 0.2f;
        _gaugePercent.raycastTarget = false;
        _shownPercent = -1;
        RenderGauge(0f);
    }

    private void OnDestroy()
    {
        if (_gaugeRingSprite != null) Destroy(_gaugeRingSprite);
        if (_gaugeRingTexture != null) Destroy(_gaugeRingTexture);
    }

    public void RenderTimer(float remainingSeconds)
    {
        _lastRemainingSeconds = remainingSeconds;
        int second = Mathf.Max(0, Mathf.CeilToInt(remainingSeconds));
        if (second == _lastRenderedSecond) return;
        _lastRenderedSecond = second;
        SetText(timerValue, FormatTime(_lastRemainingSeconds));
    }

    public float GetRemainingSeconds()
    {
        return _lastRemainingSeconds;
    }

    private string FormatTime(float seconds)
    {
        int value = Mathf.Max(0, Mathf.CeilToInt(seconds));
        return $"{value / 60:00}:{value % 60:00}";
    }

    private void HandlePauseClicked() => PauseClicked?.Invoke();
}
