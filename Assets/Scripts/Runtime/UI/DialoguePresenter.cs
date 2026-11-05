public sealed class DialoguePresenter : UiPresenter<DialogueView>
{
    private readonly DialogueSequenceData _data;
    private DialogueLine[] _lines;
    private int _index;
    private bool _completionRequested;

    public DialoguePresenter(DialogueView view, IUIRequestDispatcher requests, DialogueSequenceData data = null) : base(view, requests)
    {
        _data = data;
        View.SkipClicked += Complete;
        View.ContinueClicked += Continue;
    }

    protected override bool IsVisibleIn(GameState state) =>
        state == GameState.IntroDialogue || state == GameState.OutroDialogue;

    public void Present(DialogueUiModel model) => View.Render(model);

    protected override void OnStateApplied(GameStateChangedEvent notification)
    {
        if (!IsVisibleIn(notification.Current)) return;
        _lines = _data == null ? null : notification.Current == GameState.IntroDialogue ? _data.intro : _data.outro;
        _index = 0;
        _completionRequested = false;
        ShowCurrent();
    }

    private void ShowCurrent()
    {
        if (_lines == null || _lines.Length == 0)
        {
            string placeholder = CurrentState == GameState.IntroDialogue ? "도입 대사를 입력해 주세요." : "종료 대사를 입력해 주세요.";
            View.Render(new DialogueUiModel("임시 대사", placeholder, true));
            return;
        }
        DialogueLine line = _lines[_index];
        View.Render(new DialogueUiModel(line.speaker, line.text, line.leftSpeaker));
    }

    private void Continue()
    {
        if (!IsVisibleIn(CurrentState) || _completionRequested) return;
        if (_lines != null && _index + 1 < _lines.Length) { _index++; ShowCurrent(); }
        else Complete();
    }

    private void Complete()
    {
        if (!IsVisibleIn(CurrentState) || _completionRequested) return;
        _completionRequested = true;
        Requests.Enqueue(UIRequestType.DialogueComplete);
    }

    public override void Dispose()
    {
        View.SkipClicked -= Complete;
        View.ContinueClicked -= Continue;
    }
}
