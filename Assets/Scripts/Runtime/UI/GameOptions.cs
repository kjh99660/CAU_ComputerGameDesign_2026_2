using System;
using UnityEngine;

// 실행을 넘어 남는 플레이어 설정(마우스 감도, 음량)의 저장소. 테스트는 가짜 저장소를 넣거나 PlayerPrefs 키를 저장했다가 되돌린다.
public interface IOptionsStore
{
    bool TryLoad(string key, out float value);
    void Save(string key, float value);
}

// PlayerPrefs에 둔다. 키는 프로젝트 공통 접두사 "REBOOT."를 쓴다(BestScoreRecord와 같다).
public sealed class PlayerPrefsOptionsStore : IOptionsStore
{
    public bool TryLoad(string key, out float value)
    {
        value = 0f;
        if (!PlayerPrefs.HasKey(key)) return false;
        value = PlayerPrefs.GetFloat(key, 0f);
        return true;
    }

    public void Save(string key, float value)
    {
        PlayerPrefs.SetFloat(key, value);
        PlayerPrefs.Save();
    }
}

// 일시정지 메뉴에서 바꾸는 두 값. 버튼 한 번에 한 단계씩 움직이고 범위를 벗어나지 않으며, 바뀌면 바로 저장한다.
// - 감도(LookScale): 카메라 감도 배율. 100% = PlayerRuntime의 lookSensitivity 그대로. 시연 PC의 마우스가 달라도 맞출 수 있게 한다.
// - 음량(Volume): 전체 음량 배율. 100% = 만든 그대로(CoreBootstrap이 AudioListener.volume에 넣는다).
// 범위와 단계는 모두 임시값(provisional)이다. 저장된 값이 범위를 벗어났거나 숫자가 아니면 기본값으로 돌아간다.
// Unity에 의존하지 않아 EditMode에서 가짜 저장소로 검증한다.
public sealed class GameOptions
{
    public const string LookScaleKey = "REBOOT.LookScale";
    public const string VolumeKey = "REBOOT.Volume";
    public const int LookStepPercent = 10;
    public const int MinLookPercent = 30;
    public const int MaxLookPercent = 300;
    public const int DefaultLookPercent = 100;
    public const int VolumeStepPercent = 10;
    public const int MinVolumePercent = 0;
    public const int MaxVolumePercent = 100;
    public const int DefaultVolumePercent = 100;

    private readonly IOptionsStore _store;

    public int LookPercent { get; private set; }
    public int VolumePercent { get; private set; }
    public float LookScale => LookPercent / 100f;
    public float Volume => VolumePercent / 100f;
    public bool CanLowerLook => LookPercent > MinLookPercent;
    public bool CanRaiseLook => LookPercent < MaxLookPercent;
    public bool CanLowerVolume => VolumePercent > MinVolumePercent;
    public bool CanRaiseVolume => VolumePercent < MaxVolumePercent;

    // 값이 실제로 바뀐 뒤에만 한 번 부른다.
    public event Action Changed;

    public GameOptions(IOptionsStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        LookPercent = Load(LookScaleKey, DefaultLookPercent, MinLookPercent, MaxLookPercent, LookStepPercent);
        VolumePercent = Load(VolumeKey, DefaultVolumePercent, MinVolumePercent, MaxVolumePercent, VolumeStepPercent);
    }

    // direction: 양수 = 한 단계 올림, 음수 = 한 단계 내림. 끝에 닿아 있으면 그대로 두고 false.
    public bool StepLook(int direction)
    {
        int next = Step(LookPercent, direction, MinLookPercent, MaxLookPercent, LookStepPercent);
        if (next == LookPercent) return false;
        LookPercent = next;
        _store.Save(LookScaleKey, LookScale);
        Changed?.Invoke();
        return true;
    }

    public bool StepVolume(int direction)
    {
        int next = Step(VolumePercent, direction, MinVolumePercent, MaxVolumePercent, VolumeStepPercent);
        if (next == VolumePercent) return false;
        VolumePercent = next;
        _store.Save(VolumeKey, Volume);
        Changed?.Invoke();
        return true;
    }

    private static int Step(int current, int direction, int minimum, int maximum, int step)
    {
        if (direction == 0) return current;
        return Math.Min(maximum, Math.Max(minimum, current + (direction > 0 ? step : -step)));
    }

    // 저장값은 배율(1.2 = 120%)이다. 가장 가까운 단계로 맞추고 범위 안에 둔다.
    private int Load(string key, int fallback, int minimum, int maximum, int step)
    {
        if (!_store.TryLoad(key, out float saved) || float.IsNaN(saved) || float.IsInfinity(saved))
            return fallback;
        double percent = saved * 100.0;
        if (percent < minimum - step || percent > maximum + step)
            return fallback;
        int snapped = (int)Math.Round(percent / step, MidpointRounding.AwayFromZero) * step;
        return Math.Min(maximum, Math.Max(minimum, snapped));
    }
}
