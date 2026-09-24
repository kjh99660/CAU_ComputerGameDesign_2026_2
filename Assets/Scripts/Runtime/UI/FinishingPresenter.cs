public sealed class FinishingPresenter : UiPresenter<FinishingView>
{
    private float _remaining;
    private bool _completionRequested;

    public FinishingPresenter(FinishingView view, IUiRequestDispatcher requests) : base(view, requests)
    {
        SubscribeToFinishingScore();
    }

    protected override bool IsVisibleIn(GameState state) => state == GameState.Finishing;

    protected override void OnStateApplied(GameStateChangedEvent notification)
    {
        if (notification.Current != GameState.Finishing)
            return;
        _remaining = View.PresentationDuration;
        _completionRequested = false;
        View.PlayTimeUpAnimation();
        ApplyFinishingTimePolicy();
    }

    public override void Update(float deltaTime, float unscaledDeltaTime)
    {
        if (CurrentState != GameState.Finishing || _completionRequested)
            return;
        _remaining -= unscaledDeltaTime;
        if (_remaining > 0f)
            return;
        _completionRequested = true;
        Requests.Enqueue(UiRequestType.FinishingComplete);
    }

    public void Present(FinishingUiModel model) => View.Render(model);

    private void ApplyFinishingTimePolicy()
    {
        //TOOD : Finishing 전용 시스템이 timeScale 0.5 적용과 모든 이탈 경로의 1.0 복구를 담당하도록 연결한다.
    }

    private void SubscribeToFinishingScore()
    {
        //TOOD : Finishing Score 수용 창의 Score 변경 이벤트를 구독한다.
    }

    public override void Dispose()
    {
        //TOOD : Finishing Score 변경 이벤트 구독을 해제한다.
    }
}
