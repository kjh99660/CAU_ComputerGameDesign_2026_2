// 도입·마무리 대화의 진행을 맡는다. 대사는 unscaled time으로 한 글자씩 드러나고,
// 진행 입력은 타이핑 중이면 그 줄을 완성하고, 다 보인 줄이면 다음 줄로 넘긴다.
// 마지막 줄을 넘기거나 Skip을 누르면 같은 DialogueCompleteRequest를 한 번만 보낸다.
public sealed class DialoguePresenter : UiPresenter<DialogueView>
{
    private readonly DialogueSequenceData _data;
    private readonly DialogueProgress _progress = new DialogueProgress();
    private bool _completionRequested;
    private int _shownCharacters = -1;
    private float _inputLockRemaining;

    public DialoguePresenter(DialogueView view, IUIRequestDispatcher requests, DialogueSequenceData data = null) : base(view, requests)
    {
        _data = data;
        View.SkipClicked += Skip;
        View.ContinueClicked += Advance;
    }

    protected override bool IsVisibleIn(GameState state) =>
        state == GameState.IntroDialogue || state == GameState.OutroDialogue;

    // 진행 상태와 관계없이 한 줄을 바로 전부 보여 준다.
    public void Present(DialogueUiModel model)
    {
        View.Render(model);
        View.SetVisibleCharacters(int.MaxValue);
    }

    protected override void OnStateApplied(GameStateChangedEvent notification)
    {
        if (!IsVisibleIn(notification.Current)) return;
        _progress.Begin(_data == null ? null : notification.Current == GameState.IntroDialogue ? _data.intro : _data.outro);
        _completionRequested = false;
        _inputLockRemaining = View.InputGraceSeconds;
        View.BeginSequence();
        ShowCurrentLine();
    }

    public override void Update(float deltaTime, float unscaledDeltaTime)
    {
        if (!IsVisibleIn(CurrentState)) return;
        View.Animate(unscaledDeltaTime);
        if (_completionRequested) return;
        if (_inputLockRemaining > 0f) _inputLockRemaining -= unscaledDeltaTime;
        _progress.Tick(unscaledDeltaTime, View.CharactersPerSecond);
        RefreshVisibleCharacters();
        RefreshNextIndicator(unscaledDeltaTime);
    }

    // 키보드(Space·Enter)와 화면 클릭이 함께 쓰는 진행 입력.
    public void AdvanceFromInput() => Advance();

    private void Advance()
    {
        if (!IsVisibleIn(CurrentState) || _completionRequested || _inputLockRemaining > 0f) return;
        switch (_progress.Advance())
        {
            case DialogueProgress.AdvanceResult.RevealedLine:
                RefreshVisibleCharacters();
                RefreshNextIndicator(0f);
                break;
            case DialogueProgress.AdvanceResult.NextLine:
                ShowCurrentLine();
                break;
            case DialogueProgress.AdvanceResult.Finished:
                RequestCompletion();
                break;
        }
    }

    private void Skip()
    {
        if (!IsVisibleIn(CurrentState) || _completionRequested) return;
        _progress.Skip();
        RequestCompletion();
    }

    private void RequestCompletion()
    {
        _completionRequested = true;
        Requests.Enqueue(UIRequestType.DialogueComplete);
    }

    private void ShowCurrentLine()
    {
        _shownCharacters = -1;
        if (_progress.LineCount == 0)
        {
            string placeholder = CurrentState == GameState.IntroDialogue ? "도입 대사를 입력해 주세요." : "종료 대사를 입력해 주세요.";
            View.Render(new DialogueUiModel("임시 대사", placeholder, true));
            View.SetVisibleCharacters(int.MaxValue);
            RefreshNextIndicator(0f);
            return;
        }
        DialogueLine line = _progress.CurrentLine;
        View.Render(new DialogueUiModel(line.speaker, line.text, line.leftSpeaker));
        RefreshVisibleCharacters();
        RefreshNextIndicator(0f);
    }

    // 넘김 표시는 줄이 다 보였고 대화가 아직 끝나지 않았을 때만 보인다.
    private void RefreshNextIndicator(float unscaledDeltaTime) =>
        View.UpdateNextIndicator(_progress.IsLineComplete && !_progress.IsFinished, unscaledDeltaTime);

    // 보이는 글자 수가 바뀐 프레임에만 TMP에 반영한다. 문자열은 그대로 두므로 매 프레임 할당이 없다.
    private void RefreshVisibleCharacters()
    {
        if (_progress.LineCount == 0) return;
        int visible = _progress.VisibleCharacters;
        if (visible == _shownCharacters) return;
        _shownCharacters = visible;
        View.SetVisibleCharacters(visible);
    }

    public override void Dispose()
    {
        View.SkipClicked -= Skip;
        View.ContinueClicked -= Advance;
    }
}
