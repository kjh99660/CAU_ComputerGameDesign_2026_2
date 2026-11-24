using System;

public sealed class RetryConfirmationPresenter : UiPresenter<RetryConfirmationView>
{
    // After RESTART is confirmed the round resets on the next frame. Should that not happen, the pause menu comes
    // back after this long instead of staying hidden behind a confirmation that is no longer there.
    private const float RestartGraceSeconds = 1f;

    private bool _isOpen;
    private float _awaitingRestart = -1f;
    public bool IsOpen => _isOpen;

    // True while the confirmation covers the pause menu, whose buttons would otherwise show through it. Stays true
    // after RESTART is confirmed (the screen goes black for the reset) and turns false when it is cancelled.
    public event Action<bool> CoverChanged;

    public RetryConfirmationPresenter(RetryConfirmationView view, IUIRequestDispatcher requests) : base(view, requests)
    {
        View.ConfirmClicked += OnConfirmClicked;
        View.CancelClicked += OnCancelClicked;
        View.SetVisible(false);
    }

    protected override bool IsVisibleIn(GameState state) => _isOpen && state == GameState.Paused;

    protected override void OnStateApplied(GameStateChangedEvent notification)
    {
        // Every entry into Paused starts with the menu uncovered (PausePresenter shows it); nothing to announce.
        _awaitingRestart = -1f;
        if (notification.Current != GameState.Paused)
            Close();
    }

    public void Show()
    {
        if (CurrentState != GameState.Paused)
            return;
        _isOpen = true;
        _awaitingRestart = -1f;
        View.SetVisible(true);
        View.SetInputEnabled(true);
        CoverChanged?.Invoke(true);
    }

    // Cancel, Esc: the confirmation closes and the pause menu is back.
    public void Hide()
    {
        bool wasCovering = _isOpen || _awaitingRestart >= 0f;
        Close();
        _awaitingRestart = -1f;
        if (wasCovering)
            CoverChanged?.Invoke(false);
    }

    public override void Update(float deltaTime, float unscaledDeltaTime)
    {
        if (_awaitingRestart < 0f)
            return;
        _awaitingRestart += unscaledDeltaTime;
        if (_awaitingRestart >= RestartGraceSeconds && CurrentState == GameState.Paused)
            Hide();
    }

    private void Close()
    {
        _isOpen = false;
        View.SetVisible(false);
    }

    private void OnConfirmClicked()
    {
        if (!_isOpen) return;
        // The menu stays covered: the reset blacks the screen out next, and the menu must not flash in between.
        Close();
        _awaitingRestart = 0f;
        Requests.Enqueue(UIRequestType.Restart);
    }

    private void OnCancelClicked() => Hide();

    public override void Dispose()
    {
        View.ConfirmClicked -= OnConfirmClicked;
        View.CancelClicked -= OnCancelClicked;
        CoverChanged = null;
    }
}
