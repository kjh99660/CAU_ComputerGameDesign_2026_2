using UnityEngine;

// 회피(구르기) 설정. 실행 중에 Inspector에서 바꾸면 다음 구르기부터 바로 반영된다.
[CreateAssetMenu(menuName = "Combat/Dodge Data")]
public class DodgeData : ScriptableObject
{
    [Header("Distance (클립 이동량에 배율을 곱해 이 거리가 되도록 맞춤)")]
    public float forwardDistance = 3.5f;     // 앞구르기 거리 (m)
    public float backwardDistance = 3.0f;    // 뒷구르기 거리 (m)

    [Header("Timing (구르기 상태 시작 후 경과 시간, 초)")]
    public float cancelTime = 0.7f;          // 이 시각부터 이동/공격/재회피로 캔슬 가능
    public float invulnerableStart = 0.05f;  // 무적 시작
    public float invulnerableEnd = 0.5f;     // 무적 끝

    [Header("Rules")]
    public float cooldown = 0f;              // 회피 시작 후 다음 회피까지 대기 시간 (초). 0이면 쿨타임 없음
    public bool passThroughEnemies = false;  // 켜면 구르는 동안 적을 통과 (끄면 적에게 막힘)
    public LayerMask enemyLayers = 1 << 8;   // 통과 모드에서 무시할 레이어 (기본: 8번 Enemy)
}
