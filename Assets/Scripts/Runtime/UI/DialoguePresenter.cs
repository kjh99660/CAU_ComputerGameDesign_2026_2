// 도입·마무리 대화의 진행을 맡는다. 대사는 unscaled time으로 한 글자씩 드러나고,
// 진행 입력은 타이핑 중이면 그 줄을 완성하고, 다 보인 줄이면 다음 줄로 넘긴다.
// 마지막 줄을 넘기거나 Skip을 누르면 같은 DialogueCompleteRequest를 한 번만 보낸다.
public sealed class DialoguePresenter : UiPresenter<DialogueView>
{
    private readonly DialogueSequenceData _data;
    private readonly TextManager _textManager;
    private readonly DialogueProgress _progress = new DialogueProgress();
    private bool _completionRequested;
    private int _shownCharacters = -1;
    private float _inputLockRemaining;
    private ResultUiModel _roundStats;

    public DialoguePresenter(DialogueView view, IUIRequestDispatcher requests, DialogueSequenceData data = null,
        TextManager textManager = null) : base(view, requests)
    {
        _data = data;
        _textManager = textManager;
        View.SkipClicked += Skip;
        View.ContinueClicked += Advance;
    }

    // 마지막으로 들어온 마무리 대화가 고른 대사 묶음(성과에 반응하는 마무리, 05_STORY_AND_SCRIPT 4.4).
    public OutroVariant ChosenOutro { get; private set; }

    protected override bool IsVisibleIn(GameState state) =>
        state == GameState.IntroDialogue || state == GameState.OutroDialogue;

    // 진행 상태와 관계없이 한 줄을 바로 전부 보여 준다.
    public void Present(DialogueUiModel model)
    {
        View.Render(model);
        View.SetVisibleCharacters(int.MaxValue);
    }

    // 결과 화면과 같은 통계를 통계가 바뀔 때마다 받아 둔다. 상태 변경 알림은 UI가 전투보다 먼저 받으므로
    // 마무리 대화에 들어오는 순간 값을 새로 묻지 않고, 받아 둔 값으로 고른다. 통계는 종료 연출이 끝날 때 닫혀서
    // 마무리 대화에 들어오기 전에 확정돼 있다.
    public void PresentRoundStats(ResultUiModel stats) => _roundStats = stats;

    protected override void OnStateApplied(GameStateChangedEvent notification)
    {
        // 새 판(Ready)은 이전 판의 통계로 마무리를 고르지 않는다. 새 판의 통계는 다시 들어온다.
        if (notification.Current == GameState.Ready) _roundStats = default;
        if (!IsVisibleIn(notification.Current)) return;
        DialogueLine[] lines = null;
        DialogueSequenceData data = _textManager != null ? _textManager.Sequence : _data;
        if (data != null)
        {
            if (notification.Current == GameState.IntroDialogue)
                lines = data.intro;
            else
            {
                ChosenOutro = data.ChooseOutro(_roundStats.HitCount, _roundStats.KillCount);
                lines = data.OutroLines(ChosenOutro);
            }
        }
        _progress.Begin(lines);
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
            bool english = _textManager != null && _textManager.Language == DialogueLanguage.English;
            bool intro = CurrentState == GameState.IntroDialogue;
            string placeholder = english
                ? (intro ? "No intro dialogue configured." : "No outro dialogue configured.")
                : (intro ? "도입 대사를 입력해 주세요." : "종료 대사를 입력해 주세요.");
            View.Render(new DialogueUiModel(english ? "Placeholder" : "임시 대사", placeholder, true));
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
