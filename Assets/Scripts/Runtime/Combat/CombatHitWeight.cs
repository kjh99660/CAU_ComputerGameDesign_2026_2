using UnityEngine;

// How heavy the blow that just landed is. Feedback (hit stop, shake, kick, breaking apart) scales with it.
public enum CombatHitWeight
{
    Light,
    Medium,
    Heavy,
    Special
}

public static class CombatHitWeights
{
    private static readonly int Attack2 = Animator.StringToHash("Attack2");
    private static readonly int ChargeWindup = Animator.StringToHash("Charge_Windup");
    private static readonly int ChargeHold = Animator.StringToHash("Charge_Hold");
    private static readonly int ChargeRelease = Animator.StringToHash("Charge_Release");
    private static readonly int SkillWindup = Animator.StringToHash("Skill1_Windup");
    private static readonly int SkillDash = Animator.StringToHash("Skill1_Dash");
    private static readonly int SkillMain = Animator.StringToHash("Skill1_Main");

    // The weight of the player's blow, read from the animator state the player is in or blending into:
    // the first hit is light, the second medium, the charged attack and the skill heavy.
    public static CombatHitWeight FromPlayer(Animator player)
    {
        if (player == null || !player.isActiveAndEnabled || !player.isInitialized) return CombatHitWeight.Light;
        AnimatorStateInfo state = player.IsInTransition(0) ? player.GetNextAnimatorStateInfo(0) : player.GetCurrentAnimatorStateInfo(0);
        int name = state.shortNameHash;
        if (name == Attack2) return CombatHitWeight.Medium;
        if (name == ChargeRelease || name == ChargeWindup || name == ChargeHold ||
            name == SkillWindup || name == SkillDash || name == SkillMain)
            return CombatHitWeight.Heavy;
        return CombatHitWeight.Light;
    }
}

// Real time for feedback that must not slow down with a hit stop (the stop itself, the camera shake). When frames are
// captured at a fixed rate (tests, benchmarks, video capture) the real clock races ahead of the frames, so the fixed
// capture step is used instead and a 0.05 s stop still lasts three frames at 60 fps.
public static class FeedbackTime
{
    public static float RealDelta(float unscaledDeltaTime) =>
        Time.captureDeltaTime > 0f ? Time.captureDeltaTime : unscaledDeltaTime;
}

// Notices the special attack's own hits. The special spends the gauge before it hits anyone, so every hit that
// arrives in the frame the special was used belongs to the special.
public sealed class SpecialHitWindow
{
    private readonly RoundCombatStats _stats;
    private int _uses;
    private int _frame = -1;

    public SpecialHitWindow(RoundCombatStats stats)
    {
        _stats = stats;
        if (_stats != null)
        {
            _uses = _stats.SpecialUseCount;
            _stats.Changed += OnChanged;
        }
    }

    public bool IsSpecialHit => _frame == Time.frameCount;

    public void Dispose()
    {
        if (_stats != null) _stats.Changed -= OnChanged;
    }

    private void OnChanged()
    {
        if (_stats.SpecialUseCount > _uses) _frame = Time.frameCount;
        _uses = _stats.SpecialUseCount;
    }
}
