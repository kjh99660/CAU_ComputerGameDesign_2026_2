using UnityEngine;
using UnityEngine.InputSystem;

// Attack1, Attack2, Charge_Windup이 시작되는 순간 공격 방향을 다시 정한다.
//  - WASD 입력이 있으면: 카메라 기준 입력 방향
//  - 입력이 없으면: 캐릭터가 보던 방향 유지
// 전진(PlayerAttackLunge)과 히트박스(PlayerAttackHitbox)는 캐릭터 정면 기준이라 자동으로 새 방향을 따른다.
public class PlayerAttackAim : MonoBehaviour, IPlayerPresentation
{
    [SerializeField] private InputActionAsset inputActions;     // PlayerRuntime과 같은 에셋 (Main 씬용)
    [SerializeField] private string actionMapName = "Player";
    [SerializeField] private string moveActionName = "Move";
    [SerializeField] private Transform cameraTransform;         // 비우면 Main Camera 사용
    [SerializeField, Min(0f)] private float turnSpeed = 0f;     // 초당 회전 각도. 0이면 즉시 회전

    private Animator _animator;
    private InputActionMap _map;
    private InputAction _move;
    private int _aimedState;          // 이미 방향을 정한 공격 상태 (0이면 없음)
    private bool _turning;            // turnSpeed > 0일 때 회전 중인지
    private Quaternion _targetRotation;
    private bool _allowed = true;     // 팀원 구조가 없는 씬(CharacterTest)에서는 항상 허용
    private bool _useKeyboard;        // PlayerRuntime이 꺼진 씬(CharacterTest)이면 키보드를 직접 읽음

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        if (inputActions != null)
        {
            _map = inputActions.FindActionMap(actionMapName, false);
            _move = _map?.FindAction(moveActionName, false);
        }
    }

    private void Start()
    {
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
        _useKeyboard = !(TryGetComponent(out PlayerRuntime runtime) && runtime.enabled);
    }

    private void Update()
    {
        if (_animator == null || !_allowed) return;

        // 콤보 전환이 시작되는 순간(다음 상태)을 우선으로 본다.
        AnimatorStateInfo info = _animator.GetCurrentAnimatorStateInfo(0);
        if (_animator.IsInTransition(0))
        {
            AnimatorStateInfo next = _animator.GetNextAnimatorStateInfo(0);
            if (IsAimState(next)) info = next;
        }

        if (!IsAimState(info))
        {
            _aimedState = 0;
            _turning = false;
            return;
        }

        // 새 공격이 막 시작됨 → 방향 다시 정하기
        if (info.fullPathHash != _aimedState)
        {
            _aimedState = info.fullPathHash;
            if (TryGetInputDirection(out Vector3 dir))
            {
                _targetRotation = Quaternion.LookRotation(dir);
                if (turnSpeed <= 0f) transform.rotation = _targetRotation;
                else _turning = true;
            }
            else
            {
                _turning = false;   // 입력 없음 → 보던 방향 유지
            }
        }

        if (_turning)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, _targetRotation, turnSpeed * Time.deltaTime);
            if (Quaternion.Angle(transform.rotation, _targetRotation) < 0.5f) _turning = false;
        }
    }

    // 카메라 기준 WASD 방향 (수평). 입력이 없으면 false.
    private bool TryGetInputDirection(out Vector3 direction)
    {
        direction = Vector3.zero;
        Vector2 input = ReadMoveInput();
        if (input.sqrMagnitude < 0.01f || cameraTransform == null) return false;

        Vector3 forward = cameraTransform.forward;
        Vector3 right = cameraTransform.right;
        forward.y = 0f;
        right.y = 0f;
        direction = forward.normalized * input.y + right.normalized * input.x;
        if (direction.sqrMagnitude < 0.0001f) return false;
        direction.Normalize();
        return true;
    }

    private Vector2 ReadMoveInput()
    {
        // CharacterTest 씬: 팀원 입력 구조가 없으므로 키보드를 직접 읽는다
        if (_useKeyboard)
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return Vector2.zero;
            float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            float y = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
        }

        // Main 씬: PlayerRuntime이 켜 둔 입력 액션을 같이 읽는다 (Battle이 아니면 꺼져 있어 0)
        if (_map != null && _map.enabled && _move != null)
            return _move.ReadValue<Vector2>();
        return Vector2.zero;
    }

    private static bool IsAimState(AnimatorStateInfo info) =>
        info.IsName("Attack1") || info.IsName("Attack2") || info.IsName("Charge_Windup");

    // ── 팀원 구조가 보내 주는 알림 (PlayerRuntime이 자동으로 찾아서 호출) ──
    public void ApplyState(GameState state) =>
        _allowed = state == GameState.Battle || state == GameState.Finishing;

    public void ResetPresentation()
    {
        _aimedState = 0;
        _turning = false;
    }
}
