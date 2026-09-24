using System;

// 여덟 화면 Presenter의 상태 전달, 갱신, 입력 차단과 데이터 진입점을 소유한다.
public sealed class UiLayer : IGameLoopNode, IDisposable
{
    private readonly IGameLoopNode[] _presenters;
    private readonly IDisposable[] _disposables;
    private readonly IUiRequestDispatcher _requests;
    private bool _disposed;

    public readonly DialoguePresenter Dialogue;
    public readonly BattlePresenter Battle;
    public readonly FinishingPresenter Finishing;
    public readonly ResultPresenter Result;

    public UiLayer(IUiRequestDispatcher requests, StartView startView, DialogueView dialogueView,
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
        _presenters = new IGameLoopNode[] { start, Dialogue, countdown, Battle, pause, retry, Finishing, Result };
        _disposables = new IDisposable[] { start, Dialogue, countdown, Battle, pause, retry, Finishing, Result };
    }

    public void OnGameStateChanged(GameStateChangedEvent notification)
    {
        foreach (IGameLoopNode presenter in _presenters) presenter.OnGameStateChanged(notification);
    }

    public void Update(float deltaTime, float unscaledDeltaTime)
    {
        foreach (IGameLoopNode presenter in _presenters) presenter.Update(deltaTime, unscaledDeltaTime);
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
