using NUnit.Framework;

// 전투 중 무전의 시각 규칙: 시계가 그 시각을 지나가는 순간 한 판에 한 번만 뜬다.
public sealed class BattleRadioScheduleTests
{
    private static readonly BattleRadioLine[] Lines =
    {
        new BattleRadioLine { remainingSeconds = 50f, speaker = "노아", text = "절반 지났어. 공격 명령은 아직 살아 있어.", leftSpeaker = true },
        new BattleRadioLine { remainingSeconds = 10f, speaker = "노아", text = "10초 남았어. 버텨.", leftSpeaker = true }
    };

    [Test]
    public void EachLinePlaysOnceWhenTheClockPassesItsTime()
    {
        var schedule = new BattleRadioSchedule();
        schedule.Begin(Lines);
        Assert.AreEqual(-1, schedule.Advance(100f), "The first reading only sets the baseline.");
        Assert.AreEqual(-1, schedule.Advance(50.01f));
        Assert.AreEqual(0, schedule.Advance(49.99f));
        Assert.AreEqual(-1, schedule.Advance(49.5f), "A line plays once.");
        Assert.AreEqual(-1, schedule.Advance(10.5f));
        Assert.AreEqual(1, schedule.Advance(10f), "Reaching the time exactly plays the line.");
        Assert.AreEqual(-1, schedule.Advance(0f));
        Assert.AreEqual("10초 남았어. 버텨.", schedule.Line(1).text);
    }

    [Test]
    public void ARoundThatStartsBelowALineNeverPlaysIt()
    {
        var schedule = new BattleRadioSchedule();
        schedule.Begin(Lines);
        Assert.AreEqual(-1, schedule.Advance(12f));
        for (float t = 11.9f; t > 10.05f; t -= 0.1f)
            Assert.AreEqual(-1, schedule.Advance(t));
        Assert.AreEqual(1, schedule.Advance(9.98f), "Only the 10 s line plays in a 12 s round.");
        Assert.AreEqual(-1, schedule.Advance(0f));
    }

    [Test]
    public void BeginStartsTheNextRoundAgainAndPausesDoNotReplay()
    {
        var schedule = new BattleRadioSchedule();
        schedule.Begin(Lines);
        schedule.Advance(100f);
        Assert.AreEqual(0, schedule.Advance(49f));
        // Pause와 Resume 사이에는 시계가 그대로다. 같은 값을 다시 읽어도 다시 뜨지 않는다.
        Assert.AreEqual(-1, schedule.Advance(49f));
        Assert.AreEqual(-1, schedule.Advance(48.9f));
        schedule.Begin(Lines);
        Assert.AreEqual(-1, schedule.Advance(100f));
        Assert.AreEqual(0, schedule.Advance(49.9f), "A new round plays its lines again.");
    }

    [Test]
    public void PassingSeveralTimesAtOncePlaysOnlyTheLatestLine()
    {
        var schedule = new BattleRadioSchedule();
        schedule.Begin(Lines);
        schedule.Advance(60f);
        Assert.AreEqual(1, schedule.Advance(5f), "The 10 s line is the one that still matters.");
        Assert.AreEqual(-1, schedule.Advance(4f), "The skipped 50 s line does not play late.");
    }

    [Test]
    public void NoLinesMeansNoRadio()
    {
        var schedule = new BattleRadioSchedule();
        schedule.Begin(null);
        Assert.AreEqual(0, schedule.LineCount);
        schedule.Advance(100f);
        Assert.AreEqual(-1, schedule.Advance(0f));
        schedule.Begin(new BattleRadioLine[0]);
        schedule.Advance(100f);
        Assert.AreEqual(-1, schedule.Advance(0f));
    }
}
