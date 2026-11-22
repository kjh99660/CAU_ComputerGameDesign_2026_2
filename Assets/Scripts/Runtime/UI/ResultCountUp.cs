using System;

// 결과 화면에서 숫자가 0부터 최종값까지 올라가는 진행 규칙. Unity에 의존하지 않아 Edit Mode에서 검증한다.
// 최종 점수가 먼저 오르고, 통계 네 칸은 조금 늦게 왼쪽부터 차례로 따라 오른다. 시간은 unscaled time으로 받는다.
public sealed class ResultCountUp
{
    public const int Score = 0, Kills = 1, MaxCombo = 2, Hits = 3, Specials = 4;
    public const int FieldCount = 5;

    private readonly int[] _targets = new int[FieldCount];
    private readonly int[] _shown = new int[FieldCount];
    private float _elapsed;
    private float _scoreSeconds, _statSeconds, _statDelaySeconds, _statStaggerSeconds;

    // 모든 칸이 최종값을 보여 주고 있으면 true. Begin 전에는 목표값을 바로 보여 준다.
    public bool IsComplete { get; private set; } = true;

    public int Shown(int field) => _shown[field];

    public void Begin(ResultUiModel model, float scoreSeconds, float statSeconds, float statDelaySeconds, float statStaggerSeconds)
    {
        _scoreSeconds = Math.Max(0f, scoreSeconds);
        _statSeconds = Math.Max(0f, statSeconds);
        _statDelaySeconds = Math.Max(0f, statDelaySeconds);
        _statStaggerSeconds = Math.Max(0f, statStaggerSeconds);
        _elapsed = 0f;
        SetTargets(model);
        Evaluate();
    }

    // 올라가는 도중에 최종값이 바뀌면 지난 시간은 그대로 두고 목표만 바꾼다.
    public void Retarget(ResultUiModel model)
    {
        SetTargets(model);
        Evaluate();
    }

    // 보이는 값이 하나라도 바뀌었으면 true.
    public bool Tick(float unscaledDeltaTime)
    {
        if (IsComplete) return false;
        _elapsed += Math.Max(0f, unscaledDeltaTime);
        return Evaluate();
    }

    public void Complete()
    {
        _elapsed = float.MaxValue;
        Evaluate();
    }

    private void SetTargets(ResultUiModel model)
    {
        _targets[Score] = model.FinalScore;
        _targets[Kills] = model.KillCount;
        _targets[MaxCombo] = model.MaxCombo;
        _targets[Hits] = model.HitCount;
        _targets[Specials] = model.SpecialUseCount;
    }

    private bool Evaluate()
    {
        bool changed = false;
        bool complete = true;
        for (int field = 0; field < FieldCount; field++)
        {
            float start = field == Score ? 0f : _statDelaySeconds + (field - 1) * _statStaggerSeconds;
            float duration = field == Score ? _scoreSeconds : _statSeconds;
            float t = duration <= 0f ? (_elapsed >= start ? 1f : 0f) : UiMotion.Clamp01((_elapsed - start) / duration);
            int value = t >= 1f ? _targets[field] : (int)Math.Round(_targets[field] * (double)UiMotion.EaseOutCubic(t));
            if (t < 1f) complete = false;
            if (value == _shown[field]) continue;
            _shown[field] = value;
            changed = true;
        }
        IsComplete = complete;
        return changed;
    }
}
