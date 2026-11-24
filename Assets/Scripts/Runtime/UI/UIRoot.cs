using System.Collections;
using UnityEngine;

// Scene의 UI View를 수집하고 순수 C# Presenter 계층의 생명주기를 소유한다.
public sealed class UIRoot : MonoBehaviour, IRoundInitializable, IRoundResettable
{
    [SerializeField] private StartView startView;
    [SerializeField] private DialogueView dialogueView;
    [SerializeField] private CountdownView countdownView;
    [SerializeField] private BattleView battleView;
    [SerializeField] private PauseView pauseView;
    [SerializeField] private RetryConfirmationView retryConfirmationView;
    [SerializeField] private FinishingView finishingView;
    [SerializeField] private ResultView resultView;
    [SerializeField] private DialogueSequenceData dialogueData;
    private UiLayer _layer;
    private ScreenTransition _transition;

    // 효과음처럼 버튼과 연출을 듣기만 하는 쪽을 위한 읽기 전용 참조다. View는 계속 Presenter가 움직인다.
    public StartView StartScreen => startView;
    public DialogueView DialogueScreen => dialogueView;
    public CountdownView CountdownScreen => countdownView;
    public BattleView BattleScreen => battleView;
    public PauseView PauseScreen => pauseView;
    public RetryConfirmationView RetryConfirmationScreen => retryConfirmationView;
    public ResultView ResultScreen => resultView;

    public UiLayer Configure(IGameRequestQueue requests, GameStateManager stateManager)
    {
        AutoBind();
        if (!HasAllViews())
        {
            Debug.LogError("UIRoot requires all eight screen Views.", this);
            return null;
        }
        _layer?.Dispose();
        if (dialogueData == null) dialogueData = Resources.Load<DialogueSequenceData>("DialogueSequence");
        _layer = new UiLayer(new UIRequestDispatcher(requests, stateManager), startView, dialogueView,
            countdownView, battleView, pauseView, retryConfirmationView, finishingView, resultView, dialogueData);
        // Safe Area 맞춤과 TransitionLayer 페이드. 페이드는 UiLayer가 넘기는 상태 알림과 Reset 시작(StopProducing)을 받는다.
        _transition = UiScreenLayers.Install(this, stateManager);
        _layer.AttachTransition(_transition);
        return _layer;
    }

    public void AutoBind()
    {
        // Inspector에 Prefab 원본이 연결되어 있어도 Presenter는 반드시 현재 Scene의 View를 사용한다.
        startView = FindScreen<StartView>("Start");
        dialogueView = FindScreen<DialogueView>("Dialogue");
        countdownView = FindScreen<CountdownView>("Countdown");
        battleView = FindScreen<BattleView>("Battle");
        pauseView = FindScreen<PauseView>("Pause");
        retryConfirmationView = FindScreen<RetryConfirmationView>("RetryConfirmation");
        finishingView = FindScreen<FinishingView>("Finishing");
        resultView = FindScreen<ResultView>("Result");
    }

    public void PresentDialogue(DialogueUiModel model) => _layer?.Dialogue.Present(model);
    public void PresentBattle(BattleUiModel model) => _layer?.Battle.Present(model);
    public void PresentBattleScore(int score) => _layer?.Battle.PresentScore(score);
    public void PresentBattleGauge(float normalized) => _layer?.Battle.PresentGauge(normalized);
    public void PresentBattleTimer(float remaining) => _layer?.Battle.PresentTimer(remaining);
    public void PresentScoreChange(int delta) => _layer?.Battle.PresentScoreChange(delta);
    public void PresentPlayerHit() => _layer?.Battle.PresentPlayerHit();
    public void PresentFinishing(FinishingUiModel model) => _layer?.Finishing.Present(model);
    public void PresentResult(ResultUiModel model) => _layer?.Result.Present(model);
    // 콤보 표시: 전투 HUD와 종료 화면이 같은 값을 받는다(보이는 화면만 연출한다).
    public void PresentBattleCombo(ComboUiModel model)
    {
        _layer?.Battle.PresentCombo(model);
        _layer?.Finishing.PresentCombo(model);
    }
    // 결과 화면과 같은 통계를 마무리 대화에도 넘긴다. 마무리 대화는 들어올 때 마지막으로 받은 값으로 대사를 고른다.
    public void PresentOutroStats(ResultUiModel model) => _layer?.Dialogue.PresentRoundStats(model);
    // 스킬(Q) 재사용 대기 표시용 자리. 지금 스킬은 대기 시간이 없어 아무도 부르지 않는다(0 = 준비됨, 1 = 막 사용함).
    public void PresentSkillCooldown(float remainingNormalized) => _layer?.Battle.PresentSkillCooldown(remainingNormalized);

    // RoundResetCoordinator가 Reset을 시작할 때 부른다: 입력을 막고, 다음 Ready까지 화면을 검게 덮는다.
    public void StopProducing()
    {
        _layer?.SetInputEnabled(false);
        _transition?.BeginReset();
    }

    public IEnumerator InitializeRound(int tick)
    {
        ResetAllViews();
        _layer?.SetInputEnabled(true);
        yield break;
    }

    public IEnumerator ResetRound(int tick)
    {
        ResetAllViews();
        _layer?.SetInputEnabled(true);
        yield break;
    }

    private void ResetAllViews()
    {
        startView?.ResetView(); dialogueView?.ResetView(); countdownView?.ResetView(); battleView?.ResetView();
        pauseView?.ResetView(); retryConfirmationView?.ResetView(); finishingView?.ResetView(); resultView?.ResetView();
    }

    private T FindScreen<T>(string objectName) where T : Component
    {
        Transform child = transform.Find(objectName);
        return child != null ? child.GetComponent<T>() : null;
    }

    private bool HasAllViews() => startView != null && dialogueView != null && countdownView != null &&
        battleView != null && pauseView != null && retryConfirmationView != null && finishingView != null && resultView != null;

    private void OnDestroy() { _layer?.Dispose(); _layer = null; }
}
