using UnityEngine;

// Provisional balance values; the team can tune these in the Inspector.
[CreateAssetMenu(menuName = "Game Demo/Combat Settings")]
public sealed class CombatSettings : ScriptableObject
{
    [Min(0)] public int hitScore = 10;
    [Min(0)] public int killScore = 100;
    [Min(0)] public float hitGauge = 3f;
    [Min(0)] public float killGauge = 5f;
    [Min(0)] public float playerHitGauge = 8f;
    [Min(0.01f)] public float maximumGauge = 100f;
    [Range(0f, 1f)] public float playerHitPenalty = 0.1f;
    [Min(0f)] public float invulnerabilitySeconds = 0.8f;
    [Header("Special and combo - provisional")]
    [Range(0f, 1f)] public float specialCostFraction = 1f;
    [Min(0.1f)] public float specialRadius = 6f;
    [Min(0f)] public float specialKnockbackMultiplier = 2f;
    [Header("Special feedback - provisional")]
    [Min(0.05f)] public float specialEffectDuration = 0.55f;
    [Min(0.01f)] public float specialEffectWidth = 0.16f;
    public Color specialEffectColor = new Color(0.15f, 0.9f, 1f, 1f);
    public bool specialKillsRestoreGauge = false;
    [Min(0.1f)] public float comboTimeoutSeconds = 2f;
    [Header("Round")]
    [Min(0.1f)] public float roundDuration = 100f;
    [Min(0.1f)] public float finishingDuration = 2.5f;
    [Header("Attack hit detection - provisional")]
    [Min(0.05f)] public float attackRadius = 1.5f;
    [Min(0f)] public float attackForwardOffset = 1.25f;
    [Min(0f)] public float attackHeight = 1f;
    [Range(0f, 1f)] public float attackActiveStart = 0.2f;
    [Range(0f, 1f)] public float attackActiveEnd = 0.6f;
}
