public sealed class CountdownPresenter : UiPresenter<CountdownView>
{
    private int _phase;
    private float _remaining;
    private bool _completionRequested;

    public CountdownPresenter(CountdownView view, IUiRequestDispatcher requests) : base(view, requests) { }
    protected override bool IsVisibleIn(GameState state) => state == GameState.Countdown;

    protected override void OnStateApplied(GameStateChangedEvent notification)
    {
        if (notification.Current != GameState.Countdown)
            return;
        _phase = 3;
        _remaining = View.StepDuration;
        _completionRequested = false;
        View.ShowNumber(_phase);
    }

    public override void Update(float deltaTime, float unscaledDeltaTime)
    {
        if (CurrentState != GameState.Countdown || _completionRequested)
            return;
        _remaining -= unscaledDeltaTime;
        if (_remaining > 0f)
            return;

        if (_phase > 1)
        {
            _phase--;
            _remaining += View.StepDuration;
            View.ShowNumber(_phase);
            return;
        }

        if (_phase == 1)
        {
            _phase = 0;
            _remaining += View.StartDuration;
            View.ShowStart();
            return;
        }

        _completionRequested = true;
        Requests.Enqueue(UiRequestType.CountdownComplete);
    }

    public override void Dispose() { }
}
