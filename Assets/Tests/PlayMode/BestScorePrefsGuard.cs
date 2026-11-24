using NUnit.Framework;
using UnityEngine;

// 결과 화면은 최고 점수를 PlayerPrefs(REBOOT.BestScore)에 저장한다. 결과 화면까지 가는 Play Mode 테스트가 여럿이므로,
// 테스트 전체를 시작하기 전에 키를 저장했다가 끝난 뒤 그대로 되돌린다. 테스트를 돌려도 이 컴퓨터의 최고 점수는 바뀌지 않는다.
[SetUpFixture]
public sealed class BestScorePrefsGuard
{
    private bool _had;
    private int _value;

    [OneTimeSetUp]
    public void SaveBestScore()
    {
        _had = PlayerPrefs.HasKey(PlayerPrefsBestScoreStore.Key);
        _value = PlayerPrefs.GetInt(PlayerPrefsBestScoreStore.Key, 0);
    }

    [OneTimeTearDown]
    public void RestoreBestScore()
    {
        if (_had) PlayerPrefs.SetInt(PlayerPrefsBestScoreStore.Key, _value);
        else PlayerPrefs.DeleteKey(PlayerPrefsBestScoreStore.Key);
        PlayerPrefs.Save();
        Debug.Log($"[BestScorePrefsGuard] {PlayerPrefsBestScoreStore.Key} restored (had a value: {_had}).");
    }
}
