using TMPro;
using UnityEngine;

public sealed class FinishingView : UIScreenView
{
    // Finishing에서는 Timer가 0에 고정된다. 전투 HUD의 시간 표시와 같은 형식(mm:ss)으로 남긴다.
    private const string TimerAtZero = "00:00";

    [SerializeField] private TMP_Text scoreValue;
    [SerializeField] private TMP_Text timerValue;
    // 전투 HUD와 같은 자리의 콤보 표시. 100초 전에 시작한 공격이 종료 연출 중에 이어 가는 콤보를 보여 준다.
    [SerializeField] private HudComboCounter comboCounter;
    [SerializeField, Min(0.1f)] private float presentationDuration = 2.5f;
    private bool _bound;
    public float PresentationDuration => presentationDuration;

    public override void AutoBind()
    {
        base.AutoBind();
        if (scoreValue == null) scoreValue = FindNamed<TMP_Text>("ScoreValue");
        if (timerValue == null) timerValue = FindNamed<TMP_Text>("TimerValue");
        if (comboCounter == null) comboCounter = FindNamed<HudComboCounter>("ComboCounter");
    }

    public void Render(FinishingUiModel model)
    {
        SetText(scoreValue, model.Score.ToString("D6"));
        SetText(timerValue, TimerAtZero);
    }

    // 종료 화면이 보이기 전(전투 중)에 받은 값은 연출 없이 맞춰 두어, 화면이 켜질 때 콤보가 다시 튀지 않는다.
    public void RenderCombo(in ComboUiModel model)
    {
        if (comboCounter == null && !_bound)
        {
            _bound = true;
            AutoBind();
        }
        if (comboCounter != null) comboCounter.Render(model, isActiveAndEnabled);
    }

    public void AnimateCombo(float unscaledDeltaTime)
    {
        if (comboCounter != null) comboCounter.Animate(unscaledDeltaTime);
    }

    public override void ResetView()
    {
        Render(new FinishingUiModel(0));
        if (comboCounter != null) comboCounter.Clear();
    }

    public void PlayTimeUpAnimation()
    {
        //TOOD : TIME UP과 Slow Motion Badge 연출을 unscaled time으로 연결한다.
    }
}
