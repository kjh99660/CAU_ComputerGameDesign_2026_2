using System.Collections.Generic;
using UnityEngine;

public class PlayerAttackHitbox : MonoBehaviour, IPlayerAttackHitSource
{
    [SerializeField] private LayerMask enemyMask = ~0;      // Enemy 레이어를 만들면 그것만 선택
    [SerializeField] private AttackHitData gizmoPreview;    // Scene 뷰에서 미리 볼 공격
    [SerializeField] private bool debugLog = false;         // 켜면 판정 과정을 콘솔에 출력 (원인 확인용)

    private readonly Collider[] _buffer = new Collider[64];
    private readonly HashSet<EnemyAgent> _hitThisSwing = new();
    private AttackHitData _current;
    private float _scale = 1f;              // 이번 공격의 범위 배율 (차지 공격이면 차지 시간에 따라 커짐)
    private EnemyRuntime _enemies;
    private GameStateManager _state;
    private PlayerChargeTracker _charge;
    private CombatVfxRuntime _vfx;
    private int _attackTick = -1;

    // ── 디버그용 기록 ──
    private int _dbgSphere, _dbgHeight, _dbgAngle;   // 마지막 검사의 단계별 통과 수
    private int _dbgLastSphere = -1;                 // 같은 값이면 로그를 반복하지 않음
    private int _dbgFrames, _dbgHits, _dbgRejected;  // 이번 공격 동안 검사 프레임 수, 맞은 수, TryApplyHit 거절 수

    private void Start()
    {
        // 메인 씬에만 존재. Character Test 씬에서는 null이라 판정을 건너뜀.
        _enemies = FindFirstObjectByType<EnemyRuntime>();
        _state = FindFirstObjectByType<GameStateManager>();
        _charge = GetComponent<PlayerChargeTracker>();   // 없으면 차지 배율 없이 1배
        _vfx = FindFirstObjectByType<CombatVfxRuntime>();
    }

    // ── 애니메이션 이벤트 ──
    public void HitStart(AttackHitData data)
    {
        if (_state == null || (_state.CurrentState != GameState.Battle &&
            _state.CurrentState != GameState.Finishing)) return;
        _attackTick = _state.Tick;
        _current = data;
        _scale = (data != null && _charge != null) ? data.GetChargeScale(_charge.ChargeTime) : 1f;
        _hitThisSwing.Clear();
        if (data != null) _vfx?.PlaySlash(transform, _scale, _attackTick);

        if (debugLog)
        {
            _dbgLastSphere = -1; _dbgFrames = 0; _dbgHits = 0; _dbgRejected = 0;
            string shapeInfo = "";
            if (data != null && data.shape == AttackHitData.HitShape.Sector)
            {
                data.GetSector(_scale, out float r, out float a0, out float a1);
                shapeInfo = $" Sector({data.sectorGrowth}) radius={r:F2} angle={a0:F0}~{a1:F0}";
            }
            else if (data != null)
            {
                data.GetScaledBox(_scale, out Vector3 c, out Vector3 sz);
                shapeInfo = $" Box center={c} size={sz}";
            }
            Debug.Log($"[Hitbox] HitStart {(data != null ? data.name : "null")} " +
                      $"chargeTime={(_charge != null ? _charge.ChargeTime : -1f):F2} scale={_scale:F2}{shapeInfo} " +
                      $"| EnemyRuntime={(_enemies != null ? "OK" : "없음")} GameStateManager={(_state != null ? "OK" : "없음")} " +
                      $"mask={enemyMask.value}", this);
        }

        CheckHits();                 // 시작 프레임에도 바로 검사
    }

    public void HitEnd()
    {
        if (debugLog && _current != null)
            Debug.Log($"[Hitbox] HitEnd {_current.name} | 검사 프레임={_dbgFrames} 맞음={_dbgHits} 거절(TryApplyHit false)={_dbgRejected}", this);
        _current = null;
    }

    private void LateUpdate()
    {
        if (_current != null) CheckHits();
    }

    private void CheckHits()
    {
        if (_current == null || _enemies == null || _state == null) return;
        if (_attackTick != _state.Tick ||
            (_state.CurrentState != GameState.Battle && _state.CurrentState != GameState.Finishing)) return;

        int count = _current.shape == AttackHitData.HitShape.Sector ? QuerySector() : QueryBox();
        int enemyCount = 0, newHits = 0;

        float knockback = _current.knockbackMultiplier * (_current.chargeScalesKnockback ? _scale : 1f);
        for (int i = 0; i < count; i++)
        {
            if (!_buffer[i].TryGetComponent(out EnemyAgent enemy)) continue;
            enemyCount++;
            if (!_hitThisSwing.Add(enemy)) continue;           // 이번 공격에서 이미 맞음
            Vector3 dir = enemy.transform.position - transform.position;
            bool ok = _enemies.TryApplyHit(enemy, dir, knockback, _state.Tick);
            if (ok) { newHits++; _dbgHits++; }
            else _dbgRejected++;
            if (debugLog && !ok)
                Debug.Log($"[Hitbox]   거절: {enemy.name} state={enemy.State} enemyTick={enemy.ActiveTick} stateTick={_state.Tick}", enemy);
        }

        if (debugLog)
        {
            _dbgFrames++;
            if (_current.shape == AttackHitData.HitShape.Box) { _dbgSphere = count; _dbgHeight = count; _dbgAngle = count; }
            if (_dbgSphere != _dbgLastSphere || newHits > 0)
            {
                _dbgLastSphere = _dbgSphere;
                Debug.Log($"[Hitbox]   {(_current.shape == AttackHitData.HitShape.Sector ? "sphere" : "box")}={_dbgSphere}" +
                          $" height통과={_dbgHeight} angle통과={_dbgAngle} 그중 적={enemyCount} 새로 맞음={newHits}", this);
            }
        }
    }

    // 상자 안의 적 콜라이더를 _buffer에 모은다.
    private int QueryBox()
    {
        _current.GetScaledBox(_scale, out Vector3 localCenter, out Vector3 size);
        Vector3 center = transform.TransformPoint(localCenter);
        return Physics.OverlapBoxNonAlloc(center, size * 0.5f, _buffer, transform.rotation, enemyMask);
    }

    // 부채꼴 안의 적 콜라이더를 _buffer 앞쪽에 모은다.
    // 1) 반지름 구로 후보를 모으고  2) 높이 범위  3) 각도 범위로 거른다.
    private int QuerySector()
    {
        _current.GetSector(_scale, out float radius, out float startAngle, out float endAngle);
        Vector3 origin = transform.position + Vector3.up * _current.center.y;
        float halfHeight = _current.size.y * 0.5f;
        int count = Physics.OverlapSphereNonAlloc(origin, radius, _buffer, enemyMask);
        _dbgSphere = count; _dbgHeight = 0;

        Vector3 forward = Flat(transform.forward);
        int kept = 0;
        for (int i = 0; i < count; i++)
        {
            Collider col = _buffer[i];

            // 높이: 적 콜라이더가 [origin.y - halfHeight, origin.y + halfHeight] 구간과 겹쳐야 함
            Bounds b = col.bounds;
            if (b.max.y < origin.y - halfHeight || b.min.y > origin.y + halfHeight) continue;
            _dbgHeight++;

            // 각도: 적 중심 방향이 [startAngle, endAngle] 안이어야 함 (정면 0°, 오른쪽 +)
            Vector3 dir = Flat(col.transform.position - transform.position);
            if (dir.sqrMagnitude > 0.0001f && endAngle - startAngle < 359.9f)
            {
                float a = Vector3.SignedAngle(forward, dir, Vector3.up);
                if (!InAngleRange(a, startAngle, endAngle)) continue;
            }

            _buffer[kept++] = col;
        }
        _dbgAngle = kept;
        return kept;
    }

    // a(-180~180)가 [start, end] 범위 안인지. start가 -180보다 작은 경우(뒤쪽으로 넘어감)도 처리.
    private static bool InAngleRange(float a, float start, float end)
    {
        if (a >= start && a <= end) return true;
        if (a + 360f >= start && a + 360f <= end) return true;
        if (a - 360f >= start && a - 360f <= end) return true;
        return false;
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.zero;
    }

    // ── 팀원 구조가 보내 주는 알림 (PlayerRuntime이 자동으로 찾아서 호출) ──
    public void ApplyState(GameState state) { }    // Pause 중에는 Animator가 멈추니 따로 할 일 없음
    public void ResetPresentation() { _current = null; _attackTick = -1; _scale = 1f; _hitThisSwing.Clear(); }

    private void OnDrawGizmosSelected()
    {
        AttackHitData d = _current != null ? _current : gizmoPreview;
        if (d == null) return;
        float s = _current != null ? _scale : 1f;
        Gizmos.color = _current != null ? Color.red : Color.yellow;

        if (d.shape == AttackHitData.HitShape.Sector)
        {
            d.GetSector(s, out float radius, out float startAngle, out float endAngle);
            Vector3 origin = transform.position + Vector3.up * d.center.y;
            Vector3 forward = Flat(transform.forward);
            if (forward == Vector3.zero) forward = Vector3.forward;
            int segments = Mathf.Max(2, Mathf.CeilToInt((endAngle - startAngle) / 5f));
            Vector3 prev = origin + Quaternion.AngleAxis(startAngle, Vector3.up) * forward * radius;
            Gizmos.DrawLine(origin, prev);
            for (int i = 1; i <= segments; i++)
            {
                float a = Mathf.Lerp(startAngle, endAngle, i / (float)segments);
                Vector3 p = origin + Quaternion.AngleAxis(a, Vector3.up) * forward * radius;
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
            Gizmos.DrawLine(origin, prev);
            return;
        }

        d.GetScaledBox(s, out Vector3 localCenter, out Vector3 size);
        Gizmos.matrix = Matrix4x4.TRS(transform.TransformPoint(localCenter), transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, size);
    }
}
