using UnityEngine;

[CreateAssetMenu(menuName = "Combat/Attack Hit Data")]
public class AttackHitData : ScriptableObject
{
    public Vector3 center = new Vector3(0f, 1f, 1.2f);   // 플레이어 기준 위치 (앞쪽 = +z)
    public Vector3 size = new Vector3(3f, 2f, 2.4f);     // 상자 크기
    public float knockbackMultiplier = 1f;               // 차지 공격이면 더 크게

    [Header("Lunge (Attack1, Attack2만 사용)")]
    public float lungeStartTime = 0f;                    // 공격 시작 후 전진 시작 시각 (초)
    public float lungeEndTime = 0.15f;                   // 전진 끝 시각 (초)
    public float lungeDistance = 0f;                     // 그 사이 전진 거리 (m). 0이면 전진 안 함
}
