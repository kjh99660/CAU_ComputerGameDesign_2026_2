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
    private const float PathSlack = 1.5f;                // a walk to the player this much longer than AttackRange still counts as "next to them"
    private const float FallStartHeight = 0.6f;          // further above the ground than this is a fall, not a step down
    private const float FallGravity = 25f;               // m/s^2 for an enemy that stepped off an edge
    private const float GroundFollowSpeed = 6f;          // m/s the feet may rise or sink while walking: stairs need 2.6
    private const float LeapSpeed = 4f;                  // m/s away from the edge at least, so a falling enemy clears the wall

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

    // Level with floors above each other: the cells this enemy walks on. Null in a flat level.
    private EnemyNavField _nav;
    private int _navCell = EnemyNavField.None;
    private int _steerCell = EnemyNavField.None;
    private int _steerVersion = -1;
    private Vector3 _steerPoint;
    private bool _falling;
    private float _fallSpeed;
    private Vector3 _fallTarget;       // where the fall lands
    private float _fallDrift;          // m/s toward it, so the enemy arrives as it touches down

    private static readonly int IdleState = Animator.StringToHash("Idle");
    private static readonly int ChaseState = Animator.StringToHash("Chase");   // run cycle
    private static readonly int WalkState = Animator.StringToHash("Walk");
    private static readonly int AttackState = Animator.StringToHash("Attack");
    private static readonly int KnockbackState = Animator.StringToHash("Knockback");
    private static readonly int DyingState = Animator.StringToHash("Dying");
    // Looks for the same attack (same length, same contact moment). One is picked at random per attack; the rules
    // and the hit timing do not depend on which one plays. A controller without the extra states uses "Attack" only.
    private static readonly int[] AttackLooks =
    {
        AttackState, Animator.StringToHash("Attack2"), Animator.StringToHash("Attack3")
    };
    private readonly int[] _attackLooks = new int[AttackLooks.Length];
    private int _attackLookCount;
    private int _attackLook;           // index of the look shown by the current or last attack
    private uint _attackRandom = 1u;

    // Attack telegraph: the enemy glows this colour while it winds up, then goes back to its usual colour.
    private static readonly Color ReadyColor = new Color(0.9f, 0.22f, 0.18f, 1f);
    private static readonly Color WarningColor = new Color(1f, 0.82f, 0.2f, 1f);
    private float _attackElapsed;

    // Liveliness (EnemySettings.Liveliness): this enemy's temperament and the little timers that make it move like
    // someone sizing the player up rather than a marker sliding at them.
    private uint _mindRandom = 1u;
    private EnemyTemperament _temperament;
    private float _ringRadius = 3.5f;      // the distance it keeps while waiting for its turn
    private float _circleSign = 1f;        // which way it circles
    private float _flankSign = 1f;         // which side a flanker swings around
    private float _strafeTimer;            // until it turns round and circles the other way
    private float _hoverTime;              // time spent waiting in the ring
    private float _commitAfter;            // how long it waits there before stepping in
    private float _paceTimer;              // stop-and-go rhythm of a hesitant enemy
    private bool _pacePaused;
    private float _feintTimer;             // until the next feint
    private float _feintLeft;              // seconds left of the feint in progress

    public Rigidbody Body => _body;
    public EnemyAgentState State { get; private set; } = EnemyAgentState.Pooled;
    public int ActiveTick { get; private set; } = -1;
    public bool WantsPoolReturn => State == EnemyAgentState.Dying && _stateTime <= 0f;
    // Planar velocity in m/s. Read by tests and tuning tools.
    public Vector3 PlanarVelocity => _velocity;
    // True while a walk or run cycle is the animation being shown (or blended into).
    public bool ShowsLocomotion => _shownAnimation == ChaseState || _shownAnimation == WalkState;
    // The cell the enemy stands on in a level with floors, EnemyNavField.None otherwise.
    public int NavCell => _navCell;
    // How many attack looks the controller offers, and which one the current or last attack shows.
    public int AttackLookCount => _attackLookCount;
    public int AttackLook => _attackLook;
    // Read by tests and tuning tools.
    public EnemyTemperament Temperament => _temperament;
    public float HoverRadius => _ringRadius;
    public bool IsWindingUp => State == EnemyAgentState.Attack && !_attackHitChecked;

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
            _attackLookCount = 0;
            for (int i = 0; i < AttackLooks.Length; i++)
                if (_animator.HasState(0, AttackLooks[i]))
                    _attackLooks[_attackLookCount++] = AttackLooks[i];
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
        SetColor(ReadyColor);
    }

    // Puts a freshly activated enemy on the cells of a level with floors. Null keeps the flat-level movement.
    public void UseNavigation(EnemyNavField nav)
    {
        _nav = nav;
        _navCell = nav != null ? nav.FindNearestCell(transform.position, 6, 3f) : EnemyNavField.None;
        _steerCell = EnemyNavField.None;
        _steerVersion = -1;
        _falling = false;
        _fallSpeed = 0f;
        if (_navCell == EnemyNavField.None)
            _nav = null;   // spawned off the cells: move as in a flat level rather than not at all
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
        if (_falling)
        {
            // Off an edge and in the air: nothing to decide until it lands (RunAction moves it).
            ChangeState(EnemyAgentState.Chase);
            ShowLocomotion(true, settings);
            return;
        }
        Vector3 toTarget = target - transform.position;
        float heightGap = Mathf.Abs(toTarget.y);
        toTarget.y = 0f;
        float distance = toTarget.magnitude;
        Vector3 toward = distance > 0.0001f ? toTarget / distance : transform.forward;
        // Flat level: range is measured on the plane, as before. Level with floors: the player must also be on this
        // floor and a short walk away, so nobody attacks through a deck, a wall or a railing.
        bool onCells = _nav != null;
        bool inRange = distance <= settings.AttackRange &&
            (!onCells || (heightGap <= settings.VerticalReach && _nav.PathDistance(_navCell) <= settings.AttackRange + PathSlack));
        // A lively enemy attacks only when its turn has come: a few may attack at once (EnemyRuntime hands out the
        // turns) and the careful ones hover a while first.
        bool lively = settings.Liveliness > 0f;
        bool turnCome = !lively || _temperament == EnemyTemperament.Rusher || _hoverTime >= _commitAfter;
        // A lively enemy steps in well inside its reach before it swings, so the strike it telegraphs can land.
        float strikeDistance = lively ? Mathf.Min(settings.AttackRange, settings.AttackReach * 0.8f) : settings.AttackRange;
        bool canStrike = inRange && distance <= strikeDistance;
        if (canStrike && _attackCooldown <= 0f && turnCome && TakeAttackSlot())
        {
            StartAttack(settings);
            return;
        }

        // Out of range: close in, slowing down where the crowd pushes back instead of side-stepping at full speed.
        // In range but cooling down: hold the distance, watch the player and only shuffle sideways to make room,
        // so the ring around the player spreads out instead of piling up or pulsing in and out of range.
        Vector3 separation = ScanNeighbours(neighbours, settings.SeparationDistance, settings.BodySpacing,
            onCells ? settings.VerticalReach : float.PositiveInfinity, tick) * settings.SeparationWeight;
        // Where to walk: straight at the player on open ground, along the shared path where a wall, a drop or
        // another floor is in the way.
        Vector3 heading = toward;
        bool direct = !onCells || _nav.Goal == EnemyNavField.None || _nav.HasDirectPath(_navCell);
        if (!direct)
            heading = PathHeading(toward);
        Vector3 desired = Vector3.zero;
        bool facePlayer = false;
        if (lively && direct)
        {
            ChangeState(EnemyAgentState.Chase);
            desired = LivelyMove(toward, distance, inRange, strikeDistance, turnCome, separation, settings, deltaTime, out facePlayer);
        }
        else if (!inRange)
        {
            ChangeState(EnemyAgentState.Chase);
            Vector3 steer = heading + separation;
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
            Travel(_velocity * deltaTime, deltaTime, !inRange, true);

        bool followPath = !inRange && _velocity.sqrMagnitude > 1f &&
            (!direct || distance > settings.AttackRange * FacePlayerDistanceFactor);
        Vector3 facing = followPath ? _velocity : toward;
        if (lively && direct)
            facing = facePlayer || _velocity.sqrMagnitude < 0.25f ? toward : _velocity;   // eyes on the player up close
        if (facing.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(facing, Vector3.up), settings.TurnSpeed * deltaTime);

        ShowLocomotion(desired.sqrMagnitude > 0.0001f, settings);
    }

    private void StartAttack(EnemySettings settings)
    {
        _velocity = Vector3.zero;
        ChangeState(EnemyAgentState.Attack);
        _attackHitChecked = false;
        _attackElapsed = 0f;
        _stateTime = settings.AttackTotal;
        float spread = settings.Liveliness > 0f ? settings.AttackIntervalSpread : 0f;
        _attackCooldown = settings.AttackInterval * (1f + spread * (MindValue() * 2f - 1f));
        // The swing reaches its contact frame exactly when the wind-up ends: a long wind-up raises the arm slowly.
        _animationSpeed = Mathf.Clamp(settings.AttackDuration * 0.5f / Mathf.Max(0.01f, settings.AttackWindup), 0.05f, 1f);
        ApplyAnimatorSpeed();
        if (settings.TelegraphsAttacks)
            SetColor(WarningColor);
        // After a swing it backs off and waits again, a little less long than the first time.
        _hoverTime = 0f;
        _commitAfter = CommitDelay(settings) * 0.6f;
    }

    // Where a lively enemy wants to go this frame (m/s). Far away everyone runs in, flankers curving around the side.
    // Closer, they slow down and size the player up (the hesitant ones stop and go). In the ring around the player,
    // the ones whose turn has not come keep their own distance, circle, now and then turn round or feint, and back
    // off if they got too close; when their turn comes they step in to attack.
    private Vector3 LivelyMove(Vector3 toward, float distance, bool inRange, float strikeDistance, bool turnCome,
        Vector3 separation, EnemySettings settings, float deltaTime, out bool facePlayer)
    {
        float speed = settings.MoveSpeed * _speedScale;
        float sizeUp = settings.HoverRingMaximum + settings.SizeUpDistance;
        var around = new Vector3(toward.z, 0f, -toward.x) * _circleSign;
        bool stepIn = turnCome && _attackCooldown <= 0f && AttackSlotOpen();
        facePlayer = distance < sizeUp;
        Vector3 want;
        if (distance > sizeUp)
        {
            float swing = _temperament == EnemyTemperament.Flanker
                ? settings.FlankAngle * _flankSign * Mathf.Clamp01((distance - sizeUp) / 6f) : 0f;
            want = Quaternion.AngleAxis(swing, Vector3.up) * toward * speed;
        }
        else if (distance > _ringRadius + 0.6f || (stepIn && distance > strikeDistance))
        {
            float pace = stepIn ? 0.9f : 0.6f;
            if (!stepIn && _temperament == EnemyTemperament.Hesitant && Pace(deltaTime)) pace = 0f;
            want = toward * (speed * pace) + around * (speed * settings.CircleSpeedShare * 0.35f);
        }
        else
        {
            _hoverTime += deltaTime;
            _strafeTimer -= deltaTime;
            if (_strafeTimer <= 0f)
            {
                _circleSign = -_circleSign;
                _strafeTimer = 1.6f + 2.4f * MindValue();
            }
            float radial = Mathf.Clamp((distance - _ringRadius) * 1.5f, -1f, 1f);   // above 0: too far, step in
            if (inRange) radial = Mathf.Min(radial, -0.7f);                          // too close without a turn: back off
            want = toward * (speed * (radial * 0.6f + Feint(deltaTime))) + around * (speed * settings.CircleSpeedShare);
        }
        want += separation * (speed * 0.5f);
        want.y = 0f;
        float length = want.magnitude;
        return length > speed ? want * (speed / length) : want;
    }

    // Stop and go: walks a little, stops to look, walks on.
    private bool Pace(float deltaTime)
    {
        _paceTimer -= deltaTime;
        if (_paceTimer <= 0f)
        {
            _pacePaused = !_pacePaused;
            _paceTimer = _pacePaused ? 0.3f + 0.45f * MindValue() : 0.7f + 0.6f * MindValue();
        }
        return _pacePaused;
    }

    // Circlers and hesitant enemies now and then dart in a step and pull back: a share of Move Speed toward the player.
    private float Feint(float deltaTime)
    {
        if (_temperament != EnemyTemperament.Circler && _temperament != EnemyTemperament.Hesitant)
            return 0f;
        if (_feintLeft > 0f)
        {
            _feintLeft -= deltaTime;
            return _feintLeft > 0.25f ? 1.1f : -0.9f;
        }
        _feintTimer -= deltaTime;
        if (_feintTimer > 0f)
            return 0f;
        _feintLeft = 0.45f;
        _feintTimer = 2.5f + 3f * MindValue();
        return 1.1f;
    }

    private bool AttackSlotOpen() => _owner == null || _owner.AttackSlotOpen;

    private bool TakeAttackSlot() => _owner == null || _owner.TryTakeAttackSlot(this);

    // Picks this enemy's temperament and its own rhythm. Uses its own random stream, so the same enemy in the same
    // round always behaves the same way.
    private void PickTemperament(EnemySettings settings, float variation)
    {
        _mindRandom = (uint)(Mathf.Clamp01(variation) * 16777215f) * 2246822519u + (uint)ActiveTick * 3266489917u + 7u;
        if (_mindRandom == 0u) _mindRandom = 1u;
        Vector4 shares = settings != null ? settings.TemperamentShares : new Vector4(1f, 0f, 0f, 0f);
        float total = Mathf.Max(0.0001f, shares.x + shares.y + shares.z + shares.w);
        float roll = MindValue() * total;
        _temperament = roll < shares.x ? EnemyTemperament.Rusher
            : roll < shares.x + shares.y ? EnemyTemperament.Flanker
            : roll < shares.x + shares.y + shares.z ? EnemyTemperament.Circler
            : EnemyTemperament.Hesitant;
        float near = settings != null ? settings.HoverRingMinimum : 2.8f;
        float far = settings != null ? settings.HoverRingMaximum : 4.6f;
        _ringRadius = Mathf.Lerp(near, far, MindValue());
        _circleSign = MindValue() < 0.5f ? -1f : 1f;
        _flankSign = MindValue() < 0.5f ? -1f : 1f;
        _strafeTimer = 1.5f + 2f * MindValue();
        _hoverTime = 0f;
        _commitAfter = CommitDelay(settings);
        _paceTimer = 0.6f + 0.6f * MindValue();
        _pacePaused = false;
        _feintTimer = 1.5f + 2.5f * MindValue();
        _feintLeft = 0f;
    }

    // How long this enemy hovers before it steps in: rushers hardly at all, circlers longest.
    private float CommitDelay(EnemySettings settings)
    {
        if (settings == null) return 0f;
        float care = _temperament == EnemyTemperament.Rusher ? 0f
            : _temperament == EnemyTemperament.Flanker ? 0.35f
            : _temperament == EnemyTemperament.Hesitant ? 0.65f : 1f;
        Vector2 range = settings.CommitDelay;
        return Mathf.Lerp(range.x, range.y, Mathf.Clamp01(care * (0.7f + 0.6f * MindValue())));
    }

    private float MindValue()
    {
        _mindRandom ^= _mindRandom << 13;
        _mindRandom ^= _mindRandom >> 17;
        _mindRandom ^= _mindRandom << 5;
        return (_mindRandom >> 8) * (1f / 16777216f);
    }

    private void TurnToward(Vector3 direction, float maxDegrees)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f || maxDegrees <= 0f)
            return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction, Vector3.up), maxDegrees);
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
        if (_nav != null && Mathf.Abs(away.y) > settings.VerticalReach)
            return;   // the player is on another floor
        away.y = 0f;
        float squared = away.sqrMagnitude;
        if (squared >= radius * radius)
            return;

        float distance = Mathf.Sqrt(squared);
        Vector3 direction = distance > 0.001f ? away / distance : -transform.forward;
        Travel(direction * Mathf.Min(radius - distance, settings.PlayerPushSpeed * deltaTime), deltaTime, false, false);
    }

    // Moves the enemy. On the cells of a level with floors the move stops at walls and drops and follows the
    // ground. The velocity is then cut to what was really walked, so an enemy pressed against a wall stands or
    // slides along it instead of running on the spot.
    // chasing: the enemy is following the path to the player and may step off an edge where the path does.
    // ownMove: the step is the enemy's own walking (not a push), so its velocity should match what came of it.
    private void Travel(Vector3 step, float deltaTime, bool chasing, bool ownMove)
    {
        if (_nav == null)
        {
            transform.position += step;
            return;
        }
        if (_falling)
            return;   // in the air: Fall() moves it

        Vector3 from = transform.position;
        Vector3 to = from + step;
        _navCell = _nav.Move(_navCell, ref to, chasing);
        float ground = _nav.SampleHeight(_navCell, to);
        if (from.y - ground > FallStartHeight)
        {
            // Stepped off an edge. Leave the wall behind in one arc and be over the landing spot before touching down.
            Vector3 landing = _nav.Position(_navCell);
            float airTime = Mathf.Sqrt(2f * (from.y - landing.y) / FallGravity);
            Vector3 across = landing - to;
            across.y = 0f;
            _falling = true;
            _fallSpeed = 0f;
            _fallTarget = landing;
            _fallDrift = Mathf.Max(LeapSpeed, across.magnitude / Mathf.Max(0.05f, airTime));
            if (across.sqrMagnitude > 0.0001f)
                _velocity = across.normalized * _fallDrift;
            to.y = from.y;
            transform.position = to;
            return;
        }
        // Feet follow the ground at a limited rate, so a kerb or a cell seam is a short ramp and not a pop.
        to.y = Mathf.MoveTowards(from.y, ground, GroundFollowSpeed * Mathf.Max(deltaTime, 0.001f));
        transform.position = to;
        if (!ownMove || deltaTime <= 0f)
            return;
        Vector3 walked = to - from;
        walked.y = 0f;
        if (walked.sqrMagnitude < step.sqrMagnitude * 0.98f)
            _velocity = walked / deltaTime;
    }

    // Brings down an enemy that stepped off an edge. Runs with the other results already in progress, so a fall
    // that began before the round ended still lands.
    private void Fall(float deltaTime)
    {
        if (!_falling)
            return;
        if (_nav == null || _navCell == EnemyNavField.None || (State != EnemyAgentState.Idle && State != EnemyAgentState.Chase))
        {
            _falling = false;
            _fallSpeed = 0f;
            return;
        }

        Vector3 position = transform.position;
        Vector3 across = _fallTarget - position;
        across.y = 0f;
        float distance = across.magnitude;
        if (distance > 0.001f)
            position += across * (Mathf.Min(distance, _fallDrift * deltaTime) / distance);
        float ground = _nav.SampleHeight(_navCell, position);
        _fallSpeed += FallGravity * deltaTime;
        position.y -= _fallSpeed * deltaTime;
        if (position.y <= ground)
        {
            position.y = ground;
            _falling = false;
            _fallSpeed = 0f;
        }
        transform.position = position;
    }

    // Direction along the shared path. The point walked to is looked up again only when the enemy has entered
    // another cell or the path has been refreshed.
    private Vector3 PathHeading(Vector3 fallback)
    {
        if (_steerCell != _navCell || _steerVersion != _nav.Version)
        {
            _steerCell = _navCell;
            _steerVersion = _nav.Version;
            _steerPoint = _nav.SteerPoint(_navCell);
        }
        Vector3 to = _steerPoint - transform.position;
        to.y = 0f;
        float length = to.magnitude;
        return length > 0.05f ? to / length : fallback;
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

        Fall(deltaTime);
        if (State == EnemyAgentState.Attack)
        {
            _attackElapsed += deltaTime;
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

    // The strike: checked once, when the wind-up ends (halfway through the attack animation unless a wind-up is set).
    // While a telegraphed attack winds up, the enemy keeps turning toward the player, but slowly, so stepping aside
    // can beat it. The strike lands only within Attack Reach and inside the Attack Arc in front of the enemy.
    public bool TryAttackHit(Vector3 target, EnemySettings settings, float deltaTime, int tick)
    {
        if (tick != ActiveTick || State != EnemyAgentState.Attack || _attackHitChecked)
            return false;

        Vector3 distance = target - transform.position;
        if (_attackElapsed + deltaTime < settings.AttackWindup)
        {
            if (settings.TelegraphsAttacks)
                TurnToward(distance, settings.WindupTurnSpeed * deltaTime);
            return false;
        }

        _attackHitChecked = true;
        _animationSpeed = 1f;   // the swing itself at full speed
        ApplyAnimatorSpeed();
        if (settings.TelegraphsAttacks)
            SetColor(ReadyColor);
        if (_nav != null && Mathf.Abs(distance.y) > settings.VerticalReach)
            return false;   // the player left this floor during the wind-up
        distance.y = 0f;
        float reach = settings.AttackReach;
        if (distance.sqrMagnitude > reach * reach)
            return false;
        if (settings.AttackArcDegrees >= 359.9f || distance.sqrMagnitude < 0.0001f)
            return true;
        return Vector3.Angle(transform.forward, distance) <= settings.AttackArcDegrees * 0.5f;
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
        _owner?.ReleaseAttackSlot(this);
        State = EnemyAgentState.Pooled;
        ActiveTick = -1;
        _acceptedHit = false;
        _attackHitChecked = false;
        _attackCooldown = 0f;
        _stateTime = 0f;
        _velocity = Vector3.zero;
        _nav = null;
        _navCell = EnemyNavField.None;
        _steerCell = EnemyNavField.None;
        _falling = false;
        _fallSpeed = 0f;
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
        // Own random stream for the attack look: the same enemy in the same round always shows the same sequence.
        _attackRandom = (uint)(Mathf.Clamp01(variation) * 16777215f) * 747796405u + (uint)ActiveTick * 2891336453u + 1u;
        _attackLook = -1;   // no attack shown yet, so the first pick is not pushed off any look
        // A different mix of the same number, so the fastest enemy is not also always the largest.
        float size = 1f + Mathf.Sin(centered * 12.9f) * (settings != null ? settings.ScaleVariance : 0f);
        _visualScale = _visualDefaultScale * size;
        PickTemperament(settings, variation);
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
    // verticalGap: neighbours further above or below than this are on another floor and ignored (infinity in a flat level).
    private Vector3 ScanNeighbours(IReadOnlyList<EnemyAgent> neighbours, float distance, float bodySpacing, float verticalGap, int tick)
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
            if (Mathf.Abs(away.y) > verticalGap)
                continue;
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
        if (previous == EnemyAgentState.Attack)
        {
            // The attack is over (or cut short by a hit): give the turn back and drop the warning colour.
            _owner?.ReleaseAttackSlot(this);
            if (!_attackHitChecked && _owner != null && _owner.Settings != null && _owner.Settings.TelegraphsAttacks)
                SetColor(ReadyColor);
        }
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
                _shownAnimation = NextAttackLook();
                _animator.CrossFadeInFixedTime(_shownAnimation, AttackBlendSeconds, 0, 0f);
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

    // Random look for the attack that starts now, never the same one twice in a row when there is a choice.
    private int NextAttackLook()
    {
        if (_attackLookCount <= 1)
        {
            _attackLook = 0;
            return _attackLookCount == 1 ? _attackLooks[0] : AttackState;
        }
        _attackRandom ^= _attackRandom << 13;
        _attackRandom ^= _attackRandom >> 17;
        _attackRandom ^= _attackRandom << 5;
        int pick = (int)(_attackRandom % (uint)_attackLookCount);
        if (pick == _attackLook)
            pick = (pick + 1) % _attackLookCount;
        _attackLook = pick;
        return _attackLooks[pick];
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
