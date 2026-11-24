using System;
using UnityEngine;

[Serializable]
public struct DialogueLine
{
    public string speaker;
    [TextArea(2, 5)] public string text;
    public bool leftSpeaker;
}

// 전투 중 무전 한 줄(05_STORY_AND_SCRIPT 4.6). 전투 시계가 remainingSeconds에 닿는 순간 한 번 HUD 자막으로 뜬다.
// leftSpeaker는 대화와 같은 뜻이다(true = 노아 초상화).
[Serializable]
public struct BattleRadioLine
{
    [Min(0f)] public float remainingSeconds;
    public string speaker;
    [TextArea(1, 3)] public string text;
    public bool leftSpeaker;
}

// 성과에 반응하는 마무리(05_STORY_AND_SCRIPT 4.4)에서 고른 대사 묶음.
public enum OutroVariant
{
    Base,
    ManyHits,
    Precise
}

[CreateAssetMenu(menuName = "Game Demo/Dialogue Sequence")]
public sealed class DialogueSequenceData : ScriptableObject
{
    // 기본값은 05_STORY_AND_SCRIPT 3절의 권장 대사다(2026-11-25 채택. 제안서 2-7·2-8의 제출본에서 8줄을 고쳤다).
    // 실제로 쓰는 값은 Resources/DialogueSequence.asset이다.
    // 노아는 왼쪽, K-7은 오른쪽 초상화를 쓴다(leftSpeaker).
    public DialogueLine[] intro = {
        new DialogueLine { speaker = "노아", text = "허브 접속 확인. 비상 재부팅을 시작했어.", leftSpeaker = true },
        new DialogueLine { speaker = "K-7", text = "출구는 아직 잠겨 있군.", leftSpeaker = false },
        new DialogueLine { speaker = "노아", text = "아르카가 널 추적 중이라 잠긴 거야. 100초 뒤 허브가 재부팅되면 열려.", leftSpeaker = true },
        new DialogueLine { speaker = "K-7", text = "놈들 로봇은?", leftSpeaker = false },
        new DialogueLine { speaker = "노아", text = "네가 지닌 모듈을 노려. 그게 아르카가 지운 작전 기록의 마지막 사본이야.", leftSpeaker = true },
        new DialogueLine { speaker = "K-7", text = "기록은 지키겠어. 넌 재부팅을 끝내.", leftSpeaker = false }
    };
    public DialogueLine[] outro = {
        new DialogueLine { speaker = "노아", text = "재부팅 완료. 이 구역의 추적 정보와 공격 명령을 지웠어.", leftSpeaker = true },
        new DialogueLine { speaker = "K-7", text = "로봇들이 멈췄군. 이제 나갈 수 있나?", leftSpeaker = false },
        new DialogueLine { speaker = "노아", text = "출구가 열렸어. 외부망이 닿는 곳까지 가면 기록을 공개할 수 있어.", leftSpeaker = true },
        new DialogueLine { speaker = "K-7", text = "이번엔 아르카가 아무것도 지우게 두지 않겠어. 놈들 밑에서 내가 한 짓까지.", leftSpeaker = false }
    };

    // 전투 중 무전. 비어 있으면 쓰지 않는다. 대사와 시각은 4.6의 예문이며 임시값이다.
    // 50초 무전은 2026-11-25에 50% 생성 경고(spawnWarnings 2번)로 바뀌어 10초 한 줄만 남았다.
    [Header("Battle radio (4.6) - provisional. Empty = off")]
    public BattleRadioLine[] battleRadio = {
        new BattleRadioLine { remainingSeconds = 10f, speaker = "노아", text = "10초 남았어. 버텨.", leftSpeaker = true }
    };

    // 생성 이벤트 때 노아가 띄우는 경고(무전 자막을 경고 모양으로 쓴다). EnemySettings의 생성 단계가
    // warningNumber N으로 N번째 줄을 고른다. 비어 있으면 경고를 띄우지 않는다. 대사는 임시값이다.
    // 기본 순서: 25% 소규모 대량 생성, 50% 생성 속도 2배, 75% 대규모 대량 생성.
    [Header("Spawn warnings - provisional. Empty = off")]
    public DialogueLine[] spawnWarnings = {
        new DialogueLine { speaker = "노아", text = "로봇 신호가 한꺼번에 잡혔어. 한 무리가 몰려와.", leftSpeaker = true },
        new DialogueLine { speaker = "노아", text = "절반 지났어. 로봇이 두 배로 쏟아져 들어와.", leftSpeaker = true },
        new DialogueLine { speaker = "노아", text = "대규모 신호야. 이 구역 로봇이 전부 몰려와!", leftSpeaker = true }
    };

    // 성과에 반응하는 마무리(4.4). 변형 배열이 비어 있으면 그 변형은 꺼지고 기본 마무리(outro)를 쓴다.
    // 변형을 채울 때는 첫 줄과 끝 두 줄을 기본 마무리와 글자까지 같게 둔다(결말은 하나다). 자료 테스트가 확인한다.
    // 조건은 점수가 아니라 피격(HITS TAKEN)과 처치(KILLS)에 건다. 세 임계값 모두 임시값이다.
    [Header("Outro variants (4.4) - provisional. Empty = base outro")]
    public DialogueLine[] outroManyHits = Array.Empty<DialogueLine>();
    public DialogueLine[] outroPrecise = Array.Empty<DialogueLine>();
    // 많이 맞은 판: 피격이 이 값 이상.
    [Min(1)] public int manyHitsMinHits = 15;
    // 정밀한 판: 피격이 preciseMaxHits 이하이면서 처치가 preciseMinKills 이상.
    [Min(0)] public int preciseMaxHits = 3;
    [Min(1)] public int preciseMinKills = 200;

    // 이번 판의 통계로 고른 마무리. 두 조건이 함께 맞으면(임계값을 겹치게 바꾼 경우) 많이 맞은 판이 먼저다.
    public OutroVariant ChooseOutro(int hitCount, int killCount) =>
        OutroSelection.Choose(hitCount, killCount, manyHitsMinHits, preciseMaxHits, preciseMinKills,
            outroManyHits != null && outroManyHits.Length > 0, outroPrecise != null && outroPrecise.Length > 0);

    public DialogueLine[] OutroLines(OutroVariant variant) => variant switch
    {
        OutroVariant.ManyHits when outroManyHits != null && outroManyHits.Length > 0 => outroManyHits,
        OutroVariant.Precise when outroPrecise != null && outroPrecise.Length > 0 => outroPrecise,
        _ => outro
    };
}

// 성과에 반응하는 마무리의 선택 규칙. Unity에 의존하지 않아 EditMode에서 경계값을 검증한다.
public static class OutroSelection
{
    // 처치 0, 피격 0인 판은 언제나 기본이다(정밀한 판은 처치 조건도 맞아야 한다).
    public static OutroVariant Choose(int hitCount, int killCount, int manyHitsMinHits, int preciseMaxHits, int preciseMinKills,
        bool hasManyHits, bool hasPrecise)
    {
        if (hasManyHits && hitCount >= Math.Max(1, manyHitsMinHits))
            return OutroVariant.ManyHits;
        if (hasPrecise && hitCount <= preciseMaxHits && killCount >= Math.Max(1, preciseMinKills))
            return OutroVariant.Precise;
        return OutroVariant.Base;
    }
}
