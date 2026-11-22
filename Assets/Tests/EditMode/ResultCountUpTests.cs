using NUnit.Framework;

// 결과 화면 숫자 올리기: 0에서 시작해 한 방향으로 올라 최종값에 정확히 도착하는지, 순서와 다시 시작을 검증한다.
public sealed class ResultCountUpTests
{
    private static readonly ResultUiModel Sample = new ResultUiModel(12540, 78, 24, 3, 4);

    private static ResultCountUp Begun(ResultUiModel model)
    {
        var countUp = new ResultCountUp();
        countUp.Begin(model, 0.8f, 0.45f, 0.4f, 0.12f);
        return countUp;
    }

    private static void AssertShows(ResultCountUp countUp, ResultUiModel model)
    {
        Assert.AreEqual(model.FinalScore, countUp.Shown(ResultCountUp.Score));
        Assert.AreEqual(model.KillCount, countUp.Shown(ResultCountUp.Kills));
        Assert.AreEqual(model.MaxCombo, countUp.Shown(ResultCountUp.MaxCombo));
        Assert.AreEqual(model.HitCount, countUp.Shown(ResultCountUp.Hits));
        Assert.AreEqual(model.SpecialUseCount, countUp.Shown(ResultCountUp.Specials));
    }

    [Test]
    public void BeginStartsEveryFieldAtZero()
    {
        ResultCountUp countUp = Begun(Sample);
        Assert.IsFalse(countUp.IsComplete);
        AssertShows(countUp, new ResultUiModel(0, 0, 0, 0, 0));
    }

    [Test]
    public void ValuesRiseMonotonicallyAndLandExactlyOnTheFinalValues()
    {
        ResultCountUp countUp = Begun(Sample);
        var previous = new int[ResultCountUp.FieldCount];
        for (int frame = 0; frame < 200 && !countUp.IsComplete; frame++)
        {
            countUp.Tick(1f / 60f);
            for (int field = 0; field < ResultCountUp.FieldCount; field++)
            {
                Assert.GreaterOrEqual(countUp.Shown(field), previous[field], $"Field {field} went down.");
                previous[field] = countUp.Shown(field);
            }
        }
        Assert.IsTrue(countUp.IsComplete, "Count-up must finish within the configured time.");
        AssertShows(countUp, Sample);
    }

    [Test]
    public void ScoreLeadsAndStatsFollowLeftToRight()
    {
        ResultCountUp countUp = Begun(Sample);
        countUp.Tick(0.3f);
        Assert.Greater(countUp.Shown(ResultCountUp.Score), 0);
        Assert.AreEqual(0, countUp.Shown(ResultCountUp.Kills), "Stats wait for their delay.");

        countUp.Tick(0.2f); // 0.5초: 처치만 시작했다(다음 칸은 0.52초부터).
        Assert.Greater(countUp.Shown(ResultCountUp.Kills), 0);
        Assert.AreEqual(0, countUp.Shown(ResultCountUp.MaxCombo));

        countUp.Tick(0.4f); // 0.9초: 점수와 처치는 끝났고 마지막 칸은 아직 오르는 중이다.
        Assert.AreEqual(Sample.FinalScore, countUp.Shown(ResultCountUp.Score));
        Assert.AreEqual(Sample.KillCount, countUp.Shown(ResultCountUp.Kills));
        Assert.IsFalse(countUp.IsComplete);

        countUp.Tick(0.4f); // 1.3초: 마지막 칸(0.76 + 0.45 = 1.21초)까지 끝났다.
        Assert.IsTrue(countUp.IsComplete);
        AssertShows(countUp, Sample);
    }

    [Test]
    public void TickReportsWhetherAShownValueChangedAndStopsWhenComplete()
    {
        ResultCountUp countUp = Begun(Sample);
        Assert.IsTrue(countUp.Tick(0.1f));
        countUp.Complete();
        Assert.IsTrue(countUp.IsComplete);
        AssertShows(countUp, Sample);
        Assert.IsFalse(countUp.Tick(0.1f));
    }

    [Test]
    public void ZeroDurationsShowFinalValuesImmediately()
    {
        var countUp = new ResultCountUp();
        countUp.Begin(Sample, 0f, 0f, 0f, 0f);
        Assert.IsTrue(countUp.IsComplete);
        AssertShows(countUp, Sample);
    }

    [Test]
    public void RetargetKeepsElapsedTimeAndLandsOnTheNewValues()
    {
        ResultCountUp countUp = Begun(Sample);
        countUp.Tick(0.4f);
        var changed = new ResultUiModel(20000, 80, 30, 5, 6);
        countUp.Retarget(changed);
        Assert.IsFalse(countUp.IsComplete);
        Assert.Less(countUp.Shown(ResultCountUp.Score), changed.FinalScore);
        countUp.Tick(2f);
        Assert.IsTrue(countUp.IsComplete);
        AssertShows(countUp, changed);

        // 다 오른 뒤에 값이 바뀌면 바로 새 값을 보여 준다.
        countUp.Retarget(Sample);
        AssertShows(countUp, Sample);
    }

    [Test]
    public void BeginAgainRestartsFromZero()
    {
        ResultCountUp countUp = Begun(Sample);
        countUp.Complete();
        countUp.Begin(Sample, 0.8f, 0.45f, 0.4f, 0.12f);
        Assert.IsFalse(countUp.IsComplete);
        AssertShows(countUp, new ResultUiModel(0, 0, 0, 0, 0));
    }

    [Test]
    public void BeforeBeginItShowsWhateverItIsGiven()
    {
        var countUp = new ResultCountUp();
        Assert.IsTrue(countUp.IsComplete);
        countUp.Retarget(Sample);
        AssertShows(countUp, Sample);
    }
}
