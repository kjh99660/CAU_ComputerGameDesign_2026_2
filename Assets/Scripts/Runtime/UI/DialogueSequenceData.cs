using System;
using UnityEngine;

[Serializable]
public struct DialogueLine
{
    public string speaker;
    [TextArea(2, 5)] public string text;
    public bool leftSpeaker;
}

[CreateAssetMenu(menuName = "Game Demo/Dialogue Sequence")]
public sealed class DialogueSequenceData : ScriptableObject
{
    // Temporary text for verifying dialogue order; replace with team-approved lines.
    public DialogueLine[] intro = {
        new DialogueLine { speaker = "인물 A", text = "도입 대사 1을 입력해 주세요.", leftSpeaker = true },
        new DialogueLine { speaker = "인물 B", text = "도입 대사 2를 입력해 주세요.", leftSpeaker = false }
    };
    public DialogueLine[] outro = {
        new DialogueLine { speaker = "인물 A", text = "종료 대사를 입력해 주세요.", leftSpeaker = true }
    };
}
