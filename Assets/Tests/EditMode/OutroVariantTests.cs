using NUnit.Framework;
using UnityEngine;

// 성과에 반응하는 마무리(05_STORY_AND_SCRIPT 4.4)의 선택 규칙과 대사 자료.
// 게임 에셋의 변형 배열은 팀이 대사를 정할 때까지 비어 있다(= 언제나 기본 마무리). 규칙은 시험용 자료로 검증한다.
public sealed class OutroVariantTests
{
    private const int ManyHits = 15, PreciseHits = 3, PreciseKills = 200;

    private static OutroVariant Choose(int hits, int kills, bool hasManyHits = true, bool hasPrecise = true) =>
        OutroSelection.Choose(hits, kills, ManyHits, PreciseHits, PreciseKills, hasManyHits, hasPrecise);

    [Test]
    public void BoundariesPickTheDocumentedVariant()
    {
        Assert.AreEqual(OutroVariant.Base, Choose(0, 0), "A round with no kills and no hits is the base outro.");
        Assert.AreEqual(OutroVariant.Base, Choose(14, 0));
        Assert.AreEqual(OutroVariant.ManyHits, Choose(15, 0));
        Assert.AreEqual(OutroVariant.ManyHits, Choose(15, 500), "Many hits wins even with many kills.");
        Assert.AreEqual(OutroVariant.Precise, Choose(3, 200));
        Assert.AreEqual(OutroVariant.Base, Choose(4, 200));
        Assert.AreEqual(OutroVariant.Base, Choose(3, 199));
        Assert.AreEqual(OutroVariant.Precise, Choose(0, 200));
        Assert.AreEqual(OutroVariant.Base, Choose(10, 150), "Neither condition: base.");
    }

    [Test]
    public void AnEmptyVariantIsOffAndOverlappingThresholdsPreferManyHits()
    {
        Assert.AreEqual(OutroVariant.Base, Choose(20, 0, hasManyHits: false));
        Assert.AreEqual(OutroVariant.Base, Choose(0, 300, hasPrecise: false));
        Assert.AreEqual(OutroVariant.Base, Choose(20, 300, hasManyHits: false), "With many hits off, 20 hits is not precise either.");
        // 임계값을 겹치게 바꾸면(많이 맞음 2회 이상, 정밀 3회 이하) 많이 맞은 판이 먼저다.
        Assert.AreEqual(OutroVariant.ManyHits, OutroSelection.Choose(3, 250, 2, 3, 200, true, true));
        // 0 같은 잘못된 임계값으로 모든 판이 변형이 되지 않게 최소 1로 본다.
        Assert.AreEqual(OutroVariant.Base, OutroSelection.Choose(0, 0, 0, 3, 0, true, true));
    }

    [Test]
    public void DataPicksTheVariantLinesAndFallsBackToTheBaseOutro()
    {
        var data = TestData();
        try
        {
            Assert.AreSame(data.outro, data.OutroLines(data.ChooseOutro(0, 0)));
            Assert.AreEqual(OutroVariant.ManyHits, data.ChooseOutro(15, 3));
            Assert.AreSame(data.outroManyHits, data.OutroLines(OutroVariant.ManyHits));
            Assert.AreEqual(OutroVariant.Precise, data.ChooseOutro(3, 200));
            Assert.AreSame(data.outroPrecise, data.OutroLines(OutroVariant.Precise));

            data.outroPrecise = new DialogueLine[0];
            Assert.AreEqual(OutroVariant.Base, data.ChooseOutro(3, 200), "An empty variant array turns that variant off.");
            Assert.AreSame(data.outro, data.OutroLines(OutroVariant.Precise));
            data.outroManyHits = null;
            Assert.AreEqual(OutroVariant.Base, data.ChooseOutro(40, 0));
            Assert.AreSame(data.outro, data.OutroLines(OutroVariant.ManyHits));

            data.manyHitsMinHits = 5;
            data.outroManyHits = TestData().outroManyHits;
            Assert.AreEqual(OutroVariant.ManyHits, data.ChooseOutro(5, 0), "The thresholds are data.");
        }
        finally { Object.DestroyImmediate(data); }
    }

    [Test]
    public void ANewSequenceHasEmptyVariantsAndTheProvisionalThresholds()
    {
        var data = ScriptableObject.CreateInstance<DialogueSequenceData>();
        try
        {
            Assert.AreEqual(0, data.outroManyHits.Length);
            Assert.AreEqual(0, data.outroPrecise.Length);
            Assert.AreEqual(15, data.manyHitsMinHits);
            Assert.AreEqual(3, data.preciseMaxHits);
            Assert.AreEqual(200, data.preciseMinKills);
            Assert.AreEqual(OutroVariant.Base, data.ChooseOutro(99, 999));
        }
        finally { Object.DestroyImmediate(data); }
    }

    // 결말은 하나다: 채워진 변형마다 첫 줄과 끝 두 줄이 기본 마무리와 글자까지 같아야 한다(4.4).
    [Test]
    public void EveryFilledVariantOfTheGameAssetKeepsTheBaseOpeningAndEnding()
    {
        var data = Resources.Load<DialogueSequenceData>("DialogueSequence");
        Assert.IsNotNull(data, "Resources/DialogueSequence.asset is missing.");
        Assert.IsNull(VariantProblem(data.outro, data.outroManyHits), VariantProblem(data.outro, data.outroManyHits));
        Assert.IsNull(VariantProblem(data.outro, data.outroPrecise), VariantProblem(data.outro, data.outroPrecise));
    }

    [Test]
    public void TheVariantCheckAcceptsTheDocumentedLinesAndCatchesADifferentEnding()
    {
        var data = TestData();
        try
        {
            Assert.IsNull(VariantProblem(data.outro, data.outroManyHits));
            Assert.IsNull(VariantProblem(data.outro, data.outroPrecise));
            Assert.IsNull(VariantProblem(data.outro, new DialogueLine[0]), "An empty variant is off, not wrong.");

            DialogueLine[] changedEnding = (DialogueLine[])data.outroManyHits.Clone();
            changedEnding[changedEnding.Length - 1].text = "다른 결말.";
            StringAssert.Contains("last", VariantProblem(data.outro, changedEnding));
            DialogueLine[] changedOpening = (DialogueLine[])data.outroPrecise.Clone();
            changedOpening[0].speaker = "K-7";
            StringAssert.Contains("first", VariantProblem(data.outro, changedOpening));
            StringAssert.Contains("lines", VariantProblem(data.outro, new[] { data.outro[0], data.outro[3] }));
        }
        finally { Object.DestroyImmediate(data); }
    }

    // 무전 줄은 비어 있으면 꺼진 것이다. 채워져 있으면 화자·대사가 있고 시각이 한 판 안에 있어야 한다.
    [Test]
    public void BattleRadioLinesOfTheGameAssetAreComplete()
    {
        var data = Resources.Load<DialogueSequenceData>("DialogueSequence");
        var settings = Resources.Load<CombatSettings>("CombatSettings");
        Assert.IsNotNull(data);
        Assert.IsNotNull(settings);
        foreach (BattleRadioLine line in data.battleRadio)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(line.speaker), "A radio line has no speaker.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(line.text), $"{line.speaker}'s radio line is empty.");
            Assert.Greater(line.remainingSeconds, 0f, line.text);
            Assert.Less(line.remainingSeconds, settings.roundDuration, line.text + " would never play.");
        }
    }

    private static string VariantProblem(DialogueLine[] baseOutro, DialogueLine[] variant)
    {
        if (variant == null || variant.Length == 0) return null;
        if (baseOutro == null || baseOutro.Length < 3) return "The base outro needs at least 3 lines to compare.";
        if (variant.Length < 3) return $"A variant needs at least 3 lines (first + last two), has {variant.Length} lines.";
        if (!Same(baseOutro[0], variant[0])) return $"The first line differs: '{variant[0].text}'.";
        for (int back = 1; back <= 2; back++)
            if (!Same(baseOutro[baseOutro.Length - back], variant[variant.Length - back]))
                return $"One of the last two lines differs: '{variant[variant.Length - back].text}'.";
        return null;
    }

    private static bool Same(DialogueLine a, DialogueLine b) =>
        a.speaker == b.speaker && a.text == b.text && a.leftSpeaker == b.leftSpeaker;

    private static DialogueLine Noa(string text) => new DialogueLine { speaker = "노아", text = text, leftSpeaker = true };
    private static DialogueLine K7(string text) => new DialogueLine { speaker = "K-7", text = text, leftSpeaker = false };

    // 시험용 자료: 3절 권장 마무리와 4.4의 두 변형(6줄). 게임 에셋에는 넣지 않는다.
    private static DialogueSequenceData TestData()
    {
        var data = ScriptableObject.CreateInstance<DialogueSequenceData>();
        DialogueLine first = Noa("재부팅 완료. 이 구역의 추적 정보와 공격 명령을 지웠어.");
        DialogueLine exitOpen = Noa("출구가 열렸어. 외부망이 닿는 곳까지 가면 기록을 공개할 수 있어.");
        DialogueLine last = K7("이번엔 아르카가 아무것도 지우게 두지 않겠어. 놈들 밑에서 내가 한 짓까지.");
        data.outro = new[] { first, K7("로봇들이 멈췄군. 이제 나갈 수 있나?"), exitOpen, last };
        data.outroManyHits = new[]
        {
            first, K7("로봇들이 멈췄군."), Noa("전투 로그 확인. 너 많이 맞았어."), K7("기록은 무사해. 그거면 됐어."), exitOpen, last
        };
        data.outroPrecise = new[]
        {
            first, K7("로봇들이 멈췄군."), Noa("전투 로그 확인. 정밀한 제압이었어."),
            K7("아르카 밑에서도 이만큼 정밀했지. 표적이 달랐을 뿐이야."), exitOpen, last
        };
        return data;
    }
}
