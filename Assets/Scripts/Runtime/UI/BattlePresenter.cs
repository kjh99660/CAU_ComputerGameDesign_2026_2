using UnityEngine;

public sealed class BattlePresenter : UiPresenter<BattleView>
{
    public BattlePresenter(BattleView view, IUIRequestDispatcher requests) : base(view, requests)
    {
        View.PauseClicked += OnPauseClicked;
        SubscribeToBattleData();
        View.ResetView();
    }

    protected override bool IsVisibleIn(GameState state) => state == GameState.Battle || state == GameState.Paused;
    protected override bool AcceptsInputIn(GameState state) => state == GameState.Battle;

    protected override void OnStateApplied(GameStateChangedEvent notification) =>
        View.SetPauseInteractable(notification.Current == GameState.Battle);

    public void Present(BattleUiModel model) => View.Render(model);
    public void PresentScoreChange(int delta) => View.PlayScoreFeedback(delta);
    private void OnPauseClicked() => Requests.Enqueue(UIRequestType.Pause);

    private void SubscribeToBattleData()
    {
        //TOOD : Score, Timer와 Special Gauge 변경 이벤트를 구독한다.
    }

    public override void Dispose()
    {
        View.PauseClicked -= OnPauseClicked;
        UnsubscribeFromBattleData();
    }

    public override void Update(float deltaTime, float unscaledDeltaTime)
    {
        if (IsVisibleIn(CurrentState) == false || CurrentState != GameState.Battle)
        {
            return;
        }
        float time = Mathf.Max(0f, View.GetRemainingSeconds());
        time -= deltaTime;
        View.RenderTimer(time);
        if(time < 0.01f)
        {
            Requests.Enqueue(UIRequestType.BattleComplete);
        }
    }

    private void UnsubscribeFromBattleData()
    {
        //TOOD : Score, Timer와 Special Gauge 변경 이벤트 구독을 해제한다.
    }
}
