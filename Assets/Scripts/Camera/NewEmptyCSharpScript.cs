using UnityEngine;
using UnityEngine.InputSystem; // 신규 Input System 네임스페이스 추가

public class CameraOrbitController : MonoBehaviour
{
    [Header("References")]
    public Transform player;

    [Header("Camera Settings")]
    public float distance = 4.0f;
    public float heightOffset = 1.5f;
    public float mouseSensitivity = 0.2f; // 신규 Input System은 델타 값이 커서 감도를 낮게 조절해야 합니다.

    [Header("Pitch Limits (Vertical)")]
    public float minPitch = -20f;
    public float maxPitch = 60f;

    private float yaw = 0f;
    private float pitch = 20f;

    private void Start()
    {
        if (player == null)
        {
            GameObject pObj = GameObject.FindGameObjectWithTag("Player");
            if (pObj != null) player = pObj.transform;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void LateUpdate()
    {
        if (player == null) return;

        // [수정] 신규 Input System의 Mouse.current.delta를 이용해 마우스 이동량 읽기
        Vector2 mouseDelta = Vector2.zero;
        if (Mouse.current != null)
        {
            mouseDelta = Mouse.current.delta.ReadValue() * mouseSensitivity;
        }

        yaw += mouseDelta.x;
        pitch -= mouseDelta.y;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 targetPoint = player.position + Vector3.up * heightOffset;

        transform.position = targetPoint - (rotation * Vector3.forward * distance);
        transform.LookAt(targetPoint);
    }
}