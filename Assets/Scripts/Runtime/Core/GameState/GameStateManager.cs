using UnityEngine;

// 현재 게임 상태와 라운드 Tick을 소유하고 요청에 따라 전환한다.
// Reset 실행은 Coordinator에 맡기고 완료 후 Ready 전환만 수행한다.
public sealed class GameStateManager : MonoBehaviour
{
    public GameState CurrentState { get; private set; } = GameState.Initializing;
    public GameState PreviousState { get; private set; } = GameState.Initializing;
    public int Tick { get; private set; } = 1;
    public bool IsResetting { get; private set; }

    private IGameEventBus _events;
    private RoundResetCoordinator _reset;
    private bool _subscribed;

    // 이벤트 버스와 Reset Coordinator를 연결하고 요청 구독을 시작한다.
    public void Configure(IGameEventBus events, RoundResetCoordinator reset)
    {
        Unsubscribe();
        _events = events;
        _reset = reset;
        Subscribe();
    }

    // 컴포넌트가 활성화될 때 요청 이벤트를 구독한다.
    private void OnEnable()
    {
        Subscribe();
    }

    // 컴포넌트가 비활성화될 때 요청 이벤트 구독을 해제한다.
    private void OnDisable()
    {
        Unsubscribe();
    }

    // 중복 등록 없이 요청 이벤트를 구독한다.
    private void Subscribe()
    {
        if (_subscribed || _events == null)
            return;
        _events.RequestDequeued += HandleRequest;
        _subscribed = true;
    }

    // 등록된 요청 이벤트 구독을 해제한다.
    private void Unsubscribe()
    {
        if (!_subscribed || _events == null)
            return;
        _events.RequestDequeued -= HandleRequest;
        _subscribed = false;
    }

    // Tick과 현재 상태를 확인해 요청을 처리하거나 거부한다.
    private void HandleRequest(IGameRequest request)
    {
        if (request == null)
            return;

        if (request.Tick != Tick)
        {
            Reject(request, "stale Tick");
            return;
        }

        if (IsResetting)
        {
            Reject(request, "reset in progress");
            return;
        }

        if (GameStateRules.IsResetRequest(CurrentState, request))
        {
            StartReset(request);
            return;
        }

        if (!GameStateRules.TryGetNext(CurrentState, request, out GameState next))
        {
            Reject(request, "request not allowed in current state");
            return;
        }

        TransitionTo(next);
    }

    // 생산자를 멈추고 Tick을 갱신한 뒤 라운드 Reset을 시작한다.
    private void StartReset(IGameRequest request)
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
        TransitionTo(GameState.Ready);
    }

    // 상태를 변경하고 성공한 전환을 즉시 알린다.
    private void TransitionTo(GameState next)
    {
        if (next == CurrentState)
            return;

        PreviousState = CurrentState;
        CurrentState = next;
        _events.PublishStateChanged(new GameStateChangedEvent(PreviousState, CurrentState, Tick));
    }

    // 개발 환경에서 거부된 요청과 이유를 기록한다.
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void Reject(IGameRequest request, string reason)
    {
        Debug.LogWarning($"Frame {Time.frameCount}, Tick {Tick}, State {CurrentState}: {request.GetType().Name} rejected ({reason}).", this);
    }
}
