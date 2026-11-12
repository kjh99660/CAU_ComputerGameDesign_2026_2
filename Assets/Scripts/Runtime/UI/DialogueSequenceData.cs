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
    // 기본값은 기획서(2026.10.25) 2-7 도입 대사, 2-8 마무리 대사다. 실제로 쓰는 값은 Resources/DialogueSequence.asset이다.
    // 노아는 왼쪽, K-7은 오른쪽 초상화를 쓴다(leftSpeaker).
    public DialogueLine[] intro = {
        new DialogueLine { speaker = "노아", text = "허브 접속 확인. 비상 복구를 시작했어.", leftSpeaker = true },
        new DialogueLine { speaker = "K-7", text = "출구는 아직 잠겨 있군.", leftSpeaker = false },
        new DialogueLine { speaker = "노아", text = "네 식별 정보가 수배 명단에 남아 있어. 초기화까지 100초가 필요해.", leftSpeaker = true },
        new DialogueLine { speaker = "K-7", text = "놈들도 내가 여기 있는 걸 알아챘겠지.", leftSpeaker = false },
        new DialogueLine { speaker = "노아", text = "로봇들이 네 쪽으로 이동하고 있어. 그 모듈을 빼앗기면 기록은 영영 사라져.", leftSpeaker = true },
        new DialogueLine { speaker = "K-7", text = "기록은 지키겠어. 넌 복구를 끝내.", leftSpeaker = false }
    };
    public DialogueLine[] outro = {
        new DialogueLine { speaker = "노아", text = "재부팅 완료. 이 구역의 추적 정보와 공격 명령을 지웠어.", leftSpeaker = true },
        new DialogueLine { speaker = "K-7", text = "이제 나갈 수 있나?", leftSpeaker = false },
        new DialogueLine { speaker = "노아", text = "출구가 열렸어. 외부망에 도착하면 기록을 공개할 수 있어.", leftSpeaker = true },
        new DialogueLine { speaker = "K-7", text = "이번엔 저들이 기록을 지우게 두지 않겠어.", leftSpeaker = false }
    };
}
