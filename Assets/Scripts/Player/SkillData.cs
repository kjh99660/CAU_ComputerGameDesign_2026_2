using UnityEngine;

// 스킬 하나의 설정. 스킬이 늘어나면 이 에셋만 하나 더 만들어 PlayerSkill의 목록에 추가한다.
// 진행: ① 준비동작 → ② 돌진(투명화) → ③ 본동작(모션 + VFX + 판정)
// Animator 상태 이름 규칙: "Skill{번호}_Windup", "Skill{번호}_Dash", "Skill{번호}_Main" (Tag = Action)
[CreateAssetMenu(menuName = "Combat/Skill Data")]
public class SkillData : ScriptableObject
{
    [Header("Basic")]
    public int skillId = 1;                       // Animator의 SkillId 값, 상태 이름 번호
    public string inputActionName = "Skill1";     // 입력 설정(InputSystem_Actions)의 Player 액션 이름
    public float cooldown = 5f;                   // 사용 후 다시 쓸 수 있을 때까지 (초). 0이면 없음

    [Header("1. Windup (준비동작)")]
    public float windupTime = 0f;                 // 0이면 건너뜀

    [Header("2. Dash (투명화 후 이동)")]
    public float dashDistance = 5f;               // 이동 거리 (m). 0이면 이동 없이 바로 본동작
    public float dashDuration = 0.2f;             // 이동 시간 (초)
    public bool hideDuringDash = true;            // 돌진 중 캐릭터, 칼, 칼집, 그림자 숨김
    public bool dashPassThroughEnemies = false;   // 켜면 돌진 중 적 통과 (끄면 적에게 막힘)
    public LayerMask enemyLayers = 1 << 8;        // 통과 모드에서 무시할 레이어 (기본: 8번 Enemy)
    public bool invulnerableDuringDash = false;   // 돌진 중 무적 (나중을 위한 칸)
    public GameObject vanishVfx;                  // 사라질 때 이펙트 (나중을 위한 칸, 비워두면 없음)
    public GameObject appearVfx;                  // 나타날 때 이펙트 (나중을 위한 칸, 비워두면 없음)

    [Header("3. Main (본동작)")]
    public AttackHitData hitData;                 // 판정 범위 (원형이면 Shape=Sector, 각도 360)
    public float hitTime = 0.1f;                  // 본동작 시작 후 판정 시작 시각 (초)
    public float hitDuration = 0.05f;             // 판정 유지 시간 (초). 짧으면 "한 번만" 판정
    public GameObject vfxPrefab;                  // 본동작 이펙트 (예: Skill1_WindBlades)
    public Vector3 vfxOffset = Vector3.zero;      // 캐릭터 발밑 기준 이펙트 위치 (캐릭터 방향 기준)
    public float cancelTime = 0.8f;               // 본동작 시작 후 이 시각부터 이동/공격으로 캔슬 가능 (초)
}
