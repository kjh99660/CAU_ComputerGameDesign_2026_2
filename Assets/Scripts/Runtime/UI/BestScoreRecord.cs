using System;
using UnityEngine;

// 실행을 넘어 남는 최고 점수의 저장소. 테스트는 가짜 저장소를 넣거나 PlayerPrefs 키를 저장했다가 되돌린다.
public interface IBestScoreStore
{
    bool TryLoad(out int score);
    void Save(int score);
}

// PlayerPrefs에 최고 점수를 둔다. 키는 프로젝트 공통 접두사 "REBOOT."를 쓴다.
public sealed class PlayerPrefsBestScoreStore : IBestScoreStore
{
    public const string Key = "REBOOT.BestScore";

    public bool TryLoad(out int score)
    {
        score = 0;
        if (!PlayerPrefs.HasKey(Key)) return false;
        score = PlayerPrefs.GetInt(Key, 0);
        return true;
    }

    public void Save(int score)
    {
        PlayerPrefs.SetInt(Key, score);
        PlayerPrefs.Save();
    }
}

public readonly struct BestScoreResult
{
    public readonly int PreviousBest;
    public readonly int Best;
    public readonly bool IsNewBest;

    public BestScoreResult(int previousBest, int best, bool isNewBest)
    {
        PreviousBest = previousBest;
        Best = best;
        IsNewBest = isNewBest;
    }
}

// 한 판의 최종 점수를 최고 점수와 견준다. Unity에 의존하지 않아 EditMode에서 가짜 저장소로 검증한다.
// 최고 점수는 저장된 값보다 클 때만 바뀐다(같으면 그대로, 0점은 기록이 되지 않는다).
public sealed class BestScoreTracker
{
    private readonly IBestScoreStore _store;

    public BestScoreTracker(IBestScoreStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public int Load() => _store.TryLoad(out int score) ? Math.Max(0, score) : 0;

    // 결과 화면에 들어올 때 한 번 부른다. 새 최고 점수이면 저장한다.
    public BestScoreResult Submit(int finalScore)
    {
        int previous = Load();
        bool newBest = finalScore > previous;
        if (newBest) _store.Save(finalScore);
        return new BestScoreResult(previous, newBest ? finalScore : previous, newBest);
    }
}
