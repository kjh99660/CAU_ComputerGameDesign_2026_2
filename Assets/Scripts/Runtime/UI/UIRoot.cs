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
    private UiLayer _layer;

    public UiLayer Configure(IGameRequestQueue requests, GameStateManager stateManager)
    {
        AutoBind();
        if (!HasAllViews())
        {
            Debug.LogError("UIRoot requires all eight screen Views.", this);
            return null;
        }
        _layer?.Dispose();
        _layer = new UiLayer(new UIRequestDispatcher(requests, stateManager), startView, dialogueView,
            countdownView, battleView, pauseView, retryConfirmationView, finishingView, resultView);
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
    public void PresentScoreChange(int delta) => _layer?.Battle.PresentScoreChange(delta);
    public void PresentFinishing(FinishingUiModel model) => _layer?.Finishing.Present(model);
    public void PresentResult(ResultUiModel model) => _layer?.Result.Present(model);

    public void StopProducing() => _layer?.SetInputEnabled(false);

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
