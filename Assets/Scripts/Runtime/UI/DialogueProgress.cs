using System;

// 대화 한 묶음의 진행 규칙: 현재 줄, 타이핑으로 드러난 글자 수, 넘김과 완료.
// Unity 오브젝트에 의존하지 않아 EditMode에서 검증한다. 화면 표시는 DialoguePresenter/DialogueView가 맡는다.
public sealed class DialogueProgress
{
    public enum AdvanceResult
    {
        Ignored,      // 이미 완료됨
        RevealedLine, // 타이핑 중이던 줄을 한 번에 다 보여 줌
        NextLine,     // 다음 줄로 넘어감
        Finished      // 마지막 줄에서 넘겨 대화가 끝남(완료 요청은 한 번만)
    }

    private DialogueLine[] _lines = Array.Empty<DialogueLine>();
    private float _revealed;

    public int LineIndex { get; private set; }
    public int LineCount => _lines.Length;
    public bool IsFinished { get; private set; }
    public DialogueLine CurrentLine => _lines.Length == 0 ? default : _lines[LineIndex];
    public int CurrentLength => _lines.Length == 0 ? 0 : _lines[LineIndex].text?.Length ?? 0;
    public int VisibleCharacters => Math.Min(CurrentLength, (int)_revealed);
    public bool IsLineComplete => _revealed >= CurrentLength;

    // 대화 상태에 들어올 때마다 첫 줄, 글자 0개부터 다시 시작한다(Retry 포함).
    public void Begin(DialogueLine[] lines)
    {
        _lines = lines ?? Array.Empty<DialogueLine>();
        LineIndex = 0;
        _revealed = 0f;
        IsFinished = false;
    }

    // 글자를 초당 charactersPerSecond개씩 드러낸다. 0 이하이면 줄 전체를 바로 보여 준다.
    public void Tick(float unscaledDeltaTime, float charactersPerSecond)
    {
        if (IsFinished || IsLineComplete) return;
        if (charactersPerSecond <= 0f)
        {
            _revealed = CurrentLength;
            return;
        }
        _revealed = Math.Min(CurrentLength, _revealed + Math.Max(0f, unscaledDeltaTime) * charactersPerSecond);
    }

    // 진행 입력(키보드·클릭): 타이핑 중이면 줄을 완성하고, 다 보인 줄이면 다음 줄 또는 완료로 넘어간다.
    public AdvanceResult Advance()
    {
        if (IsFinished) return AdvanceResult.Ignored;
        if (!IsLineComplete)
        {
            _revealed = CurrentLength;
            return AdvanceResult.RevealedLine;
        }
        if (LineIndex + 1 < _lines.Length)
        {
            LineIndex++;
            _revealed = 0f;
            return AdvanceResult.NextLine;
        }
        IsFinished = true;
        return AdvanceResult.Finished;
    }

    // Skip은 남은 줄과 관계없이 대화를 끝낸다. 처음 끝낼 때만 true다.
    public bool Skip()
    {
        if (IsFinished) return false;
        IsFinished = true;
        return true;
    }
}
