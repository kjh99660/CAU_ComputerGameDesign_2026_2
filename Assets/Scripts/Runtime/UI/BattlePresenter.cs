using System.Text;

public sealed class BattlePresenter : UiPresenter<BattleView>
{
    private readonly DialogueSequenceData _data;
    private readonly BattleRadioSchedule _radio = new BattleRadioSchedule();

    public BattlePresenter(BattleView view, IUIRequestDispatcher requests, DialogueSequenceData data = null) : base(view, requests)
    {
        _data = data;
        View.PauseClicked += OnPauseClicked;
        View.ResetView();
        _radio.Begin(RadioLines);
        View.PrepareRadioText(RadioCharacters(RadioLines));
    }

    private BattleRadioLine[] RadioLines => _data != null ? _data.battleRadio : null;

    protected override bool IsVisibleIn(GameState state) => state == GameState.Battle || state == GameState.Paused;
    protected override bool AcceptsInputIn(GameState state) => state == GameState.Battle;

    protected override void OnStateApplied(GameStateChangedEvent notification)
    {
        View.SetPauseInteractable(notification.Current == GameState.Battle);
        // 새 판(시작 직후, Retry·Quit 뒤의 Ready)마다 무전을 처음부터 다시 센다. Pause와 Resume은 이어서 센다.
        if (notification.Current == GameState.Ready)
            _radio.Begin(RadioLines);
    }

    public void Present(BattleUiModel model) => View.Render(model);
    public void PresentScore(int score) => View.RenderScore(score);
    public void PresentGauge(float normalized) => View.RenderGauge(normalized);
    public void PresentScoreChange(int delta) => View.PlayScoreFeedback(delta);
    public void PresentPlayerHit() => View.PlayPlayerHitFeedback();
    public void PresentCombo(ComboUiModel model) => View.RenderCombo(model);
    public void PresentSkillCooldown(float remainingNormalized) => View.RenderSkillCooldown(remainingNormalized);

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

    private static string RadioCharacters(BattleRadioLine[] lines)
    {
        if (lines == null || lines.Length == 0) return string.Empty;
        var characters = new StringBuilder();
        foreach (BattleRadioLine line in lines)
            characters.Append(line.speaker).Append(line.text);
        return characters.ToString();
    }
}
