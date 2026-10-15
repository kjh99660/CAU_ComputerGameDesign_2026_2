using UnityEngine;

public class ReferenceAnimatorSync : MonoBehaviour
{
    public Animator mine;       // 내 캐릭터 Animator
    public Animator reference;  // 보라돌이 Animator

    void Start()
    {
        reference.speed = 0;
    }

    void LateUpdate()
    {
        var s = mine.GetCurrentAnimatorStateInfo(0);
        reference.Play(s.fullPathHash, 0, s.normalizedTime);
        reference.Update(0f); // 같은 프레임에 즉시 포즈 반영
    }
}