using System;

public enum UiRequestType
{
    Start,
    DialogueComplete,
    CountdownComplete,
    Pause,
    Resume,
    Restart,
    QuitToStart,
    FinishingComplete
}

public interface IUiRequestDispatcher
{
    bool IsInputEnabled { get; }
    void SetInputEnabled(bool enabled);
    void Enqueue(UiRequestType requestType);
}

// UI 입력을 현재 라운드 Tick이 포함된 Core Request로 정규화한다.
public sealed class UiRequestDispatcher : IUiRequestDispatcher
{
    private readonly IGameEventBus _events;
    private readonly Func<int> _tickProvider;

    public bool IsInputEnabled { get; private set; } = true;

    public UiRequestDispatcher(IGameEventBus events, GameStateManager stateManager)
        : this(events, () => stateManager != null ? stateManager.Tick : 0) { }

    public UiRequestDispatcher(IGameEventBus events, Func<int> tickProvider)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _tickProvider = tickProvider ?? throw new ArgumentNullException(nameof(tickProvider));
    }

    public void SetInputEnabled(bool enabled) => IsInputEnabled = enabled;

    public void Enqueue(UiRequestType requestType)
    {
        if (!IsInputEnabled)
            return;

        int tick = _tickProvider();
        IGameRequest request = requestType switch
        {
            UiRequestType.Start => new StartRequest(tick),
            UiRequestType.DialogueComplete => new DialogueCompleteRequest(tick),
            UiRequestType.CountdownComplete => new CountdownCompleteRequest(tick),
            UiRequestType.Pause => new PauseRequest(tick),
            UiRequestType.Resume => new ResumeRequest(tick),
            UiRequestType.Restart => new RestartRequest(tick),
            UiRequestType.QuitToStart => new QuitToStartRequest(tick),
            UiRequestType.FinishingComplete => new FinishingCompleteRequest(tick),
            _ => throw new ArgumentOutOfRangeException(nameof(requestType), requestType, null)
        };
        _events.EnqueueRequest(request);
    }
}
