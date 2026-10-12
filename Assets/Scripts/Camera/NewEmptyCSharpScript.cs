using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("References")]
    public Transform player; // Inspector창에서 Player 오브젝트 연결

    [Header("Position Offset")]
    public Vector3 offset = new Vector3(0f, 2f, -4f); // 캐릭터 기준 카메라 리그의 상대 위치

    private void LateUpdate()
    {
        if (player == null) return;

        // 플레이어의 위치를 그대로 따르되, 플레이어가 회전하는(Rotation) 값은 절대 반영하지 않음
        transform.position = player.position + offset;
    }
}