using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// 스킬 실행기. 스킬마다 SkillData 하나, 이 컴포넌트는 하나.
//  ① 준비동작(windupTime) → ② 돌진: 투명화 + 입력 방향(없으면 정면)으로 dashDistance만큼 이동
//  → ③ 본동작: 투명화 해제 + 모션 + VFX + 원형 판정 1회 → cancelTime 이후 이동/공격 캔슬 가능
// 단계 전환은 코드가 SkillNext 트리거로 진행한다. (시간은 SkillData 기준, 클립 길이와 무관)
// 중간에 회피 등으로 스킬 상태를 벗어나면 즉시 정리(투명화 해제, 이동 정지, 트리거 정리)한다.
// 게이지(SkillData.usesGauge): 키를 누를 때 PlayerRuntime을 통해 게이지를 쓰고(모자라면 시작하지 않음),
//   돌진이 시작되기 전에 끊기면 돌려받는다. 스킬 타격은 게이지를 CombatSettings의 배율만큼만 채운다.
// 판정(SkillData.hitAlongDash): 켜면 캐릭터 주변이 아니라 돌진이 실제로 지나간 길만 상자로 판정한다.
// 본동작이 공격 클립(차지 베기)을 빌려 쓰므로, 스킬이 진행되는 동안 히트박스는 클립의 HitStart·HitEnd 이벤트를 받지 않는다.
// 연출(SkillFeedback, 없으면 생략): 준비 중 바닥 안내(돌진할 길) → 돌진 경로의 칼자국과 홀로그램 잔상.
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
    [SerializeField] private Key fallbackKey = Key.E;          // 팀원 구조가 없는 씬(CharacterTest)에서 첫 번째 스킬 키
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
    private PlayerRuntime _runtime;    // 라운드 쪽 창구 (게이지). 팀원 구조가 없는 씬에서는 꺼져 있거나 없음
    private SkillFeedback _feedback;   // 바닥 안내, 칼자국, 잔상 (메인 씬에만 있음)
    private AttackHitData _pathHit;    // hitAlongDash용: 지나간 길에 맞춰 매번 다시 채우는 상자
    private bool _gaugeRefundable;     // 이번 스킬이 게이지를 썼고 아직 돌진 전이라 끊기면 돌려받을 수 있음
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
        _useKeyboard = !(TryGetComponent(out _runtime) && _runtime.enabled);
        _feedback = FindFirstObjectByType<SkillFeedback>();
    }

    private void OnDestroy()
    {
        foreach (Runtime rt in _skills)
            if (rt.vfx != null) Destroy(rt.vfx);
        if (_pathHit != null) Destroy(_pathHit);
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

        if (_allowed && HasParams() && CanStartNewSkill())
        {
            for (int i = 0; i < _skills.Count; i++)
            {
                Runtime rt = _skills[i];
                if (rt.cooldown <= 0f && Pressed(rt, i))
                {
                    // 게이지가 모자라면 시작하지 않는다 (HUD가 알려 준다).
                    bool gauge = UsesGauge(rt);
                    if (gauge && !_runtime.TryUseSkillGauge()) break;
                    StartSkill(rt);
                    _gaugeRefundable = gauge;
                    break;
                }
            }
        }

        if (_active != null) Advance(dt);
    }

    // 새 스킬은 진행 중인 스킬이 없거나, 본동작의 판정이 끝난 뒤에만 받는다.
    //  - 준비·돌진·판정 전에 받으면 게이지를 한 번 더 쓰고 앞 스킬의 타격을 버리게 된다.
    //  - 스킬 상태에서 다른 동작으로 빠져나가는 전환 중에 받으면 같은 프레임에 끊겨 방향만 바뀐다.
    private bool CanStartNewSkill()
    {
        if (_active == null) return true;
        if (_phase != Phase.Main || !_hitEnded) return false;
        return !(_animator.IsInTransition(0) &&
                 IsAnySkillState(_animator.GetCurrentAnimatorStateInfo(0)) &&
                 !IsAnySkillState(_animator.GetNextAnimatorStateInfo(0)));
    }

    private void StartSkill(Runtime rt)
    {
        if (_active != null) Abort("다른 스킬 시작");

        _active = rt;
        rt.cooldown = Mathf.Max(0f, rt.data.cooldown);
        // 방향: 이동 입력 → (설정에 따라) 카메라가 보는 쪽 → 캐릭터가 보던 쪽
        _dashDir = TryGetInputDirection(out Vector3 dir) ? dir
                 : rt.data.aimWithCameraWhenIdle && TryGetCameraForward(out Vector3 look) ? look
                 : FlatForward();
        transform.rotation = Quaternion.LookRotation(_dashDir);   // 돌진 방향으로 즉시 회전

        _animator.ResetTrigger(AttackHash);
        _animator.ResetTrigger(AttackRestartHash);
        _animator.ResetTrigger(SkillNextHash);
        _animator.SetInteger(SkillIdHash, rt.data.skillId);
        _animator.SetTrigger(SkillHash);
        if (_sword != null) _sword.Sheathe();
        if (_hitbox != null) _hitbox.AnimationEventsMuted = true;   // 본동작 클립의 판정 이벤트 대신 스킬이 판정한다

        EnterPhase(Phase.Windup);
        if (_feedback != null && rt.data.dashDistance > 0f)
            _feedback.ShowAim(transform.position, _dashDir, rt.data.dashDistance + (rt.data.hitAlongDash ? rt.data.dashHitPadding : 0f),
                rt.data.hitAlongDash ? rt.data.dashHitWidth : 0f, rt.data.windupTime);
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
                    AttackHitData hit = _active.data.hitAlongDash ? BuildPathHit(_active.data) : _active.data.hitData;
                    if (_hitbox != null && hit != null)
                        _hitbox.BeginHit(hit, false, true);   // 스킬 타격: 게이지 획득 배율 적용
                }
                if (_hitStarted && !_hitEnded && _phaseTime >= _active.data.hitTime + _active.data.hitDuration)
                {
                    _hitEnded = true;
                    if (_hitbox != null) _hitbox.EndHit();
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
        // 여기서부터는 끊겨도 게이지를 돌려받지 않는다
        if (_gaugeRefundable && _runtime != null) _runtime.CommitSkillGauge();
        _gaugeRefundable = false;
        _dashStartPos = transform.position;
        SpawnOneShot(d.vanishVfx);
        if (_feedback != null) _feedback.CapturePose(transform);   // 잔상에 쓸 자세: 숨기기 전의 모습
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
        if (_feedback != null) _feedback.TrailTo(transform.position);            // 지나간 자리에 잔상
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
        if (_feedback != null) _feedback.PlayDash(_dashStartPos, transform.position);
    }

    // ── 종료 / 중단 ──

    private void Finish()
    {
        if (debugLog && _active != null) Debug.Log($"[Skill] {_active.data.name} 종료", this);
        // 본동작(차지 베기 클립)이 칼을 뽑는다. 클립의 칼 넣기 이벤트 전에 이동 등으로 캔슬됐으면 여기서 넣는다.
        if (_sword != null) _sword.Sheathe();
        Cleanup();
    }

    // refundGauge: 돌진 전에 끊긴 스킬의 게이지를 돌려받는다. 라운드 리셋으로 끊길 때는 돌려받지 않는다.
    private void Abort(string reason, bool refundGauge = true)
    {
        if (debugLog && _active != null) Debug.Log($"[Skill] {_active.data.name} 중단: {reason}", this);
        if (HasParams())
        {
            _animator.ResetTrigger(SkillHash);
            _animator.ResetTrigger(SkillNextHash);
        }
        if (refundGauge && _gaugeRefundable && _runtime != null) _runtime.RefundSkillGauge();
        Cleanup();
    }

    private void Cleanup()
    {
        if (_phase == Phase.Main && _hitStarted && !_hitEnded && _hitbox != null) _hitbox.EndHit();
        if (_hitbox != null) _hitbox.AnimationEventsMuted = false;
        _hitStarted = _hitEnded = false;
        _gaugeRefundable = false;
        if (_feedback != null) _feedback.HideAim();
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

    private bool TryGetCameraForward(out Vector3 direction)
    {
        direction = Vector3.zero;
        if (cameraTransform == null) return false;
        Vector3 forward = cameraTransform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) return false;
        direction = forward.normalized;
        return true;
    }

    // 게이지를 쓰는 스킬인지. 팀원 구조가 없는 씬(CharacterTest)에서는 게이지 없이 쓴다.
    private bool UsesGauge(Runtime rt) => rt.data.usesGauge && !_useKeyboard && _runtime != null;

    // 돌진이 실제로 지나간 길(시작 지점~지금 위치)을 덮는 상자. 벽에 막혀 짧게 갔으면 그만큼만 잡는다.
    // 히트박스가 캐릭터 기준 좌표로 계산하므로 중심을 캐릭터 기준으로 바꿔 넣는다 (캐릭터는 돌진 방향을 보고 있다).
    private AttackHitData BuildPathHit(SkillData d)
    {
        if (_pathHit == null)
        {
            _pathHit = ScriptableObject.CreateInstance<AttackHitData>();
            _pathHit.name = "SkillPathHit";
            _pathHit.shape = AttackHitData.HitShape.Box;
        }
        Vector3 from = _dashStartPos, to = transform.position;
        Vector3 along = to - from;
        float rise = Mathf.Abs(along.y);
        along.y = 0f;
        float height = Mathf.Max(0.1f, d.dashHitHeight);
        Vector3 middle = (from + to) * 0.5f + Vector3.up * (height * 0.5f);
        _pathHit.center = transform.InverseTransformPoint(middle);
        _pathHit.size = new Vector3(Mathf.Max(0.1f, d.dashHitWidth), height + rise,
            along.magnitude + 2f * Mathf.Max(0f, d.dashHitPadding));
        _pathHit.knockbackMultiplier = d.hitData != null ? d.hitData.knockbackMultiplier : 1f;
        return _pathHit;
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
        if (_active != null) Abort("라운드 리셋", false);
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
        if (_hitbox != null) _hitbox.AnimationEventsMuted = false;
        if (_feedback != null) _feedback.HideAim();
    }
}
