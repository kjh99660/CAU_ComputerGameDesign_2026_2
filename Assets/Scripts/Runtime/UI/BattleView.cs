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
    [Header("Hit glitch - provisional (UI Lab H3 블록 깨짐)")]
    [SerializeField, Min(0.05f)] private float hitGlitchDuration = 0.18f;
    [SerializeField, Range(0f, 1.5f)] private float hitGlitchStrength = 1f;
    [SerializeField, Min(0f)] private float hudShakePixels = 8f;
    private Image _hitFlash;
    private Sprite _hitEdgeSprite;
    private Texture2D _hitEdgeTexture;
    private float _glitchRemaining;
    private RectTransform _rect;
    private Vector2 _restPosition;
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
        _rect = (RectTransform)transform;
        _restPosition = _rect.anchoredPosition;
        EnsureSpecialGauge();
        EnsureHitFlash();
        if (pauseButton != null) pauseButton.onClick.AddListener(HandlePauseClicked);
    }

    private void OnDisable()
    {
        if (pauseButton != null) pauseButton.onClick.RemoveListener(HandlePauseClicked);
        StopGlitch();
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
        scoreValue.color = delta > 0 ? UiPalette.Accent : UiPalette.Alert;
        scoreValue.transform.localScale = _scoreBaseScale * 1.18f;
    }

    // 피격: 화면 가장자리만 옅게 붉어지고(위험 신호), 짧은 블록 깨짐 글리치와 HUD 흔들림이 지나간다.
    public void PlayPlayerHitFeedback()
    {
        EnsureHitFlash();
        _hitFlashRemaining = Mathf.Max(0.05f, hitFlashDuration);
        _hitFlash.color = UiPalette.WithAlpha(UiPalette.Alert, hitFlashOpacity);
        _glitchRemaining = Mathf.Max(0.05f, hitGlitchDuration);
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
            _hitFlash.color = UiPalette.WithAlpha(UiPalette.Alert, hitFlashOpacity * _hitFlashRemaining / Mathf.Max(0.05f, hitFlashDuration));
        }
        if (_glitchRemaining > 0f)
        {
            _glitchRemaining = Mathf.Max(0f, _glitchRemaining - deltaTime);
            if (_glitchRemaining <= 0f)
            {
                StopGlitch();
                return;
            }
            // 빠르게 올라갔다가(15%) 제곱 곡선으로 빠르게 사라진다.
            float t = 1f - _glitchRemaining / Mathf.Max(0.05f, hitGlitchDuration);
            float envelope = t < 0.15f ? t / 0.15f : Mathf.Pow(1f - (t - 0.15f) / 0.85f, 2f);
            float amount = envelope * hitGlitchStrength;
            ScreenGlitchState.Set(amount, UnityEngine.Random.Range(0f, 1000f));
            if (_rect != null)
                _rect.anchoredPosition = _restPosition + new Vector2(
                    UnityEngine.Random.Range(-1f, 1f) * hudShakePixels * amount,
                    UnityEngine.Random.Range(-1f, 1f) * hudShakePixels * 0.4f * amount);
        }
    }

    private void StopGlitch()
    {
        _glitchRemaining = 0f;
        ScreenGlitchState.Clear();
        if (_rect != null) _rect.anchoredPosition = _restPosition;
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
        _hitFlash.sprite = CreateEdgeSprite();
        _hitFlash.color = Color.clear;
        _hitFlash.raycastTarget = false;
    }

    // 가운데는 투명하고 가장자리로 갈수록 진해지는 테두리. 화면 전체를 빨갛게 덮지 않는다.
    private Sprite CreateEdgeSprite()
    {
        const int width = 96, height = 54;
        _hitEdgeTexture = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float dx = (x + 0.5f) / width * 2f - 1f, dy = (y + 0.5f) / height * 2f - 1f;
            float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.25f, Mathf.Sqrt(dx * dx + dy * dy)));
            pixels[y * width + x] = new Color32(255, 255, 255, (byte)(255f * edge));
        }
        _hitEdgeTexture.SetPixels32(pixels);
        _hitEdgeTexture.Apply(false, true);
        _hitEdgeSprite = Sprite.Create(_hitEdgeTexture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f));
        return _hitEdgeSprite;
    }

    private void ClearFeedback()
    {
        StopGlitch();
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
            specialGaugeFill.color = full ? UiPalette.Accent : UiPalette.Primary;
        }
        int percent = Mathf.FloorToInt(normalized * 100f);
        if (_gaugePercent != null && percent != _shownPercent)
        {
            _shownPercent = percent;
            _gaugePercent.SetText("{0:0}%", percent);
            _gaugePercent.color = full ? UiPalette.Accent : UiPalette.Ink;
        }
    }

    // 통신 허브와 같은 육각 윤곽 게이지. 채워지지 않은 부분과 퍼센트를 함께 읽을 수 있게 한다.
    private void EnsureSpecialGauge()
    {
        if (specialGaugeFill != null) return;
        const int size = 128;
        _gaugeRingTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // 꼭짓점이 위아래인 육각형의 변까지 거리(apothem 기준)로 링 두께를 정한다.
            float px = Mathf.Abs(x + 0.5f - size * 0.5f), py = Mathf.Abs(y + 0.5f - size * 0.5f);
            float radius = Mathf.Max(px, px * 0.5f + py * 0.8660254f);
            byte alpha = (byte)(255f * Mathf.Clamp01(Mathf.Min(radius - 45f, 54f - radius)));
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
        frame.color = UiPalette.WithAlpha(UiPalette.Muted, 0.38f);
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
        specialGaugeFill.color = UiPalette.Primary;
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
        if (_hitEdgeSprite != null) Destroy(_hitEdgeSprite);
        if (_hitEdgeTexture != null) Destroy(_hitEdgeTexture);
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
