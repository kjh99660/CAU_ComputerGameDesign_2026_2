using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class PauseView : UIScreenView
{
    // Stays on while the retry confirmation covers the menu: only the menu itself is hidden, not the dimming.
    private const string OverlayName = "DimmedOverlay";

    [SerializeField] private Button resumeButton;
    [SerializeField] private Button retryButton;
    [SerializeField] private Button quitButton;
    // Player options (GameOptions): one step down or up per click. Optional: a prefab without them only lacks the panel.
    [Header("Options panel - provisional")]
    [SerializeField] private Button lookDownButton;
    [SerializeField] private Button lookUpButton;
    [SerializeField] private Button volumeDownButton;
    [SerializeField] private Button volumeUpButton;
    [SerializeField] private TMP_Text lookValue;
    [SerializeField] private TMP_Text volumeValue;

    private readonly List<GameObject> _hiddenForCover = new List<GameObject>();
    private int _shownLook = -1, _shownVolume = -1;

    public event Action ResumeClicked;
    public event Action RetryClicked;
    public event Action QuitClicked;
    // +1 = one step up, -1 = one step down.
    public event Action<int> LookStepClicked;
    public event Action<int> VolumeStepClicked;

    public bool IsMenuVisible => _hiddenForCover.Count == 0;
    public bool HasOptions => lookDownButton != null && lookUpButton != null && volumeDownButton != null &&
                              volumeUpButton != null && lookValue != null && volumeValue != null;

    public override void AutoBind()
    {
        base.AutoBind();
        if (resumeButton == null) resumeButton = FindNamed<Button>("ResumeButton");
        if (retryButton == null) retryButton = FindNamed<Button>("RetryButton");
        if (quitButton == null) quitButton = FindNamed<Button>("QuitButton");
        if (lookDownButton == null) lookDownButton = FindNamed<Button>("LookDownButton");
        if (lookUpButton == null) lookUpButton = FindNamed<Button>("LookUpButton");
        if (volumeDownButton == null) volumeDownButton = FindNamed<Button>("VolumeDownButton");
        if (volumeUpButton == null) volumeUpButton = FindNamed<Button>("VolumeUpButton");
        if (lookValue == null) lookValue = FindNamed<TMP_Text>("LookValue");
        if (volumeValue == null) volumeValue = FindNamed<TMP_Text>("VolumeValue");
    }

    private void OnEnable()
    {
        AutoBind();
        if (resumeButton != null) resumeButton.onClick.AddListener(HandleResumeClicked);
        if (retryButton != null) retryButton.onClick.AddListener(HandleRetryClicked);
        if (quitButton != null) quitButton.onClick.AddListener(HandleQuitClicked);
        if (lookDownButton != null) lookDownButton.onClick.AddListener(HandleLookDown);
        if (lookUpButton != null) lookUpButton.onClick.AddListener(HandleLookUp);
        if (volumeDownButton != null) volumeDownButton.onClick.AddListener(HandleVolumeDown);
        if (volumeUpButton != null) volumeUpButton.onClick.AddListener(HandleVolumeUp);
    }

    private void OnDisable()
    {
        if (resumeButton != null) resumeButton.onClick.RemoveListener(HandleResumeClicked);
        if (retryButton != null) retryButton.onClick.RemoveListener(HandleRetryClicked);
        if (quitButton != null) quitButton.onClick.RemoveListener(HandleQuitClicked);
        if (lookDownButton != null) lookDownButton.onClick.RemoveListener(HandleLookDown);
        if (lookUpButton != null) lookUpButton.onClick.RemoveListener(HandleLookUp);
        if (volumeDownButton != null) volumeDownButton.onClick.RemoveListener(HandleVolumeDown);
        if (volumeUpButton != null) volumeUpButton.onClick.RemoveListener(HandleVolumeUp);
    }

    public override void ResetView() => SetMenuVisible(true);

    // The retry confirmation is drawn over this menu and is see-through, so the menu's buttons would show inside it.
    // While it is open everything but the dimming is hidden; only what this call hid is shown again.
    public void SetMenuVisible(bool visible)
    {
        if (visible)
        {
            for (int i = 0; i < _hiddenForCover.Count; i++)
                if (_hiddenForCover[i] != null) _hiddenForCover[i].SetActive(true);
            _hiddenForCover.Clear();
            return;
        }
        if (_hiddenForCover.Count > 0)
            return;
        foreach (Transform child in transform)
        {
            if (child.name == OverlayName || !child.gameObject.activeSelf) continue;
            child.gameObject.SetActive(false);
            _hiddenForCover.Add(child.gameObject);
        }
    }

    // Values as whole percent. The texts are rewritten only when a value changes, and a button at the end of its
    // range is switched off.
    public void RenderOptions(int lookPercent, int volumePercent, bool canLowerLook, bool canRaiseLook, bool canLowerVolume,
        bool canRaiseVolume)
    {
        if (lookValue != null && lookPercent != _shownLook)
        {
            _shownLook = lookPercent;
            lookValue.SetText("{0:0}%", lookPercent);
        }
        if (volumeValue != null && volumePercent != _shownVolume)
        {
            _shownVolume = volumePercent;
            volumeValue.SetText("{0:0}%", volumePercent);
        }
        SetButtonInteractable(lookDownButton, canLowerLook);
        SetButtonInteractable(lookUpButton, canRaiseLook);
        SetButtonInteractable(volumeDownButton, canLowerVolume);
        SetButtonInteractable(volumeUpButton, canRaiseVolume);
    }

    private void HandleResumeClicked() => ResumeClicked?.Invoke();
    private void HandleRetryClicked() => RetryClicked?.Invoke();
    private void HandleQuitClicked() => QuitClicked?.Invoke();
    private void HandleLookDown() => LookStepClicked?.Invoke(-1);
    private void HandleLookUp() => LookStepClicked?.Invoke(1);
    private void HandleVolumeDown() => VolumeStepClicked?.Invoke(-1);
    private void HandleVolumeUp() => VolumeStepClicked?.Invoke(1);
}
