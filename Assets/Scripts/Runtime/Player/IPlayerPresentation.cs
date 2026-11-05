// Animation adapters live outside GameDemo.Systems; keep round lifecycle independent of them.
public interface IPlayerPresentation
{
    void ApplyState(GameState state);
    void ResetPresentation();
}

// Animation-event hitboxes replace the provisional runtime overlap attack.
public interface IPlayerAttackHitSource : IPlayerPresentation { }
