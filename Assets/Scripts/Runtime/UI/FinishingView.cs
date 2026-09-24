using TMPro;
using UnityEngine;

public sealed class FinishingView : UIScreenView
{
    [SerializeField] private TMP_Text scoreValue;
    [SerializeField, Min(0.1f)] private float presentationDuration = 2.5f;
    public float PresentationDuration => presentationDuration;

    public override void AutoBind()
    {
        base.AutoBind();
        if (scoreValue == null) scoreValue = FindNamed<TMP_Text>("ScoreValue");
    }

    public void Render(FinishingUiModel model) => SetText(scoreValue, model.Score.ToString("D6"));
    public override void ResetView() => Render(new FinishingUiModel(0));

    public void PlayTimeUpAnimation()
    {
        //TOOD : TIME UP과 Slow Motion Badge 연출을 unscaled time으로 연결한다.
    }
}
