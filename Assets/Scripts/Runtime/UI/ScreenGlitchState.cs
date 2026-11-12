using UnityEngine;

// 피격 화면 글리치의 현재 세기. BattleView가 상태 정책(Battle에서만 진행, Paused에서 정지)에 맞춰 갱신하고,
// 렌더러 기능(RebootHitGlitchFeature)은 읽기만 한다. 값이 0이면 렌더 패스를 추가하지 않는다.
public static class ScreenGlitchState
{
    public static float Amount { get; private set; }
    public static float Seed { get; private set; }

    public static void Set(float amount, float seed)
    {
        Amount = Mathf.Clamp01(amount);
        Seed = seed;
    }

    public static void Clear()
    {
        Amount = 0f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnLoad() => Clear();
}
