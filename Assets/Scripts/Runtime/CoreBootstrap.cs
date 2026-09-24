using UnityEngine;

// Unity 갱신을 받아 Core 서비스와 네 계층에 순차적으로 전달한다.
[DefaultExecutionOrder(-1000)]
public sealed class CoreBootstrap : MonoBehaviour
{
    [SerializeField] private EventManager eventManager;
    [SerializeField] private GameStateManager gameStateManager;
    [SerializeField] private InitializationCoordinator initializationCoordinator;
    [SerializeField] private RoundResetCoordinator roundResetCoordinator;
    [SerializeField] private UIRoot uiRoot;

    private bool _configured;
    private PlayerLayer _player;
    private EnemyLayer _enemy;
    private UiLayer _ui;
    private PhysicsLayer _physics;

    // 필수 서비스 참조를 검증하고 서로 연결한다.
    private void Awake()
    {
        ResolveUiRoot();
        if (eventManager == null || gameStateManager == null ||
            initializationCoordinator == null || roundResetCoordinator == null || uiRoot == null)
        {
            Debug.LogError("CoreBootstrap requires all Core service references and UIRoot.", this);
            return;
        }

        gameStateManager.Configure(eventManager, roundResetCoordinator);
        initializationCoordinator.Configure(eventManager);
        initializationCoordinator.RegisterParticipant(uiRoot);
        roundResetCoordinator.RegisterParticipant(uiRoot);
        _player = new PlayerLayer();
        _enemy = new EnemyLayer();
        _ui = uiRoot.Configure(eventManager, gameStateManager);
        if (_ui == null)
        {
            Debug.LogError("CoreBootstrap could not configure the UI layer.", this);
            return;
        }
        _physics = new PhysicsLayer();
        eventManager.StateChanged += OnGameStateChanged;
        OnGameStateChanged(new GameStateChangedEvent(gameStateManager.CurrentState, gameStateManager.CurrentState, gameStateManager.Tick));
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

        eventManager.DrainPreviousFrames();
        float deltaTime = Time.deltaTime;
        float unscaledDeltaTime = Time.unscaledDeltaTime;
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
    private void OnGameStateChanged(GameStateChangedEvent notification)
    {
        _player.OnGameStateChanged(notification);
        _enemy.OnGameStateChanged(notification);
        _ui.OnGameStateChanged(notification);
        _physics.OnGameStateChanged(notification);
    }

    // Bootstrap이 사라질 때 단일 상태 알림 구독을 해제한다.
    private void OnDestroy()
    {
        if (eventManager != null)
            eventManager.StateChanged -= OnGameStateChanged;
        _ui?.Dispose();
    }
}
