using UnityEngine;

[CreateAssetMenu(menuName = "Combat/Attack Hit Data")]
public class AttackHitData : ScriptableObject
{
    public Vector3 center = new Vector3(0f, 1f, 1.2f);   // 플레이어 기준 위치 (앞쪽 = +z)
    public Vector3 size = new Vector3(3f, 2f, 2.4f);     // 상자 크기
    public float knockbackMultiplier = 1f;               // 차지 공격이면 더 크게
}