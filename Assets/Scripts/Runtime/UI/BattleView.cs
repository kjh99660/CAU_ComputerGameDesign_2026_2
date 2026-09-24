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
        if (pauseButton != null) pauseButton.onClick.AddListener(HandlePauseClicked);
    }

    private void OnDisable()
    {
        if (pauseButton != null) pauseButton.onClick.RemoveListener(HandlePauseClicked);
    }

    public void Render(BattleUiModel model)
    {
        SetText(scoreValue, model.Score.ToString("D6"));
        SetText(timerValue, FormatTime(model.RemainingSeconds));
        if (specialGaugeFill != null) specialGaugeFill.fillAmount = model.GaugeNormalized;
    }

    public void SetPauseInteractable(bool interactable) => SetButtonInteractable(pauseButton, interactable);

    public override void ResetView() => Render(new BattleUiModel(0, 100f, 0f));

    public void PlayScoreFeedback(int delta)
    {
        //TOOD : Score 증감량 Pool과 Scale/Color 연출을 연결한다.
    }

    private static string FormatTime(float seconds)
    {
        int value = Mathf.Max(0, Mathf.CeilToInt(seconds));
        return $"{value / 60:00}:{value % 60:00}";
    }

    private void HandlePauseClicked() => PauseClicked?.Invoke();
}
