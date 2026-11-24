using UnityEngine;

// 피격 화면 연출의 현재 세기: 짧은 블록 깨짐 글리치와, 그 뒤에 남아 천천히 걷히는 흐림.
// BattleView가 상태 정책(Battle에서만 진행, Paused에서 정지)에 맞춰 갱신하고, 렌더러 기능(RebootHitGlitchFeature)은
// 읽기만 한다. 두 값이 모두 0이면 렌더 패스를 추가하지 않는다.
public static class ScreenGlitchState
{
    public static float Amount { get; private set; }
    public static float Seed { get; private set; }
    // 피격 뒤에 남는 화면 흐림(0..1). 글리치보다 오래 간다.
    public static float Blur { get; private set; }

    public static void Set(float amount, float seed)
    {
        Amount = Mathf.Clamp01(amount);
        Seed = seed;
    }

    public static void SetBlur(float blur)
    {
        Blur = Mathf.Clamp01(blur);
    }

    // 글리치만 끈다. 흐림은 자기 시간에 맞춰 따로 걷힌다.
    public static void ClearGlitch()
    {
        Amount = 0f;
    }

    public static void Clear()
    {
        Amount = 0f;
        Blur = 0f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnLoad() => Clear();
}
