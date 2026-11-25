using System;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// 여덟 화면 Presenter의 상태 전달, 갱신, 입력 차단과 데이터 진입점을 소유한다.
public sealed class UiLayer : IGameLoopSystem, IDisposable
{
    private readonly IGameLoopSystem[] _presenters;
    private readonly IDisposable[] _disposables;
    private readonly UIScreenView[] _views;
    private readonly IUIRequestDispatcher _requests;
    private readonly RetryConfirmationPresenter _retry;
    private readonly StartPresenter _start;
    private readonly PausePresenter _pause;
    private bool _disposed;
    private bool _hasState;
    private int _currentTick;
    private GameState _currentState;
    private ScreenTransition _transition;

    public readonly DialoguePresenter Dialogue;
    public readonly BattlePresenter Battle;
    public readonly FinishingPresenter Finishing;
    public readonly ResultPresenter Result;

    public UiLayer(IUIRequestDispatcher requests, StartView startView, DialogueView dialogueView,
        CountdownView countdownView, BattleView battleView, PauseView pauseView,
        RetryConfirmationView retryConfirmationView, FinishingView finishingView, ResultView resultView,
        DialogueSequenceData dialogueData = null, TextManager textManager = null)
    {
        _requests = requests ?? throw new ArgumentNullException(nameof(requests));
        var start = new StartPresenter(startView, requests, textManager: textManager);
        Dialogue = new DialoguePresenter(dialogueView, requests, dialogueData, textManager);
        var countdown = new CountdownPresenter(countdownView, requests);
        Battle = new BattlePresenter(battleView, requests, dialogueData, textManager);
        var pause = new PausePresenter(pauseView, requests);
        var retry = new RetryConfirmationPresenter(retryConfirmationView, requests);
        _retry = retry;
        _start = start;
        _pause = pause;
        Finishing = new FinishingPresenter(finishingView, requests);
        Result = new ResultPresenter(resultView, requests);
        pause.RetryConfirmationRequested += retry.Show;
        // 확인창은 반투명이라 뒤의 메뉴 글자가 비친다. 확인창이 떠 있는 동안에는 메뉴를 숨긴다.
        retry.CoverChanged += pause.SetCoveredByConfirmation;
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
        _transition?.ApplyState(notification);
        // 이전 화면에서 눌린 버튼이 선택된 채로 남지 않게 한다(Enter는 UI의 Submit 키이기도 하다).
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null && eventSystem.currentSelectedGameObject != null)
            eventSystem.SetSelectedGameObject(null);
    }

    // TransitionLayer 페이드도 화면들과 같은 상태 알림을 받는다(UIRoot.Configure가 연결한다).
    public void AttachTransition(ScreenTransition transition) => _transition = transition;

    // 일시정지 메뉴의 설정 칸(마우스 감도, 음량)이 보여 주고 바꾸는 값. 값의 적용은 CoreBootstrap이 한다.
    public void AttachOptions(GameOptions options) => _pause.AttachOptions(options);
    public void SetApplicationExit(IApplicationExit exit) => _start.SetApplicationExit(exit);

    public void Update(float deltaTime, float unscaledDeltaTime)
    {
        // Provisional keyboard binding. UI buttons and keyboard share the same request path.
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.escapeKey.wasPressedThisFrame)
                HandleEscape();
            if (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                HandleConfirm();
        }
        foreach (IGameLoopSystem presenter in _presenters) presenter.Update(deltaTime, unscaledDeltaTime);
    }

    // Space·Enter는 대화의 진행 입력이고, 시작 화면과 결과 화면에서는 그 화면의 하나뿐인 버튼(START, RETRY)을 누른다.
    // 버튼을 직접 누른 것과 같은 경로를 써서 효과음과 요청이 똑같이 나간다. 결과 화면은 숫자가 다 오른 뒤에만 받는다
    // (마무리 대화를 Space로 넘기던 손이 결과를 보기도 전에 넘겨 버리지 않게 한다).
    private void HandleConfirm()
    {
        if (!_hasState || !_requests.IsInputEnabled) return;
        if (_currentState == GameState.IntroDialogue || _currentState == GameState.OutroDialogue)
            Dialogue.AdvanceFromInput();
        else if (_transition != null && _transition.BlocksInput)
            return;   // 페이드가 클릭을 막는 동안에는 키보드도 받지 않는다(입력 우선순위: Transition이 가장 위).
        else if (_currentState == GameState.Ready)
            _start.StartFromInput();
        else if (_currentState == GameState.Result)
            Result.RetryFromInput();
    }

    private void HandleEscape()
    {
        if (!_hasState || !_requests.IsInputEnabled) return;
        if (_currentState == GameState.Battle)
            _requests.Enqueue(UIRequestType.Pause);
        else if (_currentState == GameState.Paused)
        {
            if (_retry.IsOpen) _retry.Hide();
            else _requests.Enqueue(UIRequestType.Resume);
        }
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
