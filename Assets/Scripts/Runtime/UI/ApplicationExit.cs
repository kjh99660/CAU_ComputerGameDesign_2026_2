using UnityEngine;

// Leaving the game from the start screen's EXIT button. It is not a game state, so it does not go through the request
// queue. The built player quits; in the Editor, where Application.Quit does nothing, Play Mode stops instead.
// Tests replace it (UIRoot.SetApplicationExit) so that pressing EXIT does not end the test run.
public interface IApplicationExit
{
    void Exit();
}

public sealed class ApplicationExit : IApplicationExit
{
    public void Exit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
