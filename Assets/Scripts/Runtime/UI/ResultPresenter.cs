public sealed class ResultPresenter : UiPresenter<ResultView>
{
    private readonly ResultCountUp _countUp = new ResultCountUp();
    private readonly BestScoreTracker _best;
    private ResultUiModel _model;
    private BestScoreResult _bestResult;
    private bool _bestRevealed;

    public ResultPresenter(ResultView view, IUIRequestDispatcher requests, IBestScoreStore bestScores = null) : base(view, requests)
    {
        _best = new BestScoreTracker(bestScores ?? new PlayerPrefsBestScoreStore());
        View.RetryClicked += OnRetryClicked;
    }

    protected override bool IsVisibleIn(GameState state) => state == GameState.Result;

    // 결과 화면이 열릴 때마다 0부터 다시 올린다. 코루틴이 없어 Reset 뒤에 남는 연출이 없다.
    // 최고 점수는 이때 한 번 견주어 저장한다. 점수가 다 오를 때까지는 이전 최고 점수를 보여 주고,
    // 다 오르면 새 최고 점수와 NEW BEST 표시를 드러낸다.
    protected override void OnStateApplied(GameStateChangedEvent notification)
    {
        if (notification.Current != GameState.Result)
            return;
        _countUp.Begin(_model, View.ScoreCountSeconds, View.StatCountSeconds, View.StatDelaySeconds, View.StatStaggerSeconds);
        _bestResult = _best.Submit(_model.FinalScore);
        _bestRevealed = false;
        View.RenderBest(_bestResult.PreviousBest, false);
        View.AnimateRetry(0f, false);
        RenderShown();
        RevealBestWhenScoreIsUp();
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
        RevealBestWhenScoreIsUp();
        View.AnimateRetry(unscaledDeltaTime, _countUp.IsComplete);
        View.AnimateBest(unscaledDeltaTime);
    }

    private void RevealBestWhenScoreIsUp()
    {
        if (_bestRevealed || _countUp.Shown(ResultCountUp.Score) != _model.FinalScore)
            return;
        _bestRevealed = true;
        View.RenderBest(_bestResult.Best, _bestResult.IsNewBest);
    }

    private void RenderShown()
    {
        for (int field = 0; field < ResultCountUp.FieldCount; field++)
            View.RenderValue(field, _countUp.Shown(field));
    }

    private void OnRetryClicked() => Requests.Enqueue(UIRequestType.Restart);

    // Keyboard confirm (UiLayer). Only once every number has finished counting up, so a key that was skipping the
    // outro cannot throw the result away before it has been seen. Presses the RETRY button itself.
    public void RetryFromInput()
    {
        if (CurrentState == GameState.Result && _countUp.IsComplete) View.PressRetry();
    }

    public override void Dispose()
    {
        View.RetryClicked -= OnRetryClicked;
    }
}
