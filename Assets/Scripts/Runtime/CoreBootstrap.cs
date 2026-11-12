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
        _specialFeedback = gameObject.AddComponent<SpecialFeedback>();
        _specialFeedback.Configure(_combat);
        _combatVfx = gameObject.AddComponent<CombatVfxRuntime>();
        _combatVfx.Configure(enemyRuntime, Resources.Load<CombatVfxSettings>("CombatVfxSettings"));
        roundResetCoordinator.RegisterParticipant(_combatVfx);
        playerRuntime.ConfigureCombat(enemyRuntime, _combat.Settings, _combat);
        initializationCoordinator.RegisterParticipant(_combat);
        roundResetCoordinator.RegisterParticipant(_combat);
        roundResetCoordinator.RegisterParticipant(_specialFeedback);
        initializationCoordinator.RegisterParticipant(uiRoot);
        roundResetCoordinator.RegisterParticipant(uiRoot);
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
        _physics.OnGameStateChanged(notification);
    }

    // Bootstrap이 사라질 때 UI Presenter의 구독을 정리한다.
    private void OnDestroy()
    {
        _ui?.Dispose();
    }
}
