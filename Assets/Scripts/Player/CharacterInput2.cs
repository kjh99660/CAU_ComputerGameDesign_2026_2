using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [Header("Movement & Rotation")]
    public float moveSpeed = 10f;
    public float rotationSpeed = 720f;
    public float acceleration = 100f; // 가감속 속도

    [Header("State Control")]
    public bool canMove = true;

    [Header("References")]
    public Transform cameraTransform;

    private Animator anim;
    private CharacterController controller;

    private List<int> xAxisStack = new List<int>();
    private HashSet<int> xAxisSet = new HashSet<int>();
    private List<int> yAxisStack = new List<int>();
    private HashSet<int> yAxisSet = new HashSet<int>();

    private float currentSpeed = 0f;
    private Vector3 lastMoveDirection = Vector3.forward; // 마지막으로 이동하던 방향 저장

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
        if (!canMove)
        {
            ClearInputStacks();
            return;
        }

        if (Keyboard.current == null) return;

        // 1. 입력 스택 업데이트
        UpdateAxisStack(Keyboard.current.wKey, yAxisStack, yAxisSet, 1);
        UpdateAxisStack(Keyboard.current.sKey, yAxisStack, yAxisSet, -1);
        UpdateAxisStack(Keyboard.current.dKey, xAxisStack, xAxisSet, 1);
        UpdateAxisStack(Keyboard.current.aKey, xAxisStack, xAxisSet, -1);

        float x = xAxisStack.Count > 0 ? xAxisStack[xAxisStack.Count - 1] : 0f;
        float y = yAxisStack.Count > 0 ? yAxisStack[yAxisStack.Count - 1] : 0f;

        Vector2 inputDir = Vector2.ClampMagnitude(new Vector2(x, y), 1f);

        // 2. 입력 여부 확인
        bool isMovingInput = inputDir.sqrMagnitude > 0.01f;

        // 3. 가속 및 감속 처리 (이동 속도만 부드럽게 줄어듦)
        float targetSpeed = isMovingInput ? moveSpeed : 0f;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, acceleration * Time.deltaTime);

        // [핵심] isRunning은 속도가 아니라 '키를 누르고 있는지(입력)'에 따라 즉시 반영
        anim.SetBool("isRunning", isMovingInput);

        // 4. 이동 및 회전 처리 (속도가 남아있는 동안은 미끄러지듯 감속 이동)
        if (currentSpeed > 0.01f)
        {
            if (isMovingInput)
            {
                Vector3 forward = cameraTransform.forward;
                Vector3 right = cameraTransform.right;
                forward.y = 0f;
                right.y = 0f;
                forward.Normalize();
                right.Normalize();

                lastMoveDirection = (forward * inputDir.y + right * inputDir.x).normalized;

                Quaternion targetRotation = Quaternion.LookRotation(lastMoveDirection);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            }

            if (controller != null)
            {
                controller.Move(lastMoveDirection * currentSpeed * Time.deltaTime);
            }
        }
    }

    private void UpdateAxisStack(UnityEngine.InputSystem.Controls.KeyControl key, List<int> stack, HashSet<int> set, int value)
    {
        if (key.wasPressedThisFrame && set.Add(value))
        {
            stack.Add(value);
        }

        if (key.wasReleasedThisFrame && set.Remove(value))
        {
            stack.Remove(value);
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus) ClearInputStacks();
    }

    private void OnDisable() => ClearInputStacks();

    public void ClearInputStacks()
    {
        xAxisStack.Clear();
        xAxisSet.Clear();
        yAxisStack.Clear();
        yAxisSet.Clear();
        currentSpeed = 0f;
        if (anim != null) anim.SetBool("isRunning", false);
    }
}