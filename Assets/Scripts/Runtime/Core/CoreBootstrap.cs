using UnityEngine;

// Scene의 Core 서비스 참조를 연결하고 첫 라운드 초기화를 시작한다.
public sealed class CoreBootstrap : MonoBehaviour
{
    [SerializeField] private EventManager eventManager;
    [SerializeField] private GameStateManager gameStateManager;
    [SerializeField] private InitializationCoordinator initializationCoordinator;
    [SerializeField] private RoundResetCoordinator roundResetCoordinator;

    private bool _configured;

    // 필수 서비스 참조를 검증하고 서로 연결한다.
    private void Awake()
    {
        if (eventManager == null || gameStateManager == null ||
            initializationCoordinator == null || roundResetCoordinator == null)
        {
            Debug.LogError("CoreBootstrap requires all four Core service references.", this);
            return;
        }

        gameStateManager.Configure(eventManager, roundResetCoordinator);
        initializationCoordinator.Configure(eventManager);
        _configured = true;
    }

    // 연결이 끝났다면 초기화 Coordinator를 실행한다.
    private void Start()
    {
        if (_configured && !initializationCoordinator.BeginInitialization(gameStateManager.Tick))
            Debug.LogError("Core initialization could not start.", this);
    }
}
