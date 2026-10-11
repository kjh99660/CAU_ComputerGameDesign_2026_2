using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class BattleView : UIScreenView
{
    [SerializeField] private Button pauseButton;
    [SerializeField] private TMP_Text scoreValue;
    [SerializeField] private TMP_Text timerValue;

    public event Action PauseClicked;
    private float _lastRemainingSeconds = 0f;
    private int _lastScore = 0;
    public override void AutoBind()
    {
        base.AutoBind();
        if (pauseButton == null) pauseButton = FindNamed<Button>("PauseButton");
        if (scoreValue == null) scoreValue = FindNamed<TMP_Text>("ScoreValue");
        if (timerValue == null) timerValue = FindNamed<TMP_Text>("TimerValue");
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
        RenderScore(model.Score);
        RenderTimer(model.RemainingSeconds);
    }

    public void SetPauseInteractable(bool interactable) => SetButtonInteractable(pauseButton, interactable);

    //TODO : 메니저 클래스에서 가져오기
    public override void ResetView() => Render(new BattleUiModel(0, 10f, 0f));

    public void PlayScoreFeedback(int delta)
    {
        //TOOD : Score 증감량 Pool과 Scale/Color 연출을 연결한다.
    }

    public void RenderScore(int score)
    {
        _lastScore = score;
        SetText(scoreValue, score.ToString("D6"));
    }

    public void RenderTimer(float remainingSeconds)
    {
        _lastRemainingSeconds = remainingSeconds;
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
