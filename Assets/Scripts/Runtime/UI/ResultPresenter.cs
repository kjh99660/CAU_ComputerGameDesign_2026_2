public sealed class ResultPresenter : UiPresenter<ResultView>
{
    private readonly ResultCountUp _countUp = new ResultCountUp();
    private ResultUiModel _model;

    public ResultPresenter(ResultView view, IUIRequestDispatcher requests) : base(view, requests)
    {
        View.RetryClicked += OnRetryClicked;
    }

    protected override bool IsVisibleIn(GameState state) => state == GameState.Result;

    // 결과 화면이 열릴 때마다 0부터 다시 올린다. 코루틴이 없어 Reset 뒤에 남는 연출이 없다.
    protected override void OnStateApplied(GameStateChangedEvent notification)
    {
        if (notification.Current != GameState.Result)
            return;
        _countUp.Begin(_model, View.ScoreCountSeconds, View.StatCountSeconds, View.StatDelaySeconds, View.StatStaggerSeconds);
        View.AnimateRetry(0f, false);
        RenderShown();
    }

    // 통계가 바뀔 때마다 전투 중에도 들어온다. 화면이 보이지 않는 동안에는 값만 기억하고 글자는 건드리지 않는다.
    public void Present(ResultUiModel model)
    {
        _model = model;
        if (CurrentState != GameState.Result)
            return;
        _countUp.Retarget(model);
        RenderShown();
    }

    public override void Update(float deltaTime, float unscaledDeltaTime)
    {
        if (CurrentState != GameState.Result)
            return;
        if (_countUp.Tick(unscaledDeltaTime))
            RenderShown();
        View.AnimateRetry(unscaledDeltaTime, _countUp.IsComplete);
    }

    private void RenderShown()
    {
        for (int field = 0; field < ResultCountUp.FieldCount; field++)
            View.RenderValue(field, _countUp.Shown(field));
    }

    private void OnRetryClicked() => Requests.Enqueue(UIRequestType.Restart);

    public override void Dispose()
    {
        View.RetryClicked -= OnRetryClicked;
    }
}
