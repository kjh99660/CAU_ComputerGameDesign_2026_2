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
    public override void ResetView() => Render(new BattleUiModel(0, 100f, 0f));

    public void PlayScoreFeedback(int delta)
    {
        //TOOD : Score 증감량 Pool과 Scale/Color 연출을 연결한다.
    }

    public void RenderScore(int score)
    {
        _lastScore = score;
        SetText(scoreValue, score.ToString("D6"));
    }

    public void RenderGauge(float value)
    {
        if (specialGaugeFill != null) specialGaugeFill.fillAmount = Mathf.Clamp01(value);
    }

    // Provisional display until the final gauge artwork is wired in the prefab.
    private void EnsureSpecialGauge()
    {
        if (specialGaugeFill != null) return;
        Sprite frameSprite = Resources.Load<Sprite>("CombatGaugeFrame");
        Sprite fillSprite = Resources.Load<Sprite>("CombatGaugeFill");
        var frameObject = new GameObject("SpecialGaugeFrame", typeof(RectTransform), typeof(Image));
        var frameRect = (RectTransform)frameObject.transform;
        frameRect.SetParent(transform, false);
        frameRect.anchorMin = frameRect.anchorMax = new Vector2(0.5f, 0f);
        frameRect.pivot = new Vector2(0.5f, 0f);
        frameRect.anchoredPosition = new Vector2(0f, 35f);
        frameRect.sizeDelta = new Vector2(88f, 88f);
        Image frame = frameObject.GetComponent<Image>();
        frame.sprite = frameSprite;
        frame.color = Color.white;
        frame.raycastTarget = false;
        var fillObject = new GameObject("SpecialGaugeFill", typeof(RectTransform), typeof(Image));
        var fillRect = (RectTransform)fillObject.transform;
        fillRect.SetParent(frameRect, false);
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(7f, 7f);
        fillRect.offsetMax = new Vector2(-7f, -7f);
        specialGaugeFill = fillObject.GetComponent<Image>();
        specialGaugeFill.sprite = fillSprite;
        specialGaugeFill.type = Image.Type.Filled;
        specialGaugeFill.fillMethod = Image.FillMethod.Radial360;
        specialGaugeFill.color = Color.white;
        specialGaugeFill.raycastTarget = false;
        specialGaugeFill.fillAmount = 0f;
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
