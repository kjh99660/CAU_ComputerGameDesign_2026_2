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
    [SerializeField, Min(0.05f)] private float hitGlitchDuration = 0.24f;
    [SerializeField, Range(0f, 1.5f)] private float hitGlitchStrength = 0.8f;
    [SerializeField, Min(0f)] private float hudShakePixels = 8f;
    // 맞은 뒤 3D 화면이 흐려졌다가 천천히 돌아온다. 연달아 맞으면 흐린 시간이 쌓인다. 세기가 0이면 쓰지 않는다.
    [Header("Hit blur - provisional")]
    [SerializeField, Min(0.05f)] private float hitBlurSeconds = 0.6f;
    [SerializeField, Range(0f, 1f)] private float hitBlurStrength = 0.4f;
    [SerializeField, Min(0.05f)] private float hitBlurMaxSeconds = 1f;
    // 남은 시간과 준비된 게이지를 눈에 띄게 하는 강조. 모두 임시값이며 unscaled time으로 재생한다.
    [Header("HUD emphasis - provisional")]
    // 남은 시간이 이 값(초) 이하로 표시되면 타이머를 경고색으로 바꾼다. 0이면 쓰지 않는다.
    [SerializeField, Min(0f)] private float timerWarningSeconds = 10f;
    [SerializeField, Min(1f)] private float timerTickScale = 1.14f;
    [SerializeField, Min(0f)] private float timerTickSeconds = 0.25f;
    // 게이지가 가득 찬 동안의 맥동. 속도가 0이거나 크기가 1이면 쓰지 않는다.
    [SerializeField, Min(0f)] private float gaugePulseHz = 1.4f;
    [SerializeField, Min(1f)] private float gaugePulseScale = 1.08f;
    private Color _timerBaseColor;
    private Vector3 _timerBaseScale;
    private bool _timerStyleCached;
    private bool _timerWarning;
    private float _timerTickElapsed = -1f;
    private RectTransform _gaugePulseTarget;
    private Vector3 _gaugeBaseScale;
    private bool _gaugeFull;
    private float _gaugePulsePhase;
    private Image _hitFlash;
    private Sprite _hitEdgeSprite;
    private Texture2D _hitEdgeTexture;
    private float _glitchRemaining;
    private float _blurRemaining;
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
        StopBlur();
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
    // 그 뒤로 3D 화면의 흐림이 남아 천천히 걷힌다.
    public void PlayPlayerHitFeedback()
    {
        EnsureHitFlash();
        _hitFlashRemaining = Mathf.Max(0.05f, hitFlashDuration);
        _hitFlash.color = UiPalette.WithAlpha(UiPalette.Alert, hitFlashOpacity);
        _glitchRemaining = Mathf.Max(0.05f, hitGlitchDuration);
        if (hitBlurStrength > 0f)
        {
            // 처음 맞으면 기본 길이, 흐림이 남은 채 또 맞으면 남은 시간의 절반이 더해진다(상한까지).
            float span = Mathf.Max(0.05f, hitBlurSeconds);
            _blurRemaining = Mathf.Min(Mathf.Max(span, hitBlurMaxSeconds), _blurRemaining * 0.5f + span);
        }
    }

    // 지금 화면에 걸려 있는 흐림 시간(초). 테스트와 조절 도구가 읽는다.
    public float HitBlurRemaining => _blurRemaining;

    public void UpdateFeedback(float deltaTime)
    {
        if (_blurRemaining > 0f)
        {
            _blurRemaining = Mathf.Max(0f, _blurRemaining - deltaTime);
            // 기본 길이보다 많이 남았으면(연달아 맞음) 최대 세기를 유지하고, 그 아래에서는 부드럽게 걷힌다.
            float left = UiMotion.Clamp01(_blurRemaining / Mathf.Max(0.05f, hitBlurSeconds));
            ScreenGlitchState.SetBlur(hitBlurStrength * left * left * (3f - 2f * left));
        }
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

    // 타이머 경고의 초 단위 튐과 가득 찬 게이지의 맥동. Battle에서만 호출되므로 Paused에서는 그대로 멈춘다.
    public void UpdateHud(float unscaledDeltaTime)
    {
        if (_timerTickElapsed >= 0f && timerValue != null)
        {
            _timerTickElapsed += unscaledDeltaTime;
            timerValue.rectTransform.localScale = _timerBaseScale * UiMotion.Pop(_timerTickElapsed, timerTickSeconds, timerTickScale);
            if (_timerTickElapsed >= timerTickSeconds) _timerTickElapsed = -1f;
        }
        if (_gaugeFull && _gaugePulseTarget != null && gaugePulseHz > 0f && gaugePulseScale > 1f)
        {
            _gaugePulsePhase += unscaledDeltaTime * gaugePulseHz;
            if (_gaugePulsePhase >= 1f) _gaugePulsePhase -= Mathf.Floor(_gaugePulsePhase);
            _gaugePulseTarget.localScale = _gaugeBaseScale * (1f + (gaugePulseScale - 1f) * UiMotion.Pulse01(_gaugePulsePhase));
        }
    }

    private void StopGlitch()
    {
        _glitchRemaining = 0f;
        ScreenGlitchState.ClearGlitch();
        if (_rect != null) _rect.anchoredPosition = _restPosition;
    }

    private void StopBlur()
    {
        _blurRemaining = 0f;
        ScreenGlitchState.SetBlur(0f);
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
        StopBlur();
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
        if (full != _gaugeFull)
        {
            // 가득 차면 UpdateHud가 맥동을 시작하고, 쓰거나 초기화되면 원래 크기로 돌아간다.
            _gaugeFull = full;
            _gaugePulsePhase = 0f;
            if (_gaugePulseTarget != null) _gaugePulseTarget.localScale = _gaugeBaseScale;
        }
    }

    // 맥동할 대상을 한 번만 정한다(코드로 만든 게이지는 틀 전체, 프리팹에 놓인 게이지는 채움 이미지).
    private void BindGaugePulseTarget(RectTransform target)
    {
        if (_gaugePulseTarget != null || target == null) return;
        _gaugePulseTarget = target;
        _gaugeBaseScale = target.localScale;
    }

    // 통신 허브와 같은 육각 윤곽 게이지. 채워지지 않은 부분과 퍼센트를 함께 읽을 수 있게 한다.
    private void EnsureSpecialGauge()
    {
        if (specialGaugeFill != null)
        {
            BindGaugePulseTarget(specialGaugeFill.rectTransform);
            return;
        }
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
        // 화면 아래에서 35px 띄운 170px 틀. 맥동이 가운데를 기준으로 커지도록 기준점을 중앙에 둔다.
        frameRect.anchorMin = frameRect.anchorMax = new Vector2(0.5f, 0f);
        frameRect.pivot = new Vector2(0.5f, 0.5f);
        frameRect.anchoredPosition = new Vector2(0f, 35f + 85f);
        frameRect.sizeDelta = new Vector2(170f, 170f);
        BindGaugePulseTarget(frameRect);
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
        ApplyTimerWarning(timerWarningSeconds > 0f && second <= timerWarningSeconds);
    }

    // 표시되는 초가 바뀔 때만 불린다. 경고 구간에서는 색을 바꾸고 초마다 한 번 튀게 한다.
    private void ApplyTimerWarning(bool warning)
    {
        if (timerValue == null) return;
        if (!_timerStyleCached)
        {
            _timerBaseColor = timerValue.color;
            _timerBaseScale = timerValue.rectTransform.localScale;
            _timerStyleCached = true;
        }
        if (warning != _timerWarning)
        {
            _timerWarning = warning;
            timerValue.color = warning ? UiPalette.Alert : _timerBaseColor;
        }
        if (warning && timerTickSeconds > 0f && timerTickScale > 1f)
            _timerTickElapsed = 0f;
        else
        {
            _timerTickElapsed = -1f;
            timerValue.rectTransform.localScale = _timerBaseScale;
        }
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
