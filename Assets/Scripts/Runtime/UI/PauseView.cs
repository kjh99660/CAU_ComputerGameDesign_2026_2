using System;
using UnityEngine;
using UnityEngine.UI;

public sealed class PauseView : UIScreenView
{
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button retryButton;
    [SerializeField] private Button quitButton;

    public event Action ResumeClicked;
    public event Action RetryClicked;
    public event Action QuitClicked;

    public override void AutoBind()
    {
        base.AutoBind();
        if (resumeButton == null) resumeButton = FindNamed<Button>("ResumeButton");
        if (retryButton == null) retryButton = FindNamed<Button>("RetryButton");
        if (quitButton == null) quitButton = FindNamed<Button>("QuitButton");
    }

    private void OnEnable()
    {
        AutoBind();
        if (resumeButton != null) resumeButton.onClick.AddListener(HandleResumeClicked);
        if (retryButton != null) retryButton.onClick.AddListener(HandleRetryClicked);
        if (quitButton != null) quitButton.onClick.AddListener(HandleQuitClicked);
    }

    private void OnDisable()
    {
        if (resumeButton != null) resumeButton.onClick.RemoveListener(HandleResumeClicked);
        if (retryButton != null) retryButton.onClick.RemoveListener(HandleRetryClicked);
        if (quitButton != null) quitButton.onClick.RemoveListener(HandleQuitClicked);
    }

    private void HandleResumeClicked() => ResumeClicked?.Invoke();
    private void HandleRetryClicked() => RetryClicked?.Invoke();
    private void HandleQuitClicked() => QuitClicked?.Invoke();
}
