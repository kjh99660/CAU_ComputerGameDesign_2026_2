using System;
using UnityEngine;

public enum UIRequestType
{
    Start,
    DialogueComplete,
    CountdownComplete,
    Pause,
    Resume,
    Restart,
    QuitToStart,
    BattleComplete,
    FinishingComplete
}

public interface IUIRequestDispatcher
{
    bool IsInputEnabled { get; }
    void SetInputEnabled(bool enabled);
    void Enqueue(UIRequestType requestType);
}

// UI 입력을 현재 라운드 Tick이 포함된 Core Request로 정규화한다.
public sealed class UIRequestDispatcher : IUIRequestDispatcher
{
    private readonly IGameRequestQueue _requests;
    private readonly Func<int> _tickProvider; //과거에 발행된 이벤트가 현재 Tick보다 늦게 처리되는 것을 방지하기 위해 현재 Tick을 제공하는 Func를 사용한다.

    public bool IsInputEnabled { get; private set; } = true;

    public UIRequestDispatcher(IGameRequestQueue requests, GameStateManager stateManager) : this(requests, () => stateManager != null ? stateManager.Tick : 0)
    { 
    }

    public UIRequestDispatcher(IGameRequestQueue requests, Func<int> tickProvider)
    {
        _requests = requests ?? throw new ArgumentNullException(nameof(requests));
        _tickProvider = tickProvider ?? throw new ArgumentNullException(nameof(tickProvider));
    }

    public void SetInputEnabled(bool enabled) => IsInputEnabled = enabled;

    public void Enqueue(UIRequestType requestType)
    {
        if (!IsInputEnabled)
            return;

        int tick = _tickProvider();
        IGameRequest request = requestType switch
        {
            UIRequestType.Start => new StartRequest(tick),
            UIRequestType.DialogueComplete => new DialogueCompleteRequest(tick),
            UIRequestType.CountdownComplete => new CountdownCompleteRequest(tick),
            UIRequestType.Pause => new PauseRequest(tick),
            UIRequestType.Resume => new ResumeRequest(tick),
            UIRequestType.Restart => new RestartRequest(tick),
            UIRequestType.QuitToStart => new QuitToStartRequest(tick),
            UIRequestType.FinishingComplete => new FinishingCompleteRequest(tick),
            UIRequestType.BattleComplete => new TimerReachedZeroRequest(tick),
            _ => throw new ArgumentOutOfRangeException(nameof(requestType), requestType, null)
        };
        Debug.Log($"Enqueueing UI request: {requestType} at tick {tick}");
        _requests.EnqueueRequest(request);
    }
}
