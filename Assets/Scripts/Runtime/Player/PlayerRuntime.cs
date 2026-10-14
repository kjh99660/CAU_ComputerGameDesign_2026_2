using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

// Player Prefab의 입력, 이동, 카메라와 현재 Animator 정책을 연결하는 Unity Adapter다.
[DisallowMultipleComponent]
public sealed class PlayerRuntime : MonoBehaviour, IPlayerRuntime, IRoundInitializable, IRoundResettable
{
    private static readonly int IsRunningHash = Animator.StringToHash("isRunning");
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int IsHoldingHash = Animator.StringToHash("IsHolding");

    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private CharacterController characterController;
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private InputActionAsset inputActions;

    [Header("Input Actions")]
    [SerializeField] private string actionMapName = "Player";
    [SerializeField] private string moveActionName = "Move";
    [SerializeField] private string lookActionName = "Look";
    [SerializeField] private string attackActionName = "Attack";

    [Header("Movement")]
    [SerializeField, Min(0f)] private float moveSpeed = 10f;
    [SerializeField, Min(0f)] private float rotationSpeed = 720f;
    [SerializeField, Min(0f)] private float acceleration = 20f;

    [Header("Camera")]
    [SerializeField, Min(0.1f)] private float cameraDistance = 4f;
    [SerializeField] private float cameraHeightOffset = 1.5f;
    [SerializeField, Min(0f)] private float lookSensitivity = 0.2f;
    [SerializeField] private float minimumPitch = -20f;
    [SerializeField] private float maximumPitch = 60f;

    [Header("Animator Attack Policy")]
    [SerializeField, Range(0f, 1f)] private float comboWindowStart = 0.3f;
    [SerializeField, Range(0f, 1f)] private float comboWindowEnd = 0.75f;

    private InputActionMap _actionMap;
    private InputAction _moveAction;
    private InputAction _lookAction;
    private InputAction _attackAction;
    private Vector2 _moveInput;
    private Vector2 _lookInput;
    private bool _attackPressed;
    private bool _attackHeld;
    private bool _attackReleased;
    private float _currentSpeed;
    private Vector3 _lastMoveDirection = Vector3.forward;
    private float _cameraYaw;
    private float _cameraPitch = 20f;
    private int _tick;
    private bool _battleActive;
    private bool _stopRequested;
    private bool _referencesCached;

    public Transform TargetTransform => transform;

    private void Awake()
    {
        CacheReferences();
        ResolveActions();
        DisableInput();
    }

    public void ConfigureSpawnPoint(Transform value) => spawnPoint = value;

    public IEnumerator InitializeRound(int tick)
    {
        CacheReferences();
        ResolveActions();
        ResetPlayer(tick);
        yield break;
    }

    public void StopProducing()
    {
        _stopRequested = true;
        DisableInput();
    }

    public IEnumerator ResetRound(int tick)
    {
        ResetPlayer(tick);
        yield break;
    }

    public void ApplyState(GameState state, int tick)
    {
        if (tick < _tick)
            return;

        _tick = tick;
        _battleActive = state == GameState.Battle;
        if (_battleActive && !_stopRequested)
            EnableInput();
        else
            DisableInput();

        if (!_battleActive)
        {
            _currentSpeed = 0f;
            SetRunning(false);
        }

        bool cursorLocked = _battleActive;
        Cursor.lockState = cursorLocked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !cursorLocked;
    }

    public void CaptureInput()
    {
        if (!_battleActive || _stopRequested || _actionMap == null || !_actionMap.enabled)
        {
            ClearFrameInput();
            return;
        }

        _moveInput = _moveAction?.ReadValue<Vector2>() ?? Vector2.zero;
        _lookInput = _lookAction?.ReadValue<Vector2>() ?? Vector2.zero;
        _attackPressed = _attackAction?.WasPressedThisFrame() ?? false;
        _attackHeld = _attackAction?.IsPressed() ?? false;
        _attackReleased = _attackAction?.WasReleasedThisFrame() ?? false;
    }

    public void UpdateAction(float deltaTime)
    {
        if (animator == null || characterController == null)
            return;

        UpdateCamera();
        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        bool attackState = IsAttackState(stateInfo);

        if (_battleActive && !_stopRequested)
        {
            UpdateAttack(stateInfo);
            UpdateMovement(deltaTime, !attackState);
        }
        else
        {
            _currentSpeed = 0f;
            SetRunning(false);
        }

        // Finishing 진입 등 입력이 닫힌 경우 차지 상태를 계속 붙잡지 않는다.
        if (!_battleActive && (stateInfo.IsName("Charge_Windup") || stateInfo.IsName("Charge_Hold")))
            animator.SetBool(IsHoldingHash, false);

        ClearFrameInput();
    }

    private void UpdateMovement(float deltaTime, bool canMove)
    {
        Vector2 input = Vector2.ClampMagnitude(_moveInput, 1f);
        bool hasInput = input.sqrMagnitude > 0.01f;
        if (!canMove)
        {
            _currentSpeed = 0f;
            SetRunning(false);
            return;
        }

        float targetSpeed = hasInput ? moveSpeed : 0f;
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, acceleration * deltaTime);
        SetRunning(hasInput);
        if (_currentSpeed <= 0.01f)
            return;

        if (hasInput)
        {
            Transform movementCamera = cameraTransform != null ? cameraTransform : transform;
            Vector3 forward = movementCamera.forward;
            Vector3 right = movementCamera.right;
            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();
            Vector3 direction = forward * input.y + right * input.x;
            if (direction.sqrMagnitude > 0.0001f)
            {
                _lastMoveDirection = direction.normalized;
                Quaternion rotation = Quaternion.LookRotation(_lastMoveDirection);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, rotation, rotationSpeed * deltaTime);
            }
        }

        characterController.Move(_lastMoveDirection * (_currentSpeed * deltaTime));
    }

    // Animator에 실제 존재하는 Attack1 -> Attack2 -> Charge 상태 전이만 구동한다.
    private void UpdateAttack(AnimatorStateInfo stateInfo)
    {
        float normalizedTime = stateInfo.normalizedTime % 1f;
        if (_attackPressed)
        {
            if (stateInfo.IsName("Idle") || stateInfo.IsTag("RunMotion"))
            {
                animator.SetTrigger(AttackHash);
            }
            else if ((stateInfo.IsName("Attack1") || stateInfo.IsName("Attack2")) &&
                     normalizedTime >= comboWindowStart && normalizedTime <= comboWindowEnd)
            {
                animator.SetTrigger(AttackHash);
            }
        }

        if (stateInfo.IsName("Charge_Windup") || stateInfo.IsName("Charge_Hold"))
            animator.SetBool(IsHoldingHash, _attackHeld && !_attackReleased);
        else if (stateInfo.IsName("Idle") || stateInfo.IsName("Run") || stateInfo.IsTag("RunMotion"))
            animator.SetBool(IsHoldingHash, false);
    }

    private void UpdateCamera()
    {
        if (cameraTransform == null)
            return;

        if (_battleActive && !_stopRequested)
        {
            _cameraYaw += _lookInput.x * lookSensitivity;
            _cameraPitch = Mathf.Clamp(_cameraPitch - _lookInput.y * lookSensitivity, minimumPitch, maximumPitch);
        }

        Quaternion rotation = Quaternion.Euler(_cameraPitch, _cameraYaw, 0f);
        Vector3 target = transform.position + Vector3.up * cameraHeightOffset;
        cameraTransform.position = target - rotation * Vector3.forward * cameraDistance;
        cameraTransform.rotation = Quaternion.LookRotation(target - cameraTransform.position, Vector3.up);
    }

    private void ResetPlayer(int tick)
    {
        _tick = tick;
        _stopRequested = false;
        _battleActive = false;
        DisableInput();
        _currentSpeed = 0f;
        _lastMoveDirection = Vector3.forward;

        if (spawnPoint == null)
            throw new InvalidOperationException("PlayerRuntime requires a PlayerSpawnPoint.");

        bool wasEnabled = characterController != null && characterController.enabled;
        if (characterController != null)
            characterController.enabled = false;
        transform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);
        if (characterController != null)
            characterController.enabled = wasEnabled;

        if (animator != null)
        {
            animator.ResetTrigger(AttackHash);
            animator.SetBool(IsHoldingHash, false);
            animator.SetBool(IsRunningHash, false);
            animator.Rebind();
            animator.Update(0f);
        }

        _cameraYaw = spawnPoint.eulerAngles.y;
        _cameraPitch = 20f;
        UpdateCamera();
    }

    private void CacheReferences()
    {
        if (_referencesCached)
            return;
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);
        if (characterController == null)
            characterController = GetComponent<CharacterController>();
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
        if (animator == null || characterController == null)
            throw new InvalidOperationException("PlayerRuntime requires an Animator and CharacterController.");
        _referencesCached = true;
    }

    private void ResolveActions()
    {
        if (_actionMap != null)
            return;
        if (inputActions == null)
            throw new InvalidOperationException("PlayerRuntime requires an InputActionAsset.");

        _actionMap = inputActions.FindActionMap(actionMapName, true);
        _moveAction = _actionMap.FindAction(moveActionName, true);
        _lookAction = _actionMap.FindAction(lookActionName, true);
        _attackAction = _actionMap.FindAction(attackActionName, true);
    }

    private void EnableInput() => _actionMap?.Enable();

    private void DisableInput()
    {
        _actionMap?.Disable();
        ClearFrameInput();
    }

    private void ClearFrameInput()
    {
        _moveInput = Vector2.zero;
        _lookInput = Vector2.zero;
        _attackPressed = false;
        _attackHeld = false;
        _attackReleased = false;
    }

    private void SetRunning(bool value)
    {
        if (animator != null)
            animator.SetBool(IsRunningHash, value);
    }

    private static bool IsAttackState(AnimatorStateInfo stateInfo) =>
        stateInfo.IsName("Attack1") || stateInfo.IsName("Attack2") ||
        stateInfo.IsName("Charge_Windup") || stateInfo.IsName("Charge_Hold") ||
        stateInfo.IsName("Charge_Release");
}
