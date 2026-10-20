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
        if (Keyboard.current == null) return;

        // 1. [핵심] canMove가 false여도 '입력 스택'은 항상 최신 상태로 갱신되어야 
        // 공격 끝날 때 미리 누르고 있던 키가 씹히지 않습니다.
        UpdateAxisStack(Keyboard.current.wKey, yAxisStack, yAxisSet, 1);
        UpdateAxisStack(Keyboard.current.sKey, yAxisStack, yAxisSet, -1);
        UpdateAxisStack(Keyboard.current.dKey, xAxisStack, xAxisSet, 1);
        UpdateAxisStack(Keyboard.current.aKey, xAxisStack, xAxisSet, -1);

        float x = xAxisStack.Count > 0 ? xAxisStack[xAxisStack.Count - 1] : 0f;
        float y = yAxisStack.Count > 0 ? yAxisStack[yAxisStack.Count - 1] : 0f;

        Vector2 inputDir = Vector2.ClampMagnitude(new Vector2(x, y), 1f);
        bool isMovingInput = inputDir.sqrMagnitude > 0.01f;

        // 2. 만약 이동이 불가능한 상태(공격 중 등)라면 이동 연산만 건너뜁니다.
        if (!canMove)
        {
            currentSpeed = 0f;
            if (anim != null) anim.SetBool("isRunning", false);
            return;
        }

        // 3. 가속 및 감속 처리
        float targetSpeed = isMovingInput ? moveSpeed : 0f;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, acceleration * Time.deltaTime);

        anim.SetBool("isRunning", isMovingInput);

        // 4. 이동 및 회전 처리
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