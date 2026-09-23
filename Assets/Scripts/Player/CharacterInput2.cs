using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [Header("Movement & Rotation")]
    public float moveSpeed = 5f;
    public float rotationSpeed = 720f; // 초당 회전 속도 (도/초)

    [Header("References")]
    public Transform cameraTransform; // 메인 카메라 (카메라 바라보는 방향 기준 이동 시 필요)

    private Animator anim;
    private CharacterController controller; // 또는 Rigidbody

    // 축별 입력 스택 (Last One)
    private List<int> xAxisStack = new List<int>();
    private List<int> yAxisStack = new List<int>();

    private void Start()
    {
        anim = GetComponent<Animator>();
        controller = GetComponent<CharacterController>();

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }
    }

    private void Update()
    {
        if (Keyboard.current == null) return;

        // 1. 입력 스택 업데이트 (Last One)
        UpdateAxisStack(Keyboard.current.wKey, yAxisStack, 1);
        UpdateAxisStack(Keyboard.current.sKey, yAxisStack, -1);
        UpdateAxisStack(Keyboard.current.dKey, xAxisStack, 1);
        UpdateAxisStack(Keyboard.current.aKey, xAxisStack, -1);

        float x = xAxisStack.Count > 0 ? xAxisStack[xAxisStack.Count - 1] : 0f;
        float y = yAxisStack.Count > 0 ? yAxisStack[yAxisStack.Count - 1] : 0f;

        Vector2 inputDir = Vector2.ClampMagnitude(new Vector2(x, y), 1f);

        // 2. 이동 및 회전 처리
        if (inputDir.sqrMagnitude > 0.01f)
        {
            // [카메라 기준 방향 계산]
            // 카메라가 바라보는 방향을 기준으로 WASD 방향 계산 (3D 쿼터뷰/숄더뷰 필수)
            Vector3 forward = cameraTransform.forward;
            Vector3 right = cameraTransform.right;
            forward.y = 0f; // 수평 이동을 위해 Y축 고정
            right.y = 0f;
            forward.Normalize();
            right.Normalize();

            // 최종 이동/바라볼 방향
            Vector3 moveDirection = (forward * inputDir.y + right * inputDir.x).normalized;

            // A. 캐릭터 회전 (목표 방향으로 부드럽게 돌아서기)
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);

            // B. 캐릭터 실제 위치 이동 (In-place 애니메이션 사용 시 스크립트로 이동)
            if (controller != null)
            {
                controller.Move(moveDirection * moveSpeed * Time.deltaTime);
            }

            anim.SetBool("isRunning", true);
        }
        else
        {
            anim.SetBool("isRunning", false);
        }
    }

    private void UpdateAxisStack(UnityEngine.InputSystem.Controls.KeyControl key, List<int> stack, int value)
    {
        if (key.wasPressedThisFrame && !stack.Contains(value)) stack.Add(value);
        if (key.wasReleasedThisFrame) stack.Remove(value);
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus) ClearInputStacks();
    }

    private void OnDisable() => ClearInputStacks();

    private void ClearInputStacks()
    {
        xAxisStack.Clear();
        yAxisStack.Clear();
        if (anim != null) anim.SetBool("isRunning", false);
    }
}