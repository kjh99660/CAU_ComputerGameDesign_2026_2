using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// 실제로 쓰는 대사 데이터(Resources/DialogueSequence.asset)가 비어 있지 않고, 인물마다 초상화 쪽이 한결같은지 확인한다.
public sealed class DialogueSequenceDataTests
{
    [Test]
    public void SequenceHasIntroAndOutroLines()
    {
        var data = Resources.Load<DialogueSequenceData>("DialogueSequence");
        Assert.IsNotNull(data, "Resources/DialogueSequence.asset is missing.");
        Assert.Greater(data.intro.Length, 0);
        Assert.Greater(data.outro.Length, 0);
    }

    [Test]
    public void EveryLineHasSpeakerAndTextAndEachSpeakerKeepsOneSide()
    {
        var data = Resources.Load<DialogueSequenceData>("DialogueSequence");
        var sides = new Dictionary<string, bool>();
        foreach (DialogueLine[] lines in new[] { data.intro, data.outro })
            foreach (DialogueLine line in lines)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(line.speaker), "Speaker name is empty.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(line.text), $"{line.speaker} has an empty line.");
                if (sides.TryGetValue(line.speaker, out bool left))
                    Assert.AreEqual(left, line.leftSpeaker, $"{line.speaker} appears on both portrait sides.");
                else
                    sides[line.speaker] = line.leftSpeaker;
            }
    }
}
