using UnityEngine;

public class PurpleFollow : MonoBehaviour
{
    public Animator mine, reference;
    void Start() { reference.speed = 0; }
    void Update()
    {
        var s = mine.GetCurrentAnimatorStateInfo(0);
        reference.Play(s.fullPathHash, 0, s.normalizedTime);
    }
}
