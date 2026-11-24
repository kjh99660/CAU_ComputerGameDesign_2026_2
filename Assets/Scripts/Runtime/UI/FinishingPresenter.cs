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
    public void PresentCombo(ComboUiModel model) => View.RenderCombo(model);

    // 종료 연출은 슬로모션이므로 콤보 연출은 unscaled time으로 진행한다(프레임 고정 캡처에서는 FeedbackTime이 맞춘다).
    public override void Update(float deltaTime, float unscaledDeltaTime)
    {
        if (CurrentState != GameState.Finishing)
            return;
        View.AnimateCombo(FeedbackTime.RealDelta(unscaledDeltaTime));
    }

    public override void Dispose() { }
}
