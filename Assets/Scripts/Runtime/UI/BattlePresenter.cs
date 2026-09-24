public sealed class BattlePresenter : UiPresenter<BattleView>
{
    public BattlePresenter(BattleView view, IUiRequestDispatcher requests) : base(view, requests)
    {
        View.PauseClicked += OnPauseClicked;
        SubscribeToBattleData();
    }

    protected override bool IsVisibleIn(GameState state) => state == GameState.Battle || state == GameState.Paused;
    protected override bool AcceptsInputIn(GameState state) => state == GameState.Battle;

    protected override void OnStateApplied(GameStateChangedEvent notification) =>
        View.SetPauseInteractable(notification.Current == GameState.Battle);

    public void Present(BattleUiModel model) => View.Render(model);
    public void PresentScoreChange(int delta) => View.PlayScoreFeedback(delta);
    private void OnPauseClicked() => Requests.Enqueue(UiRequestType.Pause);

    private void SubscribeToBattleData()
    {
        //TOOD : Score, Timer와 Special Gauge 변경 이벤트를 구독한다.
    }

    public override void Dispose()
    {
        View.PauseClicked -= OnPauseClicked;
        UnsubscribeFromBattleData();
    }

    private void UnsubscribeFromBattleData()
    {
        //TOOD : Score, Timer와 Special Gauge 변경 이벤트 구독을 해제한다.
    }
}
