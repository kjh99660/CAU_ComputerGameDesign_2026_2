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
    // 숫자 올리기와 Retry 강조. 모두 임시값이며 unscaled time으로 재생한다. 시간을 0으로 두면 바로 최종값을 보여 준다.
    [Header("Count-up - provisional")]
    [SerializeField, Min(0f)] private float scoreCountSeconds = 0.8f;
    [SerializeField, Min(0f)] private float statCountSeconds = 0.45f;
    [SerializeField, Min(0f)] private float statDelaySeconds = 0.4f;
    [SerializeField, Min(0f)] private float statStaggerSeconds = 0.12f;
    // 숫자가 다 오른 뒤 Retry 버튼의 맥동. 속도가 0이거나 크기가 1이면 쓰지 않는다.
    [SerializeField, Min(0f)] private float retryPulseHz = 1.2f;
    [SerializeField, Min(1f)] private float retryPulseScale = 1.04f;

    private readonly int[] _shown = { -1, -1, -1, -1, -1 };
    private Vector3 _retryBaseScale = Vector3.one;
    private bool _retryScaleCached;
    private bool _retryPulsing;
    private float _retryPulsePhase;
    public event Action RetryClicked;

    public float ScoreCountSeconds => scoreCountSeconds;
    public float StatCountSeconds => statCountSeconds;
    public float StatDelaySeconds => statDelaySeconds;
    public float StatStaggerSeconds => statStaggerSeconds;

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
        RenderValue(ResultCountUp.Score, model.FinalScore);
        RenderValue(ResultCountUp.Kills, model.KillCount);
        RenderValue(ResultCountUp.MaxCombo, model.MaxCombo);
        RenderValue(ResultCountUp.Hits, model.HitCount);
        RenderValue(ResultCountUp.Specials, model.SpecialUseCount);
    }

    // 값이 바뀐 칸만 글자를 다시 만든다(숫자가 오르는 동안 프레임마다 불린다).
    public void RenderValue(int field, int value)
    {
        if (_shown[field] == value) return;
        _shown[field] = value;
        switch (field)
        {
            case ResultCountUp.Score: SetText(finalScoreValue, value.ToString("D6")); break;
            case ResultCountUp.Kills: SetText(killsValue, value.ToString("D3")); break;
            case ResultCountUp.MaxCombo: SetText(maxComboValue, value.ToString("D2")); break;
            case ResultCountUp.Hits: SetText(hitsTakenValue, value.ToString("D2")); break;
            case ResultCountUp.Specials: SetText(specialUsesValue, value.ToString("D2")); break;
        }
    }

    // 숫자가 다 오른 뒤(active)에만 Retry 버튼이 천천히 맥동한다. 버튼은 그 전에도 눌린다.
    public void AnimateRetry(float unscaledDeltaTime, bool active)
    {
        if (retryButton == null) return;
        if (!_retryScaleCached)
        {
            _retryBaseScale = retryButton.transform.localScale;
            _retryScaleCached = true;
        }
        if (!active || retryPulseHz <= 0f || retryPulseScale <= 1f)
        {
            if (_retryPulsing)
            {
                _retryPulsing = false;
                _retryPulsePhase = 0f;
                retryButton.transform.localScale = _retryBaseScale;
            }
            return;
        }
        _retryPulsing = true;
        _retryPulsePhase += unscaledDeltaTime * retryPulseHz;
        if (_retryPulsePhase >= 1f) _retryPulsePhase -= Mathf.Floor(_retryPulsePhase);
        retryButton.transform.localScale = _retryBaseScale * (1f + (retryPulseScale - 1f) * UiMotion.Pulse01(_retryPulsePhase));
    }

    public override void ResetView()
    {
        Render(new ResultUiModel(0, 0, 0, 0, 0));
        AnimateRetry(0f, false);
    }

    private void HandleRetryClicked() => RetryClicked?.Invoke();
}
