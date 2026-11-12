using NUnit.Framework;

// 대화 진행 규칙: 타이핑 진행, 넘김, 완료와 Skip, 다시 시작을 검증한다.
public sealed class DialogueProgressTests
{
    private static DialogueLine[] Lines(params string[] texts)
    {
        var lines = new DialogueLine[texts.Length];
        for (int i = 0; i < texts.Length; i++)
            lines[i] = new DialogueLine { speaker = i % 2 == 0 ? "A" : "B", text = texts[i], leftSpeaker = i % 2 == 0 };
        return lines;
    }

    [Test]
    public void BeginStartsAtFirstLineWithNoVisibleCharacters()
    {
        var progress = new DialogueProgress();
        progress.Begin(Lines("가나다라", "마바"));
        Assert.AreEqual(0, progress.LineIndex);
        Assert.AreEqual(0, progress.VisibleCharacters);
        Assert.IsFalse(progress.IsLineComplete);
        Assert.IsFalse(progress.IsFinished);
    }

    [Test]
    public void TickRevealsCharactersAtTheGivenRateAndStopsAtLineEnd()
    {
        var progress = new DialogueProgress();
        progress.Begin(Lines("가나다라마바사아자차"));
        progress.Tick(0.25f, 20f);
        Assert.AreEqual(5, progress.VisibleCharacters);
        Assert.IsFalse(progress.IsLineComplete);
        progress.Tick(10f, 20f);
        Assert.AreEqual(10, progress.VisibleCharacters);
        Assert.IsTrue(progress.IsLineComplete);
    }

    [Test]
    public void NonPositiveRateShowsWholeLineAtOnce()
    {
        var progress = new DialogueProgress();
        progress.Begin(Lines("가나다"));
        progress.Tick(0.01f, 0f);
        Assert.IsTrue(progress.IsLineComplete);
        Assert.AreEqual(3, progress.VisibleCharacters);
    }

    [Test]
    public void AdvanceWhileTypingCompletesLineThenMovesToNextLine()
    {
        var progress = new DialogueProgress();
        progress.Begin(Lines("가나다라", "마바"));
        progress.Tick(0.05f, 20f);

        Assert.AreEqual(DialogueProgress.AdvanceResult.RevealedLine, progress.Advance());
        Assert.AreEqual(0, progress.LineIndex);
        Assert.AreEqual(4, progress.VisibleCharacters);

        Assert.AreEqual(DialogueProgress.AdvanceResult.NextLine, progress.Advance());
        Assert.AreEqual(1, progress.LineIndex);
        Assert.AreEqual(0, progress.VisibleCharacters);
        Assert.AreEqual("마바", progress.CurrentLine.text);
    }

    [Test]
    public void AdvanceOnLastCompletedLineFinishesOnlyOnce()
    {
        var progress = new DialogueProgress();
        progress.Begin(Lines("가나"));
        progress.Tick(1f, 20f);

        Assert.AreEqual(DialogueProgress.AdvanceResult.Finished, progress.Advance());
        Assert.IsTrue(progress.IsFinished);
        Assert.AreEqual(DialogueProgress.AdvanceResult.Ignored, progress.Advance());
        Assert.IsFalse(progress.Skip());
    }

    [Test]
    public void SkipFinishesFromAnyLineOnlyOnce()
    {
        var progress = new DialogueProgress();
        progress.Begin(Lines("가나다라", "마바", "사아"));
        progress.Tick(0.05f, 20f);

        Assert.IsTrue(progress.Skip());
        Assert.IsTrue(progress.IsFinished);
        Assert.IsFalse(progress.Skip());
        Assert.AreEqual(DialogueProgress.AdvanceResult.Ignored, progress.Advance());
    }

    [Test]
    public void BeginAgainRestartsFromFirstLine()
    {
        var progress = new DialogueProgress();
        DialogueLine[] lines = Lines("가나", "다라");
        progress.Begin(lines);
        progress.Tick(1f, 20f);
        progress.Advance();
        progress.Skip();

        progress.Begin(lines);
        Assert.AreEqual(0, progress.LineIndex);
        Assert.AreEqual(0, progress.VisibleCharacters);
        Assert.IsFalse(progress.IsFinished);
    }

    [Test]
    public void EmptySequenceFinishesOnFirstAdvance()
    {
        var progress = new DialogueProgress();
        progress.Begin(null);
        Assert.AreEqual(0, progress.LineCount);
        Assert.IsTrue(progress.IsLineComplete);
        Assert.AreEqual(DialogueProgress.AdvanceResult.Finished, progress.Advance());
    }
}
