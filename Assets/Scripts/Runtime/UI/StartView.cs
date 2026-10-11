using System;
using UnityEngine;
using UnityEngine.UI;

public sealed class StartView : UIScreenView
{
    [SerializeField] private Button startButton;
    public event Action StartClicked;

    public override void AutoBind()
    {
        base.AutoBind();
        if (startButton == null) startButton = FindNamed<Button>("StartButton");
    }

    private void OnEnable()
    {
        AutoBind();
        if (startButton != null) startButton.onClick.AddListener(HandleStartClicked);
    }

    private void OnDisable()
    {
        if (startButton != null) startButton.onClick.RemoveListener(HandleStartClicked);
    }

    public void SetStartInteractable(bool interactable) => SetButtonInteractable(startButton, interactable);
    private void HandleStartClicked()
    {
        StartClicked?.Invoke();
    }
}
