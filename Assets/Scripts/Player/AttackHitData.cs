using UnityEngine;

[CreateAssetMenu(menuName = "Combat/Attack Hit Data")]
public class AttackHitData : ScriptableObject
{
    public enum HitShape { Box, Sector }
    public enum SectorGrowth
    {
        Radius,           // 1.   각도 고정, 차지하면 반지름 증가
        AngleFromEdge,    // 2-1. 반지름 고정, 한쪽 끝은 고정하고 반대쪽으로 각도 증가
        AngleFromCenter   // 2-2. 반지름 고정, 정면을 중심으로 양쪽으로 각도 증가
    }

    [Header("Shape")]
    public HitShape shape = HitShape.Box;                // Box: 상자 / Sector: 부채꼴

    [Header("Box (shape = Box), 높이는 Sector도 사용")]
    public Vector3 center = new Vector3(0f, 1f, 1.2f);   // 플레이어 기준 위치 (앞쪽 = +z). Sector는 y(높이)만 사용
    public Vector3 size = new Vector3(3f, 2f, 2.4f);     // 상자 크기. Sector는 y(높이)만 사용
    public float knockbackMultiplier = 1f;               // 차지 공격이면 더 크게

    [Header("Sector (shape = Sector), 플레이어 위치가 부채꼴 중심")]
    public SectorGrowth sectorGrowth = SectorGrowth.Radius;
    public float sectorRadius = 3f;                      // 기본 반지름 (m). Radius 방식이면 여기에 차지 배율을 곱함
    public float sectorAngle = 270f;                     // 기본 각도 (°). Angle 방식이면 여기에 차지 배율을 곱함 (최대 360)
    public float sectorMaxAngle = 270f;                  // AngleFromEdge: 최대로 찼을 때 각도. 고정되는 끝 위치를 정함
    public bool sectorReverse = false;                   // AngleFromEdge: 켜면 오른쪽 끝 고정, 왼쪽으로 증가

    [Header("Lunge (Attack1, Attack2만 사용)")]
    public float lungeStartTime = 0f;                    // 공격 시작 후 전진 시작 시각 (초)
    public float lungeEndTime = 0.15f;                   // 전진 끝 시각 (초)
    public float lungeDistance = 0f;                     // 그 사이 전진 거리 (m). 0이면 전진 안 함

    public enum ChargeMode { None, Linear, Steps }

    [System.Serializable]
    public struct ChargeStep
    {
        public float time;    // 이 시간(초) 이상 차지하면
        public float scale;   // 이 배율
    }

    [Header("Charge (ChargeAttack만 사용)")]
    public ChargeMode chargeMode = ChargeMode.None;      // None: 범위 고정 / Linear: 선형 증가 / Steps: 단계 증가
    public float chargeMaxTime = 1.5f;                   // Linear: 이 시간만큼 차지하면 최대 배율
    public float chargeMaxScale = 2f;                    // Linear: 최대 배율 (기본은 1배)
    public ChargeStep[] chargeSteps;                     // Steps: 시간이 짧은 단계부터 순서대로
    public bool chargeScalesKnockback = false;           // 켜면 넉백도 같은 배율로 커짐

    // 차지 시간(초) → 범위 배율. 판정, Gizmo, 바닥 예상 범위 표시가 모두 이 값을 쓴다.
    public float GetChargeScale(float chargeTime)
    {
        switch (chargeMode)
        {
            case ChargeMode.Linear:
                if (chargeMaxTime <= 0f) return chargeMaxScale;
                return Mathf.Lerp(1f, chargeMaxScale, chargeTime / chargeMaxTime);   // Lerp는 0~1로 제한됨

            case ChargeMode.Steps:
                float scale = 1f;
                if (chargeSteps != null)
                    foreach (ChargeStep step in chargeSteps)
                        if (chargeTime >= step.time) scale = step.scale;
                return scale;

            default:
                return 1f;
        }
    }

    // 최대 배율 (바닥 표시 색 계산용)
    public float GetMaxChargeScale()
    {
        switch (chargeMode)
        {
            case ChargeMode.Linear:
                return chargeMaxScale;
            case ChargeMode.Steps:
                float max = 1f;
                if (chargeSteps != null)
                    foreach (ChargeStep step in chargeSteps)
                        max = Mathf.Max(max, step.scale);
                return max;
            default:
                return 1f;
        }
    }

    // Box: 배율을 적용한 상자 (플레이어 기준 로컬). 가로(x)와 앞뒤(z)만 키운다.
    public void GetScaledBox(float s, out Vector3 localCenter, out Vector3 boxSize)
    {
        localCenter = new Vector3(center.x * s, center.y, center.z * s);
        boxSize = new Vector3(size.x * s, size.y, size.z * s);
    }

    // Sector: 배율을 적용한 부채꼴. 각도는 정면 = 0°, 오른쪽 = +, 왼쪽 = - (위에서 봤을 때 시계방향이 +)
    public void GetSector(float s, out float radius, out float startAngle, out float endAngle)
    {
        radius = sectorRadius;
        float angle = sectorAngle;

        if (sectorGrowth == SectorGrowth.Radius) radius = sectorRadius * s;
        else angle = sectorAngle * s;
        angle = Mathf.Clamp(angle, 0f, 360f);

        if (sectorGrowth == SectorGrowth.AngleFromEdge)
        {
            float half = Mathf.Clamp(sectorMaxAngle, 0f, 360f) * 0.5f;   // 다 찼을 때 좌우 대칭이 되도록
            if (!sectorReverse) { startAngle = -half; endAngle = -half + angle; }   // 왼쪽 끝 고정 → 오른쪽으로
            else { endAngle = half; startAngle = half - angle; }                 // 오른쪽 끝 고정 → 왼쪽으로
        }
        else
        {
            startAngle = -angle * 0.5f;
            endAngle = angle * 0.5f;
        }
    }
}
