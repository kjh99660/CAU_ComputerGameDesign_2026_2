using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
public sealed class EnemyAgent : MonoBehaviour
{
    private const float SteerDeadZone = 0.15f;          // steering weaker than this means "stay put"
    private const float HoldDeadZone = 0.3f;            // same, for enemies already holding a spot next to the player
    private const float HoldShuffleShare = 0.5f;        // how much of MoveSpeed an enemy uses to make room while holding
    private const float SettleSpeed = 0.2f;             // wanted speeds below this (m/s) are dropped, so a blocked enemy settles
    private const float MovingSpeed = 0.05f;            // slower than this (m/s) is standing, whatever the enemy wants
    private const float WalkExitSpeed = 0.4f;           // below this (m/s) with nowhere to go, the walk cycle stops
    private const float LocomotionBlendSeconds = 0.2f;
    private const float AttackBlendSeconds = 0.06f;
    private const float RecoverBlendSeconds = 0.15f;
    private const float FacePlayerDistanceFactor = 2.5f; // within AttackRange * this, look at the player rather than the path
    private const float GaitSwitchBand = 0.2f;           // m/s either side of WalkToRunSpeed, so the gait does not flicker

    // Directions to the bodies right in front of the enemy being updated. Filled by ScanNeighbours and read by
    // AvoidBlockers within the same RunAI call (main thread only), so the neighbour list is walked once per enemy.
    private static readonly Vector3[] BlockerDirections = new Vector3[16];
    private static int _blockerCount;

    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private MaterialPropertyBlock _properties;
    private Rigidbody _body;
    private RigidbodyInterpolation _physicsInterpolation;   // the prefab's setting, used only while physics moves the body
    private Renderer[] _renderers;
    private Animator _animator;
    private Transform _visualRoot;
    private Vector3 _rootDefaultScale = Vector3.one;
    private Vector3 _visualDefaultPosition;
    private Quaternion _visualDefaultRotation;
    private Vector3 _visualDefaultScale = Vector3.one;
    private Vector3 _visualScale = Vector3.one;
    private EnemyRuntime _owner;
    private float _attackCooldown;
    private float _stateTime;
    private bool _acceptedHit;
    private bool _attackHitChecked;

    // Locomotion presentation. The gameplay State says what the enemy is doing; these say how it looks doing it.
    private Vector3 _velocity;
    private float _speedScale = 1f;
    private float _phaseOffset;
    private float _animationSpeed = 1f;
    private float _appliedAnimatorSpeed = -1f;
    private bool _animationPaused;
    private int _shownAnimation;
    private float _runClipLength = 0.6f;
    private float _walkClipLength = 0.8f;
    private float _idleClipLength = 2f;
    private bool _hasWalkClip;

    private static readonly int IdleState = Animator.StringToHash("Idle");
    private static readonly int ChaseState = Animator.StringToHash("Chase");   // run cycle
    private static readonly int WalkState = Animator.StringToHash("Walk");
    private static readonly int AttackState = Animator.StringToHash("Attack");
    private static readonly int KnockbackState = Animator.StringToHash("Knockback");
    private static readonly int DyingState = Animator.StringToHash("Dying");

    public Rigidbody Body => _body;
    public EnemyAgentState State { get; private set; } = EnemyAgentState.Pooled;
    public int ActiveTick { get; private set; } = -1;
    public bool WantsPoolReturn => State == EnemyAgentState.Dying && _stateTime <= 0f;
    // Planar velocity in m/s. Read by tests and tuning tools.
    public Vector3 PlanarVelocity => _velocity;
    // True while a walk or run cycle is the animation being shown (or blended into).
    public bool ShowsLocomotion => _shownAnimation == ChaseState || _shownAnimation == WalkState;

    private void Awake()
    {
        _body = GetComponent<Rigidbody>();
        _physicsInterpolation = _body.interpolation;
        _rootDefaultScale = transform.localScale;
        _renderers = GetComponentsInChildren<Renderer>(true);
        _animator = GetComponent<Animator>();
        _visualRoot = transform.Find("Visual");
        if (_visualRoot != null)
        {
            _visualDefaultPosition = _visualRoot.localPosition;
            _visualDefaultRotation = _visualRoot.localRotation;
            _visualDefaultScale = _visualRoot.localScale;
            _visualScale = _visualDefaultScale;
        }
        if (_animator != null && _animator.runtimeAnimatorController != null)
        {
            foreach (AnimationClip clip in _animator.runtimeAnimatorController.animationClips)
            {
                if (clip == null) continue;
                if (clip.name == "Chase") _runClipLength = Mathf.Max(0.05f, clip.length);
                else if (clip.name == "Walk") _walkClipLength = Mathf.Max(0.05f, clip.length);
                else if (clip.name == "Idle") _idleClipLength = Mathf.Max(0.05f, clip.length);
            }
            _hasWalkClip = _animator.HasState(0, WalkState);
        }
        _properties = new MaterialPropertyBlock();
    }

    public void Initialize(EnemyRuntime owner)
    {
        _owner = owner;
        if (_body == null)
            Awake();
    }

    // variation (0..1) spreads speed, size and animation phase between enemies. 0.5 is the neutral enemy.
    public void Activate(Vector3 position, int tick, float variation = 0.5f)
    {
        gameObject.SetActive(true);
        transform.SetPositionAndRotation(position, Quaternion.identity);
        transform.localScale = _rootDefaultScale;
        ActiveTick = tick;
        ApplyVariation(variation);
        _velocity = Vector3.zero;
        _animationPaused = false;
        _animationSpeed = 1f;
        if (_animator != null)
        {
            _animator.Rebind();
            _animator.speed = 1f;
            _appliedAnimatorSpeed = 1f;
        }
        ResetVisualPose();
        ChangeState(EnemyAgentState.Idle, true);
        if (_animator != null)
            _animator.Update(0f);
        _acceptedHit = false;
        _attackHitChecked = false;
        _attackCooldown = 0f;
        _stateTime = 0f;
        SetDrivenByPhysics(false);
        _body.useGravity = true;
        // An interpolated Rigidbody owns its physics pose independently of the
        // Transform. Teleport both on spawn/reuse so interpolation cannot restore
        // the prefab origin or the previous death/knockback position next frame.
        _body.position = position;
        _body.rotation = Quaternion.identity;
        SetColor(new Color(0.9f, 0.22f, 0.18f, 1f));
    }

    // Turns a freshly spawned enemy toward a point so a wave does not appear facing the same way.
    public void Face(Vector3 worldPoint)
    {
        Vector3 direction = worldPoint - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
            return;
        Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);
        transform.rotation = rotation;
        if (_body != null)
            _body.rotation = rotation;
    }

    public void SetIdle(int tick)
    {
        if (tick != ActiveTick || State == EnemyAgentState.Pooled)
            return;
        _velocity = Vector3.zero;
        ResetVisualPose();
        ChangeState(EnemyAgentState.Idle, true);
        if (_animator != null)
            _animator.Update(0f);
        SetDrivenByPhysics(false);
        _stateTime = 0f;
    }

    public void RunAI(Vector3 target, IReadOnlyList<EnemyAgent> neighbours,
        EnemySettings settings, float deltaTime, int tick)
    {
        if (tick != ActiveTick || (State != EnemyAgentState.Idle && State != EnemyAgentState.Chase))
            return;

        _attackCooldown = Mathf.Max(0f, _attackCooldown - deltaTime);
        Vector3 toTarget = target - transform.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;
        Vector3 toward = distance > 0.0001f ? toTarget / distance : transform.forward;
        bool inRange = distance <= settings.AttackRange;
        if (inRange && _attackCooldown <= 0f)
        {
            _velocity = Vector3.zero;
            ChangeState(EnemyAgentState.Attack);
            _attackHitChecked = false;
            _stateTime = settings.AttackDuration;
            _attackCooldown = settings.AttackInterval;
            return;
        }

        // Out of range: close in, slowing down where the crowd pushes back instead of side-stepping at full speed.
        // In range but cooling down: hold the distance, watch the player and only shuffle sideways to make room,
        // so the ring around the player spreads out instead of piling up or pulsing in and out of range.
        Vector3 separation = ScanNeighbours(neighbours, settings.SeparationDistance, settings.BodySpacing, tick) * settings.SeparationWeight;
        Vector3 desired = Vector3.zero;
        if (!inRange)
        {
            ChangeState(EnemyAgentState.Chase);
            Vector3 steer = toward + separation;
            steer.y = 0f;
            float strength = steer.magnitude;
            if (strength > SteerDeadZone)
                desired = steer * (Mathf.Min(1f, strength) / strength * settings.MoveSpeed * _speedScale);
        }
        else
        {
            Vector3 sideways = separation - toward * Vector3.Dot(separation, toward);
            sideways.y = 0f;
            float strength = sideways.magnitude;
            if (strength > HoldDeadZone)
                desired = sideways * (Mathf.Min(1f, strength) / strength * settings.MoveSpeed * _speedScale * HoldShuffleShare);
        }

        desired = AvoidBlockers(desired);
        if (desired.sqrMagnitude < SettleSpeed * SettleSpeed)
            desired = Vector3.zero;   // what is left after avoiding bodies is too little to be worth a step
        _velocity = Vector3.MoveTowards(_velocity, desired, settings.Acceleration * deltaTime);
        // A body already in the way stops the enemy now, not after it has coasted into it.
        _velocity = AvoidBlockers(_velocity);
        if (_velocity.sqrMagnitude > 0.000001f)
            transform.position += _velocity * deltaTime;

        bool followPath = !inRange && _velocity.sqrMagnitude > 1f && distance > settings.AttackRange * FacePlayerDistanceFactor;
        Vector3 facing = followPath ? _velocity : toward;
        if (facing.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(facing, Vector3.up), settings.TurnSpeed * deltaTime);

        ShowLocomotion(desired.sqrMagnitude > 0.0001f, settings);
    }

    // The player walks through the crowd instead of being walled in by it, so whoever stands in the player's
    // space steps back out of it.
    public void YieldTo(Vector3 target, EnemySettings settings, float deltaTime, int tick)
    {
        if (tick != ActiveTick || deltaTime <= 0f ||
            (State != EnemyAgentState.Idle && State != EnemyAgentState.Chase && State != EnemyAgentState.Attack))
            return;

        float radius = settings.PlayerPersonalRadius;
        if (radius <= 0f)
            return;
        Vector3 away = transform.position - target;
        away.y = 0f;
        float squared = away.sqrMagnitude;
        if (squared >= radius * radius)
            return;

        float distance = Mathf.Sqrt(squared);
        Vector3 direction = distance > 0.001f ? away / distance : -transform.forward;
        transform.position += direction * Mathf.Min(radius - distance, settings.PlayerPushSpeed * deltaTime);
    }

    // AI has stopped issuing moves (Finishing). Settle instead of walking on the spot.
    public void HaltLocomotion(int tick)
    {
        if (tick != ActiveTick)
            return;
        _velocity = Vector3.zero;
        if (State == EnemyAgentState.Idle || State == EnemyAgentState.Chase)
            ShowLocomotion(false, null);
    }

    public void RunAction(EnemySettings settings, float deltaTime, int tick)
    {
        if (tick != ActiveTick)
            return;

        if (State == EnemyAgentState.Attack)
        {
            _stateTime -= deltaTime;
            if (_stateTime <= 0f)
                ChangeState(EnemyAgentState.Chase);
        }
        else if (State == EnemyAgentState.Knockback)
        {
            _stateTime -= deltaTime;
            if (_stateTime <= 0f)
                BeginDying(settings.DeathDuration);
        }
        else if (State == EnemyAgentState.Dying)
        {
            _stateTime -= deltaTime;
        }
    }

    // A single provisional hit check halfway through each existing attack.
    public bool TryAttackHit(Vector3 target, EnemySettings settings, float deltaTime, int tick)
    {
        if (tick != ActiveTick || State != EnemyAgentState.Attack || _attackHitChecked ||
            _stateTime - deltaTime > settings.AttackDuration * 0.5f)
            return false;

        _attackHitChecked = true;
        Vector3 distance = target - transform.position;
        distance.y = 0f;
        return distance.sqrMagnitude <= settings.AttackRange * settings.AttackRange;
    }

    public bool ApplyHit(Vector3 impulse, EnemySettings settings, int tick)
    {
        if (tick != ActiveTick || _acceptedHit || State == EnemyAgentState.Pooled || State == EnemyAgentState.Dying)
            return false;

        _acceptedHit = true;
        _velocity = Vector3.zero;
        ChangeState(EnemyAgentState.Knockback);
        _stateTime = settings.KnockbackFallbackDuration;
        SetDrivenByPhysics(true);
        _body.useGravity = true;
        _body.AddForce(impulse, ForceMode.Impulse);
        SetColor(new Color(1f, 0.72f, 0.2f, 1f));
        return true;
    }

    public void DeactivateForPool(Transform poolRoot)
    {
        if (!_body.isKinematic)
        {
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
        }
        SetDrivenByPhysics(false);
        State = EnemyAgentState.Pooled;
        ActiveTick = -1;
        _acceptedHit = false;
        _attackHitChecked = false;
        _attackCooldown = 0f;
        _stateTime = 0f;
        _velocity = Vector3.zero;
        _speedScale = 1f;
        _phaseOffset = 0f;
        _visualScale = _visualDefaultScale;
        _shownAnimation = 0;
        _animationPaused = false;
        _animationSpeed = 1f;
        if (_animator != null)
        {
            _animator.Rebind();
            _animator.Update(0f);
        }
        ResetVisualPose();
        transform.SetParent(poolRoot, false);
        transform.localScale = _rootDefaultScale;
        gameObject.SetActive(false);
    }

    // While the AI moves the Transform, the body is kinematic and must not interpolate: an interpolated body writes
    // its own pose back over the Transform every rendered frame, which threw away most of the AI's movement (enemies
    // advanced in jerks, at a fraction of MoveSpeed that depended on the frame rate). Interpolation is kept for the
    // knockback, where physics really is moving the body.
    private void SetDrivenByPhysics(bool physics)
    {
        _body.isKinematic = !physics;
        _body.interpolation = physics ? _physicsInterpolation : RigidbodyInterpolation.None;
    }

    public void SetAnimationPaused(bool paused)
    {
        _animationPaused = paused;
        ApplyAnimatorSpeed();
    }

    private void ApplyVariation(float variation)
    {
        float centered = Mathf.Clamp01(variation) * 2f - 1f;   // -1..1, 0 for the neutral enemy
        EnemySettings settings = _owner != null ? _owner.Settings : null;
        _speedScale = 1f + centered * (settings != null ? settings.SpeedVariance : 0f);
        _phaseOffset = Mathf.Repeat(centered * 3.37f, 1f);
        // A different mix of the same number, so the fastest enemy is not also always the largest.
        float size = 1f + Mathf.Sin(centered * 12.9f) * (settings != null ? settings.ScaleVariance : 0f);
        _visualScale = _visualDefaultScale * size;
    }

    // Shows a gait only while the enemy is really going somewhere, picks walk or run from the real speed and plays
    // it at "real speed / authored speed" so the planted foot stays put.
    private void ShowLocomotion(bool wantsToMove, EnemySettings settings)
    {
        float speed = _velocity.magnitude;
        // Wanting to move is not enough: an enemy pinned by the bodies around it stands, it does not run on the spot.
        if (settings == null || speed <= MovingSpeed || (!wantsToMove && speed <= WalkExitSpeed))
        {
            Show(IdleState, LocomotionBlendSeconds, _phaseOffset * _idleClipLength);
            _animationSpeed = 1f;
        }
        else
        {
            float threshold = settings.WalkToRunSpeed + (_shownAnimation == ChaseState ? -GaitSwitchBand : GaitSwitchBand);
            if (!_hasWalkClip || speed > threshold)
            {
                Show(ChaseState, LocomotionBlendSeconds, GaitStart(_runClipLength));
                _animationSpeed = Mathf.Clamp(speed / Mathf.Max(0.1f, settings.RunClipSpeed), 0.55f, 1.4f);
            }
            else
            {
                Show(WalkState, LocomotionBlendSeconds, GaitStart(_walkClipLength));
                _animationSpeed = Mathf.Clamp(speed / Mathf.Max(0.1f, settings.WalkClipSpeed), 0.5f, 2f);
            }
        }
        ApplyAnimatorSpeed();
    }

    // Where in the cycle a gait starts: in step with the gait already playing, else at this enemy's own phase.
    private float GaitStart(float clipLength)
    {
        if (_animator != null && ShowsLocomotion && !_animator.IsInTransition(0))
            return Mathf.Repeat(_animator.GetCurrentAnimatorStateInfo(0).normalizedTime, 1f) * clipLength;
        return _phaseOffset * clipLength;
    }

    private void Show(int animationState, float blendSeconds, float startSeconds = 0f)
    {
        if (_animator == null || _shownAnimation == animationState)
            return;
        _shownAnimation = animationState;
        if (blendSeconds <= 0f)
            _animator.Play(animationState, 0, 0f);
        else
            _animator.CrossFadeInFixedTime(animationState, blendSeconds, 0, startSeconds);
    }

    // Called every AI frame; the Animator is only touched when the value really changes.
    private void ApplyAnimatorSpeed()
    {
        if (_animator == null)
            return;
        float speed = _animationPaused ? 0f : _animationSpeed;
        if (Mathf.Abs(speed - _appliedAnimatorSpeed) < 0.02f && (speed > 0f) == (_appliedAnimatorSpeed > 0f))
            return;
        _appliedAnimatorSpeed = speed;
        _animator.speed = speed;
    }

    // One walk over the neighbours: returns the separation push (same rule as before) and remembers the direction to
    // every body closer than bodySpacing, for AvoidBlockers.
    private Vector3 ScanNeighbours(IReadOnlyList<EnemyAgent> neighbours, float distance, float bodySpacing, int tick)
    {
        Vector3 result = Vector3.zero;
        _blockerCount = 0;
        float distanceSquared = distance * distance;
        float bodySquared = Mathf.Min(bodySpacing * bodySpacing, distanceSquared);
        Vector3 self = transform.position;
        for (int i = 0; i < neighbours.Count; i++)
        {
            EnemyAgent other = neighbours[i];
            if (other == null || other == this || other.ActiveTick != tick || other.State == EnemyAgentState.Pooled)
                continue;

            Vector3 away = self - other.transform.position;
            away.y = 0f;
            float squared = away.sqrMagnitude;
            if (squared <= 0.0001f || squared > distanceSquared)
                continue;
            float length = Mathf.Sqrt(squared);
            Vector3 direction = away / length;
            result += direction * (1f - length / distance);

            if (squared <= bodySquared && _blockerCount < BlockerDirections.Length &&
                (other.State == EnemyAgentState.Idle || other.State == EnemyAgentState.Chase || other.State == EnemyAgentState.Attack))
                BlockerDirections[_blockerCount++] = -direction;
        }
        return result;
    }

    // Removes the part of a velocity that points at a body closer than the body spacing, so nobody walks into a body
    // that is already there. Enemies arriving behind a full ring stop and wait their turn.
    // Every blocker is judged against the same incoming velocity and the corrections are summed, so the result does
    // not depend on the order the neighbours are listed in (the grid and the brute-force lookup list them differently).
    private static Vector3 AvoidBlockers(Vector3 velocity)
    {
        if (_blockerCount == 0 || velocity.sqrMagnitude < 0.0001f)
            return velocity;

        Vector3 blocked = Vector3.zero;
        for (int i = 0; i < _blockerCount; i++)
        {
            float into = Vector3.Dot(velocity, BlockerDirections[i]);
            if (into > 0f)
                blocked += BlockerDirections[i] * into;
        }
        Vector3 remaining = velocity - blocked;
        // Boxed in from several sides: the corrections add up to more than the velocity. Stand still, do not back up.
        return Vector3.Dot(remaining, velocity) <= 0f ? Vector3.zero : remaining;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (State != EnemyAgentState.Knockback || _owner == null)
            return;

        for (int i = 0; i < collision.contactCount; i++)
        {
            if (collision.GetContact(i).normal.y > 0.4f)
            {
                BeginDying(_owner.Settings.DeathDuration);
                break;
            }
        }
    }

    private void BeginDying(float duration)
    {
        if (!_body.isKinematic)
        {
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
        }
        SetDrivenByPhysics(false);
        ChangeState(EnemyAgentState.Dying);
        _stateTime = Mathf.Max(0f, duration);
        SetColor(new Color(0.22f, 0.22f, 0.24f, 1f));
    }

    private void SetColor(Color color)
    {
        if (_renderers == null || _renderers.Length == 0)
            return;
        _properties ??= new MaterialPropertyBlock();
        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer renderer = _renderers[i];
            if (renderer == null)
                continue;
            renderer.GetPropertyBlock(_properties);
            _properties.SetColor(BaseColor, color);
            renderer.SetPropertyBlock(_properties);
        }
    }

    // Gameplay state change. Spawning, knockback and death cut to their pose at once; the rest blend,
    // and the walk/idle choice inside Chase follows the real movement (ShowLocomotion).
    private void ChangeState(EnemyAgentState state, bool restart = false)
    {
        if (!restart && State == state)
            return;

        EnemyAgentState previous = State;
        State = state;
        if (_animator == null)
            return;

        switch (state)
        {
            case EnemyAgentState.Idle:
                _shownAnimation = IdleState;
                _animationSpeed = 1f;
                _animator.Play(IdleState, 0, _phaseOffset);
                break;
            case EnemyAgentState.Chase:
                // Coming out of an attack, recover into the ready stance; RunAI picks the walk cycle once it moves.
                if (previous == EnemyAgentState.Attack)
                {
                    _animationSpeed = 1f;
                    Show(IdleState, RecoverBlendSeconds, _phaseOffset * _idleClipLength);
                }
                break;
            case EnemyAgentState.Attack:
                _animationSpeed = 1f;
                _shownAnimation = AttackState;
                _animator.CrossFadeInFixedTime(AttackState, AttackBlendSeconds, 0, 0f);
                break;
            case EnemyAgentState.Knockback:
                _animationSpeed = 1f;
                _shownAnimation = KnockbackState;
                _animator.Play(KnockbackState, 0, 0f);
                break;
            case EnemyAgentState.Dying:
                _animationSpeed = 1f;
                _shownAnimation = DyingState;
                _animator.Play(DyingState, 0, 0f);
                break;
        }
        ApplyAnimatorSpeed();
    }

    private void ResetVisualPose()
    {
        if (_visualRoot == null)
            return;
        _visualRoot.localPosition = _visualDefaultPosition;
        _visualRoot.localRotation = _visualDefaultRotation;
        _visualRoot.localScale = _visualScale;
    }
}
