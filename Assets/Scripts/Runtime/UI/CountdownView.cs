using TMPro;
using UnityEngine;

public sealed class CountdownView : UIScreenView
{
    [SerializeField] private TMP_Text numberText;
    [SerializeField] private TMP_Text readyText;
    [SerializeField, Min(0.05f)] private float stepDuration = 0.8f;
    [SerializeField, Min(0.05f)] private float startDuration = 0.55f;

    public float StepDuration => stepDuration;
    public float StartDuration => startDuration;

    public override void AutoBind()
    {
        base.AutoBind();
        if (numberText == null) numberText = FindNamed<TMP_Text>("Number");
        if (readyText == null) readyText = FindNamed<TMP_Text>("ReadyText");
    }

    public void ShowNumber(int number)
    {
        SetText(numberText, number.ToString());
        SetText(readyText, "READY");
        PlayNumberAnimation(number);
    }

    public void ShowStart()
    {
        SetText(numberText, string.Empty);
        SetText(readyText, "START");
        PlayStartAnimation();
    }

    public override void ResetView()
    {
        SetText(numberText, "3");
        SetText(readyText, "READY");
    }

    private void PlayNumberAnimation(int number)
    {
        //TOOD : 숫자 Scale/Fade 연출을 unscaled time으로 재생한다.
    }

    private void PlayStartAnimation()
    {
        //TOOD : START Flash 연출을 unscaled time으로 재생한다.
    }
}
