using System;

public sealed class PausePresenter : UiPresenter<PauseView>
{
    public event Action RetryConfirmationRequested;

    public PausePresenter(PauseView view, IUiRequestDispatcher requests) : base(view, requests)
    {
        View.ResumeClicked += OnResumeClicked;
        View.RetryClicked += OnRetryClicked;
        View.QuitClicked += OnQuitClicked;
    }

    protected override bool IsVisibleIn(GameState state) => state == GameState.Paused;
    private void OnResumeClicked() => Requests.Enqueue(UiRequestType.Resume);
    private void OnRetryClicked() => RetryConfirmationRequested?.Invoke();
    private void OnQuitClicked() => Requests.Enqueue(UiRequestType.QuitToStart);

    public override void Dispose()
    {
        View.ResumeClicked -= OnResumeClicked;
        View.RetryClicked -= OnRetryClicked;
        View.QuitClicked -= OnQuitClicked;
        RetryConfirmationRequested = null;
    }
}
