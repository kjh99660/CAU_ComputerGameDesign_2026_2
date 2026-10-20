using UnityEngine;

public class SwordEventRelay : MonoBehaviour, IPlayerPresentation
{
    public SwordFollowBlend sword;
    public void Draw() { sword.Draw(); }
    public void Sheathe() { sword.Sheathe(); }

    public void ApplyState(GameState state)
    {
        sword.SetSimulationActive(state == GameState.Battle || state == GameState.Finishing);
    }

    public void ResetPresentation()
    {
        sword.Sheathe();
        sword.handWeight = 0f;
    }
}
