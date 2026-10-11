using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerComboAttack : MonoBehaviour
{
    private Animator anim;

    [Header("Combo Timing Settings (0.0 ~ 1.0)")]
    [Range(0f, 1f)] public float comboWindowStart = 0.3f;
    [Range(0f, 1f)] public float comboWindowEnd = 0.75f;

    [Header("Charge Attack Settings")]
    [Range(0f, 1f)] public float chargeHoldTime = 0.053f;

    private bool isPausedForCharge = false;
    public bool IsAttacking { get; private set; }

    private void Start()
    {
        anim = GetComponent<Animator>();

        if (anim == null)
        {
            Debug.LogError("[PlayerComboAttack] 캐릭터에 Animator 컴포넌트가 없습니다!");
        }
    }

    private void Update()
    {
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            Debug.Log("★ [INPUT] 마우스 좌클릭 신호 수신됨!");
        }

        if (Mouse.current == null || anim == null) return;

        AnimatorStateInfo stateInfo = anim.GetCurrentAnimatorStateInfo(0);
        float normalizedTime = stateInfo.normalizedTime;

        IsAttacking = stateInfo.IsName("Attack1") ||
                      stateInfo.IsName("Attack2") ||
                      stateInfo.IsName("ChargeAttack");

        // 마우스 클릭 시 콤보 로직 수행
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            Debug.Log($"[LOG] 현재 상태: {stateInfo.shortNameHash} | Idle인가?: {stateInfo.IsName("Idle")} | 진행률: {(normalizedTime % 1f):F2}");

            if (stateInfo.IsName("Idle"))
            {
                Debug.Log("-> [SUCCESS] Idle 진입! Attack1 실행");
                anim.SetTrigger("Attack");
            }
            else if (stateInfo.IsName("Attack1") || stateInfo.IsName("Attack2"))
            {
                float currentComboTime = normalizedTime % 1f;
                if (currentComboTime >= comboWindowStart && currentComboTime <= comboWindowEnd)
                {
                    Debug.Log($"-> [SUCCESS] 콤보 성공! (진행률: {currentComboTime:F2}) -> 다음 공격 실행");
                    anim.SetTrigger("Attack");
                }
                else
                {
                    Debug.LogWarning($"-> [FAIL] 콤보 타이밍 실패! (현재 진행률: {currentComboTime:F2})");
                }
            }
        }

        // 차지 공격 로직
        if (stateInfo.IsName("ChargeAttack"))
        {
            anim.ResetTrigger("Attack");

            float chargeProgress = normalizedTime % 1f;

            if (chargeProgress >= chargeHoldTime && Mouse.current.leftButton.isPressed && !isPausedForCharge)
            {
                Debug.Log($"[Charge] 차지 멈춤 동작 시작! (멈춘 시점 진행률: {chargeProgress:F3})");
                isPausedForCharge = true;
                anim.speed = 0f;
                anim.SetBool("isCharging", true); // ✅ 1. 소문자 "isCharging"으로 변경
            }

            if (isPausedForCharge && Mouse.current.leftButton.wasReleasedThisFrame)
            {
                Debug.Log("[Charge] 차지 공격 발사!");
                ResumeAnimation();
            }
        }
        else if (isPausedForCharge || anim.speed == 0f)
        {
            ResumeAnimation();
        }
    }

    void ResumeAnimation()
    {
        isPausedForCharge = false;
        anim.speed = 1f;
        anim.SetBool("isCharging", false); // ✅ 2. 소문자 "isCharging"으로 변경
    }

    private void OnDisable()
    {
        if (anim != null) anim.speed = 1f;
    }
}