using System;

// 여덟 화면 Presenter의 상태 전달, 갱신, 입력 차단과 데이터 진입점을 소유한다.
public sealed class UiLayer : IGameLoopSystem, IDisposable
{
    private readonly IGameLoopSystem[] _presenters;
    private readonly IDisposable[] _disposables;
    private readonly UIScreenView[] _views;
    private readonly IUIRequestDispatcher _requests;
    private bool _disposed;
    private bool _hasState;
    private int _currentTick;
    private GameState _currentState;

    public readonly DialoguePresenter Dialogue;
    public readonly BattlePresenter Battle;
    public readonly FinishingPresenter Finishing;
    public readonly ResultPresenter Result;

    public UiLayer(IUIRequestDispatcher requests, StartView startView, DialogueView dialogueView,
        CountdownView countdownView, BattleView battleView, PauseView pauseView,
        RetryConfirmationView retryConfirmationView, FinishingView finishingView, ResultView resultView)
    {
        _requests = requests ?? throw new ArgumentNullException(nameof(requests));
        var start = new StartPresenter(startView, requests);
        Dialogue = new DialoguePresenter(dialogueView, requests);
        var countdown = new CountdownPresenter(countdownView, requests);
        Battle = new BattlePresenter(battleView, requests);
        var pause = new PausePresenter(pauseView, requests);
        var retry = new RetryConfirmationPresenter(retryConfirmationView, requests);
        Finishing = new FinishingPresenter(finishingView, requests);
        Result = new ResultPresenter(resultView, requests);
        pause.RetryConfirmationRequested += retry.Show;
        _views = new UIScreenView[] { startView, dialogueView, countdownView, battleView,
            pauseView, retryConfirmationView, finishingView, resultView };
        _presenters = new IGameLoopSystem[] { start, Dialogue, countdown, Battle, pause, retry, Finishing, Result };
        _disposables = new IDisposable[] { start, Dialogue, countdown, Battle, pause, retry, Finishing, Result };
    }

    public void OnGameStateChanged(GameStateChangedEvent notification)
    {
        if (notification == null || (_hasState && notification.Tick < _currentTick))
            return;
        if (_hasState && notification.Tick == _currentTick && notification.Current == _currentState)
            return;

        _hasState = true;
        _currentTick = notification.Tick;
        _currentState = notification.Current;

        // 이전 화면 상태가 남지 않도록 모두 끈 뒤 현재 상태의 Presenter만 필요한 화면을 다시 켠다.
        foreach (UIScreenView view in _views)
        {
            view.SetInputEnabled(false);
            view.SetVisible(false);
        }

        foreach (IGameLoopSystem presenter in _presenters) presenter.OnGameStateChanged(notification);
    }

    public void Update(float deltaTime, float unscaledDeltaTime)
    {
        foreach (IGameLoopSystem presenter in _presenters) presenter.Update(deltaTime, unscaledDeltaTime);
    }

    public void SetInputEnabled(bool enabled) => _requests.SetInputEnabled(enabled);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (IDisposable presenter in _disposables) presenter.Dispose();
    }
}
