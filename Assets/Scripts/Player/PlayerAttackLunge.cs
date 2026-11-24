using UnityEngine;

// Attack1, Attack2 상태에서 정해진 시간 구간 동안 일정한 속도로 앞으로 전진시킨다.
// 시간은 Animator 기준이라 Pause(애니메이터 정지), Finishing 슬로모션, Retry(Rebind)에 자동으로 맞춰진다.
[RequireComponent(typeof(CharacterController))]
public class PlayerAttackLunge : MonoBehaviour, IPlayerPresentation
{
    [SerializeField] private AttackHitData attack1;   // Attack1 상태에서 쓸 값
    [SerializeField] private AttackHitData attack2;   // Attack2 상태에서 쓸 값
    // 플레이어는 이제 적 무리에 몸이 막히지 않고 지나간다(PlayerRuntime). 전진만은 맨 앞 적 앞에서 멈춰야
    // 겨냥한 적을 지나쳐 공격이 빗나가지 않는다. (임시값)
    [SerializeField] private LayerMask stopAtLayers = 1 << 8;     // 전진을 멈출 대상 레이어 (기본: 8번 Enemy). 0이면 멈추지 않음
    [SerializeField, Min(0f)] private float stopMargin = 0.2f;    // 적 몸 앞에 남길 거리 (m)

    private Animator _animator;
    private CharacterController _controller;

    private int _trackedState;      // 지금 추적 중인 공격 상태 (0이면 없음)
    private float _lastElapsed;     // 지난 프레임의 경과 시간 (초)
    private float _lastProgress;    // 지난 프레임의 전진 진행도 (0~1)

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _controller = GetComponent<CharacterController>();
    }

    private void Update()
    {
        if (_animator == null || _controller == null || !_controller.enabled) return;

        // 콤보 전환 중이면 넘어가는 쪽(다음 공격)을 우선으로 본다.
        AnimatorStateInfo info = _animator.GetCurrentAnimatorStateInfo(0);
        if (_animator.IsInTransition(0))
        {
            AnimatorStateInfo next = _animator.GetNextAnimatorStateInfo(0);
            if (GetData(next) != null) info = next;
        }

        AttackHitData data = GetData(info);
        if (data == null || data.lungeDistance <= 0f || data.lungeEndTime <= data.lungeStartTime)
        {
            _trackedState = 0;
            return;
        }

        float elapsed = info.normalizedTime * info.length;   // 이 상태가 시작된 뒤 지난 시간 (초)

        // 새 공격이 시작됐으면 처음부터 다시 잰다.
        if (info.fullPathHash != _trackedState || elapsed < _lastElapsed)
        {
            _trackedState = info.fullPathHash;
            _lastProgress = 0f;
        }
        _lastElapsed = elapsed;

        float progress = Progress(data, elapsed);
        float delta = progress - _lastProgress;
        _lastProgress = progress;
        if (delta <= 0f) return;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        forward.Normalize();
        _controller.Move(forward * ClampToFirstBody(forward, data.lungeDistance * delta));
    }

    // 전진 경로에 적이 있으면 그 앞까지만 간다. 이미 몸이 겹친 적도 잡히도록 조금 뒤에서부터 쓸어 본다.
    private float ClampToFirstBody(Vector3 forward, float step)
    {
        if (stopAtLayers.value == 0 || step <= 0f) return step;
        const float runUp = 1.5f;
        float radius = _controller.radius * 0.6f;
        Vector3 origin = transform.position + _controller.center - forward * runUp;
        if (!Physics.SphereCast(origin, radius, forward, out RaycastHit hit, runUp + step + stopMargin + _controller.radius,
                stopAtLayers, QueryTriggerInteraction.Ignore))
            return step;
        // hit.distance는 쓸어 본 구의 중심이 간 거리. 플레이어 몸(반지름)이 적 몸에 닿기 전까지가 갈 수 있는 거리다.
        float allowed = hit.distance - runUp - (_controller.radius - radius) - stopMargin;
        return Mathf.Clamp(allowed, 0f, step);
    }

    // 경과 시간 → 전진 진행도 (0~1). 지금은 직선(일정한 속도).
    // 나중에 커브로 바꾸려면 AttackHitData에 AnimationCurve를 추가하고 여기 한 줄만 바꾸면 된다.
    private static float Progress(AttackHitData data, float elapsed)
    {
        return Mathf.InverseLerp(data.lungeStartTime, data.lungeEndTime, elapsed);
    }

    private AttackHitData GetData(AnimatorStateInfo info)
    {
        if (info.IsName("Attack1")) return attack1;
        if (info.IsName("Attack2")) return attack2;
        return null;
    }

    // 팀원 구조(PlayerRuntime)가 자동으로 호출. 시간은 Animator 기준이라 상태 변화 때 따로 할 일 없음.
    public void ApplyState(GameState state) { }
    public void ResetPresentation() => _trackedState = 0;
}
