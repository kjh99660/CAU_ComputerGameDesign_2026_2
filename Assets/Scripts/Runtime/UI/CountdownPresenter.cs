public sealed class CountdownPresenter : UiPresenter<CountdownView>
{
    private int _phase;
    private float _remaining;
    private bool _completionRequested;

    public CountdownPresenter(CountdownView view, IUIRequestDispatcher requests) : base(view, requests) { }
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
        {
            Animate();
            return;
        }

        if (_phase > 1)
        {
            _phase--;
            _remaining += View.StepDuration;
            View.ShowNumber(_phase);
            Animate();
            return;
        }

        if (_phase == 1)
        {
            _phase = 0;
            _remaining += View.StartDuration;
            View.ShowStart();
            Animate();
            return;
        }

        // START 연출을 끝 자세로 둔 채 완료를 요청한다. 화면은 Battle로 바뀔 때 꺼진다.
        View.AnimateStart(View.StartDuration);
        _completionRequested = true;
        Requests.Enqueue(UIRequestType.CountdownComplete);
    }

    // 단계가 시작된 뒤 지난 시간(unscaled)을 View에 넘긴다. 코루틴이 없어 Reset 뒤에 남는 연출이 없다.
    private void Animate()
    {
        if (_phase > 0) View.AnimateNumber(View.StepDuration - _remaining);
        else View.AnimateStart(View.StartDuration - _remaining);
    }

    public override void Dispose() { }
}
