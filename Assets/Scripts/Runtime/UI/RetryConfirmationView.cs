using System;
using UnityEngine;
using UnityEngine.UI;

public sealed class RetryConfirmationView : UIScreenView
{
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;

    public event Action ConfirmClicked;
    public event Action CancelClicked;

    public override void AutoBind()
    {
        base.AutoBind();
        if (confirmButton == null) confirmButton = FindNamed<Button>("ConfirmButton");
        if (cancelButton == null) cancelButton = FindNamed<Button>("CancelButton");
    }

    private void OnEnable()
    {
        AutoBind();
        if (confirmButton != null) confirmButton.onClick.AddListener(HandleConfirmClicked);
        if (cancelButton != null) cancelButton.onClick.AddListener(HandleCancelClicked);
    }

    private void OnDisable()
    {
        if (confirmButton != null) confirmButton.onClick.RemoveListener(HandleConfirmClicked);
        if (cancelButton != null) cancelButton.onClick.RemoveListener(HandleCancelClicked);
    }

    private void HandleConfirmClicked() => ConfirmClicked?.Invoke();
    private void HandleCancelClicked() => CancelClicked?.Invoke();
}
