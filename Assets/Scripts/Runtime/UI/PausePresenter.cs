using System;

public sealed class PausePresenter : UiPresenter<PauseView>
{
    private GameOptions _options;

    public event Action RetryConfirmationRequested;

    public PausePresenter(PauseView view, IUIRequestDispatcher requests) : base(view, requests)
    {
        View.ResumeClicked += OnResumeClicked;
        View.RetryClicked += OnRetryClicked;
        View.QuitClicked += OnQuitClicked;
        View.LookStepClicked += OnLookStepClicked;
        View.VolumeStepClicked += OnVolumeStepClicked;
    }

    protected override bool IsVisibleIn(GameState state) => state == GameState.Paused;

    // Every pause opens with the whole menu showing and the current option values.
    protected override void OnStateApplied(GameStateChangedEvent notification)
    {
        if (notification.Current != GameState.Paused)
            return;
        View.SetMenuVisible(true);
        RenderOptions();
    }

    // The player's settings shown in the options panel (mouse sensitivity, volume). Changing them is not a game state
    // change, so it goes straight to GameOptions instead of the request queue; CoreBootstrap applies the values.
    public void AttachOptions(GameOptions options)
    {
        if (_options != null) _options.Changed -= RenderOptions;
        _options = options;
        if (_options != null) _options.Changed += RenderOptions;
        RenderOptions();
    }

    // The retry confirmation is see-through: while it is open the menu behind it is hidden (RetryConfirmationPresenter).
    public void SetCoveredByConfirmation(bool covered) => View.SetMenuVisible(!covered);

    private void RenderOptions()
    {
        if (_options == null) return;
        View.RenderOptions(_options.LookPercent, _options.VolumePercent, _options.CanLowerLook, _options.CanRaiseLook,
            _options.CanLowerVolume, _options.CanRaiseVolume);
    }

    private void OnResumeClicked() => Requests.Enqueue(UIRequestType.Resume);
    private void OnRetryClicked() => RetryConfirmationRequested?.Invoke();
    private void OnQuitClicked() => Requests.Enqueue(UIRequestType.QuitToStart);

    private void OnLookStepClicked(int direction)
    {
        if (CurrentState == GameState.Paused && Requests.IsInputEnabled) _options?.StepLook(direction);
    }

    private void OnVolumeStepClicked(int direction)
    {
        if (CurrentState == GameState.Paused && Requests.IsInputEnabled) _options?.StepVolume(direction);
    }

    public override void Dispose()
    {
        View.ResumeClicked -= OnResumeClicked;
        View.RetryClicked -= OnRetryClicked;
        View.QuitClicked -= OnQuitClicked;
        View.LookStepClicked -= OnLookStepClicked;
        View.VolumeStepClicked -= OnVolumeStepClicked;
        if (_options != null) _options.Changed -= RenderOptions;
        _options = null;
        RetryConfirmationRequested = null;
    }
}
