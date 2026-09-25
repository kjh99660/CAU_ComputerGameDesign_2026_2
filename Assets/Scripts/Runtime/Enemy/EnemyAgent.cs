using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider), typeof(Rigidbody), typeof(MeshRenderer))]
public sealed class EnemyAgent : MonoBehaviour
{
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private MaterialPropertyBlock _properties;
    private Rigidbody _body;
    private Renderer _renderer;
    private EnemyRuntime _owner;
    private float _attackCooldown;
    private float _stateTime;
    private bool _acceptedHit;

    public Rigidbody Body => _body;
    public EnemyAgentState State { get; private set; } = EnemyAgentState.Pooled;
    public int ActiveTick { get; private set; } = -1;
    public bool WantsPoolReturn => State == EnemyAgentState.Dying && _stateTime <= 0f;

    private void Awake()
    {
        _body = GetComponent<Rigidbody>();
        _renderer = GetComponent<Renderer>();
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
        ActiveTick = tick;
        State = EnemyAgentState.Idle;
        _acceptedHit = false;
        _attackCooldown = 0f;
        _stateTime = 0f;
        _body.isKinematic = true;
        _body.useGravity = true;
        SetColor(new Color(0.9f, 0.22f, 0.18f, 1f));
    }

    public void SetIdle(int tick)
    {
        if (tick != ActiveTick || State == EnemyAgentState.Pooled)
            return;
        State = EnemyAgentState.Idle;
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
                State = EnemyAgentState.Attack;
                _stateTime = settings.AttackDuration;
                _attackCooldown = settings.AttackInterval;
            }
            return;
        }

        State = EnemyAgentState.Chase;
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
                State = EnemyAgentState.Chase;
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

    public bool ApplyHit(Vector3 impulse, EnemySettings settings, int tick)
    {
        if (tick != ActiveTick || _acceptedHit || State == EnemyAgentState.Pooled || State == EnemyAgentState.Dying)
            return false;

        _acceptedHit = true;
        State = EnemyAgentState.Knockback;
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
        transform.SetParent(poolRoot, false);
        gameObject.SetActive(false);
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
        State = EnemyAgentState.Dying;
        _stateTime = Mathf.Max(0f, duration);
        SetColor(new Color(0.22f, 0.22f, 0.24f, 1f));
    }

    private void SetColor(Color color)
    {
        if (_renderer == null)
            return;
        _properties ??= new MaterialPropertyBlock();
        _renderer.GetPropertyBlock(_properties);
        _properties.SetColor(BaseColor, color);
        _renderer.SetPropertyBlock(_properties);
    }
}
