using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Player Prefab의 입력, 이동, 카메라와 현재 Animator 정책을 연결하는 Unity Adapter다.
[DisallowMultipleComponent]
public sealed class PlayerRuntime : MonoBehaviour, IPlayerRuntime, IRoundInitializable, IRoundResettable
{
    private static readonly int IsRunningHash = Animator.StringToHash("isRunning");
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int IsHoldingHash = Animator.StringToHash("IsHolding");
    private static readonly int AttackRestartHash = Animator.StringToHash("AttackRestart");   // [WH] 후딜 캔슬 → Attack1 재시작

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
    [SerializeField] private string specialActionName = "Interact"; // Provisional existing binding: E.

    [Header("Movement")]
    [SerializeField, Min(0f)] private float moveSpeed = 10f;
    [SerializeField, Min(0f)] private float rotationSpeed = 720f;
    [SerializeField, Min(0f)] private float acceleration = 20f;

    // Provisional feel values. Tune in the Inspector; none of them changes score, gauge or hit rules.
    [Header("Movement feel - provisional")]
    [Tooltip("Braking rate (m/s^2) once the move input is released.")]
    [SerializeField, Min(0f)] private float deceleration = 120f;
    [Tooltip("Acceleration multiplier while the input points against the current velocity (quick turn-around).")]
    [SerializeField, Min(1f)] private float reverseBoost = 1.6f;
    [SerializeField, Min(0f)] private float gravity = 30f;
    [Tooltip("Downward speed that keeps the controller on the ground while walking down steps and slopes.")]
    [SerializeField, Min(0f)] private float groundStickSpeed = 4f;
    [Tooltip("Layers the controller walks through instead of being body-blocked (the enemy crowd). Enemies step aside themselves.")]
    [SerializeField] private LayerMask crowdPassThroughLayers = 1 << 8;
    [Tooltip("Root speed (m/s) the run loop clip was authored for. Used to speed the run cycle up toward the real speed.")]
    [SerializeField, Min(0.1f)] private float runAnimationReferenceSpeed = 4.72f;
    [SerializeField, Min(1f)] private float runAnimationMaxScale = 1.25f;
    [Tooltip("Playback speed of the run-stop clip once the body has stopped, so it does not keep stepping in place for a second.")]
    [SerializeField, Min(1f)] private float runStopAnimationSpeed = 2f;

    [Header("Input buffering - provisional")]
    [Tooltip("An attack press that arrives early is kept this long and fires as soon as the combo or cancel window opens.")]
    [SerializeField, Min(0f)] private float attackBufferSeconds = 0.4f;

    [Header("Camera")]
    [SerializeField, Min(0.1f)] private float cameraDistance = 4f;
    [SerializeField] private float cameraHeightOffset = 1.5f;
    [SerializeField, Min(0f)] private float lookSensitivity = 0.2f;
    [SerializeField] private float minimumPitch = -20f;
    [SerializeField] private float maximumPitch = 60f;

    [Header("Camera feel - provisional")]
    [Tooltip("Smoothing of the point the camera orbits. Absorbs attack lunges and steps; looking around stays immediate.")]
    [SerializeField, Min(0f)] private float cameraFollowSmoothTime = 0.06f;
    [Tooltip("Level geometry the camera must not pass through.")]
    [SerializeField] private LayerMask cameraCollisionLayers = 1 << 0;
    [SerializeField, Min(0f)] private float cameraCollisionRadius = 0.25f;
    [SerializeField, Min(0f)] private float cameraMinimumDistance = 0.7f;
    [Tooltip("How fast (m/s) the camera moves back out after an obstacle is gone. Pulling in is immediate.")]
    [SerializeField, Min(0f)] private float cameraDistanceRecoverSpeed = 6f;

    [Header("Animator Attack Policy")]
    [SerializeField, Range(0f, 1f)] private float comboWindowStart = 0.3f;
    [SerializeField, Range(0f, 1f)] private float comboWindowEnd = 0.75f;

    private InputActionMap _actionMap;
    private InputAction _moveAction;
    private InputAction _lookAction;
    private InputAction _attackAction;
    private InputAction _specialAction;
    private Vector2 _moveInput;
    private Vector2 _lookInput;
    private bool _attackPressed;
    private bool _attackHeld;
    private bool _attackReleased;
    private bool _specialPressed;
    private Vector3 _velocity;                 // planar velocity (m/s)
    private float _verticalVelocity;
    private bool _grounded;
    private Vector3 _lastMoveDirection = Vector3.forward;
    private float _attackBuffer;               // seconds an early attack press is still remembered
    private float _cameraYaw;
    private float _cameraPitch = 20f;
    private Vector3 _cameraFocus;
    private Vector3 _cameraFocusVelocity;
    private float _cameraCurrentDistance;
    private bool _cameraSettled;
    private int _tick;
    private bool _battleActive;
    private bool _stopRequested;
    private bool _referencesCached;
    private float _animationSpeed;
    private readonly List<IPlayerPresentation> _presentations = new List<IPlayerPresentation>();
    private readonly List<IPlayerCancelGate> _cancelGates = new List<IPlayerCancelGate>();   // [WH] 후딜 캔슬 가능 여부를 알려주는 컴포넌트
    private bool _hasAttackRestartParam;   // [WH] Animator에 AttackRestart 파라미터가 없으면 경고가 매 프레임 찍히지 않도록 건너뜀
    private bool _attackRestartParamChecked;
    private readonly List<IPlayerInvulnerability> _invulnerabilities = new List<IPlayerInvulnerability>();   // [WH] 회피 무적 등
    private readonly Collider[] _attackResults = new Collider[512];
    private EnemyRuntime _combatEnemies;
    private CombatSettings _combatSettings;
    private CombatRoundRuntime _combatRound;
    private bool _attackStartedInBattle;
    private bool _hasAttackHitSource;
    private GameState _gameState = GameState.Initializing;

    public Transform TargetTransform => transform;

    // Planar speed in m/s. Read by tests and tuning tools.
    public float PlanarSpeed => _velocity.magnitude;
    public bool IsGrounded => _grounded;

    // [WH] 켜져 있는 무적 컴포넌트(회피 등) 중 하나라도 무적이면 true. CombatRoundRuntime이 피격 처리 전에 확인한다.
    public bool IsInvulnerable
    {
        get
        {
            foreach (IPlayerInvulnerability source in _invulnerabilities)
            {
                if (source is Behaviour behaviour && !behaviour.isActiveAndEnabled)
                    continue;
                if (source.IsInvulnerable)
                    return true;
            }
            return false;
        }
    }

    private void Awake()
    {
        // CharacterTest explicitly disables this adapter and keeps its standalone controls.
        if (!enabled)
            return;
        CacheReferences();
        ResolveActions();
        DisableInput();
    }

    public void ConfigureSpawnPoint(Transform value) => spawnPoint = value;

    public void ConfigureCombat(EnemyRuntime enemies, CombatSettings settings, CombatRoundRuntime combat = null)
    {
        _combatEnemies = enemies;
        _combatSettings = settings;
        _combatRound = combat;
    }

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

        // CoreBootstrap can dispatch Initializing before this component's Awake.
        CacheReferences();
        _tick = tick;
        _gameState = state;
        _battleActive = state == GameState.Battle;
        animator.speed = state == GameState.Battle || state == GameState.Finishing ? _animationSpeed : 0f;
        foreach (IPlayerPresentation presentation in _presentations)
            presentation.ApplyState(state);
        if (_battleActive && !_stopRequested)
            EnableInput();
        else
            DisableInput();

        if (!_battleActive)
        {
            _velocity = Vector3.zero;
            _attackBuffer = 0f;
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
        _specialPressed = _specialAction?.WasPressedThisFrame() ?? false;
    }

    public void UpdateAction(float deltaTime)
    {
        if (animator == null || characterController == null)
            return;

        ApplyLookInput();
        ApplyGravity(deltaTime);
        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        bool attackState = IsAttackState(stateInfo);
        // While a transition is blending, the state being entered is the one the player is really in.
        // Judging by the state being left let a second click during Idle -> Attack1 skip straight to Attack2.
        bool inTransition = animator.IsInTransition(0);
        AnimatorStateInfo logicalState = inTransition ? animator.GetNextAnimatorStateInfo(0) : stateInfo;

        if (_battleActive && attackState)
            _attackStartedInBattle = true;
        if (!attackState && !_attackPressed && _battleActive)
            _attackStartedInBattle = false;
        if ((_gameState == GameState.Battle || _gameState == GameState.Finishing) && _attackStartedInBattle)
            ApplyAttackHits(stateInfo);

        if (_battleActive && !_stopRequested)
        {
            if (_specialPressed)
                _combatRound?.TryUseSpecial(_tick, transform.position);
            if (_attackPressed)
                _attackBuffer = Mathf.Max(attackBufferSeconds, 0.0001f);
            // [WH] 후딜 캔슬 구간이면 공격 상태여도 이동과 재공격(AttackRestart)을 허용한다.
            bool cancelOpen = IsCancelOpen();
            // [WH] 공격 상태뿐 아니라 Animator에서 "Action" 태그를 단 상태(회피, 이후 스킬)도 이동을 잠근다.
            bool actionLocked = IsAttackState(logicalState) || logicalState.IsTag("Action");
            UpdateAttack(logicalState, cancelOpen);
            // 남아 있는 공격 입력은 곧 콤보로 이어지므로, 그동안은 이동 캔슬로 공격을 끊지 않는다.
            // (이동 캔슬 시점을 콤보 구간보다 앞당겨도 W를 누른 채 연타하면 콤보가 이어진다.)
            bool attackPending = _attackBuffer > 0f;
            UpdateMovement(deltaTime, !actionLocked || (cancelOpen && !attackPending));
            if (_attackBuffer > 0f)
                _attackBuffer = Mathf.Max(0f, _attackBuffer - deltaTime);
        }
        else
        {
            _velocity = Vector3.zero;
            _attackBuffer = 0f;
            SetRunning(false);
        }

        UpdateRunAnimationSpeed(stateInfo, inTransition);
        PlaceCamera(deltaTime, false);

        // Finishing 진입 등 입력이 닫힌 경우 차지 상태를 계속 붙잡지 않는다.
        if (!_battleActive && (stateInfo.IsName("Charge_Windup") || stateInfo.IsName("Charge_Hold")))
            animator.SetBool(IsHoldingHash, false);

        ClearFrameInput();
    }

    // Velocity model: the body reaches full speed and stops within a few frames, turns around through zero
    // instead of sliding backwards, and a short tap still moves a useful distance.
    private void UpdateMovement(float deltaTime, bool canMove)
    {
        Vector2 input = Vector2.ClampMagnitude(_moveInput, 1f);
        bool hasInput = input.sqrMagnitude > 0.01f;
        if (!canMove)
        {
            _velocity = Vector3.zero;
            SetRunning(false);
            return;
        }

        Vector3 desired = Vector3.zero;
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
                desired = _lastMoveDirection * (moveSpeed * Mathf.Clamp01(input.magnitude));
                Quaternion rotation = Quaternion.LookRotation(_lastMoveDirection);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, rotation, rotationSpeed * deltaTime);
            }
        }

        _velocity = PlayerLocomotion.Step(_velocity, desired, acceleration, deceleration, reverseBoost, deltaTime);
        SetRunning(hasInput);
        if (_velocity.sqrMagnitude > 0.0001f)
            characterController.Move(_velocity * deltaTime);
    }

    // Keeps the controller on the floor. Attacks and dodges move the controller themselves, so this
    // runs every frame regardless of the current action.
    private void ApplyGravity(float deltaTime)
    {
        if (deltaTime <= 0f || gravity <= 0f || !characterController.enabled)
            return;

        if (_grounded && _verticalVelocity <= 0f)
            _verticalVelocity = -Mathf.Max(groundStickSpeed, _velocity.magnitude);
        else
            _verticalVelocity -= gravity * deltaTime;

        CollisionFlags flags = characterController.Move(Vector3.up * (_verticalVelocity * deltaTime));
        _grounded = (flags & CollisionFlags.Below) != 0;
        if ((flags & CollisionFlags.Above) != 0 && _verticalVelocity > 0f)
            _verticalVelocity = 0f;
    }

    // The run loop covers runAnimationReferenceSpeed m/s, so it is played faster while running to narrow the gap to
    // the real speed. The run-stop clip is hurried once the body has stopped. Only Animator.speed is touched, so the
    // state machine keeps its own (interruptible) transitions.
    private void UpdateRunAnimationSpeed(AnimatorStateInfo stateInfo, bool inTransition)
    {
        float scale = 1f;
        bool running = stateInfo.IsTag("RunMotion") &&
                       (!inTransition || animator.GetNextAnimatorStateInfo(0).IsTag("RunMotion"));
        if (running)
        {
            if (stateInfo.IsName("Run_Combat_Fast_Stop"))
                scale = _velocity.sqrMagnitude < 0.25f ? Mathf.Max(1f, runStopAnimationSpeed) : 1f;
            else if (runAnimationReferenceSpeed > 0f)
                scale = Mathf.Clamp(_velocity.magnitude / runAnimationReferenceSpeed, 1f, Mathf.Max(1f, runAnimationMaxScale));
        }
        animator.speed = _animationSpeed * scale;
    }

    // Animator에 실제 존재하는 Attack1 -> Attack2 -> Charge 상태 전이만 구동한다.
    // stateInfo is the logical state (the one being entered while a transition blends).
    private void UpdateAttack(AnimatorStateInfo stateInfo, bool cancelOpen)
    {
        float normalizedTime = stateInfo.normalizedTime % 1f;
        // A press is remembered for attackBufferSeconds, so mashing lands on the first frame a window opens.
        if (_attackBuffer > 0f)
        {
            if (stateInfo.IsName("Idle") || stateInfo.IsTag("RunMotion"))
            {
                _attackStartedInBattle = true;
                animator.SetTrigger(AttackHash);
                _attackBuffer = 0f;
            }
            else if ((stateInfo.IsName("Attack1") || stateInfo.IsName("Attack2")) &&
                     normalizedTime >= comboWindowStart && normalizedTime <= comboWindowEnd)
            {
                _attackStartedInBattle = true;
                animator.SetTrigger(AttackHash);
                _attackBuffer = 0f;
            }
            // [WH] 콤보 구간 밖이지만 후딜 캔슬 구간이면 Attack1부터 다시 시작한다. (Attack1/Attack2/Charge_Release → Attack1 트랜지션)
            // [WH] 회피 등 "Action" 태그 상태의 후반에서도 같은 방식으로 Attack1 재시작을 허용한다.
            // 콤보 구간이 아직 오지 않은 Attack1/Attack2에서는 재시작하지 않고 입력을 남겨 두어 다음 타로 잇는다.
            else if (cancelOpen && HasAttackRestartParam() && (IsAttackState(stateInfo) || stateInfo.IsTag("Action")) &&
                     !ComboWindowAhead(stateInfo, normalizedTime))
            {
                _attackStartedInBattle = true;
                animator.SetTrigger(AttackRestartHash);
                _attackBuffer = 0f;
            }
        }
        // [WH] 캔슬 구간이 아니면 쓰이지 않은 재시작 트리거가 남아 나중에 발동하지 않도록 지운다.
        if (!cancelOpen && HasAttackRestartParam())
            animator.ResetTrigger(AttackRestartHash);

        if (stateInfo.IsName("Charge_Windup") || stateInfo.IsName("Charge_Hold"))
            animator.SetBool(IsHoldingHash, _attackHeld && !_attackReleased);
        else if (stateInfo.IsName("Idle") || stateInfo.IsName("Run") || stateInfo.IsTag("RunMotion"))
            animator.SetBool(IsHoldingHash, false);
    }

    // Orbit camera. Looking around is immediate (applied before movement, which is camera-relative).
    private void ApplyLookInput()
    {
        if (cameraTransform == null)
            return;

        if (_battleActive && !_stopRequested)
        {
            _cameraYaw += _lookInput.x * lookSensitivity;
            _cameraPitch = Mathf.Clamp(_cameraPitch - _lookInput.y * lookSensitivity, minimumPitch, maximumPitch);
        }
        cameraTransform.rotation = Quaternion.Euler(_cameraPitch, _cameraYaw, 0f);
    }

    // Places the camera after the body has moved. Only the followed point is smoothed, and level geometry between
    // the player and the camera pulls the camera in instead of hiding the player behind a wall.
    private void PlaceCamera(float deltaTime, bool snap)
    {
        if (cameraTransform == null)
            return;

        Vector3 focus = transform.position + Vector3.up * cameraHeightOffset;
        if (snap || !_cameraSettled || cameraFollowSmoothTime <= 0f || deltaTime <= 0f)
        {
            _cameraFocus = focus;
            _cameraFocusVelocity = Vector3.zero;
        }
        else
        {
            _cameraFocus = Vector3.SmoothDamp(_cameraFocus, focus, ref _cameraFocusVelocity, cameraFollowSmoothTime, Mathf.Infinity, deltaTime);
        }

        Quaternion rotation = Quaternion.Euler(_cameraPitch, _cameraYaw, 0f);
        Vector3 back = rotation * Vector3.back;
        float wanted = cameraDistance;
        if (cameraCollisionLayers.value != 0)
        {
            // Cast from the player (always in free space) so a wall next to the smoothed point cannot be skipped.
            Vector3 toCamera = _cameraFocus + back * cameraDistance - focus;
            float length = toCamera.magnitude;
            if (length > 0.001f &&
                Physics.SphereCast(focus, Mathf.Max(0.01f, cameraCollisionRadius), toCamera / length, out RaycastHit hit, length,
                    cameraCollisionLayers, QueryTriggerInteraction.Ignore))
                wanted = Mathf.Clamp(hit.distance, Mathf.Min(cameraMinimumDistance, cameraDistance), cameraDistance);
        }

        if (snap || !_cameraSettled || wanted < _cameraCurrentDistance)
            _cameraCurrentDistance = wanted;
        else
            _cameraCurrentDistance = Mathf.MoveTowards(_cameraCurrentDistance, wanted, cameraDistanceRecoverSpeed * deltaTime);
        _cameraSettled = true;

        cameraTransform.SetPositionAndRotation(_cameraFocus + back * _cameraCurrentDistance, rotation);
    }

    private void ResetPlayer(int tick)
    {
        _tick = tick;
        _stopRequested = false;
        _battleActive = false;
        _attackStartedInBattle = false;
        _gameState = GameState.Ready;
        DisableInput();
        _velocity = Vector3.zero;
        _verticalVelocity = 0f;
        _grounded = false;
        _attackBuffer = 0f;
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
            if (HasAttackRestartParam()) animator.ResetTrigger(AttackRestartHash);   // [WH]
            animator.SetBool(IsHoldingHash, false);
            animator.SetBool(IsRunningHash, false);
            animator.Rebind();
            animator.Update(0f);
            animator.speed = 0f;
        }

        foreach (IPlayerPresentation presentation in _presentations)
            presentation.ResetPresentation();

        _cameraYaw = spawnPoint.eulerAngles.y;
        _cameraPitch = 20f;
        PlaceCamera(0f, true);
    }

    private void CacheReferences()
    {
        if (_referencesCached)
            return;
        if (animator == null)
            animator = GetComponent<Animator>();
        if (characterController == null)
            characterController = GetComponent<CharacterController>();
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
        if (animator == null || characterController == null)
            throw new InvalidOperationException("PlayerRuntime requires an Animator and CharacterController.");
        // The crowd no longer body-blocks the player; enemies give way instead (EnemyAgent.YieldTo).
        characterController.excludeLayers |= crowdPassThroughLayers;
        _animationSpeed = animator.speed;
        foreach (MonoBehaviour component in GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component is IPlayerPresentation presentation)
                _presentations.Add(presentation);
            if (component is IPlayerCancelGate cancelGate)   // [WH]
                _cancelGates.Add(cancelGate);
            if (component is IPlayerInvulnerability invulnerability)   // [WH]
                _invulnerabilities.Add(invulnerability);
            // Bootstrap can cache these references before the scene object becomes active.
            if (component is IPlayerAttackHitSource && component.enabled)
                _hasAttackHitSource = true;
        }
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
        _specialAction = _actionMap.FindAction(specialActionName, false);
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
        _specialPressed = false;
    }

    private void ApplyAttackHits(AnimatorStateInfo stateInfo)
    {
        if (_hasAttackHitSource || _combatEnemies == null || _combatSettings == null ||
            !(stateInfo.IsName("Attack1") || stateInfo.IsName("Attack2") || stateInfo.IsName("Charge_Release")))
            return;
        float normalized = stateInfo.normalizedTime;
        if (normalized < _combatSettings.attackActiveStart || normalized > _combatSettings.attackActiveEnd)
            return;
        Vector3 center = transform.position + transform.forward * _combatSettings.attackForwardOffset + Vector3.up * _combatSettings.attackHeight;
        int count = Physics.OverlapSphereNonAlloc(center, Mathf.Max(0.05f, _combatSettings.attackRadius), _attackResults, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            Collider candidate = _attackResults[i];
            if (candidate != null && candidate.TryGetComponent(out EnemyAgent enemy))
                _combatEnemies.TryApplyHit(enemy, transform.forward, 1f, _tick);
            _attackResults[i] = null;
        }
    }

    private void SetRunning(bool value)
    {
        if (animator != null)
            animator.SetBool(IsRunningHash, value);
    }

    // [WH] Animator에 AttackRestart 트리거가 있는지 한 번만 확인한다. (없으면 재시작 기능만 꺼지고 나머지는 그대로)
    private bool HasAttackRestartParam()
    {
        if (_attackRestartParamChecked)
            return _hasAttackRestartParam;
        if (animator == null || !animator.isInitialized)
            return false;
        _attackRestartParamChecked = true;
        foreach (AnimatorControllerParameter parameter in animator.parameters)
            if (parameter.nameHash == AttackRestartHash && parameter.type == AnimatorControllerParameterType.Trigger)
                _hasAttackRestartParam = true;
        return _hasAttackRestartParam;
    }

    // [WH] 켜져 있는 캔슬 게이트 중 하나라도 "캔슬 가능"이면 true.
    private bool IsCancelOpen()
    {
        foreach (IPlayerCancelGate gate in _cancelGates)
        {
            if (gate is Behaviour behaviour && !behaviour.isActiveAndEnabled)
                continue;
            if (gate.IsCancelOpen)
                return true;
        }
        return false;
    }

    private static bool IsAttackState(AnimatorStateInfo stateInfo) =>
        stateInfo.IsName("Attack1") || stateInfo.IsName("Attack2") ||
        stateInfo.IsName("Charge_Windup") || stateInfo.IsName("Charge_Hold") ||
        stateInfo.IsName("Charge_Release");

    // Attack1/Attack2 whose combo window has not opened yet: a press now should wait for the next hit, not restart.
    private bool ComboWindowAhead(AnimatorStateInfo stateInfo, float normalizedTime) =>
        (stateInfo.IsName("Attack1") || stateInfo.IsName("Attack2")) && normalizedTime < comboWindowStart;
}
