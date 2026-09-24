using UnityEngine;

public sealed class StartPresenter : UiPresenter<StartView>
{
    public StartPresenter(StartView view, IUIRequestDispatcher requests) : base(view, requests)
    {
        View.StartClicked += OnStartClicked;
    }

    protected override bool IsVisibleIn(GameState state) => state == GameState.Ready;

    // 초기화 완료 전 클릭은 상태 요청 큐에 들어가지 않도록 막는다.
    protected override bool AcceptsInputIn(GameState state) => state == GameState.Ready;
    private void OnStartClicked()
    {
        Debug.Log("Start button clicked.");
        Requests.Enqueue(UIRequestType.Start);
    }
    public override void Dispose() => View.StartClicked -= OnStartClicked;
}
