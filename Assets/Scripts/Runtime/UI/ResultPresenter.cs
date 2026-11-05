public sealed class ResultPresenter : UiPresenter<ResultView>
{
    public ResultPresenter(ResultView view, IUIRequestDispatcher requests) : base(view, requests)
    {
        View.RetryClicked += OnRetryClicked;
    }

    protected override bool IsVisibleIn(GameState state) => state == GameState.Result;
    public void Present(ResultUiModel model) => View.Render(model);
    private void OnRetryClicked() => Requests.Enqueue(UIRequestType.Restart);

    public override void Dispose()
    {
        View.RetryClicked -= OnRetryClicked;
    }
}
