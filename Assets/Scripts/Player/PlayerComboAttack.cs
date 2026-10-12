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

        // 2. Attack2 이후 윈드업이나 홀드 상태에 진입했을 때부터 마우스 누름 상태 동기화
        if (stateInfo.IsName("Charge_Windup") || stateInfo.IsName("Charge_Hold"))
        {
            // 마우스를 계속 누르고 있는 동안은 IsHolding을 true로 유지
            if (Mouse.current.leftButton.isPressed)
            {
                anim.SetBool("IsHolding", true);
            }

            // 아주 빨리 떼었거나, 누르고 있던 마우스를 떼는 순간
            if (Mouse.current.leftButton.wasReleasedThisFrame || !Mouse.current.leftButton.isPressed)
            {
                anim.SetBool("IsHolding", false);
            }
        }
        else
        {
            // 차지가 아닐 때는 IsHolding을 안전하게 false로 초기화
            if (stateInfo.IsName("Idle"))
            {
                anim.SetBool("IsHolding", false);
            }
        }
    }
}