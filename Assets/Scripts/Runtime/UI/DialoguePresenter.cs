public sealed class DialoguePresenter : UiPresenter<DialogueView>
{
    public DialoguePresenter(DialogueView view, IUIRequestDispatcher requests) : base(view, requests)
    {
        View.SkipClicked += OnDialogueComplete;
        View.ContinueClicked += OnDialogueComplete;
        SubscribeToDialogueData();
    }

    protected override bool IsVisibleIn(GameState state) =>
        state == GameState.IntroDialogue || state == GameState.OutroDialogue;

    public void Present(DialogueUiModel model) => View.Render(model);
    private void OnDialogueComplete() => Requests.Enqueue(UIRequestType.DialogueComplete);

    private void SubscribeToDialogueData()
    {
        //TOOD : Intro/Outro Dialogue 데이터 공급자와 현재 대사 변경 이벤트를 구독한다.
    }

    public override void Dispose()
    {
        View.SkipClicked -= OnDialogueComplete;
        View.ContinueClicked -= OnDialogueComplete;
        UnsubscribeFromDialogueData();
    }

    private void UnsubscribeFromDialogueData()
    {
        //TOOD : Dialogue 데이터 공급자 이벤트 구독을 해제한다.
    }
}
