using UnityEngine;

public class ReferenceAnimatorSync : MonoBehaviour
{
    public Animator mine;       // 내 캐릭터 Animator
    public Animator reference;  // 보라돌이 Animator

    void Start()
    {
        reference.speed = 0;
        // 포즈만 따라 읽는 Animator라 클립의 이벤트(Draw, HitStart 등)를 받을 대상이 없다.
        // 켜 두면 공격할 때마다 "AnimationEvent ... has no receiver" 오류가 이벤트 수만큼 찍힌다.
        reference.fireEvents = false;
    }

    void LateUpdate()
    {
        var s = mine.GetCurrentAnimatorStateInfo(0);
        reference.Play(s.fullPathHash, 0, s.normalizedTime);
        reference.Update(0f); // 같은 프레임에 즉시 포즈 반영
    }
}