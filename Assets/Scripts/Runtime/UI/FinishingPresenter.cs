public sealed class FinishingPresenter : UiPresenter<FinishingView>
{
    public FinishingPresenter(FinishingView view, IUIRequestDispatcher requests) : base(view, requests)
    { }

    protected override bool IsVisibleIn(GameState state) => state == GameState.Finishing;

    protected override void OnStateApplied(GameStateChangedEvent notification)
    {
        if (notification.Current != GameState.Finishing)
            return;
        View.PlayTimeUpAnimation();
    }

    public void Present(FinishingUiModel model) => View.Render(model);

    public override void Dispose() { }
}
