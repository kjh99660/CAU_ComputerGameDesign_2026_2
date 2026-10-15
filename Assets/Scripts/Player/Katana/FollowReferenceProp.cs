using UnityEngine;

[DefaultExecutionOrder(100)]
public class FollowReferenceProp : MonoBehaviour
{
    public Transform refProp;   // 보라돌이의 weapon (칼집은 sheath)
    public Transform refHand;   // 보라돌이의 RightHand (칼집은 부착 기준 본)
    public Transform myHand;    // 내 캐릭터의 RightHand
    public float sizeRatio = 1f; // 내 캐릭터 키 / 보라돌이 키

    void LateUpdate()
    {
        Vector3 offset = refProp.position - refHand.position;
        transform.position = myHand.position + offset * sizeRatio;
        transform.rotation = refProp.rotation;
    }
}

