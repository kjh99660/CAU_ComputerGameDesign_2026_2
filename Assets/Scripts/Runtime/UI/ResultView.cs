using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ResultView : UIScreenView
{
    [SerializeField] private Button retryButton;
    [SerializeField] private TMP_Text finalScoreValue;
    [SerializeField] private TMP_Text killsValue;
    [SerializeField] private TMP_Text maxComboValue;
    [SerializeField] private TMP_Text hitsTakenValue;
    [SerializeField] private TMP_Text specialUsesValue;
    public event Action RetryClicked;

    public override void AutoBind()
    {
        base.AutoBind();
        if (retryButton == null) retryButton = FindNamed<Button>("RetryButton");
        if (finalScoreValue == null) finalScoreValue = FindNamed<TMP_Text>("FinalScoreValue");
        if (killsValue == null) killsValue = FindNamed<TMP_Text>("KillsValue");
        if (maxComboValue == null) maxComboValue = FindNamed<TMP_Text>("MaxComboValue");
        if (hitsTakenValue == null) hitsTakenValue = FindNamed<TMP_Text>("HitsTakenValue");
        if (specialUsesValue == null) specialUsesValue = FindNamed<TMP_Text>("SpecialUsesValue");
    }

    private void OnEnable()
    {
        AutoBind();
        if (retryButton != null) retryButton.onClick.AddListener(HandleRetryClicked);
    }

    private void OnDisable()
    {
        if (retryButton != null) retryButton.onClick.RemoveListener(HandleRetryClicked);
    }

    public void Render(ResultUiModel model)
    {
        SetText(finalScoreValue, model.FinalScore.ToString("D6"));
        SetText(killsValue, model.KillCount.ToString("D3"));
        SetText(maxComboValue, model.MaxCombo.ToString("D2"));
        SetText(hitsTakenValue, model.HitCount.ToString("D2"));
        SetText(specialUsesValue, model.SpecialUseCount.ToString("D2"));
    }

    public override void ResetView() => Render(new ResultUiModel(0, 0, 0, 0, 0));
    private void HandleRetryClicked() => RetryClicked?.Invoke();
}
