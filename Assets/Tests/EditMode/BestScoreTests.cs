using NUnit.Framework;
using UnityEngine;

// 최고 점수 규칙. 판정은 가짜 저장소로 보고, PlayerPrefs 저장소는 키를 저장했다가 그대로 되돌린다.
public sealed class BestScoreTests
{
    private sealed class MemoryStore : IBestScoreStore
    {
        public bool HasValue;
        public int Value;
        public int Saves;

        public bool TryLoad(out int score)
        {
            score = Value;
            return HasValue;
        }

        public void Save(int score)
        {
            HasValue = true;
            Value = score;
            Saves++;
        }
    }

    [Test]
    public void OnlyAHigherScoreBecomesTheNewBest()
    {
        var store = new MemoryStore();
        var tracker = new BestScoreTracker(store);
        Assert.AreEqual(0, tracker.Load());

        BestScoreResult zero = tracker.Submit(0);
        Assert.IsFalse(zero.IsNewBest, "A round with no score is not a record.");
        Assert.AreEqual(0, store.Saves);

        BestScoreResult first = tracker.Submit(660);
        Assert.IsTrue(first.IsNewBest);
        Assert.AreEqual(0, first.PreviousBest);
        Assert.AreEqual(660, first.Best);
        Assert.AreEqual(660, store.Value);

        BestScoreResult lower = tracker.Submit(110);
        Assert.IsFalse(lower.IsNewBest);
        Assert.AreEqual(660, lower.PreviousBest);
        Assert.AreEqual(660, lower.Best);
        BestScoreResult equal = tracker.Submit(660);
        Assert.IsFalse(equal.IsNewBest, "Matching the best is not a new best.");
        Assert.AreEqual(1, store.Saves, "Only a new best is written.");

        BestScoreResult higher = tracker.Submit(12540);
        Assert.IsTrue(higher.IsNewBest);
        Assert.AreEqual(660, higher.PreviousBest);
        Assert.AreEqual(12540, tracker.Load());
    }

    [Test]
    public void PlayerPrefsStoreUsesTheRebootKeyAndThisTestRestoresIt()
    {
        string key = PlayerPrefsBestScoreStore.Key;
        StringAssert.StartsWith("REBOOT.", key);
        bool had = PlayerPrefs.HasKey(key);
        int old = PlayerPrefs.GetInt(key, 0);
        try
        {
            PlayerPrefs.DeleteKey(key);
            var store = new PlayerPrefsBestScoreStore();
            Assert.IsFalse(store.TryLoad(out int none));
            Assert.AreEqual(0, none);
            store.Save(4321);
            Assert.IsTrue(store.TryLoad(out int saved));
            Assert.AreEqual(4321, saved);
            Assert.AreEqual(4321, new BestScoreTracker(store).Load());
        }
        finally
        {
            if (had) PlayerPrefs.SetInt(key, old);
            else PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        }
        Assert.AreEqual(had, PlayerPrefs.HasKey(key));
    }
}
