public sealed class ResultPresenter : UiPresenter<ResultView>
{
    public ResultPresenter(ResultView view, IUiRequestDispatcher requests) : base(view, requests)
    {
        View.RetryClicked += OnRetryClicked;
        SubscribeToResultData();
    }

    protected override bool IsVisibleIn(GameState state) => state == GameState.Result;
    public void Present(ResultUiModel model) => View.Render(model);
    private void OnRetryClicked() => Requests.Enqueue(UiRequestType.Restart);

    private void SubscribeToResultData()
    {
        //TOOD : 확정된 Round 결과 데이터 공급자를 연결한다.
    }

    public override void Dispose()
    {
        View.RetryClicked -= OnRetryClicked;
        //TOOD : Round 결과 데이터 공급자 이벤트 구독을 해제한다.
    }
}
