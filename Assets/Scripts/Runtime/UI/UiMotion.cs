using System;

// UI 연출에 쓰는 순수 계산식. Unity에 의존하지 않아 Edit Mode에서 검증한다.
// 시간은 모두 초 단위이고(View가 unscaled time으로 넘긴다), 범위를 벗어난 입력은 끝값으로 고정한다.
public static class UiMotion
{
    public static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;

    public static float Lerp(float from, float to, float t) => from + (to - from) * Clamp01(t);

    // 빠르게 시작해 부드럽게 멈춘다(0→1).
    public static float EaseOutCubic(float t)
    {
        float inverse = 1f - Clamp01(t);
        return 1f - inverse * inverse * inverse;
    }

    // 튀어나오기: 시작할 때 fromScale, seconds 뒤에는 1.
    public static float Pop(float elapsed, float seconds, float fromScale)
    {
        if (seconds <= 0f) return 1f;
        return fromScale + (1f - fromScale) * EaseOutCubic(elapsed / seconds);
    }

    // 사라지기: 시작할 때 1, seconds 뒤에는 0. 처음에 빨리 줄어든다.
    public static float Decay(float elapsed, float seconds)
    {
        if (seconds <= 0f) return 0f;
        float remaining = 1f - Clamp01(elapsed / seconds);
        return remaining * remaining;
    }

    // 구간의 마지막 tailSeconds 동안 0→1로 진행한다(그 전에는 0).
    public static float Tail(float elapsed, float totalSeconds, float tailSeconds)
    {
        if (tailSeconds <= 0f) return elapsed >= totalSeconds ? 1f : 0f;
        return Clamp01((elapsed - (totalSeconds - tailSeconds)) / tailSeconds);
    }

    // 맥동: phase(주기 수)가 정수일 때 0, 반 주기에서 1.
    public static float Pulse01(float phase) => 0.5f - 0.5f * (float)Math.Cos(phase * 2.0 * Math.PI);
}
