using NUnit.Framework;
using UnityEngine;

// 게임은 PlayerPrefs에 세 가지를 남긴다: 최고 점수(REBOOT.BestScore, 결과 화면이 저장)와 일시정지 메뉴의 설정 두 개
// (REBOOT.LookScale, REBOOT.Volume, CoreBootstrap이 씬을 열 때마다 읽어 카메라 감도와 전체 음량에 적용).
// Play Mode 테스트 전체를 시작하기 전에 세 키를 저장하고, 설정 두 개는 지워서 모든 테스트가 기본값(100%)으로 돌게 한다
// (이 컴퓨터에 저장해 둔 감도로 카메라 회전 테스트가 달라지지 않게). 끝난 뒤 세 키와 전체 음량을 그대로 되돌린다.
// 테스트를 돌려도 이 컴퓨터의 최고 점수와 설정은 바뀌지 않는다.
[SetUpFixture]
public sealed class BestScorePrefsGuard
{
    private bool _had, _hadLook, _hadVolume;
    private int _value;
    private float _look, _volume, _listenerVolume;

    [OneTimeSetUp]
    public void SaveBestScore()
    {
        _had = PlayerPrefs.HasKey(PlayerPrefsBestScoreStore.Key);
        _value = PlayerPrefs.GetInt(PlayerPrefsBestScoreStore.Key, 0);
        _hadLook = PlayerPrefs.HasKey(GameOptions.LookScaleKey);
        _look = PlayerPrefs.GetFloat(GameOptions.LookScaleKey, 1f);
        _hadVolume = PlayerPrefs.HasKey(GameOptions.VolumeKey);
        _volume = PlayerPrefs.GetFloat(GameOptions.VolumeKey, 1f);
        _listenerVolume = AudioListener.volume;
        PlayerPrefs.DeleteKey(GameOptions.LookScaleKey);
        PlayerPrefs.DeleteKey(GameOptions.VolumeKey);
    }

    [OneTimeTearDown]
    public void RestoreBestScore()
    {
        if (_had) PlayerPrefs.SetInt(PlayerPrefsBestScoreStore.Key, _value);
        else PlayerPrefs.DeleteKey(PlayerPrefsBestScoreStore.Key);
        if (_hadLook) PlayerPrefs.SetFloat(GameOptions.LookScaleKey, _look);
        else PlayerPrefs.DeleteKey(GameOptions.LookScaleKey);
        if (_hadVolume) PlayerPrefs.SetFloat(GameOptions.VolumeKey, _volume);
        else PlayerPrefs.DeleteKey(GameOptions.VolumeKey);
        PlayerPrefs.Save();
        AudioListener.volume = _listenerVolume;
        Debug.Log($"[BestScorePrefsGuard] {PlayerPrefsBestScoreStore.Key} restored (had a value: {_had}); options restored (look: {_hadLook}, volume: {_hadVolume}).");
    }
}
