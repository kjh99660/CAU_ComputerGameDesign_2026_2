public sealed class StartPresenter : UiPresenter<StartView>
{
    public StartPresenter(StartView view, IUiRequestDispatcher requests) : base(view, requests)
    {
        View.StartClicked += OnStartClicked;
    }

    // Awake에서 Initializing 상태를 전달받는 즉시 Start 화면을 표시한다.
    protected override bool IsVisibleIn(GameState state) =>
        state == GameState.Initializing || state == GameState.Ready;

    // 초기화 완료 전 클릭은 상태 요청 큐에 들어가지 않도록 막는다.
    protected override bool AcceptsInputIn(GameState state) => state == GameState.Ready;
    private void OnStartClicked() => Requests.Enqueue(UiRequestType.Start);
    public override void Dispose() => View.StartClicked -= OnStartClicked;
}
