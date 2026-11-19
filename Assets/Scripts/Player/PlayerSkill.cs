using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// 스킬 실행기. 스킬마다 SkillData 하나, 이 컴포넌트는 하나.
//  ① 준비동작(windupTime) → ② 돌진: 투명화 + 입력 방향(없으면 정면)으로 dashDistance만큼 이동
//  → ③ 본동작: 투명화 해제 + 모션 + VFX + 원형 판정 1회 → cancelTime 이후 이동/공격 캔슬 가능
// 단계 전환은 코드가 SkillNext 트리거로 진행한다. (시간은 SkillData 기준, 클립 길이와 무관)
// 중간에 회피 등으로 스킬 상태를 벗어나면 즉시 정리(투명화 해제, 이동 정지, 트리거 정리)한다.
// Animator 요구사항: 파라미터 Trigger "Skill", Int "SkillId", Trigger "SkillNext"
//                    상태 "Skill{n}_Windup" → "Skill{n}_Dash" → "Skill{n}_Main" (모두 Tag = Action)
[RequireComponent(typeof(CharacterController))]
public class PlayerSkill : MonoBehaviour, IPlayerCancelGate, IPlayerInvulnerability, IPlayerPresentation
{
    private static readonly int SkillHash = Animator.StringToHash("Skill");
    private static readonly int SkillIdHash = Animator.StringToHash("SkillId");
    private static readonly int SkillNextHash = Animator.StringToHash("SkillNext");
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int AttackRestartHash = Animator.StringToHash("AttackRestart");

    [SerializeField] private SkillData[] skills;
    [SerializeField] private InputActionAsset inputActions;   // PlayerRuntime과 같은 에셋 (Main 씬용)
    [SerializeField] private string actionMapName = "Player";
    [SerializeField] private string moveActionName = "Move";
    [SerializeField] private Transform cameraTransform;       // 비우면 Main Camera 사용
    [SerializeField] private Key fallbackKey = Key.Q;          // 팀원 구조가 없는 씬(CharacterTest)에서 첫 번째 스킬 키
    [SerializeField] private bool debugLog = false;

    private enum Phase { None, Windup, Dash, Main }

    private class Runtime
    {
        public SkillData data;
        public InputAction action;
        public float cooldown;
        public int windupHash, dashHash, mainHash;
        public GameObject vfx;
        public ParticleSystem vfxSystem;
        public float vfxTimer;          // 남은 재생 시간. 0 이하가 되면 이펙트 오브젝트를 꺼둠
        public readonly List<ParticleSystem> pausedSystems = new List<ParticleSystem>();
    }

    private Animator _animator;
    private CharacterController _controller;
    private SwordEventRelay _sword;
    private PlayerAttackHitbox _hitbox;
    private InputActionMap _map;
    private InputAction _move;
    private bool _useKeyboard;
    private bool _allowed = true;
    private bool _paramsChecked, _hasParams;
    private readonly List<Runtime> _skills = new List<Runtime>();

    // 진행 중인 스킬
    private Runtime _active;
    private Phase _phase;
    private float _phaseTime;          // 현재 단계에 들어온 뒤 지난 시간 (Animator 기준)
    private float _waitTime;           // 트리거를 보낸 뒤 해당 상태에 아직 못 들어간 시간
    private Vector3 _dashDir;
    private bool _hitStarted, _hitEnded;
    private Vector3 _dashStartPos;     // 디버그: 돌진 시작 위치

    // 투명화
    private Renderer[] _renderers;
    private readonly List<Renderer> _hidden = new List<Renderer>();
    private LayerMask _originalExclude;
    private bool _excluding;
    private bool _vfxPaused;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _controller = GetComponent<CharacterController>();
        _sword = GetComponentInChildren<SwordEventRelay>(true);
        _hitbox = GetComponent<PlayerAttackHitbox>();

        // 숨길 대상: 캐릭터 아래의 모든 메쉬 (몸, 칼, 칼집). 원래 꺼져 있던 것은 숨길 때 건드리지 않음.
        var list = new List<Renderer>();
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            if (r is MeshRenderer || r is SkinnedMeshRenderer) list.Add(r);
        _renderers = list.ToArray();

        if (inputActions != null)
        {
            _map = inputActions.FindActionMap(actionMapName, false);
            _move = _map?.FindAction(moveActionName, false);
        }

        if (skills == null) return;
        foreach (SkillData data in skills)
        {
            if (data == null) continue;
            var rt = new Runtime
            {
                data = data,
                action = _map?.FindAction(data.inputActionName, false),
                windupHash = Animator.StringToHash($"Skill{data.skillId}_Windup"),
                dashHash = Animator.StringToHash($"Skill{data.skillId}_Dash"),
                mainHash = Animator.StringToHash($"Skill{data.skillId}_Main"),
            };
            if (data.vfxPrefab != null)
            {
                // 이펙트는 미리 하나 만들어두고 재사용 (캐릭터를 따라다니지 않도록 부모 없음)
                rt.vfx = Instantiate(data.vfxPrefab);
                rt.vfx.name = $"{data.name} VFX (auto)";
                rt.vfxSystem = rt.vfx.GetComponent<ParticleSystem>();
                rt.vfx.SetActive(false);
            }
            if (rt.action == null && _map != null)
                Debug.LogWarning($"[Skill] 입력 액션 \"{data.inputActionName}\"을 찾을 수 없습니다. InputSystem_Actions의 Player에 추가하세요.", this);
            _skills.Add(rt);
        }
    }

    private void Start()
    {
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
        _useKeyboard = !(TryGetComponent(out PlayerRuntime runtime) && runtime.enabled);
    }

    private void OnDestroy()
    {
        foreach (Runtime rt in _skills)
            if (rt.vfx != null) Destroy(rt.vfx);
    }

    // ── PlayerRuntime이 매 프레임 물어보는 값 ──

    public bool IsCancelOpen
    {
        get
        {
            if (_active == null || _phase != Phase.Main || _animator == null) return false;
            if (!TryGetJudgedState(out AnimatorStateInfo info) || info.shortNameHash != _active.mainHash) return false;
            return Elapsed(info) >= _active.data.cancelTime;
        }
    }

    public bool IsInvulnerable => _active != null && _phase == Phase.Dash && _active.data.invulnerableDuringDash;

    // ── 매 프레임 ──

    private void Update()
    {
        if (_animator == null) return;
        float dt = Time.deltaTime * _animator.speed;   // Pause 중(애니메이터 정지)에는 0

        foreach (Runtime rt in _skills)
        {
            if (rt.cooldown > 0f) rt.cooldown -= dt;

            // 이펙트 재생이 끝나면 꺼둠 (Pause 중에는 시간이 흐르지 않음)
            if (rt.vfx != null && rt.vfx.activeSelf && !_vfxPaused)
            {
                rt.vfxTimer -= Time.deltaTime;
                if (rt.vfxTimer <= 0f) rt.vfx.SetActive(false);
            }
        }

        if (_allowed && HasParams())
        {
            for (int i = 0; i < _skills.Count; i++)
            {
                Runtime rt = _skills[i];
                if (rt.cooldown <= 0f && Pressed(rt, i))
                {
                    StartSkill(rt);
                    break;
                }
            }
        }

        if (_active != null) Advance(dt);
    }

    private void StartSkill(Runtime rt)
    {
        if (_active != null) Abort("다른 스킬 시작");

        _active = rt;
        rt.cooldown = Mathf.Max(0f, rt.data.cooldown);
        _dashDir = TryGetInputDirection(out Vector3 dir) ? dir : FlatForward();
        transform.rotation = Quaternion.LookRotation(_dashDir);   // 돌진 방향으로 즉시 회전

        _animator.ResetTrigger(AttackHash);
        _animator.ResetTrigger(AttackRestartHash);
        _animator.ResetTrigger(SkillNextHash);
        _animator.SetInteger(SkillIdHash, rt.data.skillId);
        _animator.SetTrigger(SkillHash);
        if (_sword != null) _sword.Sheathe();

        EnterPhase(Phase.Windup);
        if (debugLog) Debug.Log($"[Skill] {rt.data.name} 시작", this);
    }

    private void Advance(float dt)
    {
        int expected = _phase == Phase.Windup ? _active.windupHash
                     : _phase == Phase.Dash ? _active.dashHash
                     : _active.mainHash;

        // 준비·돌진 중에 스킬이 아닌 상태(회피 등)로 넘어가기 시작하면 즉시 중단 (투명화가 남지 않게)
        if (_phase != Phase.Main && _animator.IsInTransition(0) &&
            IsAnySkillState(_animator.GetCurrentAnimatorStateInfo(0)) &&
            !IsAnySkillState(_animator.GetNextAnimatorStateInfo(0)))
        {
            Abort("다른 동작으로 끊김");
            return;
        }

        bool inExpected = TryGetJudgedState(out AnimatorStateInfo info) && info.shortNameHash == expected;
        if (!inExpected)
        {
            // 본동작이 끝까지 재생됐거나 캔슬로 다른 상태에 도착함 → 정상 종료
            if (_phase == Phase.Main && _phaseTime > 0f)
            {
                Finish();
                return;
            }
            // 트리거를 보낸 직후라 아직 못 들어간 경우는 잠깐 기다림.
            // 스킬 상태 밖(회피 등)으로 나갔거나, 너무 오래 못 들어가면 정리.
            _waitTime += dt;   // Animator 기준 시간 (Pause 중에는 늘지 않음)
            bool inSkill = IsAnySkillState(info) || IsAnySkillState(_animator.GetCurrentAnimatorStateInfo(0));
            if ((!inSkill && _waitTime > 0.1f) || _waitTime > 0.5f)
                Abort(inSkill ? "다음 단계로 넘어가지 못함" : "스킬 상태를 벗어남");
            return;
        }
        _waitTime = 0f;
        _phaseTime += dt;

        switch (_phase)
        {
            case Phase.Windup:
                if (_phaseTime >= _active.data.windupTime)
                    NextPhase(Phase.Dash);
                break;

            case Phase.Dash:
                DashMove(dt);
                if (_phaseTime >= _active.data.dashDuration || _active.data.dashDistance <= 0f)
                    NextPhase(Phase.Main);
                break;

            case Phase.Main:
                if (!_hitStarted && _phaseTime >= _active.data.hitTime)
                {
                    _hitStarted = true;
                    if (_hitbox != null && _active.data.hitData != null)
                        _hitbox.BeginHit(_active.data.hitData, false);
                }
                if (_hitStarted && !_hitEnded && _phaseTime >= _active.data.hitTime + _active.data.hitDuration)
                {
                    _hitEnded = true;
                    if (_hitbox != null) _hitbox.HitEnd();
                }
                break;
        }
    }

    private void NextPhase(Phase next)
    {
        if (_phase == Phase.Dash) EndDash();
        _animator.SetTrigger(SkillNextHash);
        EnterPhase(next);
    }

    private void EnterPhase(Phase phase)
    {
        _phase = phase;
        _phaseTime = 0f;
        _waitTime = 0f;

        if (phase == Phase.Dash) BeginDash();
        if (phase == Phase.Main)
        {
            _hitStarted = false;
            _hitEnded = false;
            PlayMainVfx();
        }
        if (debugLog && _active != null) Debug.Log($"[Skill] {_active.data.name} → {phase}", this);
    }

    // ── 돌진 ──

    private void BeginDash()
    {
        SkillData d = _active.data;
        _dashStartPos = transform.position;
        SpawnOneShot(d.vanishVfx);
        if (d.hideDuringDash) Hide();
        if (d.dashPassThroughEnemies && !_excluding)
        {
            _originalExclude = _controller.excludeLayers;
            _controller.excludeLayers = _originalExclude | d.enemyLayers;
            _excluding = true;
        }
    }

    private void DashMove(float dt)
    {
        SkillData d = _active.data;
        if (d.dashDuration <= 0f || d.dashDistance <= 0f || !_controller.enabled) return;
        float remaining = Mathf.Max(0f, d.dashDuration - (_phaseTime - dt));
        float step = Mathf.Min(dt, remaining);
        _controller.Move(_dashDir * (d.dashDistance / d.dashDuration * step));   // 적·벽에 막히면 그 자리에서 멈춤
    }

    private void EndDash()
    {
        if (debugLog && _active != null)
        {
            Vector3 moved = transform.position - _dashStartPos;
            moved.y = 0f;
            Debug.Log($"[Skill] 돌진: {_dashStartPos} → {transform.position} (이동 {moved.magnitude:F2}m / 목표 {_active.data.dashDistance:F2}m)", this);
        }
        RestoreLayers();
        Show();
        if (_active != null) SpawnOneShot(_active.data.appearVfx);
    }

    // ── 종료 / 중단 ──

    private void Finish()
    {
        if (debugLog && _active != null) Debug.Log($"[Skill] {_active.data.name} 종료", this);
        Cleanup();
    }

    private void Abort(string reason)
    {
        if (debugLog && _active != null) Debug.Log($"[Skill] {_active.data.name} 중단: {reason}", this);
        if (HasParams())
        {
            _animator.ResetTrigger(SkillHash);
            _animator.ResetTrigger(SkillNextHash);
        }
        Cleanup();
    }

    private void Cleanup()
    {
        if (_phase == Phase.Main && _hitStarted && !_hitEnded && _hitbox != null) _hitbox.HitEnd();
        _hitStarted = _hitEnded = false;
        RestoreLayers();
        Show();
        _active = null;
        _phase = Phase.None;
        _phaseTime = 0f;
        _waitTime = 0f;
    }

    // ── 투명화 ──

    private void Hide()
    {
        foreach (Renderer r in _renderers)
        {
            if (r != null && r.enabled)
            {
                r.enabled = false;
                _hidden.Add(r);
            }
        }
    }

    private void Show()
    {
        foreach (Renderer r in _hidden)
            if (r != null) r.enabled = true;
        _hidden.Clear();
    }

    private void RestoreLayers()
    {
        if (!_excluding) return;
        _controller.excludeLayers = _originalExclude;
        _excluding = false;
    }

    // ── 이펙트 ──

    // 이펙트 재생: 끄기 → 위치 옮기기 → 켜기 → 켜져 있는 파티클 시스템을 하나씩 처음부터 재생.
    // (켜진 채로 멀리 옮기면 World 공간 파티클이 이전 위치 쪽에서 나올 수 있어서, 매번 끄고 옮긴다)
    private void PlayMainVfx()
    {
        if (_active?.vfx == null) return;
        GameObject go = _active.vfx;

        Quaternion rot = Quaternion.LookRotation(FlatForward());
        Vector3 pos = transform.position + rot * _active.data.vfxOffset;

        go.SetActive(false);
        go.transform.SetPositionAndRotation(pos, rot);
        go.SetActive(true);

        // 켜져 있는 파티클 시스템만 (Smoke 등 꺼둔 자식은 제외)
        ParticleSystem[] systems = go.GetComponentsInChildren<ParticleSystem>(false);

        // 다른 시스템의 Sub Emitter로 쓰이는 시스템은 부모가 대신 내보내므로 직접 재생하지 않음
        var subEmitters = new HashSet<ParticleSystem>();
        foreach (ParticleSystem ps in systems)
        {
            ParticleSystem.SubEmittersModule sub = ps.subEmitters;
            if (!sub.enabled) continue;
            for (int i = 0; i < sub.subEmittersCount; i++)
            {
                ParticleSystem child = sub.GetSubEmitterSystem(i);
                if (child != null) subEmitters.Add(child);
            }
        }

        float longest = 0f;
        var played = new List<string>();
        foreach (ParticleSystem ps in systems)
        {
            ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Clear(false);
        }
        foreach (ParticleSystem ps in systems)
        {
            ParticleSystem.MainModule main = ps.main;
            longest = Mathf.Max(longest, main.duration + main.startDelay.constantMax + main.startLifetime.constantMax);
            if (subEmitters.Contains(ps)) continue;
            ps.Play(false);
            played.Add(ps.name);
        }
        _active.vfxTimer = longest + 0.5f;   // 여유를 두고 끝나면 꺼둠

        if (debugLog)
            Debug.Log($"[Skill] VFX 재생 위치 {pos} (캐릭터 {transform.position}), 재생: {string.Join(", ", played)}" +
                      (subEmitters.Count > 0 ? $" / Sub Emitter {subEmitters.Count}개" : ""), this);
    }

    private void SpawnOneShot(GameObject prefab)
    {
        if (prefab == null) return;
        GameObject go = Instantiate(prefab, transform.position, Quaternion.LookRotation(FlatForward()));
        Destroy(go, 3f);
    }

    private void SetVfxPaused(bool paused)
    {
        if (paused == _vfxPaused) return;
        _vfxPaused = paused;
        foreach (Runtime rt in _skills)
        {
            if (rt.vfx == null || !rt.vfx.activeSelf) continue;
            if (paused)
            {
                // 재생 중인 시스템만 멈추고 기억 (끝난 시스템이나 Sub Emitter를 다시 틀지 않도록)
                rt.pausedSystems.Clear();
                foreach (ParticleSystem ps in rt.vfx.GetComponentsInChildren<ParticleSystem>(false))
                {
                    if (!ps.isPlaying) continue;
                    ps.Pause(false);
                    rt.pausedSystems.Add(ps);
                }
            }
            else
            {
                foreach (ParticleSystem ps in rt.pausedSystems)
                    if (ps != null && ps.isPaused) ps.Play(false);
                rt.pausedSystems.Clear();
            }
        }
    }

    // ── 상태·입력 도우미 ──

    // 판단 기준 상태: 넘어가는 중이면 넘어가는 쪽이 스킬 상태일 때 그쪽, 아니면 현재 상태.
    private bool TryGetJudgedState(out AnimatorStateInfo info)
    {
        info = _animator.GetCurrentAnimatorStateInfo(0);
        if (_animator.IsInTransition(0))
        {
            AnimatorStateInfo next = _animator.GetNextAnimatorStateInfo(0);
            if (IsAnySkillState(next)) info = next;
        }
        return true;
    }

    private bool IsAnySkillState(AnimatorStateInfo info)
    {
        if (_active == null) return false;
        int h = info.shortNameHash;
        return h == _active.windupHash || h == _active.dashHash || h == _active.mainHash;
    }

    private static float Elapsed(AnimatorStateInfo info) => info.normalizedTime * info.length;

    private bool Pressed(Runtime rt, int index)
    {
        if (_useKeyboard)
            return index == 0 && Keyboard.current != null && Keyboard.current[fallbackKey].wasPressedThisFrame;
        return _map != null && _map.enabled && rt.action != null && rt.action.WasPressedThisFrame();
    }

    private Vector3 FlatForward()
    {
        Vector3 f = transform.forward;
        f.y = 0f;
        return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
    }

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

    private bool HasParams()
    {
        if (_paramsChecked) return _hasParams;
        if (_animator == null || !_animator.isInitialized) return false;
        _paramsChecked = true;
        bool skill = false, id = false, next = false;
        foreach (AnimatorControllerParameter p in _animator.parameters)
        {
            if (p.nameHash == SkillHash && p.type == AnimatorControllerParameterType.Trigger) skill = true;
            if (p.nameHash == SkillIdHash && p.type == AnimatorControllerParameterType.Int) id = true;
            if (p.nameHash == SkillNextHash && p.type == AnimatorControllerParameterType.Trigger) next = true;
        }
        _hasParams = skill && id && next;
        if (!_hasParams)
            Debug.LogWarning("[Skill] Animator에 Trigger \"Skill\", Int \"SkillId\", Trigger \"SkillNext\" 파라미터가 필요합니다. 스킬이 비활성화됩니다.", this);
        return _hasParams;
    }

    // ── 팀원 구조가 보내 주는 알림 (PlayerRuntime이 자동으로 찾아서 호출) ──

    public void ApplyState(GameState state)
    {
        _allowed = state == GameState.Battle;
        SetVfxPaused(!(state == GameState.Battle || state == GameState.Finishing));   // Pause 중 이펙트도 정지
    }

    public void ResetPresentation()
    {
        if (_active != null) Abort("라운드 리셋");
        foreach (Runtime rt in _skills)
        {
            rt.cooldown = 0f;
            rt.pausedSystems.Clear();
            if (rt.vfx != null)
            {
                if (rt.vfxSystem != null) rt.vfxSystem.Clear(true);
                rt.vfx.SetActive(false);
            }
        }
        _vfxPaused = false;
    }

    private void OnDisable()
    {
        RestoreLayers();
        Show();
    }
}
