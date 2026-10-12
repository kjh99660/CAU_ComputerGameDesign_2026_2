using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerComboAttack : MonoBehaviour
{
    private Animator anim;
    private PlayerController playerController;

    [Header("Combo Timing Settings")]
    [Range(0f, 1f)] public float comboWindowStart = 0.3f;
    [Range(0f, 1f)] public float comboWindowEnd = 0.75f;

    public bool IsAttacking { get; private set; }

    private void Start()
    {
        anim = GetComponent<Animator>();
        playerController = GetComponent<PlayerController>();
    }

    private void Update()
    {
        if (Mouse.current == null || anim == null) return;

        AnimatorStateInfo stateInfo = anim.GetCurrentAnimatorStateInfo(0);
        float normalizedTime = stateInfo.normalizedTime % 1f;

        // [수정] 현재 재생 중인 애니메이션이 실제 공격/차지 관련 상태인지 확인
        bool isAttackState = stateInfo.IsName("Attack1") ||
                             stateInfo.IsName("Attack2") ||
                             stateInfo.IsName("Charge_Windup") ||
                             stateInfo.IsName("Charge_Hold") ||
                             stateInfo.IsName("Charge_Release");



        IsAttacking = isAttackState;

        // [수정] 공격 상태일 때만 이동/회전 차단, Idle이나 Run일 때는 이동 가능
        if (playerController != null)
        {
            playerController.canMove = !isAttackState;
        }

        // 1. 기본 콤보 입력 (Idle, Attack1, Attack2)
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (stateInfo.IsName("Idle") || stateInfo.IsTag("RunMotion")) // Run 중에도 첫 타 공격 진입 허용 시 추가 가능
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

        // 2. 차지 관련 로직
        if (stateInfo.IsName("Charge_Windup") || stateInfo.IsName("Charge_Hold"))
        {
            if (Mouse.current.leftButton.isPressed)
            {
                anim.SetBool("IsHolding", true);
            }

            if (Mouse.current.leftButton.wasReleasedThisFrame || !Mouse.current.leftButton.isPressed)
            {
                anim.SetBool("IsHolding", false);
            }
        }
        else
        {
            if (stateInfo.IsName("Idle") || stateInfo.IsName("Run"))
            {
                anim.SetBool("IsHolding", false);
            }
        }
    }
}