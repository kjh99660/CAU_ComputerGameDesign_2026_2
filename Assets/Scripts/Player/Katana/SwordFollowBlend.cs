using UnityEngine;

[DefaultExecutionOrder(110)]   // 칼집 스크립트(100)보다 뒤에 실행
public class SwordFollowBlend : MonoBehaviour
{
    [Header("보라돌이 참조")]
    public Transform refWeapon;
    public Transform refRightHand;

    [Header("내 캐릭터")]
    public Transform myRightHand;
    public Transform mySheath;          // 내 칼집 오브젝트(FollowReferenceProp이 움직이는 것)

    [Header("칼집 안에 있을 때의 칼 위치 (mySheath 기준, 한 번만 맞춤)")]
    public Vector3 sheathLocalPos;
    public Vector3 sheathLocalEuler;

    [Header("전환")]
    [Range(0f, 1f)] public float handWeight = 0f;  // 0=칼집 기준, 1=손 기준
    public float blendSpeed = 12f;
    public bool drawn;                              // 발도 상태 (이벤트로 변경)

    public void SetDrawn(bool value) { drawn = value; }   // Animation Event용
    public void Draw() { drawn = true; }
    public void Sheathe() { drawn = false; }

    public float sizeRatio = 1f;

    void LateUpdate()
    {
        handWeight = Mathf.MoveTowards(handWeight, drawn ? 1f : 0f, blendSpeed * Time.deltaTime);

        // 1) 손 기준 (기존 방식)
        Vector3 handPos = myRightHand.position + (refWeapon.position - refRightHand.position) * sizeRatio;
        Quaternion handRot = refWeapon.rotation;

        // 2) 칼집 기준
        Vector3 sheathPos = mySheath.TransformPoint(sheathLocalPos);
        Quaternion sheathRot = mySheath.rotation * Quaternion.Euler(sheathLocalEuler);

        transform.position = Vector3.Lerp(sheathPos, handPos, handWeight);
        transform.rotation = Quaternion.Slerp(sheathRot, handRot, handWeight);
    }
}