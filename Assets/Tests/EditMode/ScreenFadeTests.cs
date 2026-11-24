using NUnit.Framework;
using UnityEngine;

// 화면 전환 페이드(TransitionLayer)의 규칙과 Safe Area 계산식. 상태 흐름에는 관여하지 않고 관찰한 상태로 불투명도만 정한다.
public sealed class ScreenFadeTests
{
    private const float Ready = 0.35f;
    private const float Result = 0.2f;
    private const float Threshold = 0.5f;

    [Test]
    public void InitializingIsCoveredAndTheFirstReadyFadesInFromBlack()
    {
        var fade = new ScreenFade();
        fade.Observe(GameState.Initializing, 1, false, Ready, Result);
        Assert.AreEqual(1f, fade.Opacity);
        Assert.IsTrue(fade.IsCovering);
        Assert.IsTrue(fade.BlocksInput(Threshold));
        fade.Advance(5f);
        Assert.AreEqual(1f, fade.Opacity, "The cover holds until the game is ready.");

        fade.Observe(GameState.Ready, 1, false, Ready, Result);
        Assert.AreEqual(1f, fade.Opacity, "Ready starts from black.");
        Assert.IsFalse(fade.IsCovering);
        Assert.IsTrue(fade.IsFading);
        AssertFadesOut(fade, Ready);
    }

    [Test]
    public void EveryResetIsCoveredAndReadyFadesInFromBlack()
    {
        var fade = new ScreenFade();
        fade.Observe(GameState.Battle, 1, false, Ready, Result);
        fade.Observe(GameState.Paused, 1, false, Ready, Result);
        Assert.AreEqual(0f, fade.Opacity, "Pause does not darken the screen.");

        // RestartRequest/QuitToStartRequest: the manager raises the Tick and resets while the state is still Paused.
        fade.Observe(GameState.Paused, 2, true, Ready, Result);
        Assert.AreEqual(1f, fade.Opacity);
        Assert.IsTrue(fade.IsCovering);
        fade.Advance(1f);
        fade.Observe(GameState.Paused, 2, true, Ready, Result);
        Assert.AreEqual(1f, fade.Opacity, "The screen stays covered for the whole reset.");
        fade.Observe(GameState.Ready, 2, false, Ready, Result);
        Assert.AreEqual(1f, fade.Opacity);
        AssertFadesOut(fade, Ready);
    }

    [Test]
    public void AResetFinishedWithinOneFrameStillFadesInFromBlack()
    {
        var fade = new ScreenFade();
        fade.Observe(GameState.Result, 3, false, Ready, Result);
        fade.Advance(1f);
        Assert.AreEqual(0f, fade.Opacity);
        fade.Observe(GameState.Ready, 4, false, Ready, Result);
        Assert.AreEqual(1f, fade.Opacity, "A new round (new Tick) at Ready always starts from black.");
        AssertFadesOut(fade, Ready);
    }

    [Test]
    public void ResultGetsAShortFadeFromBlack()
    {
        var fade = new ScreenFade();
        fade.Observe(GameState.OutroDialogue, 1, false, Ready, Result);
        Assert.AreEqual(0f, fade.Opacity, "The outro dialogue itself is not faded.");
        fade.Observe(GameState.Result, 1, false, Ready, Result);
        Assert.AreEqual(1f, fade.Opacity);
        AssertFadesOut(fade, Result);
    }

    [Test]
    public void AStateChangeDuringAFadeEndsItTransparentAndNonBlocking()
    {
        var fade = new ScreenFade();
        fade.Observe(GameState.Initializing, 1, false, Ready, Result);
        fade.Observe(GameState.Ready, 1, false, Ready, Result);
        fade.Advance(0.02f);
        Assert.IsTrue(fade.BlocksInput(Threshold));
        fade.Observe(GameState.IntroDialogue, 1, false, Ready, Result);
        Assert.AreEqual(0f, fade.Opacity);
        Assert.IsFalse(fade.BlocksInput(Threshold));
        Assert.IsFalse(fade.IsFading);
        fade.Advance(1f);
        Assert.AreEqual(0f, fade.Opacity);
    }

    [Test]
    public void OtherTransitionsNeverDarkenTheScreen()
    {
        var fade = new ScreenFade();
        fade.Observe(GameState.Ready, 1, false, Ready, Result);
        fade.Advance(1f);
        GameState[] flow =
        {
            GameState.IntroDialogue, GameState.Countdown, GameState.Battle, GameState.Paused, GameState.Battle,
            GameState.Finishing, GameState.OutroDialogue
        };
        foreach (GameState state in flow)
        {
            fade.Observe(state, 1, false, Ready, Result);
            Assert.AreEqual(0f, fade.Opacity, state.ToString());
            Assert.IsFalse(fade.BlocksInput(Threshold), state.ToString());
        }
    }

    [Test]
    public void SeeingTheSameStateAgainDoesNotRestartTheFade()
    {
        var fade = new ScreenFade();
        fade.Observe(GameState.Ready, 1, false, Ready, Result);
        fade.Advance(0.1f);
        float opacity = fade.Opacity;
        Assert.Less(opacity, 1f);
        fade.Observe(GameState.Ready, 1, false, Ready, Result);
        Assert.AreEqual(opacity, fade.Opacity);
    }

    [Test]
    public void ACoverThatEndsWithoutANewRoundClears()
    {
        // A reset that could not start: the manager stops resetting and the state stays Paused.
        var fade = new ScreenFade();
        fade.Observe(GameState.Paused, 1, false, Ready, Result);
        fade.Observe(GameState.Paused, 1, true, Ready, Result);
        Assert.AreEqual(1f, fade.Opacity);
        fade.Observe(GameState.Paused, 1, false, Ready, Result);
        Assert.AreEqual(0f, fade.Opacity);
        Assert.IsFalse(fade.BlocksInput(Threshold));
    }

    [Test]
    public void ZeroDurationMeansNoFade()
    {
        var fade = new ScreenFade();
        fade.Observe(GameState.Initializing, 1, false, 0f, 0f);
        fade.Observe(GameState.Ready, 1, false, 0f, 0f);
        Assert.AreEqual(0f, fade.Opacity);
        Assert.IsFalse(fade.BlocksInput(Threshold));
    }

    [Test]
    public void SafeAreaIsNormalizedToTheScreenAndFallsBackToTheWholeScreen()
    {
        Rect area = SafeAreaFitter.Normalize(Rect.MinMaxRect(132f, 63f, 2304f, 1125f), new Vector2(2436f, 1125f));
        Assert.AreEqual(132f / 2436f, area.xMin, 1e-5f);
        Assert.AreEqual(63f / 1125f, area.yMin, 1e-5f);
        Assert.AreEqual(2304f / 2436f, area.xMax, 1e-5f);
        Assert.AreEqual(1f, area.yMax, 1e-5f);

        Rect full = new Rect(0f, 0f, 1f, 1f);
        Assert.AreEqual(full, SafeAreaFitter.Normalize(new Rect(0f, 0f, 1920f, 1080f), new Vector2(1920f, 1080f)));
        Assert.AreEqual(full, SafeAreaFitter.Normalize(new Rect(0f, 0f, 1920f, 1080f), Vector2.zero), "No screen size: whole screen.");
        Assert.AreEqual(full, SafeAreaFitter.Normalize(new Rect(0f, 0f, 0f, 0f), new Vector2(1920f, 1080f)), "Empty safe area: whole screen.");
        Rect clamped = SafeAreaFitter.Normalize(new Rect(-50f, -20f, 2100f, 1200f), new Vector2(1920f, 1080f));
        Assert.AreEqual(full, clamped, "A safe area larger than the screen is clamped to it.");
    }

    [Test]
    public void BackgroundAnchorsReachTheWholeScreenFromInsideTheSafeArea()
    {
        Rect safe = Rect.MinMaxRect(0.06f, 0.03f, 0.95f, 0.92f);
        SafeAreaFitter.FullScreenAnchors(safe, out Vector2 min, out Vector2 max);
        // A child anchor a inside the safe-area parent sits at safe.min + a * safe.size on the screen.
        Assert.AreEqual(0f, safe.xMin + min.x * safe.width, 1e-5f);
        Assert.AreEqual(0f, safe.yMin + min.y * safe.height, 1e-5f);
        Assert.AreEqual(1f, safe.xMin + max.x * safe.width, 1e-5f);
        Assert.AreEqual(1f, safe.yMin + max.y * safe.height, 1e-5f);

        SafeAreaFitter.FullScreenAnchors(new Rect(0f, 0f, 1f, 1f), out min, out max);
        Assert.AreEqual(Vector2.zero, min, "On a desktop screen the backgrounds keep their authored anchors.");
        Assert.AreEqual(Vector2.one, max);
    }

    // 실제 시간으로 seconds 동안 줄곧 옅어지다가 투명해지고, 거의 불투명한 동안에만 입력을 막는다.
    private static void AssertFadesOut(ScreenFade fade, float seconds)
    {
        float previous = fade.Opacity;
        bool releasedInput = false;
        const float step = 0.01f;
        for (float elapsed = 0f; elapsed < seconds - step * 0.5f; elapsed += step)
        {
            fade.Advance(step);
            Assert.LessOrEqual(fade.Opacity, previous, "The fade only gets lighter.");
            Assert.AreEqual(fade.Opacity >= Threshold, fade.BlocksInput(Threshold), "Input is blocked only while mostly opaque.");
            releasedInput |= !fade.BlocksInput(Threshold) && fade.Opacity > 0f;
            previous = fade.Opacity;
        }
        fade.Advance(step);
        Assert.AreEqual(0f, fade.Opacity, "The fade ends transparent.");
        Assert.IsFalse(fade.BlocksInput(Threshold));
        Assert.IsFalse(fade.IsFading);
        Assert.IsTrue(releasedInput, "Input is released while the fade is still visible.");
    }
}
