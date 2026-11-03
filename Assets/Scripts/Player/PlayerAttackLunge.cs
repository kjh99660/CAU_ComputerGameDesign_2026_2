using UnityEngine;

// Attack1, Attack2 상태에서 정해진 시간 구간 동안 일정한 속도로 앞으로 전진시킨다.
// 시간은 Animator 기준이라 Pause(애니메이터 정지), Finishing 슬로모션, Retry(Rebind)에 자동으로 맞춰진다.
[RequireComponent(typeof(CharacterController))]
public class PlayerAttackLunge : MonoBehaviour, IPlayerPresentation
{
    [SerializeField] private AttackHitData attack1;   // Attack1 상태에서 쓸 값
    [SerializeField] private AttackHitData attack2;   // Attack2 상태에서 쓸 값

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
        _controller.Move(forward.normalized * (data.lungeDistance * delta));
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
