using System;

public abstract class UiPresenter<TView> : IGameLoopNode, IDisposable where TView : UIScreenView
{
    protected readonly TView View;
    protected readonly IUiRequestDispatcher Requests;
    protected GameState CurrentState { get; private set; } = GameState.Initializing;
    protected int CurrentTick { get; private set; }
    private bool _hasState;

    protected UiPresenter(TView view, IUiRequestDispatcher requests)
    {
        View = view ?? throw new ArgumentNullException(nameof(view));
        Requests = requests ?? throw new ArgumentNullException(nameof(requests));
    }

    public void OnGameStateChanged(GameStateChangedEvent notification)
    {
        if (notification == null || (_hasState && notification.Tick < CurrentTick))
            return;
        if (_hasState && notification.Tick == CurrentTick && notification.Current == CurrentState)
            return;

        CurrentState = notification.Current;
        CurrentTick = notification.Tick;
        _hasState = true;
        bool visible = IsVisibleIn(CurrentState);
        View.SetVisible(visible);
        View.SetInputEnabled(visible && AcceptsInputIn(CurrentState));
        OnStateApplied(notification);
    }

    public virtual void Update(float deltaTime, float unscaledDeltaTime) { }
    protected abstract bool IsVisibleIn(GameState state);
    protected virtual bool AcceptsInputIn(GameState state) => IsVisibleIn(state);
    protected virtual void OnStateApplied(GameStateChangedEvent notification) { }
    public abstract void Dispose();
}
