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
