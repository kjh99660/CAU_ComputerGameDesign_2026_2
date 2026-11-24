using TMPro;
using UnityEngine;

public sealed class FinishingView : UIScreenView
{
    // Finishing에서는 Timer가 0에 고정된다. 전투 HUD의 시간 표시와 같은 형식(mm:ss)으로 남긴다.
    private const string TimerAtZero = "00:00";

    [SerializeField] private TMP_Text scoreValue;
    [SerializeField] private TMP_Text timerValue;
    [SerializeField, Min(0.1f)] private float presentationDuration = 2.5f;
    public float PresentationDuration => presentationDuration;

    public override void AutoBind()
    {
        base.AutoBind();
        if (scoreValue == null) scoreValue = FindNamed<TMP_Text>("ScoreValue");
        if (timerValue == null) timerValue = FindNamed<TMP_Text>("TimerValue");
    }

    public void Render(FinishingUiModel model)
    {
        SetText(scoreValue, model.Score.ToString("D6"));
        SetText(timerValue, TimerAtZero);
    }

    public override void ResetView() => Render(new FinishingUiModel(0));

    public void PlayTimeUpAnimation()
    {
        //TOOD : TIME UP과 Slow Motion Badge 연출을 unscaled time으로 연결한다.
    }
}
