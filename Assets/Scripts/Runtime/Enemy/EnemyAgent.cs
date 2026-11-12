using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
public sealed class EnemyAgent : MonoBehaviour
{
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private MaterialPropertyBlock _properties;
    private Rigidbody _body;
    private Renderer[] _renderers;
    private Animator _animator;
    private Transform _visualRoot;
    private Vector3 _rootDefaultScale = Vector3.one;
    private Vector3 _visualDefaultPosition;
    private Quaternion _visualDefaultRotation;
    private Vector3 _visualDefaultScale = Vector3.one;
    private EnemyRuntime _owner;
    private float _attackCooldown;
    private float _stateTime;
    private bool _acceptedHit;
    private bool _attackHitChecked;

    private static readonly int IdleState = Animator.StringToHash("Idle");
    private static readonly int ChaseState = Animator.StringToHash("Chase");
    private static readonly int AttackState = Animator.StringToHash("Attack");
    private static readonly int KnockbackState = Animator.StringToHash("Knockback");
    private static readonly int DyingState = Animator.StringToHash("Dying");

    public Rigidbody Body => _body;
    public EnemyAgentState State { get; private set; } = EnemyAgentState.Pooled;
    public int ActiveTick { get; private set; } = -1;
    public bool WantsPoolReturn => State == EnemyAgentState.Dying && _stateTime <= 0f;

    private void Awake()
    {
        _body = GetComponent<Rigidbody>();
        _rootDefaultScale = transform.localScale;
        _renderers = GetComponentsInChildren<Renderer>(true);
        _animator = GetComponent<Animator>();
        _visualRoot = transform.Find("Visual");
        if (_visualRoot != null)
        {
            _visualDefaultPosition = _visualRoot.localPosition;
            _visualDefaultRotation = _visualRoot.localRotation;
            _visualDefaultScale = _visualRoot.localScale;
        }
        _properties = new MaterialPropertyBlock();
    }

    public void Initialize(EnemyRuntime owner)
    {
        _owner = owner;
        if (_body == null)
            Awake();
    }

    public void Activate(Vector3 position, int tick)
    {
        gameObject.SetActive(true);
        transform.SetPositionAndRotation(position, Quaternion.identity);
        transform.localScale = _rootDefaultScale;
        ActiveTick = tick;
        if (_animator != null)
        {
            _animator.Rebind();
            _animator.speed = 1f;
        }
        ResetVisualPose();
        ChangeState(EnemyAgentState.Idle, true);
        if (_animator != null)
            _animator.Update(0f);
        _acceptedHit = false;
        _attackHitChecked = false;
        _attackCooldown = 0f;
        _stateTime = 0f;
        _body.isKinematic = true;
        _body.useGravity = true;
        // An interpolated Rigidbody owns its physics pose independently of the
        // Transform. Teleport both on spawn/reuse so interpolation cannot restore
        // the prefab origin or the previous death/knockback position next frame.
        _body.position = position;
        _body.rotation = Quaternion.identity;
        SetColor(new Color(0.9f, 0.22f, 0.18f, 1f));
    }

    public void SetIdle(int tick)
    {
        if (tick != ActiveTick || State == EnemyAgentState.Pooled)
            return;
        ResetVisualPose();
        ChangeState(EnemyAgentState.Idle, true);
        if (_animator != null)
            _animator.Update(0f);
        _body.isKinematic = true;
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
        if (toTarget.sqrMagnitude <= settings.AttackRange * settings.AttackRange)
        {
            if (_attackCooldown <= 0f)
            {
                ChangeState(EnemyAgentState.Attack);
                _attackHitChecked = false;
                _stateTime = settings.AttackDuration;
                _attackCooldown = settings.AttackInterval;
            }
            return;
        }

        ChangeState(EnemyAgentState.Chase);
        Vector3 separation = CalculateSeparation(neighbours, settings.SeparationDistance, tick);
        Vector3 direction = toTarget.normalized + separation * settings.SeparationWeight;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
            return;

        direction.Normalize();
        transform.position += direction * (settings.MoveSpeed * deltaTime);
        transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
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
        ChangeState(EnemyAgentState.Knockback);
        _stateTime = settings.KnockbackFallbackDuration;
        _body.isKinematic = false;
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
        _body.isKinematic = true;
        State = EnemyAgentState.Pooled;
        ActiveTick = -1;
        _acceptedHit = false;
        _attackHitChecked = false;
        _attackCooldown = 0f;
        _stateTime = 0f;
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

    public void SetAnimationPaused(bool paused)
    {
        if (_animator != null)
            _animator.speed = paused ? 0f : 1f;
    }

    private Vector3 CalculateSeparation(IReadOnlyList<EnemyAgent> neighbours, float distance, int tick)
    {
        Vector3 result = Vector3.zero;
        float distanceSquared = distance * distance;
        for (int i = 0; i < neighbours.Count; i++)
        {
            EnemyAgent other = neighbours[i];
            if (other == null || other == this || other.ActiveTick != tick || other.State == EnemyAgentState.Pooled)
                continue;

            Vector3 away = transform.position - other.transform.position;
            away.y = 0f;
            float squared = away.sqrMagnitude;
            if (squared <= 0.0001f || squared > distanceSquared)
                continue;
            result += away.normalized * (1f - Mathf.Sqrt(squared) / distance);
        }
        return result;
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
        _body.isKinematic = true;
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

    private void ChangeState(EnemyAgentState state, bool restart = false)
    {
        if (!restart && State == state)
            return;

        State = state;
        if (_animator == null)
            return;

        int stateHash = state switch
        {
            EnemyAgentState.Idle => IdleState,
            EnemyAgentState.Chase => ChaseState,
            EnemyAgentState.Attack => AttackState,
            EnemyAgentState.Knockback => KnockbackState,
            EnemyAgentState.Dying => DyingState,
            _ => 0
        };
        if (stateHash != 0)
            _animator.Play(stateHash, 0, 0f);
    }

    private void ResetVisualPose()
    {
        if (_visualRoot == null)
            return;
        _visualRoot.localPosition = _visualDefaultPosition;
        _visualRoot.localRotation = _visualDefaultRotation;
        _visualRoot.localScale = _visualDefaultScale;
    }
}
