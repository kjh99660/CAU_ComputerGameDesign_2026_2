using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerComboAttack : MonoBehaviour
{
    private Animator anim;

    [Header("Combo Timing Settings")]
    [Range(0f, 1f)] public float comboWindowStart = 0.3f;
    [Range(0f, 1f)] public float comboWindowEnd = 0.75f;

    public bool IsAttacking { get; private set; }

    private void Start()
    {
        anim = GetComponent<Animator>();
    }

    private void Update()
    {
        if (Mouse.current == null || anim == null) return;

        AnimatorStateInfo stateInfo = anim.GetCurrentAnimatorStateInfo(0);
        float normalizedTime = stateInfo.normalizedTime % 1f;

        IsAttacking = !stateInfo.IsName("Idle");

        // 1. 기본 콤보 입력 (Idle, Attack1, Attack2)
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (stateInfo.IsName("Idle"))
            {
                anim.SetTrigger("Attack");
            }
            else if (stateInfo.IsName("Attack1") || stateInfo.IsName("Attack2"))
            {
                if (normalizedTime >= comboWindowStart && normalizedTime <= comboWindowEnd)
                {
                    anim.SetTrigger("Attack");
                }
            }
        }

        // 2. 차지 홀드 중일 때 마우스 떼기 감지
        // 애니메이터가 "Charge_Hold" 스테이트에 진입해 있다면 마우스 떼기를 감시
        if (stateInfo.IsName("Charge_Hold"))
        {
            // 만약 마우스를 떼거나, 혹은 마우스를 누르고 있지 않다면 발사 트리거 On
            if (Mouse.current.leftButton.wasReleasedThisFrame)
            {
                anim.SetTrigger("Release"); // 애니메이터의 Charge_Release로 넘어가는 트리거
            }
        }
    }
}