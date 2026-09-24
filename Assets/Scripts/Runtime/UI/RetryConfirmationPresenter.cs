public sealed class RetryConfirmationPresenter : UiPresenter<RetryConfirmationView>
{
    private bool _isOpen;

    public RetryConfirmationPresenter(RetryConfirmationView view, IUIRequestDispatcher requests) : base(view, requests)
    {
        View.ConfirmClicked += OnConfirmClicked;
        View.CancelClicked += OnCancelClicked;
        View.SetVisible(false);
    }

    protected override bool IsVisibleIn(GameState state) => _isOpen && state == GameState.Paused;

    protected override void OnStateApplied(GameStateChangedEvent notification)
    {
        if (notification.Current != GameState.Paused)
            Hide();
    }

    public void Show()
    {
        if (CurrentState != GameState.Paused)
            return;
        _isOpen = true;
        View.SetVisible(true);
        View.SetInputEnabled(true);
    }

    public void Hide()
    {
        _isOpen = false;
        View.SetVisible(false);
    }

    private void OnConfirmClicked()
    {
        if (!_isOpen) return;
        Hide();
        Requests.Enqueue(UIRequestType.Restart);
    }

    private void OnCancelClicked() => Hide();

    public override void Dispose()
    {
        View.ConfirmClicked -= OnConfirmClicked;
        View.CancelClicked -= OnCancelClicked;
    }
}
