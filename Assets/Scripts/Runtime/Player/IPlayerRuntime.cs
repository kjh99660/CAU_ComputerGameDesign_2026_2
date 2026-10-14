using UnityEngine;

// Player 계층과 Unity Player 오브젝트 사이의 최소 계약이다.
public interface IPlayerRuntime
{
    Transform TargetTransform { get; }
    void ApplyState(GameState state, int tick);
    void CaptureInput();
    void UpdateAction(float deltaTime);
}
