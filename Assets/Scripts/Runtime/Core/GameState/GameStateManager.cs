using UnityEngine;

// 현재 게임 상태와 라운드 Tick을 소유하고 Processor가 승인한 전환을 적용한다.
// Reset 실행은 Coordinator에 맡기고 완료 후 Ready 전환만 수행한다.
public sealed class GameStateManager : MonoBehaviour
{
    public GameState CurrentState { get; private set; } = GameState.Initializing;
    public GameState PreviousState { get; private set; } = GameState.Initializing;
    public int Tick { get; private set; } = 1;
    public bool IsResetting { get; private set; }

    private RoundResetCoordinator _reset;
    private IGameStateChangeHandler _stateChangeHandler;

    // Reset Coordinator와 상태 변경의 명시적인 적용 대상을 연결한다.
    public void Configure(RoundResetCoordinator reset, IGameStateChangeHandler stateChangeHandler)
    {
        _reset = reset;
        _stateChangeHandler = stateChangeHandler;
    }

    // Tick과 Reset 상태를 확인해 현재 요청을 처리할 수 있는지 판정한다.
    internal bool CanProcess(IGameRequest request)
    {
        if (request == null)
            return false;

        if (request.Tick != Tick)
        {
            Reject(request, "stale Tick");
            return false;
        }

        if (IsResetting)
        {
            Reject(request, "reset in progress");
            return false;
        }

        return true;
    }

    // 생산자를 멈추고 Tick을 갱신한 뒤 라운드 Reset을 시작한다.
    internal void BeginReset(IGameRequest request)
    {
        if (_reset == null || !_reset.StopProducers())
        {
            Reject(request, "reset coordinator unavailable");
            return;
        }

        IsResetting = true;
        Tick++;
        if (!_reset.BeginReset(Tick, OnResetCompleted))
        {
            IsResetting = false;
            Debug.LogError("Round reset could not start.", this);
        }
    }

    // 현재 Tick의 Reset 완료 보고를 받아 Ready로 전환한다.
    private void OnResetCompleted(int completedTick)
    {
        if (!IsResetting || completedTick != Tick)
            return;

        IsResetting = false;
        ApplyTransition(GameState.Ready);
    }

    // 상태를 변경하고 성공한 전환을 명시적인 Handler에 즉시 적용한다.
    internal void ApplyTransition(GameState next)
    {
        if (next == CurrentState)
            return;

        if (_stateChangeHandler == null)
        {
            Debug.LogError("GameStateManager requires an IGameStateChangeHandler.", this);
            return;
        }

        PreviousState = CurrentState;
        CurrentState = next;
        _stateChangeHandler.HandleGameStateChanged(new GameStateChangedEvent(PreviousState, CurrentState, Tick));
    }

    // 개발 환경에서 거부된 요청과 이유를 기록한다.
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    internal void Reject(IGameRequest request, string reason)
    {
        Debug.LogWarning($"Frame {Time.frameCount}, Tick {Tick}, State {CurrentState}: {request.GetType().Name} rejected ({reason}).", this);
    }
}
