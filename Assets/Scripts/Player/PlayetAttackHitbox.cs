using System.Collections.Generic;
using UnityEngine;

public class PlayerAttackHitbox : MonoBehaviour, IPlayerPresentation
{
    [SerializeField] private LayerMask enemyMask = ~0;      // Enemy 레이어를 만들면 그것만 선택
    [SerializeField] private AttackHitData gizmoPreview;    // Scene 뷰에서 미리 볼 공격

    private readonly Collider[] _buffer = new Collider[64];
    private readonly HashSet<EnemyAgent> _hitThisSwing = new();
    private AttackHitData _current;
    private EnemyRuntime _enemies;
    private GameStateManager _state;

    private void Start()
    {
        // 메인 씬에만 존재. Character Test 씬에서는 null이라 판정을 건너뜀.
        _enemies = FindFirstObjectByType<EnemyRuntime>();
        _state = FindFirstObjectByType<GameStateManager>();
    }

    // ── 애니메이션 이벤트 ──
    public void HitStart(AttackHitData data)
    {
        _current = data;
        _hitThisSwing.Clear();
        CheckHits();                 // 시작 프레임에도 바로 검사
    }

    public void HitEnd() => _current = null;

    private void LateUpdate()
    {
        if (_current != null) CheckHits();
    }

    private void CheckHits()
    {
        if (_current == null || _enemies == null || _state == null) return;

        Vector3 center = transform.TransformPoint(_current.center);
        int count = Physics.OverlapBoxNonAlloc(center, _current.size * 0.5f,
                        _buffer, transform.rotation, enemyMask);

        for (int i = 0; i < count; i++)
        {
            if (!_buffer[i].TryGetComponent(out EnemyAgent enemy)) continue;
            if (!_hitThisSwing.Add(enemy)) continue;           // 이번 공격에서 이미 맞음
            Vector3 dir = enemy.transform.position - transform.position;
            _enemies.TryApplyHit(enemy, dir, _current.knockbackMultiplier, _state.Tick);
        }
    }

    // ── 팀원 구조가 보내 주는 알림 (PlayerRuntime이 자동으로 찾아서 호출) ──
    public void ApplyState(GameState state) { }    // Pause 중에는 Animator가 멈추니 따로 할 일 없음
    public void ResetPresentation() { _current = null; _hitThisSwing.Clear(); }

    private void OnDrawGizmosSelected()
    {
        AttackHitData d = _current != null ? _current : gizmoPreview;
        if (d == null) return;
        Gizmos.color = _current != null ? Color.red : Color.yellow;
        Gizmos.matrix = Matrix4x4.TRS(transform.TransformPoint(d.center), transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, d.size);
    }
}