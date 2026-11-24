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
    // 실행을 넘어 남는 최고 점수(BEST)와, 이번 판이 그것을 넘었을 때의 NEW BEST 표시.
    [Header("Best score - provisional")]
    [SerializeField] private TMP_Text bestScoreLabel;
    [SerializeField] private TMP_Text bestScoreValue;
    [SerializeField] private RectTransform newBestMark;
    [SerializeField, Min(0f)] private float newBestPopSeconds = 0.3f;
    [SerializeField, Min(1f)] private float newBestPopScale = 1.25f;
    [SerializeField, Min(0f)] private float newBestPulseHz = 1.1f;
    [SerializeField, Min(1f)] private float newBestPulseScale = 1.06f;

    private int _shownBest = -1;
    private float _newBestElapsed = -1f;
    private Vector3 _newBestBaseScale = Vector3.one;
    private bool _newBestScaleCached;

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
        if (bestScoreLabel == null) bestScoreLabel = FindNamed<TMP_Text>("BestScoreLabel");
        if (bestScoreValue == null) bestScoreValue = FindNamed<TMP_Text>("BestScoreValue");
        if (newBestMark == null) newBestMark = FindNamed<RectTransform>("NewBestMark");
    }

    public bool NewBestShown => newBestMark != null && newBestMark.gameObject.activeSelf;

    // 최고 점수 줄. newBest이면 NEW BEST 표시가 튀어나온 뒤 천천히 맥동한다(AnimateBest, unscaled time).
    public void RenderBest(int best, bool newBest)
    {
        if (bestScoreLabel != null) bestScoreLabel.color = UiPalette.Muted;
        if (bestScoreValue != null)
        {
            bestScoreValue.color = newBest ? UiPalette.Accent : UiPalette.Ink;
            if (best != _shownBest)
            {
                _shownBest = best;
                bestScoreValue.SetText("{0:000000}", best);
            }
        }
        if (newBestMark == null) return;
        if (!_newBestScaleCached)
        {
            _newBestBaseScale = newBestMark.localScale;
            _newBestScaleCached = true;
        }
        if (newBestMark.gameObject.activeSelf != newBest) newBestMark.gameObject.SetActive(newBest);
        _newBestElapsed = newBest ? 0f : -1f;
        newBestMark.localScale = newBest ? _newBestBaseScale * newBestPopScale : _newBestBaseScale;
    }

    public void AnimateBest(float unscaledDeltaTime)
    {
        if (_newBestElapsed < 0f || newBestMark == null) return;
        _newBestElapsed += unscaledDeltaTime;
        float scale;
        if (_newBestElapsed < newBestPopSeconds)
            scale = UiMotion.Pop(_newBestElapsed, newBestPopSeconds, newBestPopScale);
        else if (newBestPulseHz > 0f && newBestPulseScale > 1f)
        {
            float phase = (_newBestElapsed - newBestPopSeconds) * newBestPulseHz;
            scale = 1f + (newBestPulseScale - 1f) * UiMotion.Pulse01(phase - Mathf.Floor(phase));
        }
        else
            scale = 1f;
        newBestMark.localScale = _newBestBaseScale * scale;
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
        if (newBestMark != null)
        {
            if (_newBestScaleCached) newBestMark.localScale = _newBestBaseScale;
            if (newBestMark.gameObject.activeSelf) newBestMark.gameObject.SetActive(false);
        }
        _newBestElapsed = -1f;
    }

    private void HandleRetryClicked() => RetryClicked?.Invoke();
}
