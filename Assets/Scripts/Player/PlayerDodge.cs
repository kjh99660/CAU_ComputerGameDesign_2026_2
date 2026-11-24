using UnityEngine;
using UnityEngine.InputSystem;

// 회피(구르기).
//  - 회피 키 + 방향 입력 있음 → 카메라 기준 입력 방향으로 즉시 돌고 앞구르기 (Dodge_F)
//  - 회피 키 + 방향 입력 없음 → 보던 방향 그대로 뒷구르기 (Dodge_B)
//  - 이동: 클립의 루트 이동량 × 배율 (DodgeData의 거리에 맞춤). CharacterController로 움직여 벽/적과 충돌.
//  - cancelTime 이후: 이동(→Run), 공격(→Attack1), 재회피 가능. (이동·공격 캔슬은 PlayerRuntime이 IPlayerCancelGate를 보고 처리)
//  - invulnerableStart~End 동안 무적 (CombatRoundRuntime이 IPlayerInvulnerability를 보고 피격 무시)
// Animator 요구사항: 상태 "Dodge_F", "Dodge_B" (Tag = Action), 파라미터 Trigger "Dodge", Bool "DodgeBack".
[RequireComponent(typeof(CharacterController))]
public class PlayerDodge : MonoBehaviour, IPlayerCancelGate, IPlayerInvulnerability, IPlayerPresentation
{
    private static readonly int DodgeHash = Animator.StringToHash("Dodge");
    private static readonly int DodgeBackHash = Animator.StringToHash("DodgeBack");
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int AttackRestartHash = Animator.StringToHash("AttackRestart");

    [SerializeField] private DodgeData data;
    [SerializeField] private InputActionAsset inputActions;          // PlayerRuntime과 같은 에셋 (Main 씬용)
    [SerializeField] private string actionMapName = "Player";
    [SerializeField] private string moveActionName = "Move";
    [SerializeField] private string dodgeActionName = "Sprint";      // 회피 키. Sprint = Left Shift, Jump = Space
    [SerializeField] private Transform cameraTransform;              // 비우면 Main Camera 사용
    [SerializeField, Min(0f)] private float inputBufferSeconds = 0.2f; // 조금 일찍 누른 회피 입력을 이 시간만큼 기억했다가 가능해지는 순간 실행 (임시값)
    [SerializeField] private bool debugLog = false;                  // 켜면 회피 시작/끝, 실제 이동 거리를 콘솔에 출력

    private Animator _animator;
    private CharacterController _controller;
    private SwordEventRelay _sword;
    private IPlayerDodgeBlocker[] _blockers;
    private InputActionMap _map;
    private InputAction _move;
    private InputAction _dodge;
    private bool _useKeyboard;            // 팀원 구조가 없는 씬(CharacterTest)이면 키보드를 직접 읽음
    private bool _allowed = true;         // 새 회피는 Battle에서만 (팀원 구조가 없는 씬에서는 항상 허용)
    private bool _paramsChecked;
    private bool _hasParams;              // Animator에 Dodge/DodgeBack 파라미터가 있는지 (없으면 회피 비활성 + 경고 1회)

    private float _cooldownRemaining;
    private float _bufferRemaining;       // 일찍 눌린 회피 입력이 남아 있는 시간
    private bool _triggerPending;         // 트리거를 보냈지만 아직 구르기 상태로 들어가지 않음
    private float _triggerAge;
    private LayerMask _originalExclude;
    private bool _excluding;              // 적 통과 모드로 레이어를 무시 중인지

    // 디버그: 구르기 한 번 동안 실제로 움직인 거리
    private int _rollHash;
    private float _rollLastElapsed;
    private float _rollMoved;
    private float _rollTarget;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _controller = GetComponent<CharacterController>();
        _sword = GetComponentInChildren<SwordEventRelay>(true);
        if (inputActions != null)
        {
            _map = inputActions.FindActionMap(actionMapName, false);
            _move = _map?.FindAction(moveActionName, false);
            _dodge = _map?.FindAction(dodgeActionName, false);
        }
    }

    private void Start()
    {
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
        _useKeyboard = !(TryGetComponent(out PlayerRuntime runtime) && runtime.enabled);
        _blockers = GetComponentsInChildren<IPlayerDodgeBlocker>(true);
    }

    // ── PlayerRuntime이 매 프레임 물어보는 값 (PlayerRuntime이 먼저 실행되므로 그때그때 계산) ──

    public bool IsCancelOpen
    {
        get
        {
            if (data == null || _animator == null) return false;
            return TryGetJudgedRoll(out AnimatorStateInfo roll) && Elapsed(roll) >= data.cancelTime;
        }
    }

    public bool IsInvulnerable
    {
        get
        {
            if (data == null || _animator == null) return false;
            if (!TryGetJudgedRoll(out AnimatorStateInfo roll)) return false;
            float t = Elapsed(roll);
            return t >= data.invulnerableStart && t <= data.invulnerableEnd;
        }
    }

    // ── 입력 → 회피 시작 ──

    private void Update()
    {
        if (data == null || _animator == null) return;

        if (_cooldownRemaining > 0f)
            _cooldownRemaining -= Time.deltaTime * _animator.speed;   // Pause 중(애니메이터 정지)에는 줄지 않음

        // 구르기 캔슬 시각 직전처럼 아직 회피할 수 없는 순간에 누른 입력을 버리지 않고 잠깐 기억한다.
        if (!_allowed)
            _bufferRemaining = 0f;
        else if (DodgePressed())
            _bufferRemaining = Mathf.Max(inputBufferSeconds, 0.0001f);
        else if (_bufferRemaining > 0f && _animator.speed > 0f)   // Pause 중에는 줄지 않음
            _bufferRemaining = Mathf.Max(0f, _bufferRemaining - Time.deltaTime);

        if (_allowed && _bufferRemaining > 0f && HasParams() && CanStartDodge())
        {
            _bufferRemaining = 0f;
            StartDodge();
        }
    }

    private bool CanStartDodge()
    {
        if (_triggerPending || _cooldownRemaining > 0f) return false;

        // 나중에 필살기/스킬이 "지금은 회피 불가"를 알려줄 수 있음
        if (_blockers != null)
            foreach (IPlayerDodgeBlocker blocker in _blockers)
                if (blocker is Behaviour b && b.isActiveAndEnabled && blocker.BlocksDodge)
                    return false;

        // 구르기 중이면 캔슬 시각 이후에만 다시 구를 수 있음 (연타 방지)
        if (TryGetJudgedRoll(out AnimatorStateInfo roll) && Elapsed(roll) < data.cancelTime)
            return false;

        return true;
    }

    private void StartDodge()
    {
        bool back = !TryGetInputDirection(out Vector3 dir);
        if (!back)
            transform.rotation = Quaternion.LookRotation(dir);   // 입력 방향으로 즉시 회전 → 앞구르기

        // 공격 입력이 남아 있다가 구르기 직후 공격이 나가지 않도록 정리
        _animator.ResetTrigger(AttackHash);
        _animator.ResetTrigger(AttackRestartHash);
        _animator.SetBool(DodgeBackHash, back);
        _animator.SetTrigger(DodgeHash);

        _triggerPending = true;
        _triggerAge = 0f;
        _cooldownRemaining = Mathf.Max(0f, data.cooldown);
        if (_sword != null) _sword.Sheathe();                     // 칼을 든 채 회피하면 칼 넣기

        if (debugLog)
            Debug.Log($"[Dodge] 시작: {(back ? "뒷구르기 (입력 없음)" : "앞구르기")}", this);
    }

    // ── 매 프레임 상태 정리 (트리거 정리, 적 통과 모드, 디버그) ──

    private void LateUpdate()
    {
        if (data == null || _animator == null) return;

        bool inRoll = TryGetJudgedRoll(out AnimatorStateInfo roll);

        // 트리거를 보냈는데 일정 시간 안에 구르기로 못 들어갔으면 지워서 나중에 엉뚱하게 발동하지 않게 함
        if (_triggerPending)
        {
            _triggerAge += Time.deltaTime;
            if (inRoll)
                _triggerPending = false;
            else if (_triggerAge > 0.2f)
            {
                _animator.ResetTrigger(DodgeHash);
                _triggerPending = false;
                _cooldownRemaining = 0f;
            }
        }

        // 적 통과 모드: 구르는 동안만 Enemy 레이어 충돌 무시
        bool wantExclude = inRoll && data.passThroughEnemies;
        if (wantExclude && !_excluding)
        {
            _originalExclude = _controller.excludeLayers;
            _controller.excludeLayers = _originalExclude | data.enemyLayers;
            _excluding = true;
        }
        else if (!wantExclude && _excluding)
        {
            RestoreLayers();
        }

        if (debugLog) TrackRollForDebug(inRoll, roll);
    }

    // ── 루트 모션: 구르기 상태에서만 클립 이동량 × 배율로 이동 ──
    // (이 함수가 있으면 Animator가 루트 모션을 자동 적용하지 않음 → 구르기 외 상태는 지금처럼 이동량을 버림)

    private void OnAnimatorMove()
    {
        if (data == null || _animator == null || _controller == null || !_controller.enabled) return;
        if (!TryGetMovingRoll(out AnimatorStateInfo roll, out bool fromNext)) return;

        float clipDistance = ClipDistance(fromNext);
        if (clipDistance < 0.01f) return;

        float target = roll.IsName("Dodge_B") ? data.backwardDistance : data.forwardDistance;
        Vector3 delta = _animator.deltaPosition;
        delta.y = 0f;
        Vector3 step = delta * (target / clipDistance);

        Vector3 before = transform.position;
        _controller.Move(step);
        _rollMoved += Vector3.Distance(before, transform.position);
        _rollTarget = target;
    }

    // ── 상태 판단 도우미 ──

    // 캔슬/무적 판단 기준: 구르기로 넘어가는 중이면 넘어가는 쪽, 구르기에서 빠져나가는 중이면 떠나는 쪽.
    private bool TryGetJudgedRoll(out AnimatorStateInfo roll)
    {
        roll = default;
        if (_animator.IsInTransition(0))
        {
            AnimatorStateInfo next = _animator.GetNextAnimatorStateInfo(0);
            if (IsRoll(next)) { roll = next; return true; }
        }
        AnimatorStateInfo current = _animator.GetCurrentAnimatorStateInfo(0);
        if (IsRoll(current)) { roll = current; return true; }
        return false;
    }

    // 이동 판단 기준: 구르기로 들어가는 중이거나 구르기 진행 중일 때만. 구르기에서 빠져나가는 중(캔슬/종료)에는 이동 정지.
    private bool TryGetMovingRoll(out AnimatorStateInfo roll, out bool fromNext)
    {
        roll = default;
        fromNext = false;
        if (_animator.IsInTransition(0))
        {
            AnimatorStateInfo next = _animator.GetNextAnimatorStateInfo(0);
            if (!IsRoll(next)) return false;
            roll = next;
            fromNext = true;
            return true;
        }
        AnimatorStateInfo current = _animator.GetCurrentAnimatorStateInfo(0);
        if (!IsRoll(current)) return false;
        roll = current;
        return true;
    }

    // 클립에 들어 있는 수평 이동 거리 (m). 배율 = 원하는 거리 / 이 값
    private float ClipDistance(bool fromNext)
    {
        AnimatorClipInfo[] infos = fromNext ? _animator.GetNextAnimatorClipInfo(0) : _animator.GetCurrentAnimatorClipInfo(0);
        if (infos.Length == 0 || infos[0].clip == null) return 0f;
        AnimationClip clip = infos[0].clip;
        Vector3 speed = clip.averageSpeed;
        speed.y = 0f;
        float distance = speed.magnitude * clip.length;
        if (_animator.isHuman) distance *= _animator.humanScale;
        return distance;
    }

    private bool HasParams()
    {
        if (_paramsChecked) return _hasParams;
        if (!_animator.isInitialized) return false;
        _paramsChecked = true;
        bool trigger = false, back = false;
        foreach (AnimatorControllerParameter p in _animator.parameters)
        {
            if (p.nameHash == DodgeHash && p.type == AnimatorControllerParameterType.Trigger) trigger = true;
            if (p.nameHash == DodgeBackHash && p.type == AnimatorControllerParameterType.Bool) back = true;
        }
        _hasParams = trigger && back;
        if (!_hasParams)
            Debug.LogWarning("[Dodge] Animator에 Trigger \"Dodge\"와 Bool \"DodgeBack\" 파라미터가 필요합니다. 회피가 비활성화됩니다.", this);
        return _hasParams;
    }

    private static bool IsRoll(AnimatorStateInfo info) => info.IsName("Dodge_F") || info.IsName("Dodge_B");

    private static float Elapsed(AnimatorStateInfo info) => info.normalizedTime * info.length;

    // ── 입력 ──

    private bool DodgePressed()
    {
        if (_useKeyboard)
            return Keyboard.current != null && Keyboard.current.leftShiftKey.wasPressedThisFrame;
        return _map != null && _map.enabled && _dodge != null && _dodge.WasPressedThisFrame();
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
        if (_useKeyboard)
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return Vector2.zero;
            float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            float y = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
        }
        if (_map != null && _map.enabled && _move != null)
            return _move.ReadValue<Vector2>();
        return Vector2.zero;
    }

    // ── 디버그 ──

    private void TrackRollForDebug(bool inRoll, AnimatorStateInfo roll)
    {
        if (!inRoll)
        {
            if (_rollHash != 0)
            {
                Debug.Log($"[Dodge] 끝: 실제 이동 {_rollMoved:F2}m (목표 {_rollTarget:F2}m)", this);
                _rollHash = 0;
            }
            return;
        }

        float elapsed = Elapsed(roll);
        if (roll.fullPathHash != _rollHash || elapsed < _rollLastElapsed)
        {
            if (_rollHash != 0)
                Debug.Log($"[Dodge] 재회피로 끊김: 실제 이동 {_rollMoved:F2}m (목표 {_rollTarget:F2}m)", this);
            _rollHash = roll.fullPathHash;
            _rollMoved = 0f;
        }
        _rollLastElapsed = elapsed;
    }

    private void RestoreLayers()
    {
        if (!_excluding) return;
        _controller.excludeLayers = _originalExclude;
        _excluding = false;
    }

    // ── 팀원 구조가 보내 주는 알림 (PlayerRuntime이 자동으로 찾아서 호출) ──

    public void ApplyState(GameState state) => _allowed = state == GameState.Battle;

    public void ResetPresentation()
    {
        _cooldownRemaining = 0f;
        _bufferRemaining = 0f;
        _triggerPending = false;
        _rollHash = 0;
        RestoreLayers();
        if (_animator != null && HasParams())
        {
            _animator.ResetTrigger(DodgeHash);
            _animator.SetBool(DodgeBackHash, false);
        }
    }

    private void OnDisable() => RestoreLayers();
}
