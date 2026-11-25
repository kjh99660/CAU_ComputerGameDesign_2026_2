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
    [Min(0.1f)] public float specialRadius = 8f;
    [Min(0f)] public float specialKnockbackMultiplier = 2f;
    // The skill (E, the dash along a line) shares the special's gauge. It needs and spends skillCostFraction of the
    // full gauge for one use (0 = free). A blow of the skill charges the gauge by skillGaugeGainFraction of the usual
    // hitGauge and killGauge: 0, like the special's own kills (specialKillsRestoreGauge), so that neither pays for
    // itself; only normal attacks and being hit fill the gauge.
    [Header("Skill (E) gauge - provisional")]
    [Range(0f, 1f)] public float skillCostFraction = 0.5f;
    [Range(0f, 1f)] public float skillGaugeGainFraction = 0f;
    [Header("Special feedback - provisional")]
    [Min(0.05f)] public float specialEffectDuration = 0.55f;
    [Min(0.01f)] public float specialEffectWidth = 0.16f;
    public Color specialEffectColor = new Color(0.15f, 0.9f, 1f, 1f);
    public bool specialKillsRestoreGauge = false;
    [Min(0.1f)] public float comboTimeoutSeconds = 2f;
    // Combo score bonus (AGENTS.md 9: configurable while no value is fixed). OFF while comboScoreStep is 0, which keeps
    // every kill at hitScore + killScore. When on, the kill that makes combo n scores
    // floor((hitScore + killScore) * (1 + min(comboScoreMaxBonus, comboScoreStep * (n - 1)))). See RoundCombatStats.
    [Header("Combo score bonus - provisional, off by default")]
    [Min(0f)] public float comboScoreStep = 0f;
    [Min(0f)] public float comboScoreMaxBonus = 1f;
    // Whether the special's own kills are combo kills (provisional; true is how the combo has counted them so far).
    // True: using the special ends the running combo and its kills start a new one at once, so they show on the combo
    // HUD, count toward Max Combo and, while the bonus is on, each scores with the multiplier of the combo it makes
    // (20 special kills at step 0.25 and cap 1 score 4124 instead of 2200). False: each scores hitScore + killScore,
    // starts or extends no combo and is left out of Max Combo; the combo HUD fades when the special is used.
    [Tooltip("True: the special's kills start a new combo (combo HUD, Max Combo and, when it is on, the combo score bonus). " +
             "False: they score the base value and stay out of the combo.")]
    public bool comboCountsSpecialKills = true;
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
