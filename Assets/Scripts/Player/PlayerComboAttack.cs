using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerComboAttack : MonoBehaviour
{
    private Animator anim;

    [Header("Combo Timing Settings (0.0 ~ 1.0)")]
    [Range(0f, 1f)] public float comboWindowStart = 0.3f;
    [Range(0f, 1f)] public float comboWindowEnd = 0.75f;

    [Header("Charge Attack Settings")]
    [Range(0f, 1f)] public float chargeHoldTime = 0.5f;

    private bool isPausedForCharge = false;
    public bool IsAttacking { get; private set; }

    private void Start()
    {
        anim = GetComponent<Animator>();

        // Animator 컴포넌트가 누락되었는지 검사
        if (anim == null)
        {
            Debug.LogError("[PlayerComboAttack] 캐릭터에 Animator 컴포넌트가 없습니다!");
        }
    }

    private void Update()
    {
        // =========================================================================
        // [테스트 1] 최우선 마우스 클릭 감지 (이 로그조차 안 뜨면 컴포넌트 비활성화 상태)
        // =========================================================================
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            Debug.Log("★ [INPUT] 마우스 좌클릭 신호 수신됨!");
        }

        if (Mouse.current == null || anim == null) return;

        AnimatorStateInfo stateInfo = anim.GetCurrentAnimatorStateInfo(0);
        float normalizedTime = stateInfo.normalizedTime % 1f;

        IsAttacking = stateInfo.IsName("Attack1") ||
                      stateInfo.IsName("Attack2") ||
                      stateInfo.IsName("ChargeAttack");

        // 마우스 클릭 시 콤보 로직 수행
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            Debug.Log($"[LOG] 현재 상태: {stateInfo.shortNameHash} | Idle인가?: {stateInfo.IsName("Idle")} | 진행률: {normalizedTime:F2}");

            if (stateInfo.IsName("Idle"))
            {
                Debug.Log("-> [SUCCESS] Idle 진입! Attack1 실행");
                anim.SetTrigger("Attack");
            }
            else if (stateInfo.IsName("Attack1") || stateInfo.IsName("Attack2"))
            {
                if (normalizedTime >= comboWindowStart && normalizedTime <= comboWindowEnd)
                {
                    Debug.Log($"-> [SUCCESS] 콤보 성공! (진행률: {normalizedTime:F2}) -> 다음 공격 실행");
                    anim.SetTrigger("Attack");
                }
                else
                {
                    Debug.LogWarning($"-> [FAIL] 콤보 타이밍 실패! (현재 진행률: {normalizedTime:F2})");
                }
            }
        }

        // 차지 공격 로직
        if (stateInfo.IsName("ChargeAttack"))
        {
            if (normalizedTime >= chargeHoldTime && Mouse.current.leftButton.isPressed)
            {
                if (!isPausedForCharge)
                {
                    Debug.Log("[Charge] 차지 멈춤 동작 시작");
                    isPausedForCharge = true;
                    anim.speed = 0f;
                }
            }

            if (Mouse.current.leftButton.wasReleasedThisFrame && isPausedForCharge)
            {
                Debug.Log("[Charge] 차지 공격 발사!");
                ResumeAnimation();
            }
        }
        else if (anim.speed == 0f)
        {
            ResumeAnimation();
        }
    }

    private void ResumeAnimation()
    {
        isPausedForCharge = false;
        anim.speed = 1f;
    }

    private void OnDisable()
    {
        if (anim != null) anim.speed = 1f;
    }
}