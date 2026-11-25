public sealed class StartPresenter : UiPresenter<StartView>
{
    private readonly BestScoreTracker _best;
    private readonly TextManager _textManager;
    private IApplicationExit _exit;

    public StartPresenter(StartView view, IUIRequestDispatcher requests, IBestScoreStore bestScores = null,
        IApplicationExit exit = null, TextManager textManager = null) : base(view, requests)
    {
        _best = new BestScoreTracker(bestScores ?? new PlayerPrefsBestScoreStore());
        _exit = exit ?? new ApplicationExit();
        _textManager = textManager;
        View.StartClicked += OnStartClicked;
        View.ExitClicked += OnExitClicked;
        View.LanguageClicked += OnLanguageClicked;
        View.RenderLanguage(_textManager != null ? _textManager.Language : null);
    }

    // Tests put a stand-in here so that pressing EXIT does not stop the test run.
    public void SetApplicationExit(IApplicationExit exit) => _exit = exit ?? new ApplicationExit();

    protected override bool IsVisibleIn(GameState state) => state == GameState.Ready;

    // 초기화 완료 전 클릭은 상태 요청 큐에 들어가지 않도록 막는다.
    protected override bool AcceptsInputIn(GameState state) => state == GameState.Ready;

    // 시작 화면이 열릴 때마다 저장된 최고 점수를 다시 읽는다(직전 판의 결과 화면이 새 기록을 저장했을 수 있다).
    protected override void OnStateApplied(GameStateChangedEvent notification)
    {
        if (notification.Current == GameState.Ready)
            View.RenderBest(_best.Load());
    }

    private void OnStartClicked() => Requests.Enqueue(UIRequestType.Start);

    private void OnLanguageClicked()
    {
        if (CurrentState != GameState.Ready || _textManager == null) return;
        _textManager.ToggleLanguage();
        View.RenderLanguage(_textManager.Language);
    }

    // Leaving the game is not a game state and is not queued as a request. Only from the start screen (the button is
    // hidden everywhere else); the pause menu's QUIT still only returns here.
    private void OnExitClicked()
    {
        if (CurrentState == GameState.Ready) _exit.Exit();
    }

    // Keyboard confirm (UiLayer): presses the START button itself, so the click sound and the request are the same.
    public void StartFromInput()
    {
        if (CurrentState == GameState.Ready) View.PressStart();
    }

    public override void Dispose()
    {
        View.StartClicked -= OnStartClicked;
        View.ExitClicked -= OnExitClicked;
        View.LanguageClicked -= OnLanguageClicked;
    }
}
