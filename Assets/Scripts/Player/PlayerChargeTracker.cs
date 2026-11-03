using UnityEngine;

// 차지 상태(Charge_Windup, Charge_Hold)에 머문 시간을 잰다.
// 버튼을 떼서 Charge_Release로 넘어가면 그 순간 값이 고정되고, 다음 차지를 시작할 때 0부터 다시 잰다.
// Animator 속도를 곱해서 재기 때문에 Pause(애니메이터 정지) 중에는 늘지 않는다.
public class PlayerChargeTracker : MonoBehaviour, IPlayerPresentation
{
    public float ChargeTime { get; private set; }   // 마지막(또는 지금) 차지 시간 (초)
    public bool IsCharging { get; private set; }    // 지금 차지 중인지

    private Animator _animator;

    private void Awake() => _animator = GetComponent<Animator>();

    private void Update()
    {
        if (_animator == null) return;

        // 상태가 넘어가는 중이면 넘어갈 쪽 기준 (Attack2 → 차지 시작, 차지 → 발동을 바로 반영)
        AnimatorStateInfo info = _animator.IsInTransition(0)
            ? _animator.GetNextAnimatorStateInfo(0)
            : _animator.GetCurrentAnimatorStateInfo(0);

        bool charging = info.IsName("Charge_Windup") || info.IsName("Charge_Hold");
        if (charging)
        {
            if (!IsCharging) ChargeTime = 0f;                    // 새 차지 시작
            ChargeTime += Time.deltaTime * _animator.speed;
        }
        IsCharging = charging;
    }

    // ── 팀원 구조가 보내 주는 알림 (PlayerRuntime이 자동으로 찾아서 호출) ──
    public void ApplyState(GameState state) { }
    public void ResetPresentation()
    {
        ChargeTime = 0f;
        IsCharging = false;
    }
}
