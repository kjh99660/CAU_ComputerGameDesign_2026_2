// Animation adapters live outside GameDemo.Systems; keep round lifecycle independent of them.
public interface IPlayerPresentation
{
    void ApplyState(GameState state);
    void ResetPresentation();
}

// Animation-event hitboxes replace the provisional runtime overlap attack.
public interface IPlayerAttackHitSource : IPlayerPresentation { }

// Reports that the current action (e.g. attack recovery) may be cancelled by movement or a restart attack.
// Implemented outside GameDemo.Systems (PlayerAttackCancel). Reusable later for dodge/skill cancel windows.
public interface IPlayerCancelGate
{
    bool IsCancelOpen { get; }
}

// [WH] Reports that the player currently ignores enemy hits (e.g. dodge i-frames).
// PlayerRuntime aggregates these; CombatRoundRuntime skips RecordPlayerHit while invulnerable.
public interface IPlayerInvulnerability
{
    bool IsInvulnerable { get; }
}

// [WH] Lets future actions (special, skills) forbid starting a dodge during all or part of their state.
// PlayerDodge checks these before triggering a dodge.
public interface IPlayerDodgeBlocker
{
    bool BlocksDodge { get; }
}
