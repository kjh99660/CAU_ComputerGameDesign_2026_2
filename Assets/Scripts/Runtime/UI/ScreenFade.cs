// 화면 전환 페이드(TransitionLayer)의 순수 규칙과 계산. Unity 시간에 의존하지 않아 Edit Mode에서 검증한다.
// 관찰한 상태에 맞춰 불투명도만 정한다. 요청을 막거나 상태 전환을 늦추지 않는다.
//  - Initializing 동안과 Reset이 진행되는 동안: 검게 덮어 둔다.
//  - Ready에 들어오면(초기화 뒤, 매 Reset 뒤): 검은 화면에서 readySeconds 동안 걷힌다.
//  - Result에 들어오면(OutroDialogue 뒤): 검은 화면에서 resultSeconds 동안 짧게 걷힌다.
//  - 그 밖의 상태 변경은 진행 중인 페이드를 바로 끝낸다(투명). 끊긴 페이드가 남지 않는다.
public sealed class ScreenFade
{
    private bool _observed;
    private GameState _state;
    private int _tick;
    private bool _covering;
    private float _elapsed;
    private float _duration;

    public float Opacity { get; private set; }
    public bool IsCovering => _covering;
    public bool IsFading => !_covering && Opacity > 0f;

    // 불투명도가 threshold 이상인 동안만 포인터 입력을 막는다. 투명해지면 절대 막지 않는다.
    public bool BlocksInput(float threshold) => Opacity > 0f && Opacity >= threshold;

    // 현재 상태를 관찰해 덮기/걷기/끝내기를 정한다. 같은 상태가 이어지면 아무것도 바꾸지 않는다.
    public void Observe(GameState state, int tick, bool resetting, float readySeconds, float resultSeconds)
    {
        bool changed = !_observed || state != _state || tick != _tick;
        bool wasCovering = _covering;
        _observed = true;
        _state = state;
        _tick = tick;

        if (resetting || state == GameState.Initializing)
        {
            Cover();
            return;
        }
        if (!changed && !wasCovering)
            return;

        if (state == GameState.Ready)
            FadeFromBlack(readySeconds);
        else if (state == GameState.Result && changed)
            FadeFromBlack(resultSeconds);
        else
            Clear();
    }

    // 실제 시간(unscaled)으로 걷는다. 덮고 있는 동안은 그대로 둔다.
    public void Advance(float unscaledDeltaTime)
    {
        if (_covering || Opacity <= 0f)
            return;
        if (unscaledDeltaTime > 0f)
            _elapsed += unscaledDeltaTime;
        // 처음에 빨리 밝아지고 끝에서 부드럽게 사라진다.
        Opacity = _elapsed >= _duration ? 0f : UiMotion.Decay(_elapsed, _duration);
    }

    private void Clear()
    {
        _covering = false;
        _elapsed = _duration = 0f;
        Opacity = 0f;
    }

    private void Cover()
    {
        _covering = true;
        _elapsed = _duration = 0f;
        Opacity = 1f;
    }

    private void FadeFromBlack(float seconds)
    {
        _covering = false;
        _elapsed = 0f;
        _duration = seconds;
        Opacity = seconds > 0f ? 1f : 0f;
    }
}
