using UnityEngine;

// Unity 갱신을 받아 Core 서비스와 네 계층에 순차적으로 전달한다.
[DefaultExecutionOrder(-1000)]
public sealed class CoreBootstrap : MonoBehaviour, IGameStateChangeHandler
{
    [SerializeField] private EventManager eventManager;
    [SerializeField] private GameStateManager gameStateManager;
    [SerializeField] private InitializationCoordinator initializationCoordinator;
    [SerializeField] private RoundResetCoordinator roundResetCoordinator;
    [SerializeField] private UIRoot uiRoot;
    [SerializeField] private EnemyRuntime enemyRuntime;
    [SerializeField] private PlayerRuntime playerRuntime;
    [SerializeField] private CombatSettings combatSettings;

    private bool _configured;
    private PlayerLayer _player;
    private EnemyLayer _enemy;
    private UiLayer _ui;
    private PhysicsLayer _physics;
    private PhysicsPauseManager _physicsPauseManager;
    private GameRequestProcessor _requestProcessor;
    private CombatRoundRuntime _combat;
    private SpecialFeedback _specialFeedback;
    private CombatVfxRuntime _combatVfx;
    private CombatCameraFeedback _cameraFeedback;
    private CombatImpactFeedback _impactFeedback;
    private EnemyRemainsRuntime _remains;
    private SfxRuntime _sfx;
    private PerfRuntime _perf;
    private bool _spawnWarningsWired;

    // 필수 서비스 참조를 검증하고 서로 연결한다.
    private void Awake()
    {
        ResolveUiRoot();
        ResolveEnemyRuntime();
        ResolvePlayerRuntime();
        if (eventManager == null || gameStateManager == null ||
            initializationCoordinator == null || roundResetCoordinator == null ||
            uiRoot == null || enemyRuntime == null || playerRuntime == null)
        {
            Debug.LogError("CoreBootstrap requires all Core service references, UIRoot, EnemyRuntime and PlayerRuntime.", this);
            return;
        }

        gameStateManager.Configure(roundResetCoordinator, this);
        initializationCoordinator.Configure(eventManager);
        _physicsPauseManager = new PhysicsPauseManager();
        initializationCoordinator.RegisterParticipant(playerRuntime);
        roundResetCoordinator.RegisterParticipant(playerRuntime);
        enemyRuntime.SetTarget(playerRuntime.TargetTransform);
        enemyRuntime.Configure(_physicsPauseManager);
        initializationCoordinator.RegisterParticipant(enemyRuntime);
        roundResetCoordinator.RegisterParticipant(enemyRuntime);
        _player = new PlayerLayer(playerRuntime);
        _enemy = enemyRuntime.CreateLayer();
        _ui = uiRoot.Configure(eventManager, gameStateManager);
        if (_ui == null)
        {
            Debug.LogError("CoreBootstrap could not configure the UI layer.", this);
            return;
        }
        if (combatSettings == null)
            combatSettings = Resources.Load<CombatSettings>("CombatSettings");
        _combat = gameObject.AddComponent<CombatRoundRuntime>();
        _combat.Configure(enemyRuntime, uiRoot, combatSettings, eventManager, playerRuntime);
        var vfxSettings = Resources.Load<CombatVfxSettings>("CombatVfxSettings");
        _specialFeedback = gameObject.AddComponent<SpecialFeedback>();
        _specialFeedback.Configure(_combat, vfxSettings != null ? vfxSettings.groundIndicatorMaterial : null);
        _combatVfx = gameObject.AddComponent<CombatVfxRuntime>();
        _combatVfx.Configure(enemyRuntime, vfxSettings, _combat);
        roundResetCoordinator.RegisterParticipant(_combatVfx);
        // 전투 연출: 공격·적중·특수 공격 때의 카메라 줌 펄스와, 군중 속에서도 보이는 주인공 테두리.
        Animator playerAnimator = playerRuntime.GetComponent<Animator>();
        _cameraFeedback = gameObject.AddComponent<CombatCameraFeedback>();
        _cameraFeedback.Configure(Camera.main, enemyRuntime, _combatVfx, _combat, vfxSettings, playerAnimator);
        roundResetCoordinator.RegisterParticipant(_cameraFeedback);
        // 타격감: 맞는 순간의 히트스톱과 적 번쩍임, 그리고 쓰러진 적의 시체(갉아먹히듯 사라짐)와 부서진 파츠.
        _impactFeedback = gameObject.AddComponent<CombatImpactFeedback>();
        _impactFeedback.Configure(vfxSettings, enemyRuntime, _combat, playerAnimator);
        roundResetCoordinator.RegisterParticipant(_impactFeedback);
        _remains = gameObject.AddComponent<EnemyRemainsRuntime>();
        _remains.Configure(vfxSettings, enemyRuntime, _combat, playerAnimator);
        roundResetCoordinator.RegisterParticipant(_remains);
        if (vfxSettings != null)
            playerRuntime.gameObject.AddComponent<PlayerOutline>().Configure(vfxSettings.playerOutlineShader,
                vfxSettings.playerOutlineColor, vfxSettings.playerOutlinePixels, vfxSettings.playerOutlineThroughOccluders);
        // 효과음: 전투 이벤트, UI 버튼, 상태 변경에 맞춰 소리를 낸다. Pause는 소리를 멈추고 Retry는 끊는다.
        _sfx = gameObject.AddComponent<SfxRuntime>();
        _sfx.Configure(Resources.Load<SfxSettings>("SfxSettings"), enemyRuntime, _combatVfx, _combat, uiRoot, playerAnimator);
        roundResetCoordinator.RegisterParticipant(_sfx);
        playerRuntime.ConfigureCombat(enemyRuntime, _combat.Settings, _combat);
        initializationCoordinator.RegisterParticipant(_combat);
        roundResetCoordinator.RegisterParticipant(_combat);
        roundResetCoordinator.RegisterParticipant(_specialFeedback);
        initializationCoordinator.RegisterParticipant(uiRoot);
        roundResetCoordinator.RegisterParticipant(uiRoot);
        // 성능 도구: F3 오버레이(F4 이웃 검색, F5 실험용 적 상한), 라운드 FPS 기록, -rebootBenchmark 자동 측정.
        _perf = PerfRuntime.Attach(gameObject, gameStateManager, eventManager, enemyRuntime, roundResetCoordinator);
        // 생성 이벤트(25% 소규모 대량 생성, 50% 생성 속도 2배, 75% 대규모 대량 생성)마다 노아의 경고를 HUD에 띄운다.
        enemyRuntime.SpawnPhaseStarted += OnSpawnPhaseStarted;
        _spawnWarningsWired = true;
        _physics = new PhysicsLayer(_physicsPauseManager);
        _requestProcessor = new GameRequestProcessor(gameStateManager);
        HandleGameStateChanged(new GameStateChangedEvent(gameStateManager.CurrentState, gameStateManager.CurrentState, gameStateManager.Tick));
        _configured = true;
    }

    private void ResolveUiRoot()
    {
        if (uiRoot != null)
            return;

        GameObject canvasObject = GameObject.Find("UICanvas");
        if (canvasObject == null)
            return;

        uiRoot = canvasObject.GetComponent<UIRoot>();
        if (uiRoot == null)
            uiRoot = canvasObject.AddComponent<UIRoot>();
    }

    private void ResolveEnemyRuntime()
    {
        if (enemyRuntime != null)
            return;

        enemyRuntime = GetComponent<EnemyRuntime>();
        if (enemyRuntime == null)
            enemyRuntime = gameObject.AddComponent<EnemyRuntime>();
    }

    private void ResolvePlayerRuntime()
    {
        if (playerRuntime == null)
            playerRuntime = FindFirstObjectByType<PlayerRuntime>(FindObjectsInactive.Include);
    }

    // 연결이 끝났다면 초기화 Coordinator를 실행한다.
    private void Start()
    {
        if (_configured && !initializationCoordinator.BeginInitialization(gameStateManager.Tick))
            Debug.LogError("Core initialization could not start.", this);
    }

    // 지난 프레임의 요청을 먼저 처리한 뒤 네 계층을 정해진 순서로 갱신한다.
    private void Update()
    {
        if (!_configured)
            return;

        eventManager.DrainPreviousFrames(_requestProcessor);
        float deltaTime = Time.deltaTime;
        float unscaledDeltaTime = Time.unscaledDeltaTime;
        _combat.Advance(deltaTime, unscaledDeltaTime);
        _player.Update(deltaTime, unscaledDeltaTime);
        _enemy.Update(deltaTime, unscaledDeltaTime);
        _ui.Update(deltaTime, unscaledDeltaTime);
        _physics.Update(deltaTime, unscaledDeltaTime);
    }

    // Unity 물리 주기를 Physics 계층에만 전달한다.
    private void FixedUpdate()
    {
        if (_configured)
            _physics.FixedUpdate(Time.fixedDeltaTime);
    }

    // 성공한 상태 변경을 네 계층과 그 하위 요소에 전달한다.
    public void HandleGameStateChanged(GameStateChangedEvent notification)
    {
        _player.OnGameStateChanged(notification);
        _enemy.OnGameStateChanged(notification);
        _ui.OnGameStateChanged(notification);
        _combat?.ApplyState(notification);
        _specialFeedback?.ApplyState(notification);
        _combatVfx?.ApplyState(notification);
        _cameraFeedback?.ApplyState(notification);
        _impactFeedback?.ApplyState(notification);
        _remains?.ApplyState(notification);
        _sfx?.ApplyState(notification);
        _physics.OnGameStateChanged(notification);
        _perf?.ApplyState(notification);
    }

    // 생성 이벤트 알림: 경고 번호가 있으면 전투 HUD에 노아의 경고를 띄운다(현재 판의 알림만).
    private void OnSpawnPhaseStarted(int phaseIndex, int warningIndex, int tick)
    {
        if (warningIndex >= 0 && tick == gameStateManager.Tick)
            uiRoot.PresentSpawnWarning(warningIndex);
    }

    // Bootstrap이 사라질 때 UI Presenter의 구독과 생성 이벤트 구독을 정리한다.
    private void OnDestroy()
    {
        if (_spawnWarningsWired && enemyRuntime != null)
            enemyRuntime.SpawnPhaseStarted -= OnSpawnPhaseStarted;
        _ui?.Dispose();
    }
}
