using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 특수 게이지 옆의 작은 키 표시(Q 궁극기, E 스킬). Presenter가 정한 상태만 그린다.
//  Dim   : 지금은 쓸 수 없음(게이지가 덜 참, 재사용 대기 중). 흐리게.
//  Ready : 쓸 수 있음. 보통 밝기.
//  Lit   : 지금 눌러야 할 때(게이지 가득). 강조색으로 켜고 맥동한다(unscaled time, Pause에서는 멈춘다).
// 재사용 대기 표시(SetCooldown)는 나중을 위한 자리다. 지금 스킬에는 대기 시간이 없다.
// Deny: 쓸 수 없는데 눌렀을 때 잠깐 좌우로 흔들린다(게이지가 모자란 스킬 키).
public sealed class HudKeyBadge : MonoBehaviour
{
    public enum State { Dim, Ready, Lit }

    [SerializeField] private TMP_Text keyText;
    [SerializeField] private TMP_Text labelText;
    [SerializeField] private Graphic edge;
    [SerializeField] private Graphic baseLine;
    [SerializeField] private RectTransform cooldownFill;
    [SerializeField] private RectTransform pulseTarget;
    [SerializeField] private CanvasGroup group;
    [Header("Key badge - provisional")]
    [SerializeField, Range(0f, 1f)] private float dimAlpha = 0.5f;
    [SerializeField, Min(0f)] private float pulseHz = 1.4f;
    [SerializeField, Min(1f)] private float pulseScale = 1.12f;
    // 쓸 수 없는데 눌렀을 때의 좌우 흔들림(Deny). 시간이나 폭이 0이면 쓰지 않는다.
    [SerializeField, Min(0f)] private float denySeconds = 0.25f;
    [SerializeField, Min(0f)] private float denyPixels = 7f;

    private float _denyElapsed = -1f;
    private Vector2 _restPosition;
    private State _state = State.Ready;
    private bool _applied;
    private float _pulsePhase;
    private float _cooldown;
    private Vector3 _baseScale = Vector3.one;
    private bool _baseCaptured;

    public State Current => _state;
    public float Alpha => group != null ? group.alpha : 1f;
    public float PulseScale => pulseTarget != null ? pulseTarget.localScale.x / Mathf.Max(0.0001f, _baseScale.x) : 1f;
    public float Cooldown => _cooldown;
    public Color KeyColor => keyText != null ? keyText.color : Color.clear;
    public bool IsDenying => _denyElapsed >= 0f;

    public void AutoBind()
    {
        if (keyText == null) keyText = Find<TMP_Text>("Key");
        if (labelText == null) labelText = Find<TMP_Text>("KeyLabel");
        if (edge == null) edge = Find<Graphic>("KeyEdge");
        if (baseLine == null) baseLine = Find<Graphic>("KeyBaseLine");
        if (cooldownFill == null) cooldownFill = Find<RectTransform>("CooldownFill");
        if (pulseTarget == null) pulseTarget = transform as RectTransform;
        if (group == null) group = GetComponent<CanvasGroup>();
        if (!_baseCaptured && pulseTarget != null)
        {
            _baseScale = pulseTarget.localScale;
            _baseCaptured = true;
        }
    }

    public void SetState(State state)
    {
        if (_applied && state == _state) return;
        AutoBind();
        _applied = true;
        _state = state;
        _pulsePhase = 0f;
        if (pulseTarget != null) pulseTarget.localScale = _baseScale;
        Color tone = state == State.Lit ? UiPalette.Accent : state == State.Ready ? UiPalette.Ink : UiPalette.Muted;
        if (keyText != null) keyText.color = tone;
        if (labelText != null) labelText.color = state == State.Lit ? UiPalette.Accent : UiPalette.Muted;
        if (edge != null) edge.color = state == State.Lit ? UiPalette.Accent : UiPalette.Muted;
        if (baseLine != null) baseLine.color = UiPalette.WithAlpha(state == State.Lit ? UiPalette.Accent : UiPalette.Muted, 0.55f);
        if (group != null) group.alpha = state == State.Dim ? dimAlpha : 1f;
    }

    // 재사용 대기(0 = 준비됨, 1 = 막 사용함). 대기 중에는 아래에서부터 어두운 막이 덮고 흐리게 보인다.
    public void SetCooldown(float remainingNormalized)
    {
        AutoBind();
        _cooldown = UiMotion.Clamp01(remainingNormalized);
        if (cooldownFill != null)
        {
            bool show = _cooldown > 0f;
            if (cooldownFill.gameObject.activeSelf != show) cooldownFill.gameObject.SetActive(show);
            Vector3 scale = cooldownFill.localScale;
            scale.y = _cooldown;
            cooldownFill.localScale = scale;
        }
        SetState(_cooldown > 0f ? State.Dim : State.Ready);
    }

    // 지금은 쓸 수 없는데 키를 눌렀다: 제자리에서 잠깐 좌우로 흔들린다(Animate가 unscaled time으로 진행한다).
    public void Deny()
    {
        if (denySeconds <= 0f || denyPixels <= 0f || !(transform is RectTransform rect)) return;
        if (_denyElapsed < 0f) _restPosition = rect.anchoredPosition;
        _denyElapsed = 0f;
    }

    // 흔들리던 중이면 제자리로 돌려놓는다(새 판, 화면 초기화).
    public void ClearDeny()
    {
        if (_denyElapsed < 0f) return;
        _denyElapsed = -1f;
        if (transform is RectTransform rect) rect.anchoredPosition = _restPosition;
    }

    public void Animate(float unscaledDeltaTime)
    {
        if (_denyElapsed >= 0f && transform is RectTransform shaken)
        {
            _denyElapsed += unscaledDeltaTime;
            float progress = _denyElapsed / denySeconds;
            if (progress >= 1f)
                ClearDeny();
            else
                shaken.anchoredPosition = _restPosition +
                    new Vector2(Mathf.Sin(progress * Mathf.PI * 6f) * denyPixels * (1f - progress), 0f);
        }
        if (_state != State.Lit || pulseTarget == null || pulseHz <= 0f || pulseScale <= 1f) return;
        _pulsePhase += unscaledDeltaTime * pulseHz;
        if (_pulsePhase >= 1f) _pulsePhase -= Mathf.Floor(_pulsePhase);
        pulseTarget.localScale = _baseScale * (1f + (pulseScale - 1f) * UiMotion.Pulse01(_pulsePhase));
    }

    private T Find<T>(string objectName) where T : Component
    {
        foreach (T candidate in GetComponentsInChildren<T>(true))
            if (candidate.name == objectName) return candidate;
        return null;
    }
}
