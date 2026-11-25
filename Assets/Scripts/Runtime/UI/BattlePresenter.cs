using System.Text;

public sealed class BattlePresenter : UiPresenter<BattleView>
{
    private readonly DialogueSequenceData _data;
    private readonly TextManager _textManager;
    private DialogueSequenceData _shownData;
    private readonly BattleRadioSchedule _radio = new BattleRadioSchedule();

    public BattlePresenter(BattleView view, IUIRequestDispatcher requests, DialogueSequenceData data = null,
        TextManager textManager = null) : base(view, requests)
    {
        _data = data;
        _textManager = textManager;
        View.PauseClicked += OnPauseClicked;
        View.ResetView();
        PrepareRadio();
    }

    private void PrepareRadio()
    {
        DialogueSequenceData data = _textManager != null ? _textManager.Sequence : _data;
        _radio.Begin(data != null ? data.battleRadio : null);
        if (_shownData == data) return;
        _shownData = data;
        View.PrepareRadioText(RadioCharacters(data != null ? data.battleRadio : null, data != null ? data.spawnWarnings : null));
    }

    protected override bool IsVisibleIn(GameState state) => state == GameState.Battle || state == GameState.Paused;
    protected override bool AcceptsInputIn(GameState state) => state == GameState.Battle;

    protected override void OnStateApplied(GameStateChangedEvent notification)
    {
        View.SetPauseInteractable(notification.Current == GameState.Battle);
        // 흔들리던 스킬 키는 전투를 벗어나면 제자리로 돌아간다(Pause에서는 HUD 연출이 멈춰 어긋난 채 남는다).
        if (notification.Current != GameState.Battle) View.StopSkillDenied();
        // 새 판(시작 직후, Retry·Quit 뒤의 Ready)마다 무전을 처음부터 다시 센다. Pause와 Resume은 이어서 센다.
        if (notification.Current == GameState.Ready || notification.Current == GameState.IntroDialogue)
            PrepareRadio();
    }

    public void Present(BattleUiModel model) => View.Render(model);
    public void PresentScore(int score) => View.RenderScore(score);
    public void PresentGauge(float normalized) => View.RenderGauge(normalized);
    public void PresentScoreChange(int delta) => View.PlayScoreFeedback(delta);
    public void PresentPlayerHit() => View.PlayPlayerHitFeedback();
    public void PresentCombo(ComboUiModel model) => View.RenderCombo(model);
    public void PresentSkillCooldown(float remainingNormalized) => View.RenderSkillCooldown(remainingNormalized);
    public void PresentSkillCost(float normalized) => View.RenderSkillCost(normalized);

    // 게이지가 모자란 스킬 입력. 전투 중에만 보인다(다른 상태에서는 스킬 입력 자체가 없다).
    public void PresentSkillDenied()
    {
        if (CurrentState == GameState.Battle) View.PlaySkillDenied();
    }

    // 전투 시계. Battle에서만 무전 시각을 확인하므로 Ready·Countdown·Pause·Finishing에는 무전이 뜨지 않는다.
    public void PresentTimer(float remaining)
    {
        View.RenderTimer(remaining);
        if (CurrentState != GameState.Battle) return;
        int line = _radio.Advance(remaining);
        if (line < 0) return;
        BattleRadioLine radio = _radio.Line(line);
        View.ShowRadio(radio.speaker, radio.text, radio.leftSpeaker);
    }

    // 생성 이벤트(대량 생성, 생성 속도 증가) 때 노아의 경고. Battle에서만, 없는 번호면 아무것도 띄우지 않는다.
    // 무전과 같은 자리를 쓰며, 앞의 자막이 떠 있으면 새 경고로 바뀐다.
    public void PresentSpawnWarning(int index)
    {
        if (CurrentState != GameState.Battle) return;
        DialogueLine[] lines = _shownData != null ? _shownData.spawnWarnings : null;
        if (lines == null || index < 0 || index >= lines.Length) return;
        DialogueLine line = lines[index];
        View.ShowRadio(line.speaker, line.text, line.leftSpeaker, true);
    }

    private void OnPauseClicked() => Requests.Enqueue(UIRequestType.Pause);

    public override void Dispose()
    {
        View.PauseClicked -= OnPauseClicked;
    }

    public override void Update(float deltaTime, float unscaledDeltaTime)
    {
        if (CurrentState != GameState.Battle)
            return;
        View.UpdateFeedback(deltaTime);
        View.UpdateHud(unscaledDeltaTime);
    }

    // 무전과 생성 경고에 쓰일 글자 전부(폰트 아틀라스에 미리 넣는다). 경고 채널 글자도 함께.
    private static string RadioCharacters(BattleRadioLine[] lines, DialogueLine[] warnings)
    {
        var characters = new StringBuilder();
        if (lines != null)
            foreach (BattleRadioLine line in lines)
                characters.Append(line.speaker).Append(line.text);
        if (warnings != null && warnings.Length > 0)
        {
            foreach (DialogueLine line in warnings)
                characters.Append(line.speaker).Append(line.text);
            characters.Append("WARNING");
        }
        return characters.ToString();
    }
}
