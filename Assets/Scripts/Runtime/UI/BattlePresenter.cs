public sealed class BattlePresenter : UiPresenter<BattleView>
{
    public BattlePresenter(BattleView view, IUIRequestDispatcher requests) : base(view, requests)
    {
        View.PauseClicked += OnPauseClicked;
        View.ResetView();
    }

    protected override bool IsVisibleIn(GameState state) => state == GameState.Battle || state == GameState.Paused;
    protected override bool AcceptsInputIn(GameState state) => state == GameState.Battle;

    protected override void OnStateApplied(GameStateChangedEvent notification) =>
        View.SetPauseInteractable(notification.Current == GameState.Battle);

    public void Present(BattleUiModel model) => View.Render(model);
    public void PresentScore(int score) => View.RenderScore(score);
    public void PresentGauge(float normalized) => View.RenderGauge(normalized);
    public void PresentTimer(float remaining) => View.RenderTimer(remaining);
    public void PresentScoreChange(int delta) => View.PlayScoreFeedback(delta);
    private void OnPauseClicked() => Requests.Enqueue(UIRequestType.Pause);

    public override void Dispose()
    {
        View.PauseClicked -= OnPauseClicked;
    }

}
